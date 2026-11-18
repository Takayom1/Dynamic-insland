// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace HoshiBarInstaller;

internal static class InstallerVisuals
{
    public static readonly Color Gold = Color.FromRgb(0xF2, 0xD1, 0x7D);
    public static readonly Color Pink = Color.FromRgb(0xEA, 0xB0, 0xCF);
    public static readonly Color Sky = Color.FromRgb(0x83, 0xCF, 0xF3);
    public static readonly Color DarkBg = Color.FromRgb(0x18, 0x14, 0x1F);
    public static readonly Color Surface = Color.FromRgb(0x24, 0x1F, 0x2E);
    public static readonly Color CompanionBg = Color.FromRgb(0x28, 0x22, 0x33);
    public static readonly Color ControlBg = Color.FromRgb(0x33, 0x2B, 0x3F);
    public static readonly Color BorderCol = Color.FromRgb(0x4A, 0x3D, 0x59);
    public static readonly Color TextCol = Color.FromRgb(0xF4, 0xED, 0xF6);
    public static readonly Color MutedCol = Color.FromRgb(0xA5, 0x9C, 0xAC);

    public static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static LinearGradientBrush CreateHeteroBrush()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0.3), EndPoint = new Point(1, 0.7) };
        g.GradientStops.Add(new GradientStop(Gold, 0.0));
        g.GradientStops.Add(new GradientStop(Pink, 0.5));
        g.GradientStops.Add(new GradientStop(Sky, 1.0));
        g.Freeze();
        return g;
    }

    public static (FrameworkElement Visual, TranslateTransform Gaze, Action<bool> SetSleepy) CreateHoshinoCompanionScene()
    {
        var root = new Grid { Width = 156, Height = 156, HorizontalAlignment = HorizontalAlignment.Center };

        // Thin geometric Eye of Horus backdrop ring
        var horus = new Canvas { Width = 148, Height = 148, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.42 };
        var ringBrush = CreateHeteroBrush();
        horus.Children.Add(new Ellipse
        {
            Width = 128,
            Height = 128,
            Stroke = ringBrush,
            StrokeThickness = 1.2,
            Margin = new Thickness(10),
        });
        horus.Children.Add(new Ellipse
        {
            Width = 104,
            Height = 104,
            Stroke = Frozen(Color.FromArgb(0x55, 0xEA, 0xB0, 0xCF)),
            StrokeThickness = 1.0,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Margin = new Thickness(22),
        });
        // Crosshair ticks
        foreach (var (x1, y1, x2, y2) in new[] { (74d, 2d, 74d, 14d), (74d, 134d, 74d, 146d), (2d, 74d, 14d, 74d), (134d, 74d, 146d, 74d) })
        {
            horus.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = ringBrush, StrokeThickness = 1.3 });
        }
        root.Children.Add(horus);

        // Canonical Hoshino head + WhalePin + blinking/gazing eyes
        var canvas = new Canvas { Width = 120, Height = 126 };
        var floatBounce = new TranslateTransform();
        canvas.RenderTransform = floatBounce;

        void AddShape(string pathData, Color fill)
        {
            var g = Geometry.Parse(pathData);
            g.Freeze();
            canvas.Children.Add(new Path { Data = g, Fill = Frozen(fill) });
        }

        AddShape("M55 24C49 11 61 1 72 7c6 3 7 8 4 12-1-6-6-8-11-6-5 2-6 7-4 11C82 21 99 36 101 56l10 8-10 1 6 7-9-1c1 15 2 24 8 35l-10-4 2 12-11-8-2 10c-8-8-11-18-12-29H47c-1 11-4 21-12 29l-2-10-11 8 2-12-10 4c6-11 7-20 8-35l-9 1 6-7-10-1 10-8C21 36 34 24 55 24Z", Pink);
        AddShape("M30 57C30 36 90 36 90 57V71C90 87 78 97 60 98 42 97 30 87 30 71Z", Color.FromRgb(0xFF, 0xF0, 0xE9));
        AddShape("M23 59C25 34 38 21 60 24 82 21 98 37 98 59L104 65C94 64 89 59 85 54c4 9 3 15-2 19-2-8-3-16-7-23 0 12-6 19-15 24 4-13 1-23-2-32-1 14-5 23-12 28 1-13-1-21-4-27-1 12-5 21-13 26l3-13c-4 4-9 7-15 8Z", Pink);

        // Turquoise Abydos Whale Hairpin on bangs
        var whalePin = new Canvas { Width = 120, Height = 120, RenderTransform = new RotateTransform(-9, 79, 41) };
        whalePin.Children.Add(new Path
        {
            Data = Geometry.Parse("M 67.5,43.5 C 67.2,36.2 73.5,33.0 80.2,34.2 C 84.4,35.0 86.6,38.2 88.2,39.4 C 90.0,37.8 92.5,35.5 94.2,36.0 C 93.8,38.6 92.2,40.4 90.8,41.2 C 92.5,42.0 94.0,43.6 93.6,45.4 C 91.5,45.2 89.5,43.8 88.0,42.6 C 85.4,45.8 80.2,47.0 73.2,46.6 C 69.4,46.4 67.6,45.2 67.5,43.5 Z"),
            Fill = Frozen(Sky),
            Stroke = Frozen(Color.FromRgb(0x4E, 0xA4, 0xD2)),
            StrokeThickness = 1.0,
        });
        var pinEye = new Ellipse { Width = 2.3, Height = 2.3, Fill = Frozen(DarkBg) };
        Canvas.SetLeft(pinEye, 72.2);
        Canvas.SetTop(pinEye, 39.2);
        whalePin.Children.Add(pinEye);
        canvas.Children.Add(whalePin);

        // Open heterochromatic eyes with blink + cursor gaze
        var gaze = new TranslateTransform();
        var openEyes = new Canvas { RenderTransform = gaze };
        foreach (var (x, col) in new[] { (47d, Gold), (73d, Sky) })
        {
            var eye = new Ellipse
            {
                Width = 11.6,
                Height = 11.0,
                Fill = Frozen(col),
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Canvas.SetLeft(eye, x - 5.8);
            Canvas.SetTop(eye, 71.5);
            var lid = new ScaleTransform(1, 1);
            eye.RenderTransform = lid;
            var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.8), RepeatBehavior = RepeatBehavior.Forever };
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.45))));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.58))));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.72))));
            lid.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
            openEyes.Children.Add(eye);
        }
        canvas.Children.Add(openEyes);

        // Cozy closed lids + warm blush for Screen 2 ("Обустраиваем островок…")
        var sleepyLayer = new Canvas { Visibility = Visibility.Collapsed };
        sleepyLayer.Children.Add(new Path
        {
            Data = Geometry.Parse("M 41,76.5 Q 47,80.5 53,76.5 M 67,76.5 Q 73,80.5 79,76.5"),
            Stroke = Frozen(Color.FromRgb(0x4E, 0x3A, 0x52)),
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
        foreach (var cx in new[] { 36d, 84d })
        {
            var blush = new Ellipse { Width = 12, Height = 6, Fill = Frozen(Color.FromArgb(0x90, 0xEC, 0x81, 0xA3)) };
            Canvas.SetLeft(blush, cx - 6);
            Canvas.SetTop(blush, 81);
            sleepyLayer.Children.Add(blush);
        }
        canvas.Children.Add(sleepyLayer);

        floatBounce.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-2.8, 2.8, TimeSpan.FromSeconds(2.4))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });

        root.Children.Add(new Viewbox { Width = 132, Height = 138, Child = canvas });

        void SetSleepy(bool sleepy)
        {
            openEyes.Visibility = sleepy ? Visibility.Collapsed : Visibility.Visible;
            sleepyLayer.Visibility = sleepy ? Visibility.Visible : Visibility.Collapsed;
        }

        return (root, gaze, SetSleepy);
    }

    public static FrameworkElement CreateDriftingWhaleBanner()
    {
        var border = new Border
        {
            Height = 62,
            CornerRadius = new CornerRadius(14),
            Background = Frozen(Color.FromRgb(0x13, 0x1D, 0x2C)),
            BorderBrush = Frozen(Color.FromArgb(0x66, 0x83, 0xCF, 0xF3)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var scene = new Canvas { Width = 440, Height = 62 };

        // Soft glowing bubbles in the water
        foreach (var (bx, by, br, op) in new[] { (42d, 20d, 4d, 0.35), (128d, 42d, 5d, 0.25), (290d, 16d, 3.5d, 0.35), (378d, 38d, 5.5d, 0.28) })
        {
            var dot = new Ellipse { Width = br, Height = br, Fill = Frozen(Sky), Opacity = op };
            Canvas.SetLeft(dot, bx);
            Canvas.SetTop(dot, by);
            scene.Children.Add(dot);
        }

        // Glowing turquoise Abydos whale drifting smoothly left <-> right
        var whale = new Canvas { Width = 84, Height = 40 };
        Canvas.SetLeft(whale, 140);
        Canvas.SetTop(whale, 12);

        whale.Children.Add(new Path
        {
            Data = Geometry.Parse("M 10,24 C 9,11 22,5 38,8 C 48,10 54,16 60,18 C 65,14 71,9 75,11 C 74,16 70,20 66,22 C 71,24 74,28 73,32 C 68,32 63,28 59,25 C 52,32 39,35 22,33 C 14,32 10,28 10,24 Z"),
            Fill = Frozen(Sky),
            Stroke = Frozen(Color.FromRgb(0xC8, 0xEE, 0xFF)),
            StrokeThickness = 1.2,
        });
        whale.Children.Add(new Path
        {
            Data = Geometry.Parse("M 13,25 C 21,23 35,24 46,28 C 38,32 23,32 14,29 Z"),
            Fill = Frozen(Color.FromRgb(0xEE, 0xFA, 0xFF)),
        });
        var eye = new Ellipse { Width = 3.5, Height = 3.5, Fill = Frozen(DarkBg) };
        Canvas.SetLeft(eye, 21);
        Canvas.SetTop(eye, 17);
        whale.Children.Add(eye);

        var drift = new TranslateTransform();
        whale.RenderTransform = drift;
        drift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-48, 64, TimeSpan.FromSeconds(4.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
        drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-2.5, 2.5, TimeSpan.FromSeconds(1.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });

        scene.Children.Add(whale);
        border.Child = scene;
        return border;
    }
}
