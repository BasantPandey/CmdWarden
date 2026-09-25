using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class ApprovalInputGuardTests
{
    private static readonly DateTime T0 = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void No_hook_event_gives_no_credit() =>
        Assert.False(new ApprovalInputGuard().TryConsume(T0));

    [Theory]
    [InlineData(ApprovalInputGuard.VkReturn)]
    [InlineData(ApprovalInputGuard.VkSpace)]
    [InlineData(ApprovalInputGuard.VkA)]
    public void Real_key_to_the_popup_gives_one_credit(int vk)
    {
        var guard = new ApprovalInputGuard();
        guard.OnKeyDown(vk, injected: false, windowIsForeground: true, T0);
        Assert.True(guard.TryConsume(T0.AddMilliseconds(40)));
        Assert.False(guard.TryConsume(T0.AddMilliseconds(50)));
    }

    [Fact]
    public void Injected_key_gives_no_credit()
    {
        var guard = new ApprovalInputGuard();
        guard.OnKeyDown(ApprovalInputGuard.VkReturn, injected: true, windowIsForeground: true, T0);
        Assert.False(guard.TryConsume(T0));
    }

    [Fact]
    public void Key_to_another_window_gives_no_credit()
    {
        var guard = new ApprovalInputGuard();
        guard.OnKeyDown(ApprovalInputGuard.VkReturn, injected: false, windowIsForeground: false, T0);
        Assert.False(guard.TryConsume(T0));
    }

    [Fact]
    public void Other_keys_give_no_credit()
    {
        var guard = new ApprovalInputGuard();
        guard.OnKeyDown(0x42, injected: false, windowIsForeground: true, T0);
        Assert.False(guard.TryConsume(T0));
    }

    [Fact]
    public void Real_click_over_the_popup_gives_a_credit_and_injected_or_outside_clicks_do_not()
    {
        var guard = new ApprovalInputGuard();
        guard.OnLeftButtonUp(injected: true, overWindow: true, T0);
        Assert.False(guard.TryConsume(T0));
        guard.OnLeftButtonUp(injected: false, overWindow: false, T0);
        Assert.False(guard.TryConsume(T0));
        guard.OnLeftButtonUp(injected: false, overWindow: true, T0);
        Assert.True(guard.TryConsume(T0.AddMilliseconds(10)));
    }

    [Fact]
    public void Old_credit_expires()
    {
        var guard = new ApprovalInputGuard();
        guard.OnLeftButtonUp(injected: false, overWindow: true, T0);
        Assert.False(guard.TryConsume(T0 + ApprovalInputGuard.CreditWindow + TimeSpan.FromMilliseconds(1)));
    }
}
