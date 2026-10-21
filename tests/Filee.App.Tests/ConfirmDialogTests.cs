// The yes / no question WindowService asks: its buttons say "OK" and "Cancel" unless the caller names them, so that
// "throw the drawings away?" can answer "Discard" and "Keep drawing" instead of a second, confusing "Cancel".

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Filee.App.Services;
using Filee.Core.Localization;

namespace Filee.App.Tests;

public class ConfirmDialogTests
{
    private static (WindowService Service, Window Owner) Start()
    {
        TestServices.EnsureInitialized("ko");
        var service = new WindowService(AppHost.Get<ILocalizer>(), AppHost.Get<IServiceProvider>(), AppHost.Get<MemoryTrimmer>());
        var owner = new Window { Width = 500, Height = 300 };
        owner.Show();
        return (service, owner);
    }

    private static Window Dialog(Window owner)
    {
        Dispatcher.UIThread.RunJobs();
        return Assert.Single(owner.OwnedWindows);
    }

    [AvaloniaFact]
    public void A_question_with_named_buttons_shows_them_and_answers_by_them()
    {
        var (service, owner) = Start();
        var answer = service.ConfirmAsync(owner, "그린 내용을 버리고 닫을까요?", yes: "버리기", no: "계속 그리기");
        var dialog = Dialog(owner);

        // "Keep drawing" on the left of "Discard", as the default (Enter) button is the one on the right.
        var buttons = dialog.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(["계속 그리기", "버리기"], buttons.Select(b => b.Content));
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "그린 내용을 버리고 닫을까요?");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (var frame = dialog.CaptureRenderedFrame()!)
        using (var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, "tool-discard-ko.png")))
            frame.Save(stream, new PngBitmapEncoderOptions());

        buttons.Single(b => Equals(b.Content, "계속 그리기")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(answer.IsCompleted);
        Assert.False(answer.Result);
        owner.Close();
    }

    [AvaloniaFact]
    public void Discard_answers_yes()
    {
        var (service, owner) = Start();
        var answer = service.ConfirmAsync(owner, "?", yes: "버리기", no: "계속 그리기");
        var discard = Dialog(owner).GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "버리기"));
        discard.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(answer.IsCompleted);
        Assert.True(answer.Result);
        owner.Close();
    }

    [AvaloniaFact]
    public void Without_names_the_buttons_are_OK_and_Cancel_as_before_and_Delete_when_destructive()
    {
        var (service, owner) = Start();
        _ = service.ConfirmAsync(owner, "?");
        var dialog = Dialog(owner);
        Assert.Equal(["취소", "확인"], dialog.GetVisualDescendants().OfType<Button>().Select(b => b.Content));
        dialog.Close(false);
        Dispatcher.UIThread.RunJobs();

        _ = service.ConfirmAsync(owner, "?", destructive: true);
        dialog = Dialog(owner);
        Assert.Equal(["취소", "삭제"], dialog.GetVisualDescendants().OfType<Button>().Select(b => b.Content));
        dialog.Close(false);
        Dispatcher.UIThread.RunJobs();
        owner.Close();
    }
}
