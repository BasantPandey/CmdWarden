using System.Runtime.InteropServices;
using CmdWarden.Contracts;

namespace CmdWarden.ApprovalGate;

/// <summary>
/// Windows Hello through UserConsentVerifier (#24). The WinRT call goes through COM directly: the
/// Windows SDK projection would add 25 MB to the popup for one method. The prompt is owned by the
/// popup window, so it opens in front of it.
/// </summary>
internal static class WindowsHello
{
    private const string ClassId = "Windows.Security.Credentials.UI.UserConsentVerifier";
    private static readonly Guid IidUserConsentVerifierInterop = new("39e050c3-4e74-441a-8dc0-b81104df949c");
    // IAsyncOperation<UserConsentVerificationResult>, from the pinterface signature.
    private static readonly Guid IidAsyncOperationOfResult = new("fd596ffd-2318-558f-9dbe-d21df43764a5");
    private static readonly Guid IidAsyncInfo = new("00000036-0000-0000-c000-000000000046");

    private const int AsyncStarted = 0;
    private const int AsyncCompleted = 1;

    /// <summary>Ask for Hello. Any failure of the API itself counts as not available.</summary>
    public static async Task<HelloCheck> VerifyAsync(IntPtr window, string message)
    {
        IntPtr operation;
        try
        {
            operation = Start(window, message);
        }
        catch
        {
            return HelloCheck.NotAvailable;
        }

        try
        {
            // The UI thread keeps pumping messages while the Hello prompt is open.
            int status;
            while ((status = Status(operation)) == AsyncStarted)
                await Task.Delay(100);
            if (status != AsyncCompleted)
                return HelloCheck.Canceled;
            return ApprovalAnswer.FromVerificationResult(Result(operation));
        }
        catch
        {
            return HelloCheck.NotAvailable;
        }
        finally
        {
            Marshal.Release(operation);
        }
    }

    private static unsafe IntPtr Start(IntPtr window, string message)
    {
        IntPtr classId = IntPtr.Zero, text = IntPtr.Zero, factory = IntPtr.Zero;
        try
        {
            Check(WindowsCreateString(ClassId, ClassId.Length, out classId));
            Check(WindowsCreateString(message, message.Length, out text));
            var iid = IidUserConsentVerifierInterop;
            Check(RoGetActivationFactory(classId, ref iid, out factory));
            // IUserConsentVerifierInterop: IInspectable (6 slots), then RequestVerificationForWindowAsync.
            var vtable = *(IntPtr**)factory;
            var request = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, Guid*, IntPtr*, int>)vtable[6];
            var opIid = IidAsyncOperationOfResult;
            IntPtr operation;
            Check(request(factory, window, text, &opIid, &operation));
            return operation;
        }
        finally
        {
            if (factory != IntPtr.Zero)
                Marshal.Release(factory);
            WindowsDeleteString(text);
            WindowsDeleteString(classId);
        }
    }

    private static unsafe int Status(IntPtr operation)
    {
        var iid = IidAsyncInfo;
        Check(Marshal.QueryInterface(operation, in iid, out var info));
        try
        {
            // IAsyncInfo: IInspectable (6 slots), get_Id, get_Status.
            var getStatus = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)(*(IntPtr**)info)[7];
            int status;
            Check(getStatus(info, &status));
            return status;
        }
        finally
        {
            Marshal.Release(info);
        }
    }

    private static unsafe int Result(IntPtr operation)
    {
        // IAsyncOperation<T>: IInspectable (6 slots), put_Completed, get_Completed, GetResults.
        var getResults = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)(*(IntPtr**)operation)[8];
        int result;
        Check(getResults(operation, &result));
        return result;
    }

    private static void Check(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
}
