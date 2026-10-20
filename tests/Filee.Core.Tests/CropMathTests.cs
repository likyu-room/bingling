// Tests for the geometry of the crop tool: ratios, the first box, refitting a box on another picture, pixels.

using Filee.Core.Presets;

namespace Filee.Core.Tests;

public class CropMathTests
{
    private static (double X, double Y, double Width, double Height) Tuple(CropRect r) => (r.X, r.Y, r.Width, r.Height);

    [Fact]
    public void Initial_box_is_the_largest_centred_box_of_the_ratio()
    {
        var r = CropMath.Initial(1.0, 400, 200);
        Assert.Equal((0.25, 0.0, 0.5, 1.0), Tuple(r));
        Assert.Equal(1.0, r.Aspect);
        var free = CropMath.Initial(null, 400, 200);
        Assert.Equal((0.0, 0.0, 1.0, 1.0), Tuple(free));
        Assert.Null(free.Aspect);
    }

    [Fact]
    public void Initial_box_of_a_wide_ratio_on_a_tall_picture_is_limited_by_the_width()
    {
        var r = CropMath.Initial(16.0 / 9, 900, 1600);
        Assert.Equal(1.0, r.Width, 9);
        Assert.Equal(900 / (16.0 / 9) / 1600, r.Height, 9);
        Assert.Equal(.5, r.Y + r.Height / 2, 9);
    }

    [Fact]
    public void Fixed_ratio_keeps_centre_and_width_and_recomputes_height_on_another_size()
    {
        var crop = new CropRect { X = .25, Y = 0, Width = .5, Height = 1, Aspect = 1 }; // square on 400×200
        var r = CropMath.Refit(crop, 200, 400);            // portrait image
        Assert.Equal(100, r.Width * 200, 3);               // 50 % of 200 px
        Assert.Equal(100, r.Height * 400, 3);              // square: 100 px high
        Assert.Equal(.5, r.Y + r.Height / 2, 3);           // same centre
    }

    [Fact]
    public void A_box_that_would_leave_the_image_is_pushed_back_inside()
    {
        var crop = new CropRect { X = .8, Y = .8, Width = .4, Height = .4, Aspect = 16.0 / 9 };
        var r = CropMath.Refit(crop, 1000, 1000);
        Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.Width <= 1 + 1e-9 && r.Y + r.Height <= 1 + 1e-9);
        Assert.Equal(16.0 / 9, r.Width * 1000 / (r.Height * 1000), 3);
    }

    [Fact]
    public void A_fixed_box_too_tall_for_the_picture_shrinks_to_fit_with_its_ratio()
    {
        // Full width of a wide picture, square: on a flat picture the square can't keep its width.
        var crop = new CropRect { X = 0, Y = 0, Width = 1, Height = 1, Aspect = 1 };
        var r = CropMath.Refit(crop, 400, 100);
        Assert.Equal(100, r.Width * 400, 3);
        Assert.Equal(100, r.Height * 100, 3);
        Assert.Equal(1.0, r.Height, 9);
    }

    [Fact]
    public void Free_boxes_keep_their_fractions_and_pixels_stay_inside()
    {
        var crop = new CropRect { X = .1, Y = .1, Width = .9, Height = .9 };
        Assert.Equal((.1, .1, .9, .9), Tuple(CropMath.Refit(crop, 300, 100)));
        var px = CropMath.ToPixels(crop, 3, 3);
        Assert.True(px.Width >= 1 && px.X + px.Width <= 3 && px.Y + px.Height <= 3);
    }

    [Fact]
    public void A_free_box_outside_the_picture_is_cut_to_it_and_never_empty()
    {
        var r = CropMath.Refit(new CropRect { X = -.2, Y = .5, Width = 3, Height = 2 }, 100, 100);
        Assert.Equal((0.0, .5, 1.0, .5), Tuple(r));
        var px = CropMath.ToPixels(new CropRect { X = 1, Y = 1, Width = 0, Height = 0 }, 10, 10);
        Assert.Equal((9, 9, 1, 1), px);
    }

    [Fact]
    public void A_ratio_that_is_not_positive_is_taken_as_a_free_box()
    {
        var r = CropMath.Refit(new CropRect { X = .1, Y = .1, Width = .5, Height = .5, Aspect = 0 }, 100, 50);
        Assert.Equal((.1, .1, .5, .5), Tuple(r));
    }

    [Fact]
    public void Pixels_are_rounded_and_the_result_keeps_the_ratio_within_a_pixel()
    {
        var crop = new CropRect { X = .25, Y = 0, Width = .5, Height = 1, Aspect = 1 };
        Assert.Equal((100, 0, 200, 200), CropMath.ToPixels(crop, 400, 200));
        var px = CropMath.ToPixels(crop, 401, 199);
        Assert.InRange(px.Width - px.Height, -1, 1);
        Assert.True(px.X >= 0 && px.Y >= 0 && px.X + px.Width <= 401 && px.Y + px.Height <= 199);
    }

    [Theory]
    [InlineData("free", null)]
    [InlineData("original", 2.0)]
    [InlineData("1:1", 1.0)]
    [InlineData("16:9", 16.0 / 9)]
    [InlineData("9:16", 9.0 / 16)]
    [InlineData("4:3", 4.0 / 3)]
    [InlineData("3:4", 0.75)]
    [InlineData("nonsense", null)]
    public void Ratio_ids_map_to_width_over_height(string id, double? expected) =>
        Assert.Equal(expected, CropMath.RatioOf(id, 400, 200));

    [Fact]
    public void Every_offered_ratio_id_is_understood()
    {
        Assert.Equal(["free", "original", "1:1", "16:9", "9:16", "4:3", "3:4"], CropMath.AspectIds);
        Assert.All(CropMath.AspectIds.Where(id => id != "free"), id => Assert.NotNull(CropMath.RatioOf(id, 400, 200)));
    }
}
