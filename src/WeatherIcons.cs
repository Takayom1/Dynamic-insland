// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Skin-aware vector weather icons on a 24×24 grid across dark glass, matte Shittim tablet,
/// outlined Chibi Pop cartoon stickers, and aliased Pixel Tamagotchi LCD matrix modes.
/// </summary>
public static class WeatherIcons
{
    private static readonly Brush SunGold = Frozen(Color.FromRgb(0xFF, 0xD6, 0x0A));
    private static readonly Brush SunAmber = Frozen(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush SunSticker = Frozen(Color.FromRgb(0xFF, 0xE0, 0x66));
    private static readonly Brush MoonLight = Frozen(Color.FromRgb(0xE5, 0xE5, 0xEA));
    private static readonly Brush MoonSlate = Frozen(Color.FromRgb(0x4A, 0x6D, 0x94));
    private static readonly Brush CloudWhite = Frozen(Color.FromRgb(0xF6, 0xFA, 0xFF));
    private static readonly Brush CloudGray = Frozen(Color.FromRgb(0xD2, 0xDC, 0xE8));
    private static readonly Brush CloudSticker = Frozen(Color.FromRgb(0xE8, 0xF6, 0xFF));
    private static readonly Brush ShittimCloudStroke = Frozen(Color.FromRgb(0x36, 0x8C, 0xC8));
    private static readonly Brush StickerInk = Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E));
    private static readonly Brush Water = Frozen(Color.FromRgb(0x38, 0xBD, 0xF8));
    private static readonly Brush PixelInk = Frozen(Color.FromRgb(0x14, 0x2D, 0x2A));

    private const string CloudPath = "M7,19 H17.5 A4.5,4.5 0 0 0 17.5,10 A6,6 0 0 0 6.2,11.6 A3.75,3.75 0 0 0 7,19 Z";
    private const string SunRays = "M12,1.5 V4 M12,20 V22.5 M1.5,12 H4 M20,12 H22.5 M4.6,4.6 L6.3,6.3 M17.7,17.7 L19.4,19.4 " +
                                   "M4.6,19.4 L6.3,17.7 M17.7,6.3 L19.4,4.6";

    public static FrameworkElement Create(WeatherKind kind, bool isDay, double size)
    {
        var spec = IslandSkinEngine.Current;
        if (spec.IsPixelMode)
            return CreatePixel(kind, isDay, size);

        var canvas = new Canvas { Width = 24, Height = 24 };
        var isSticker = spec.IsCartoonSticker;
        var isLight = spec.IsLight;

        var sunFill = isSticker ? SunSticker : isLight ? SunAmber : SunGold;
        var sunStroke = isSticker ? StickerInk : null;
        var cloudFill = isSticker ? CloudSticker : CloudWhite;
        var cloudOvercast = isSticker ? Frozen(Color.FromRgb(0xD8, 0xE8, 0xF6)) : CloudGray;
        var cloudStroke = isSticker ? StickerInk : isLight ? ShittimCloudStroke : Frozen(Color.FromArgb(0x55, 0x83, 0xCF, 0xF3));
        var cloudThickness = isSticker ? 1.75 : isLight ? 1.4 : 0.9;

        switch (kind)
        {
            case WeatherKind.Clear:
                AddSky(canvas, isDay, 1, 0, 0, sunFill, sunStroke, isLight);
                break;
            case WeatherKind.PartlyCloudy:
                AddSky(canvas, isDay, 0.64, -3.2, -3.4, sunFill, sunStroke, isLight);
                Add(canvas, FillAndStroke(CloudPath, cloudFill, cloudStroke, cloudThickness), 1.5, 1.5);
                break;
            case WeatherKind.Cloudy:
                Add(canvas, FillAndStroke(CloudPath, cloudOvercast, cloudStroke, cloudThickness), 0, -1);
                break;
            case WeatherKind.Fog:
                canvas.Children.Add(Stroke("M4,9 H20 M6,13 H18 M4,17 H20", isSticker ? StickerInk : isLight ? ShittimCloudStroke : CloudGray, 2));
                break;
            case WeatherKind.Drizzle:
                Add(canvas, FillAndStroke(CloudPath, cloudOvercast, cloudStroke, cloudThickness), 0, -4);
                canvas.Children.Add(Stroke("M9,18.5 L8.5,20 M15,18.5 L14.5,20 M12,20.5 L11.5,22", isSticker ? StickerInk : Water, 1.7));
                break;
            case WeatherKind.Rain:
                Add(canvas, FillAndStroke(CloudPath, cloudOvercast, cloudStroke, cloudThickness), 0, -4);
                canvas.Children.Add(Stroke("M8.5,18 L7.3,21.5 M12.5,18 L11.3,21.5 M16.5,18 L15.3,21.5", Water, 1.8));
                break;
            case WeatherKind.Snow:
                Add(canvas, FillAndStroke(CloudPath, cloudOvercast, cloudStroke, cloudThickness), 0, -4);
                canvas.Children.Add(FillAndStroke(
                    "M8,19 m-1.4,0 a1.4,1.4 0 1 0 2.8,0 a1.4,1.4 0 1 0 -2.8,0 " +
                    "M12,21.5 m-1.4,0 a1.4,1.4 0 1 0 2.8,0 a1.4,1.4 0 1 0 -2.8,0 " +
                    "M16,19 m-1.4,0 a1.4,1.4 0 1 0 2.8,0 a1.4,1.4 0 1 0 -2.8,0",
                    Brushes.White, isLight ? cloudStroke : null, isLight ? 1.0 : 0));
                break;
            case WeatherKind.Thunder:
                Add(canvas, FillAndStroke(CloudPath, cloudOvercast, cloudStroke, cloudThickness), 0, -4);
                canvas.Children.Add(FillAndStroke("M12.8,15 L9.6,19.6 H12 L10.6,23.2 L15.2,17.8 H12.6 L14.2,15 Z",
                    sunFill, isSticker ? StickerInk : null, isSticker ? 1.2 : 0));
                break;
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static FrameworkElement CreatePixel(WeatherKind kind, bool isDay, double size)
    {
        var canvas = new Canvas { Width = 16, Height = 16 };
        RenderOptions.SetEdgeMode(canvas, EdgeMode.Aliased);
        var ink = PixelInk;

        const string pxCloud =
            "M 5,6 H 11 V 8 H 14 V 12 H 3 V 8 H 5 Z";
        const string pxCloudHole =
            "M 5,8 H 12 V 11 H 4 V 9 H 5 Z";

        switch (kind)
        {
            case WeatherKind.Clear:
                if (isDay)
                {
                    canvas.Children.Add(PixelRect("M 5,5 H 11 V 11 H 5 Z M 7,1 H 9 V 3 H 7 Z M 7,13 H 9 V 15 H 7 Z M 1,7 H 3 V 9 H 1 Z M 13,7 H 15 V 9 H 13 Z M 3,3 H 4 V 4 H 3 Z M 12,3 H 13 V 4 H 12 Z M 3,12 H 4 V 13 H 3 Z M 12,12 H 13 V 13 H 12 Z", ink));
                }
                else
                {
                    canvas.Children.Add(PixelRect("M 6,2 H 11 V 4 H 8 V 12 H 11 V 14 H 6 V 12 H 4 V 4 H 6 Z", ink));
                }
                break;
            case WeatherKind.PartlyCloudy:
                canvas.Children.Add(PixelRect("M 2,3 H 7 V 8 H 2 Z M 4,1 H 5 V 2 H 4 Z M 0,5 H 1 V 6 H 0 Z", ink));
                canvas.Children.Add(PixelRect(pxCloud, ink));
                canvas.Children.Add(PixelRect(pxCloudHole, Frozen(Color.FromRgb(0xC9, 0xED, 0xE3))));
                break;
            case WeatherKind.Fog:
                canvas.Children.Add(PixelRect("M 2,5 H 14 V 7 H 2 Z M 4,9 H 13 V 11 H 4 Z M 2,13 H 14 V 15 H 2 Z", ink));
                break;
            case WeatherKind.Drizzle:
            case WeatherKind.Rain:
                canvas.Children.Add(PixelRect("M 4,2 H 11 V 4 H 14 V 9 H 2 V 4 H 4 Z", ink));
                canvas.Children.Add(PixelRect("M 4,11 H 6 V 14 H 4 Z M 8,12 H 10 V 15 H 8 Z M 12,11 H 14 V 14 H 12 Z", ink));
                break;
            case WeatherKind.Snow:
                canvas.Children.Add(PixelRect("M 4,2 H 11 V 4 H 14 V 9 H 2 V 4 H 4 Z", ink));
                canvas.Children.Add(PixelRect("M 4,11 H 6 V 13 H 4 Z M 8,13 H 10 V 15 H 8 Z M 12,11 H 14 V 13 H 12 Z", ink));
                break;
            case WeatherKind.Thunder:
                canvas.Children.Add(PixelRect("M 4,2 H 11 V 4 H 14 V 9 H 2 V 4 H 4 Z", ink));
                canvas.Children.Add(PixelRect("M 8,9 H 11 V 11 H 9 V 13 H 11 V 15 H 7 V 12 H 6 V 10 H 8 Z", ink));
                break;
            default:
                canvas.Children.Add(PixelRect(pxCloud, ink));
                break;
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static Path PixelRect(string data, Brush fill)
    {
        var geo = Geometry.Parse(data);
        geo.Freeze();
        var p = new Path { Data = geo, Fill = fill };
        RenderOptions.SetEdgeMode(p, EdgeMode.Aliased);
        return p;
    }

    private static void AddSky(Canvas canvas, bool isDay, double scale, double dx, double dy, Brush sunFill, Brush? sunStroke, bool isLight)
    {
        var group = new Canvas { Width = 24, Height = 24, RenderTransformOrigin = new Point(0.5, 0.5) };
        if (isDay)
        {
            if (sunStroke is not null)
                group.Children.Add(Stroke(SunRays, sunStroke, 3.0));
            group.Children.Add(Stroke(SunRays, sunFill, 1.8));
            group.Children.Add(FillAndStroke("M12,7 A5,5 0 1 1 11.99,7 Z", sunFill, sunStroke, sunStroke is null ? 0 : 1.5));
        }
        else
        {
            var moon = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(12, 12), 8, 8), new EllipseGeometry(new Point(16.5, 8.5), 7, 7));
            moon.Freeze();
            group.Children.Add(new Path
            {
                Data = moon,
                Fill = isLight ? MoonSlate : MoonLight,
                Stroke = sunStroke,
                StrokeThickness = sunStroke is null ? 0 : 1.4,
            });
        }
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(dx, dy));
        group.RenderTransform = transform;
        canvas.Children.Add(group);
    }

    private static void Add(Canvas canvas, UIElement element, double dx, double dy)
    {
        element.RenderTransform = new TranslateTransform(dx, dy);
        canvas.Children.Add(element);
    }

    private static Path FillAndStroke(string data, Brush fill, Brush? stroke, double thickness) => new()
    {
        Data = Geometry.Parse(data),
        Fill = fill,
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeLineJoin = PenLineJoin.Round,
    };

    private static Path Stroke(string data, Brush brush, double thickness) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = brush,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
