// The image tool window: puts in the tool's picture view and panel, opens near the cursor at the size of the picture,
// and turns the keys into the view model's commands (Enter, Esc, undo, zoom, files); the tool's own keys go to it.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.ViewModels.Tools;
using Filee.App.Views.Tools;
using Filee.Core.Presets;
using SkiaSharp;

namespace Filee.App.Views;

/// <summary>Edits pictures with an image tool; closes on Save or Cancel (the result is the view model's).</summary>
public partial class ImageToolWindow : Window
{
    /// <summary>Width of the tool's panel (as in the XAML).</summary>
    private const double PanelWidth = 260;

    /// <summary>What the window needs besides the picture: the panel, the top and bottom bars, the room around it.</summary>
    internal static readonly Size Chrome = new(PanelWidth + 2 * PictureView.Inset, 110 + 2 * PictureView.Inset);

    /// <summary>The smallest window (as MinWidth / MinHeight in the XAML), unless the screen is smaller (<see cref="Placement"/>).</summary>
    internal static readonly Size Smallest = new(720, 480);

    private ImageToolViewModel? _model;

    /// <summary>The tool's picture view: zoom, pan and the keys go to it.</summary>
    private PictureView? _picture;

    /// <summary>The view model is done (Save, or Cancel agreed to): closing asks nothing more.</summary>
    private bool _finished;

    /// <summary>A close is being decided in <see cref="OnClosing"/>: Finished must not start another one.</summary>
    private bool _deciding;

    public ImageToolWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        // Space let go in another window never reaches this one: drags would keep panning.
        Deactivated += (_, _) => _picture?.PanKeyDown = false;
        // Undo, redo and fit take no focus; a press on them hands it to the picture first, which ends a text half typed (it goes
        // on, and the undo takes it) and takes a size typed in the panel. Tunnelled and also when handled: the buttons take the press.
        HistoryButtons.AddHandler(PointerPressedEvent, (_, _) => _picture?.Focus(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>The fit button: the whole picture again, after the wheel zoomed it or Space + drag moved it.</summary>
    private void OnFit(object? sender, RoutedEventArgs e) => _picture?.Fit();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.Finished -= OnFinished;
            _model.FileLeaving -= OnFileLeaving;
        }
        _model = DataContext as ImageToolViewModel;
        if (_model is null)
            return;
        _model.Finished += OnFinished;
        _model.FileLeaving += OnFileLeaving;
        // Each tool its own view and panel (Views/Tools); a tool without them never gets here (ImageToolViewModel.Create).
        (ToolPanel.Content, _picture) = _model switch
        {
            CropToolViewModel => (new CropPanel(), CropCanvas()),
            BackgroundToolViewModel => (new BackgroundPanel(), BackdropCanvas()),
            RedactToolViewModel => (new RedactPanel(), BoxesCanvas()),
            AnnotateToolViewModel => (new AnnotatePanel(), DrawingCanvas()),
            _ => ((Control?, PictureView?))(null, null),
        };
        PictureHost.Child = _picture;
    }

    /// <summary>The crop box over the preview bitmap; a double click in it saves.</summary>
    private ImageCanvas CropCanvas()
    {
        var canvas = new ImageCanvas
        {
            [!ImageCanvas.SourceProperty] = CompiledBinding.Create<ImageToolViewModel, Avalonia.Media.Imaging.Bitmap?>(vm => vm.Preview),
            [!ImageCanvas.CropProperty] = CompiledBinding.Create<CropToolViewModel, CropRect>(vm => vm.Crop),
        };
        canvas.BoxDoubleClicked += (_, _) => _model?.SaveCommand.Execute(null);
        return canvas;
    }

    /// <summary>The result (the picture on its backdrop) over the preview picture; nothing is dragged on it.</summary>
    private BackgroundCanvas BackdropCanvas() => new()
    {
        [!PictureView.PictureProperty] = CompiledBinding.Create<ImageToolViewModel, SKImage?>(vm => vm.PreviewImage),
        [!PictureView.FullShortSideProperty] = CompiledBinding.Create<ImageToolViewModel, int>(vm => vm.FullShortSide),
        [!PictureView.EditProperty] = CompiledBinding.Create<BackgroundToolViewModel, ImageEdit?>(vm => vm.PreviewEdit),
    };

    /// <summary>The preview picture with the redact boxes, which are drawn, picked, moved and resized on it.</summary>
    private RedactCanvas BoxesCanvas() => new()
    {
        [!PictureView.PictureProperty] = CompiledBinding.Create<ImageToolViewModel, SKImage?>(vm => vm.PreviewImage),
        [!PictureView.FullShortSideProperty] = CompiledBinding.Create<ImageToolViewModel, int>(vm => vm.FullShortSide),
        [!RedactCanvas.RedactProperty] = CompiledBinding.Create<RedactToolViewModel, RedactEdit>(vm => vm.Redact),
        [!RedactCanvas.SelectedIndexProperty] = CompiledBinding.Create<RedactToolViewModel, int>(vm => vm.SelectedIndex),
    };

