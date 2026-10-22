// What the "add background", "redact" and "annotate" tools put on a picture: boxes to hide, shapes and text to draw, and a
// backdrop to set it on. Part of ImageEdit; the engines draw them, these classes only say what and where.
//
// Every position and size is a fraction (0 to 1) of the picture after its EXIF orientation, rotation, flip and crop, so
// an edit made on the picture as it is shown means the same thing at any size. Widths and sizes that are not a box's
// own (a pen's thickness, a corner, a padding) are fractions of the picture's short side.

using System.Text.Json.Serialization;

namespace Filee.Core.Presets;

/// <summary>How a redacted box is hidden. A mode from a newer Filee reads as <see cref="Black"/>; new modes go at the end.</summary>
public enum RedactMode
{
    /// <summary>Covered with a black box.</summary>
    Black,

    /// <summary>Replaced by large blocks of the colours below it.</summary>
    Mosaic,

    /// <summary>Replaced by large blocks of the colours below it, and those blocks blurred: nothing finer than a block is left.</summary>
    Blur,
}

/// <summary>What an <see cref="Annotation"/> is. A kind from a newer Filee reads as <see cref="Pen"/>.</summary>
public enum AnnotationKind
{
    /// <summary>A free line along the points.</summary>
    Pen,

    /// <summary>A wide, see-through line along the points.</summary>
    Highlighter,

    /// <summary>A line with a head at its second point.</summary>
    Arrow,

    /// <summary>A straight line between two points.</summary>
    Line,

    /// <summary>An outlined box between two corners.</summary>
    Rectangle,

    /// <summary>An outlined ellipse in the box between two corners.</summary>
    Ellipse,

    /// <summary>A text at its anchor.</summary>
    Text,

    /// <summary>A numbered circle at its anchor.</summary>
    Number,
}

/// <summary>How thick an <see cref="Annotation"/> is drawn. A level from a newer Filee reads as <see cref="Thin"/>.</summary>
public enum StrokeLevel
{
    /// <summary>The thinnest lines and the smallest text and numbers.</summary>
    Thin,

    /// <summary>The middle size.</summary>
    Normal,

    /// <summary>The thickest lines and the largest text and numbers.</summary>
    Thick,
}

/// <summary>What <see cref="BackgroundEdit"/> sets the picture on. A kind from a newer Filee reads as <see cref="Gradient"/>.</summary>
public enum BackgroundKind
{
    /// <summary>One of the built-in gradients (<see cref="BackgroundEdit.Value"/> is its id).</summary>
    Gradient,

    /// <summary>One colour (<see cref="BackgroundEdit.Value"/> is <c>#RRGGBB</c>).</summary>
    Solid,

    /// <summary>A picture of the user's (<see cref="BackgroundEdit.ImagePath"/>).</summary>
    Image,
}

/// <summary>A point on the picture, as fractions (0 to 1) of its width and height.</summary>
public sealed class EditPoint
{
    /// <summary>Distance from the left edge, as a fraction of the width.</summary>
    public double X { get; set; }

    /// <summary>Distance from the top edge, as a fraction of the height.</summary>
    public double Y { get; set; }
}

/// <summary>A box on the picture, as fractions (0 to 1) of its width and height.</summary>
public sealed class EditRect
{
    /// <summary>Left edge, as a fraction of the width.</summary>
    public double X { get; set; }

    /// <summary>Top edge, as a fraction of the height.</summary>
    public double Y { get; set; }

    /// <summary>Width, as a fraction of the picture's width.</summary>
    public double Width { get; set; }

    /// <summary>Height, as a fraction of the picture's height.</summary>
    public double Height { get; set; }

    /// <summary>A copy of the box.</summary>
    public EditRect Clone() => (EditRect)MemberwiseClone();
}

/// <summary>The boxes of a picture that are hidden, and how.</summary>
public sealed class RedactEdit
{
    /// <summary>How every box is hidden.</summary>
    [JsonConverter(typeof(LenientEnumJsonConverter<RedactMode>))]
    public RedactMode Mode { get; set; }

    /// <summary>What to hide.</summary>
    public List<EditRect> Boxes { get; set; } = [];

    /// <summary>A deep copy: its boxes are copies too, so changing one changes nothing here.</summary>
    public RedactEdit Clone() => new() { Mode = Mode, Boxes = [.. Boxes.Select(b => b.Clone())] };
}

/// <summary>One thing drawn on the picture: a line, a shape, a text or a number.</summary>
public sealed class Annotation
{
    /// <summary>What it is; it decides what <see cref="Points"/> mean.</summary>
    [JsonConverter(typeof(LenientEnumJsonConverter<AnnotationKind>))]
    public AnnotationKind Kind { get; set; }

    /// <summary>The colour, <c>#RRGGBB</c>.</summary>
    public string Color { get; set; } = "#FF3B30";

    /// <summary>How thick the lines are; the size of a text or a number follows it.</summary>
    [JsonConverter(typeof(LenientEnumJsonConverter<StrokeLevel>))]
    public StrokeLevel Width { get; set; } = StrokeLevel.Normal;

    /// <summary>
    /// Pen and highlighter: the path. Arrow and line: from, to. Rectangle and ellipse: two opposite corners. Text and
    /// number: the anchor (one point).
    /// </summary>
    public List<EditPoint> Points { get; set; } = [];

    /// <summary>The words of a <see cref="AnnotationKind.Text"/>; null for the others.</summary>
    public string? Text { get; set; }

    /// <summary>The figure in a <see cref="AnnotationKind.Number"/>; 0 for the others.</summary>
    public int Number { get; set; }

    /// <summary>A deep copy: its points are copies too, so moving one moves nothing here.</summary>
    public Annotation Clone()
    {
        var copy = (Annotation)MemberwiseClone();
        copy.Points = [.. Points.Select(p => new EditPoint { X = p.X, Y = p.Y })];
        return copy;
    }
}

/// <summary>The backdrop a picture is set on, with room around it, rounded corners and a shadow.</summary>
public sealed class BackgroundEdit
{
    /// <summary>What <see cref="Value"/> and <see cref="ImagePath"/> mean.</summary>
    [JsonConverter(typeof(LenientEnumJsonConverter<BackgroundKind>))]
    public BackgroundKind Kind { get; set; }

    /// <summary>A gradient's id or a colour, <c>#RRGGBB</c>, by <see cref="Kind"/>.</summary>
    public string Value { get; set; } = "peach";

    /// <summary>The backdrop picture when <see cref="Kind"/> is <see cref="BackgroundKind.Image"/>.</summary>
    public string? ImagePath { get; set; }

    /// <summary>Room around the picture, 0 to 0.40 of its short side.</summary>
    public double Padding { get; set; } = 0.08;

    /// <summary>Corner radius of the picture, 0 to 0.25 of its short side.</summary>
    public double Corner { get; set; } = 0.03;

    /// <summary>How strong the shadow under the picture is, 0 to 100.</summary>
    public int Shadow { get; set; } = 40;

    /// <summary>The shape of the result: <c>auto</c> (the picture and its padding), <c>1:1</c>, <c>16:9</c>, <c>9:16</c>, <c>4:5</c> or <c>4:3</c>.</summary>
    public string Aspect { get; set; } = "auto";

    /// <summary>A copy of the backdrop settings.</summary>
    public BackgroundEdit Clone() => (BackgroundEdit)MemberwiseClone();
}
