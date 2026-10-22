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
    [InlineData(ApprovalInputGuard.Vk1)]
    [InlineData(ApprovalInputGuard.VkNumPad1)]
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
    public void Real_click_after_real_moves_onto_the_popup_gives_a_credit_and_injected_or_outside_clicks_do_not()
    {
        var guard = new ApprovalInputGuard();
        var p = new Pointer(guard);
        p.Move(10, overWindow: false);
        p.Move(10, overWindow: true);
        p.Up(injected: false, overWindow: false);
        Assert.False(guard.TryConsume(T0));
        p.Up(injected: false, overWindow: true);
        Assert.True(guard.TryConsume(T0.AddMilliseconds(10)));

        p.Up(injected: true, overWindow: true);
        Assert.False(guard.TryConsume(T0));
    }

    [Fact]
    public void Pointer_moved_with_no_input_onto_the_popup_gives_no_credit_until_it_leaves_and_comes_back()
    {
        var guard = new ApprovalInputGuard();
        var p = new Pointer(guard);
        p.Move(10, overWindow: false);
        p.Jump(500);
        p.Up(injected: false, overWindow: true);
        Assert.False(guard.TryConsume(T0));
        p.Move(5, overWindow: true);
        p.Up(injected: false, overWindow: true);
        Assert.False(guard.TryConsume(T0));

        p.Move(200, overWindow: false);
        p.Move(200, overWindow: true);
        p.Up(injected: false, overWindow: true);
        Assert.True(guard.TryConsume(T0));
    }

    [Fact]
    public void Pointer_already_over_the_popup_at_the_first_event_gives_no_credit()
    {
        var guard = new ApprovalInputGuard();
        var p = new Pointer(guard);
        p.Up(injected: false, overWindow: true);
        Assert.False(guard.TryConsume(T0));
    }

    [Fact]
    public void Injected_move_onto_the_popup_gives_no_credit_until_the_pointer_leaves()
    {
        var guard = new ApprovalInputGuard();
        var p = new Pointer(guard);
        p.Move(10, overWindow: false);
        p.Move(10, overWindow: true, injected: true);
        p.Up(injected: false, overWindow: true);
        Assert.False(guard.TryConsume(T0));
        p.Move(10, overWindow: false);
        p.Move(10, overWindow: true);
        p.Up(injected: false, overWindow: true);
        Assert.True(guard.TryConsume(T0));
    }

    [Fact]
    public void A_jump_outside_the_popup_needs_no_extra_step()
    {
        var guard = new ApprovalInputGuard();
        var p = new Pointer(guard);
        p.Jump(300);
        p.Move(10, overWindow: false);
        p.Move(10, overWindow: true);
        p.Up(injected: false, overWindow: true);
        Assert.True(guard.TryConsume(T0));
    }

    [Fact]
    public void Old_credit_expires()
    {
        var guard = new ApprovalInputGuard();
        var p = new Pointer(guard);
        p.Move(10, overWindow: false);
        p.Move(10, overWindow: true);
        p.Up(injected: false, overWindow: true);
        Assert.False(guard.TryConsume(T0 + ApprovalInputGuard.CreditWindow + TimeSpan.FromMilliseconds(1)));
    }

    /// <summary>A pointer like the hooks see it: each event starts where the last one ended, unless it jumps.</summary>
    private sealed class Pointer(ApprovalInputGuard guard)
    {
        private (int X, int Y) _at = (100, 100);

        public void Move(int dx, bool overWindow, bool injected = false)
        {
            var before = _at;
            _at = (_at.X + dx, _at.Y);
            guard.OnMouse(injected, overWindow, before, _at, leftButtonUp: false, T0);
        }

        public void Jump(int dx) => _at = (_at.X + dx, _at.Y);

        public void Up(bool injected, bool overWindow) =>
            guard.OnMouse(injected, overWindow, _at, _at, leftButtonUp: true, T0);
    }
}
