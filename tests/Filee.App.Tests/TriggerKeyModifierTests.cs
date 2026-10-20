using Filee.App.Services.Triggers;
using Filee.Core.Settings;
using SharpHook.Data;

namespace Filee.App.Tests;

public class TriggerKeyModifierTests
{
    [Theory]
    [InlineData(KeyCode.VcLeftShift, ModifierKeys.Shift)]
    [InlineData(KeyCode.VcRightShift, ModifierKeys.Shift)]
    [InlineData(KeyCode.VcLeftControl, ModifierKeys.Ctrl)]
    [InlineData(KeyCode.VcRightControl, ModifierKeys.Ctrl)]
    [InlineData(KeyCode.VcLeftAlt, ModifierKeys.Alt)]
    [InlineData(KeyCode.VcRightAlt, ModifierKeys.Alt)]
    [InlineData(KeyCode.VcSpace, ModifierKeys.None)]
    public void A_modifier_key_names_the_modifier_it_adds(KeyCode key, ModifierKeys expected) =>
        Assert.Equal(expected, TriggerService.KeyModifier(key));

    [Fact]
    public void The_modifier_key_is_added_even_when_the_hook_mask_misses_it() =>
        Assert.Equal(ModifierKeys.Shift, TriggerService.Map(EventMask.None) | TriggerService.KeyModifier(KeyCode.VcLeftShift));
}