    /// <summary>The preview picture with the file's drawings, which are drawn, picked, moved, retyped and deleted on it.</summary>
    private AnnotateCanvas DrawingCanvas() => new()
    {
        [!PictureView.PictureProperty] = CompiledBinding.Create<ImageToolViewModel, SKImage?>(vm => vm.PreviewImage),
        [!PictureView.FullShortSideProperty] = CompiledBinding.Create<ImageToolViewModel, int>(vm => vm.FullShortSide),
        [!AnnotateCanvas.AnnotationsProperty] = CompiledBinding.Create<AnnotateToolViewModel, IReadOnlyList<Annotation>>(vm => vm.Annotations),
        [!AnnotateCanvas.SelectedIndexProperty] = CompiledBinding.Create<AnnotateToolViewModel, int>(vm => vm.SelectedIndex),
        [!AnnotateCanvas.ToolProperty] = CompiledBinding.Create<AnnotateToolViewModel, AnnotationKind?>(vm => vm.DrawingTool),
        [!AnnotateCanvas.ColorProperty] = CompiledBinding.Create<AnnotateToolViewModel, string>(vm => vm.Color),
        [!AnnotateCanvas.StrokeWidthProperty] = CompiledBinding.Create<AnnotateToolViewModel, StrokeLevel>(vm => vm.StrokeWidth),
    };

    private void OnFinished(object? sender, EventArgs e)
    {
        _finished = true;
        if (!_deciding)
            Close();
    }

    /// <summary>‹ ›, ← / → (or anything else) is about to show another file: the picture view ends its work on this one first.</summary>
    private void OnFileLeaving(object? sender, EventArgs e) => _picture?.LeaveFile();

    /// <summary>
    /// The title bar's X, Alt+F4 or a close from code: the same as Cancel, which may ask first (annotate, with drawings).
    /// While it asks, the window stays; its "yes" finishes the view model, and that closes the window again. A close after
    /// Save, or by the app or the system shutting down, asks nothing.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_finished || e.Cancel || _model is null || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;
        _deciding = true;
        try
        {
            _model.CancelCommand.Execute(null); // done at once when there is nothing to ask
        }
        finally
        {
            _deciding = false;
        }
        e.Cancel = !_finished;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // The donut's window never takes the focus, so nothing hands it over: take it, for the keys.
        Activate();
        _picture?.Focus();
    }

    /// <summary>Places the window on the screen under <paramref name="cursor"/>, sized to the picture shown.</summary>
    public void PlaceNear(PixelPoint cursor)
    {
        if (_model is null || (Screens.ScreenFromPoint(cursor) ?? Screens.Primary) is not { } screen)
            return;
        var (size, smallest, position) = Placement(screen.WorkingArea, screen.Scaling, cursor, new PixelSize(_model.ImageWidth, _model.ImageHeight));
        // The XAML's smallest size would win over the size placed here on a small screen and push the bottom bar off it.
        (MinWidth, MinHeight) = (smallest.Width, smallest.Height);
        (Width, Height) = (size.Width, size.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = position;
    }

    /// <summary>
    /// The window's size and smallest size (DIPs) and position (pixels) for a <paramref name="picture"/> of this size: the
    /// picture at its own size when that fits, smaller when not, the window at most 80 % of the working
    /// <paramref name="area"/> (its smallest size too: a 1920 × 1040 screen at 250 % is only 768 × 416), centred on the
    /// cursor and kept on that screen.
    /// </summary>
    internal static (Size Size, Size Smallest, PixelPoint Position) Placement(PixelRect area, double scaling, PixelPoint cursor, PixelSize picture)
    {
        var max = new Size(area.Width * .8 / scaling, area.Height * .8 / scaling);
        var smallest = new Size(Math.Min(Smallest.Width, max.Width), Math.Min(Smallest.Height, max.Height));
        // DIPs per pixel of the picture: one screen pixel each, or less to fit.
        var scale = Math.Min(1 / scaling, Math.Min((max.Width - Chrome.Width) / picture.Width, (max.Height - Chrome.Height) / picture.Height));
        var size = new Size(
            Math.Min(max.Width, Math.Max(smallest.Width, picture.Width * scale + Chrome.Width)),
            Math.Min(max.Height, Math.Max(smallest.Height, picture.Height * scale + Chrome.Height)));
        var (w, h) = ((int)(size.Width * scaling), (int)(size.Height * scaling));
        var x = Math.Clamp(cursor.X - w / 2, area.X, Math.Max(area.X, area.Right - w));
        var y = Math.Clamp(cursor.Y - h / 2, area.Y, Math.Max(area.Y, area.Bottom - h));
        return (size, smallest, new PixelPoint(x, y));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _model is not { } vm)
            return;
        // A text box keeps the keys for typing: ← / → move the caret there, digits are typed.
        var typing = FocusManager?.GetFocusedElement() as TextBox;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var plain = typing is null && e.KeyModifiers == KeyModifiers.None;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Enter when typing is not null:
                // A typed size is taken first; the next Enter saves.
                BindingOperations.GetBindingExpressionBase(typing, TextBox.TextProperty)?.UpdateSource();
                _picture?.Focus();
                break;
            case Key.Enter:
                vm.SaveCommand.Execute(null);
                break;
            case Key.Escape:
                vm.CancelCommand.Execute(null);
                break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                vm.RedoCommand.Execute(null);
                break;
            case Key.Z when ctrl:
                vm.UndoCommand.Execute(null);
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                _picture?.Fit();
                break;
            case Key.D1 or Key.NumPad1 when ctrl:
                _picture?.ShowActualSize(vm.ImageWidth);
                break;
            case Key.Left when plain:
                vm.PreviousCommand.Execute(null);
                break;
            case Key.Right when plain:
                vm.NextCommand.Execute(null);
                break;
            case Key.Space when typing is null:
                _picture?.PanKeyDown = true;
                break;
            default:
                // The tool's own keys (crop: 1 to 7), unless they are being typed.
                e.Handled = typing is null && vm.HandleKey(e.Key, e.KeyModifiers);
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
            _picture?.PanKeyDown = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_model is not null)
        {
            _model.Finished -= OnFinished;
            _model.FileLeaving -= OnFileLeaving;
        }
        base.OnClosed(e);
    }
}
