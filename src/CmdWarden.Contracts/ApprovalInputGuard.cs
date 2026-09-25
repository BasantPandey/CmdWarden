namespace CmdWarden.Contracts;

/// <summary>
/// Approve and Allow for session accept only real keyboard or mouse input (#23). The Approval Gate
/// feeds this guard from its low-level hooks, which see each hardware event before the window does.
/// Injected input (SendInput) carries the injected flag. Posted messages and UI Automation Invoke
/// never pass the hooks. So neither leaves a credit, and a click without a credit is ignored.
/// </summary>
public sealed class ApprovalInputGuard
{
    /// <summary>A hook event older than this does not vouch for a click.</summary>
    public static readonly TimeSpan CreditWindow = TimeSpan.FromMilliseconds(500);

    public const int VkReturn = 0x0D;
    public const int VkSpace = 0x20;
    public const int VkA = 0x41;

    private DateTime? _creditUtc;

    /// <summary>A key went down. Only Enter, Space, and A to our window count.</summary>
    public void OnKeyDown(int virtualKey, bool injected, bool windowIsForeground, DateTime nowUtc)
    {
        if (injected || !windowIsForeground)
            return;
        if (virtualKey is VkReturn or VkSpace or VkA)
            _creditUtc = nowUtc;
    }

    /// <summary>The left mouse button went up. Only a release over our window counts.</summary>
    public void OnLeftButtonUp(bool injected, bool overWindow, DateTime nowUtc)
    {
        if (!injected && overWindow)
            _creditUtc = nowUtc;
    }

    /// <summary>True once for each real input. The click that uses the credit clears it.</summary>
    public bool TryConsume(DateTime nowUtc)
    {
        var credit = _creditUtc;
        _creditUtc = null;
        return credit is { } at && nowUtc >= at && nowUtc - at <= CreditWindow;
    }

    public const string IgnoredInputLine = "Use your keyboard or mouse.";
}
