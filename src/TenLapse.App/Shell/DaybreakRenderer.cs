using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using TenLapse.Presentation.FirstLaunch;
using Windows.UI;
using DrawingColor = System.Drawing.Color;

namespace TenLapse.Shell;

/// <summary>
/// Draws the Daybreak harbor (#29) with Win2D from <see cref="DaybreakScene"/>'s numbers: the frame at one phase,
/// and the strip of thumbnails with the selected one ringed. Stateless; all sizes in DIPs.
/// </summary>
internal static class DaybreakRenderer
{
    /// <summary>The frame's corners (RadiusOverlay, the prototype's 8 px).</summary>
    public const float FrameRadius = 8;

    /// <summary>A thumbnail's corners (RadiusThumb).</summary>
    public const float ThumbRadius = 3;

    /// <summary>The selection ring's width outside the thumbnail.</summary>
    public const float RingWidth = 2;

    private const float GlintRadius = 2;

    /// <summary>The harbor at <paramref name="sky"/>, filling <paramref name="width"/> by <paramref name="height"/> with rounded corners.</summary>
    public static void DrawFrame(ICanvasResourceCreator creator, CanvasDrawingSession session, float width, float height, DaybreakSky sky, DaybreakPalette palette)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        using var clip = CanvasGeometry.CreateRoundedRectangle(creator, 0, 0, width, height, FrameRadius, FrameRadius);
        using var layer = session.CreateLayer(1f, clip);

        var (middle, horizon) = DaybreakScene.SkyGradient;
        using (var skyBrush = Vertical(creator, 0, height, (0, sky.Top), ((float)middle, sky.Middle), ((float)horizon, sky.Horizon)))
        {
            session.FillRectangle(0, 0, width, height, skyBrush);
        }

        DrawSun(creator, session, width, height, sky);
        DrawStars(session, width, height, sky, palette);

        var (bandTop, bandHeight) = DaybreakScene.LandBand;
        var top = (float)(bandTop * height);
        var band = (float)(bandHeight * height);
        using (var skyline = CanvasGeometry.CreatePolygon(creator, [.. DaybreakScene.Skyline.Select(point => new Vector2(point.X * width, top + (point.Y * band)))]))
        {
            session.FillGeometry(skyline, Win(sky.Land));
            if (sky.LightsOpacity > 0)
            {
                using var lit = session.CreateLayer((float)sky.LightsOpacity, skyline);
                var radius = DaybreakScene.LightRadius;
                foreach (var (center, pale) in DaybreakScene.Lights)
                {
                    session.FillEllipse(new Vector2(center.X * width, top + (center.Y * band)), radius.X, radius.Y, Win(pale ? palette.LightPale : palette.Light));
                }
            }
        }

        var waterTop = (float)(DaybreakScene.WaterTop * height);
        using (var water = Vertical(creator, waterTop, height, (0, sky.WaterTop), (1, sky.WaterBottom)))
        {
            session.FillRectangle(0, waterTop, width, height - waterTop, water);
        }

        if (sky.GlintOpacity > 0)
        {
            var (x, y, w, h) = DaybreakScene.Glint;
            var glintTop = (float)(y * height);
            using var glint = Vertical(creator, glintTop, glintTop + (float)(h * height), (0, sky.SunGlow), (1, Clear(sky.SunGlow)));
            glint.Opacity = (float)sky.GlintOpacity;
            session.FillRoundedRectangle((float)(x * width), glintTop, (float)(w * width), (float)(h * height), GlintRadius, GlintRadius, glint);
        }
    }

    /// <summary>
    /// The strip: twelve thumbnails with <paramref name="gap"/> between them, each <paramref name="thumbWidth"/> by
    /// <paramref name="thumbHeight"/>, starting at (<paramref name="inset"/>, <paramref name="inset"/>) so the ring
    /// around <paramref name="selected"/> fits; the ring in <paramref name="ring"/>.
    /// </summary>
    public static void DrawStrip(ICanvasResourceCreator creator, CanvasDrawingSession session, float inset, float thumbWidth, float thumbHeight, float gap, int selected, Color ring, DaybreakPalette palette)
    {
        if (thumbWidth <= 0 || thumbHeight <= 0)
        {
            return;
        }

        for (var i = 0; i < Daybreak.ThumbCount; i++)
        {
            var x = inset + (i * (thumbWidth + gap));
            var (top, horizon, land) = palette.ThumbAt(Daybreak.ThumbPhase(i));
            var split = (float)DaybreakScene.ThumbHorizon;
            using var brush = Vertical(creator, inset, inset + thumbHeight, (0, top), (split, horizon), (split, land), (1, land));
            session.FillRoundedRectangle(x, inset, thumbWidth, thumbHeight, ThumbRadius, ThumbRadius, brush);

            if (i == selected)
            {
                // A 2 px ring right outside the thumbnail (the prototype's 0 0 0 2px box shadow).
                var half = RingWidth / 2;
                session.DrawRoundedRectangle(x - half, inset - half, thumbWidth + RingWidth, thumbHeight + RingWidth, ThumbRadius + half, ThumbRadius + half, ring, RingWidth);
            }
        }
    }

    private static void DrawSun(ICanvasResourceCreator creator, CanvasDrawingSession session, float width, float height, DaybreakSky sky)
    {
        if (sky.SunOpacity <= 0)
        {
            return;
        }

        var (x, diameter) = DaybreakScene.Sun;
        var (core, glow, clear, reach) = DaybreakScene.SunGradient;
        var size = (float)(diameter * width);
        var center = new Vector2((float)(x * width), (float)(sky.SunY * height));
        using var brush = new CanvasRadialGradientBrush(creator,
        [
            new CanvasGradientStop { Position = 0, Color = Win(sky.Sun) },
            new CanvasGradientStop { Position = (float)core, Color = Win(sky.Sun) },
            new CanvasGradientStop { Position = (float)glow, Color = Win(sky.SunGlow) },
            new CanvasGradientStop { Position = (float)clear, Color = Win(Clear(sky.SunGlow)) },
        ])
        {
            Center = center,
            RadiusX = (float)(reach * size),
            RadiusY = (float)(reach * size),
            Opacity = (float)sky.SunOpacity,
        };
        session.FillCircle(center, size / 2, brush);
    }

    private static void DrawStars(CanvasDrawingSession session, float width, float height, DaybreakSky sky, DaybreakPalette palette)
    {
        if (sky.StarsOpacity <= 0)
        {
            return;
        }

        var band = (float)(DaybreakScene.StarBand * height);
        var star = Win(palette.Star);
        star.A = (byte)Math.Round(star.A * sky.StarsOpacity);
        foreach (var (center, radius) in DaybreakScene.Stars)
        {
            session.FillCircle(new Vector2(center.X * width, center.Y * band), radius, star);
        }
    }

    private static CanvasLinearGradientBrush Vertical(ICanvasResourceCreator creator, float from, float to, params (float Position, DrawingColor Color)[] stops) =>
        Vertical(creator, from, to, [.. stops.Select(stop => (stop.Position, Win(stop.Color)))]);

    private static CanvasLinearGradientBrush Vertical(ICanvasResourceCreator creator, float from, float to, (float Position, Color Color)[] stops) =>
        new(creator, [.. stops.Select(stop => new CanvasGradientStop { Position = stop.Position, Color = stop.Color })])
        {
            StartPoint = new Vector2(0, from),
            EndPoint = new Vector2(0, to),
        };

    private static Color Win(DrawingColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    private static DrawingColor Clear(DrawingColor color) => DrawingColor.FromArgb(0, color);
}
