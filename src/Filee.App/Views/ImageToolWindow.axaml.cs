// The image tool window: picks the tool's panel, opens near the cursor at the size of the picture, and turns the
// keys into the view model's commands (Enter, Esc, undo, zoom, files, ratios).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views.Tools;
using Filee.Core.Presets;

namespace Filee.App.Views;

/// <summary>Edits pictures with an image tool; closes on Save or Cancel (the result is the view model's).</summary>
public partial class ImageToolWindow : Window
{
    /// <summary>Width of the tool's panel (as in the XAML).</summary>
    private const double PanelWidth = 260;

    /// <summary>What the window needs besides the picture: the panel, the top and bottom bars, the room around it.</summary>
    internal static readonly Size Chrome = new(PanelWidth + 2 * ImageCanvas.Inset, 110 + 2 * ImageCanvas.Inset);

    /// <summary>The smallest window (as MinWidth / MinHeight in the XAML).</summary>
    internal static readonly Size Smallest = new(720, 480);

    private ImageToolViewModel? _model;

    public ImageToolWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        Picture.BoxDoubleClicked += (_, _) => _model?.SaveCommand.Execute(null);
        // Space let go in another window never reaches this one: drags would keep panning.
        Deactivated += (_, _) => Picture.PanKeyDown = false;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
            _model.Finished -= OnFinished;
        _model = DataContext as ImageToolViewModel;
        if (_model is null)
            return;
        _model.Finished += OnFinished;
        ToolPanel.Content = _model.Tool switch
        {
            ImageToolKind.Crop => new CropPanel(),
            _ => null,
        };
    }

    private void OnFinished(object? sender, EventArgs e) => Close();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // The donut's window never takes the focus, so nothing hands it over: take it, for the keys.
        Activate();
        Picture.Focus();
    }

    /// <summary>Places the window on the screen under <paramref name="cursor"/>, sized to the picture shown.</summary>
    public void PlaceNear(PixelPoint cursor)
    {
        if (_model is null || (Screens.ScreenFromPoint(cursor) ?? Screens.Primary) is not { } screen)
            return;
        var (size, position) = Placement(screen.WorkingArea, screen.Scaling, cursor, new PixelSize(_model.ImageWidth, _model.ImageHeight));
        (Width, Height) = (size.Width, size.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = position;
    }

    /// <summary>
    /// The window's size (DIPs) and position (pixels) for a <paramref name="picture"/> of this size: the picture at
    /// its own size when that fits, smaller when not, the window at most 80 % of the working <paramref name="area"/>,
    /// centred on the cursor and kept on that screen.
    /// </summary>
    internal static (Size Size, PixelPoint Position) Placement(PixelRect area, double scaling, PixelPoint cursor, PixelSize picture)
    {
        var max = new Size(area.Width * .8 / scaling, area.Height * .8 / scaling);
        // DIPs per pixel of the picture: one screen pixel each, or less to fit.
        var scale = Math.Min(1 / scaling, Math.Min((max.Width - Chrome.Width) / picture.Width, (max.Height - Chrome.Height) / picture.Height));
        var size = new Size(
            Math.Min(max.Width, Math.Max(Smallest.Width, picture.Width * scale + Chrome.Width)),
            Math.Min(max.Height, Math.Max(Smallest.Height, picture.Height * scale + Chrome.Height)));
        var (w, h) = ((int)(size.Width * scaling), (int)(size.Height * scaling));
        var x = Math.Clamp(cursor.X - w / 2, area.X, Math.Max(area.X, area.Right - w));
        var y = Math.Clamp(cursor.Y - h / 2, area.Y, Math.Max(area.Y, area.Bottom - h));
        return (size, new PixelPoint(x, y));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _model is not { } vm)
            return;
        // A size box keeps the keys for typing: ← / → move the caret there, digits are typed.
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
                Picture.Focus();
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
                Picture.Fit();
                break;
            case Key.D1 or Key.NumPad1 when ctrl:
                Picture.ShowActualSize(vm.ImageWidth);
                break;
            case Key.Left when plain:
                vm.PreviousCommand.Execute(null);
                break;
            case Key.Right when plain:
                vm.NextCommand.Execute(null);
                break;
            case >= Key.D1 and <= Key.D7 when plain:
                vm.Aspect = vm.Aspects[e.Key - Key.D1];
                break;
            case >= Key.NumPad1 and <= Key.NumPad7 when plain:
                vm.Aspect = vm.Aspects[e.Key - Key.NumPad1];
                break;
            case Key.Space when typing is null:
                Picture.PanKeyDown = true;
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
            Picture.PanKeyDown = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_model is not null)
            _model.Finished -= OnFinished;
        base.OnClosed(e);
    }
}
