using Filee.Core.Settings;

namespace Filee.Core.Tests;

public class TriggerDefaultsTests
{
    [Fact]
    public void Windows_defaults_open_the_donut_with_shift_drag_first()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows defaults");
        var d = TriggerGesture.Defaults();
        Assert.Equal(3, d.Count);
        Assert.Equal((TriggerKind.Drag, ModifierKeys.Shift), (d[0].Kind, d[0].Modifiers));
        Assert.Equal((TriggerKind.Drag, ModifierKeys.Ctrl), (d[1].Kind, d[1].Modifiers));
        Assert.Equal(TriggerKind.KeyChord, d[2].Kind);
    }
}
