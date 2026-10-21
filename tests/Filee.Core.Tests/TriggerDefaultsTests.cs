using Filee.Core.Settings;

namespace Filee.Core.Tests;

public class TriggerDefaultsTests
{
    [Fact]
    public void Windows_defaults_keep_shift_drag_but_switched_off()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows defaults");
        var d = TriggerGesture.Defaults();
        Assert.Equal(3, d.Count);
        Assert.Equal((TriggerKind.Drag, ModifierKeys.Ctrl, true), (d[0].Kind, d[0].Modifiers, d[0].Enabled));
        Assert.Equal(TriggerKind.KeyChord, d[1].Kind);
        Assert.Equal((TriggerKind.Drag, ModifierKeys.Shift, false), (d[2].Kind, d[2].Modifiers, d[2].Enabled));
    }
}
