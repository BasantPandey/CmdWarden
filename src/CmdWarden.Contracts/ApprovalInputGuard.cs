namespace CmdWarden.Contracts;

/// <summary>
/// Approve and Allow for session accept only real keyboard or mouse input (#23). The Approval Gate
/// feeds this guard from its low-level hooks, which see each hardware event before the window does.
/// Injected input (SendInput) carries the injected flag. Posted messages and UI Automation Invoke
/// never pass the hooks. So neither leaves a credit, and a click without a credit is ignored.
/// A program can also move the pointer with no input (SetCursorPos) onto Approve, so the next
/// real click lands there. A click counts only when real moves brought the pointer onto the window.
/// </summary>
public sealed class ApprovalInputGuard
{
    /// <summary>A hook event older than this does not vouch for a click.</summary>
    public static readonly TimeSpan CreditWindow = TimeSpan.FromMilliseconds(500);

    public const int VkReturn = 0x0D;
    public const int VkSpace = 0x20;
    public const int VkA = 0x41;
    public const int Vk1 = 0x31;
    public const int VkNumPad1 = 0x61;

    private DateTime? _creditUtc;
    private (int X, int Y)? _lastPoint;
    private bool _pointerReal;

    /// <summary>A key went down. Only Enter, Space, A, and 1 to our window count.</summary>
    public void OnKeyDown(int virtualKey, bool injected, bool windowIsForeground, DateTime nowUtc)
    {
        if (injected || !windowIsForeground)
            return;
        if (virtualKey is VkReturn or VkSpace or VkA or Vk1 or VkNumPad1)
            _creditUtc = nowUtc;
    }

    /// <summary>
    /// Each mouse event, in physical pixels. <paramref name="before"/> is the pointer when the event
    /// came. When it is not where the last event put it, a program moved the pointer. Then the
    /// pointer must leave the window and come back by real moves. Only a real release over the window counts.
    /// </summary>
    public void OnMouse(bool injected, bool overWindow, (int X, int Y) before, (int X, int Y) at, bool leftButtonUp, DateTime nowUtc)
    {
        var jumped = _lastPoint != before;
        _lastPoint = at;
        if (injected || (jumped && overWindow))
            _pointerReal = false;
        else if (!overWindow)
            _pointerReal = true;
        if (leftButtonUp && !injected && overWindow && _pointerReal)
            _creditUtc = nowUtc;
    }

    /// <summary>True once for each real input. The click that uses the credit clears it.</summary>
    public bool TryConsume(DateTime nowUtc)
    {
        var credit = _creditUtc;
        _creditUtc = null;
        return credit is { } at && nowUtc >= at && nowUtc - at <= CreditWindow;
    }

    public const string IgnoredInputLine = "Use your keyboard, or move the mouse off this card and back.";
}
