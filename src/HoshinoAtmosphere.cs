// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Vector atmosphere details, idle/night drowsiness detection, weather micro-badges and contextual phrase pools for Hoshino.
/// </summary>
public static class HoshinoAtmosphere
{
    private static long lastIdleSampleTick;
    private static TimeSpan cachedIdle = TimeSpan.Zero;

    private static readonly ShufflePool NightPool = new(new[]
    {
        "Ухе… уже совсем поздно. Даже старичок подумывает прикрыть глаза.",
        "Тихо как… если задремлю на минутку, считай это ночным дежурством.",
        "Такой час хороший — никто не торопит. Но ты про сон тоже не забывай.",
        "Ночь длинная, а силы у тебя одни. Может, потихоньку сворачиваем дела?",
        "Ухе… монитор светится уютно, но подушка сейчас явно выигрывает спор.",
        "Пока тут тихо, я немножко подремлю вполглаза. Если что — зови.",
        "В такую пору даже пустыня засыпает. Не сиди допоздна без повода.",
        "Если не спится — просто посидим спокойно, без сложных задач.",
        "Глаза сами слипаются… это не лень, это энергосберегающий режим.",
        "Ночной патруль докладывает: вокруг тишина, можно выдохнуть.",
    });

    private static readonly ShufflePool MorningPool = new(new[]
    {
        "Ухе… доброе утро. Давай входить в день мягко, без резких движений.",
        "Утро только просыпается. Сначала что-нибудь тёплое в кружку, потом списки.",
        "Ещё сонно, зато впереди целый чистый лист. Не спеши его весь исписать.",
        "Старичок проснулся… почти. Дай пару минут на загрузку бодрости.",
        "Хорошее утро начинается не с подвига, а со спокойного вдоха.",
        "Если проснулся рано — пусть первые минуты будут только твоими.",
        "Свет за окном ещё мягкий. Самое время не устраивать гонку с порога.",
        "Ухе… проверим чайник и тихонько начнём с одного простого дела.",
    });

    private static readonly ShufflePool RainPool = new(new[]
    {
        "Под такой дождь особенно уютно сидеть в тепле и никуда не бежать.",
        "Капли за окном держат отличный ритм. Можно работать совсем не спеша.",
        "Ухе… дождик шепчет так убедительно, что хочется завернуться в плед.",
        "Когда снаружи непогода, маленький сухой островок ценится вдвойне.",
        "Слышишь, как стучит по окну? Хороший фон, чтобы спокойно разобрать дела.",
        "В такую погоду даже старичок никуда бы не высовывал нос без зонта.",
        "Дождь смывает лишнюю суету. Оставим на сегодня только действительно нужное.",
        "Если за окном сыро или снежно — самое время налить горячего чая.",
    });

    private static readonly ShufflePool HeadpatPool = new(new[]
    {
        "Ухе… эй, ты чего? Так я и правда окончательно разомлею.",
        "Хе-хе… ладно-ладно, причёску помнёшь, ахогэ потом не уложится.",
        "Ох, старичка по макушке гладят… странное чувство, но приятное.",
        "Ухе~ если будешь так гладить, я точно усну прямо на посту.",
        "Ну всё, засмущал… Зато настроение у обоих стало чуточку теплее.",
        "Тёплая ладонь… ладно, на пару секунд разрешаю, только никому не говори.",
        "Эй-эй, я тут вообще-то старшая… хотя ладно, продолжай немножко.",
        "М-м… так спокойно. Сразу хочется никуда не торопиться.",
        "Ахогэ на месте? Ну хорошо. Спасибо за маленькую передышку.",
        "Ухе… вот так просто взял и подкупил старичка вниманием.",
    });

    private static readonly Brush PinkBrush = Frozen(Color.FromRgb(0xEA, 0xB0, 0xCF));
    private static readonly Brush SkyBrush = Frozen(Color.FromRgb(0x83, 0xCF, 0xF3));
    private static readonly Brush GoldBrush = Frozen(Color.FromRgb(0xF2, 0xD1, 0x7D));
    private static readonly Brush LightBrush = Frozen(Color.FromRgb(0xF4, 0xED, 0xF6));
    private static readonly Brush DarkCupBrush = Frozen(Color.FromRgb(0x33, 0x2B, 0x3D));
    private static readonly Brush DarkRimBrush = Frozen(Color.FromRgb(0x18, 0x14, 0x1F));

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static TimeSpan UserIdleTime()
    {
        var nowTick = Environment.TickCount64;
        if (nowTick - lastIdleSampleTick < 1000) return cachedIdle;
        lastIdleSampleTick = nowTick;

        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return cachedIdle = TimeSpan.Zero;
        var elapsedMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return cachedIdle = TimeSpan.FromMilliseconds(elapsedMs);
    }

    public static bool IsNightWindow(DateTime now)
    {
        var t = now.TimeOfDay;
        return t >= new TimeSpan(1, 30, 0) && t < new TimeSpan(5, 30, 0);
    }

    public static bool IsMorningWindow(DateTime now)
    {
        var t = now.TimeOfDay;
        return t >= new TimeSpan(5, 30, 0) && t < new TimeSpan(10, 0, 0);
    }

    public static bool IsDrowsy(DateTime now) =>
        IsNightWindow(now) || UserIdleTime() >= TimeSpan.FromMinutes(12);

    public static string NextHeadpatPhrase() => HeadpatPool.Next();

    public static string? PickContextualPhrase(DateTime now, WeatherReport? weather)
    {
        if (IsDrowsy(now)) return NightPool.Next();
        if (weather is { Kind: WeatherKind.Drizzle or WeatherKind.Rain or WeatherKind.Thunder or WeatherKind.Snow })
            return RainPool.Next();
        if (IsMorningWindow(now)) return MorningPool.Next();
        return null;
    }

    /// <summary>Hoshino's stylized floating geometric Horus halo above the ahoge (#EAB0CF outer + #83CFF3 inner).</summary>
    public static (Canvas Root, TranslateTransform Offset, ScaleTransform Scale) CreateHalo()
    {
        var canvas = new Canvas { Width = 120, Height = 120, IsHitTestVisible = false };
        var outer = new Path
        {
            Data = Geometry.Parse("M 37,8.5 A 23,4.2 0 1 1 83,8.5 A 23,4.2 0 1 1 37,8.5 M 31,8.5 H 36 M 84,8.5 H 89 M 60,2.2 V 4.3 M 60,12.7 V 14.8"),
            Stroke = PinkBrush,
            StrokeThickness = 2.0,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        var inner = new Path
        {
            Data = Geometry.Parse("M 44,8.5 A 16,2.6 0 1 1 76,8.5 A 16,2.6 0 1 1 44,8.5 M 57.5,8.5 L 60,6.9 L 62.5,8.5 L 60,10.1 Z"),
            Stroke = SkyBrush,
            StrokeThickness = 1.05,
            StrokeLineJoin = PenLineJoin.Round,
        };
        canvas.Children.Add(outer);
        canvas.Children.Add(inner);

        var scale = new ScaleTransform(1, 1, 60, 8.5);
        var offset = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(offset);
        canvas.RenderTransform = group;
        return (canvas, offset, scale);
    }

    /// <summary>Vector zZz particles (#EAB0CF / #83CFF3) that gently float and fade during night/idle drowsiness.</summary>
    public static (Canvas Root, (Path Shape, TranslateTransform Move, ScaleTransform Scale)[] Items) CreateSleepParticles()
    {
        var root = new Canvas { Width = 120, Height = 120, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var items = new (Path, TranslateTransform, ScaleTransform)[3];
        var zGeo = Geometry.Parse("M 0,0 H 5.6 L 0.4,6 H 6");
        zGeo.Freeze();

        for (var i = 0; i < 3; i++)
        {
            var move = new TranslateTransform();
            var scale = new ScaleTransform(1, 1, 3, 3);
            var tg = new TransformGroup();
            tg.Children.Add(scale);
            tg.Children.Add(move);

            var path = new Path
            {
                Data = zGeo,
                Stroke = i % 2 == 0 ? PinkBrush : SkyBrush,
                StrokeThickness = 1.45,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0,
                RenderTransform = tg,
            };
            root.Children.Add(path);
            items[i] = (path, move, scale);
        }
        return (root, items);
    }

    /// <summary>Vector coworking headphones (#83CFF3 headband/accents + #332B3D earcups) worn while focus timer runs.</summary>
    public static Canvas CreateHeadphones()
    {
        var root = new Canvas { Width = 120, Height = 120, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var band = new Path
        {
            Data = Geometry.Parse("M 24.5,63 C 22,29 98,29 95.5,63"),
            Stroke = SkyBrush,
            StrokeThickness = 3.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        var innerBand = new Path
        {
            Data = Geometry.Parse("M 26.5,62 C 25,33 95,33 93.5,62"),
            Stroke = DarkCupBrush,
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        root.Children.Add(band);
        root.Children.Add(innerBand);

        foreach (var left in new[] { true, false })
        {
            var x = left ? 17.5 : 91.5;
            var cup = new Border
            {
                Width = 11,
                Height = 20,
                CornerRadius = new CornerRadius(5.5),
                Background = DarkCupBrush,
                BorderBrush = SkyBrush,
                BorderThickness = new Thickness(1.5),
            };
            Canvas.SetLeft(cup, x);
            Canvas.SetTop(cup, 58);
            root.Children.Add(cup);

            var accentDot = new Ellipse { Width = 3.6, Height = 6.5, Fill = PinkBrush, Opacity = 0.85 };
            Canvas.SetLeft(accentDot, x + 3.7);
            Canvas.SetTop(accentDot, 64.7);
            root.Children.Add(accentDot);
        }
        return root;
    }

    public static string WeatherKey(WeatherReport? report, bool enabled)
    {
        if (!enabled || report is null) return "off";
        var hot = IsHotWeather(report);
        return $"{report.Kind}|{report.IsDay}|{hot}|{Math.Round(report.Temperature)}";
    }

    public static bool IsHotWeather(WeatherReport report)
    {
        var tempC = report.Fahrenheit ? (report.Temperature - 32) * 5.0 / 9.0 : report.Temperature;
        var feelsC = report.Fahrenheit ? (report.FeelsLike - 32) * 5.0 / 9.0 : report.FeelsLike;
        return Math.Max(tempC, feelsC) >= 27.0;
    }

    /// <summary>Updates the unobtrusive vector weather micro-detail beside Hoshino.</summary>
    public static void RebuildWeatherBadge(Canvas badgeHost, Canvas sunglassesOverlay, WeatherReport? report, bool enabled)
    {
        badgeHost.Children.Clear();
        sunglassesOverlay.Children.Clear();
        if (!enabled || report is null)
        {
            badgeHost.Visibility = Visibility.Collapsed;
            sunglassesOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var hot = IsHotWeather(report);
        badgeHost.Visibility = Visibility.Visible;

        if (report.Kind is WeatherKind.Drizzle or WeatherKind.Rain or WeatherKind.Thunder)
        {
            // Mini-umbrella + raindrop
            var dome = new Path
            {
                Data = Geometry.Parse("M 9,23 C 9,15.5 23,15.5 23,23 Z"),
                Fill = PinkBrush,
                Stroke = SkyBrush,
                StrokeThickness = 1.1,
            };
            var stick = new Path
            {
                Data = Geometry.Parse("M 16,16 V 26.5 A 2.1,2.1 0 0 1 12,26.5"),
                Stroke = LightBrush,
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            var drop = new Path
            {
                Data = Geometry.Parse("M 25.5,13 C 25.5,13 28,16.5 28,18 A 2.5,2.5 0 1 1 23,18 C 23,16.5 25.5,13 25.5,13 Z"),
                Fill = SkyBrush,
                Opacity = 0.92,
            };
            badgeHost.Children.Add(stick);
            badgeHost.Children.Add(dome);
            badgeHost.Children.Add(drop);
        }
        else if (report.Kind == WeatherKind.Snow)
        {
            // 6-pointed vector snowflake
            var crystal = new Path
            {
                Data = Geometry.Parse("M 17,13 V 27 M 11,16.5 L 23,23.5 M 23,16.5 L 11,23.5 M 15.2,14.8 L 17,16.5 L 18.8,14.8 M 15.2,25.2 L 17,23.5 L 18.8,25.2"),
                Stroke = SkyBrush,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            var core = new Ellipse { Width = 3.6, Height = 3.6, Fill = LightBrush };
            Canvas.SetLeft(core, 15.2);
            Canvas.SetTop(core, 18.2);
            badgeHost.Children.Add(crystal);
            badgeHost.Children.Add(core);
        }
        else if (hot)
        {
            // Golden sun-glint sparkle + stylish mini-sunglasses resting on Hoshino's bangs
            var glint = new Path
            {
                Data = Geometry.Parse("M 17,12 L 18.8,17.2 L 24,19 L 18.8,20.8 L 17,26 L 15.2,20.8 L 10,19 L 15.2,17.2 Z"),
                Fill = GoldBrush,
                Opacity = 0.92,
            };
            badgeHost.Children.Add(glint);

            sunglassesOverlay.Visibility = Visibility.Visible;
            var leftLens = new Path
            {
                Data = Geometry.Parse("M 38,49 H 55 L 53.5,56.5 C 52.5,58.5 40,58.5 39,56.5 Z"),
                Fill = DarkRimBrush,
                Stroke = SkyBrush,
                StrokeThickness = 1.3,
                Opacity = 0.9,
            };
            var rightLens = new Path
            {
                Data = Geometry.Parse("M 65,49 H 82 L 81,56.5 C 80,58.5 67.5,58.5 66.5,56.5 Z"),
                Fill = DarkRimBrush,
                Stroke = PinkBrush,
                StrokeThickness = 1.3,
                Opacity = 0.9,
            };
            var bridge = new Path
            {
                Data = Geometry.Parse("M 55,51 Q 60,49.5 65,51 M 34,51 L 38,50 M 82,50 L 86,51"),
                Stroke = GoldBrush,
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            var shine = new Path
            {
                Data = Geometry.Parse("M 41.5,51.2 L 44.5,55.2 M 68.5,51.2 L 71.5,55.2"),
                Stroke = LightBrush,
                StrokeThickness = 1.1,
                Opacity = 0.55,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            sunglassesOverlay.Children.Add(bridge);
            sunglassesOverlay.Children.Add(leftLens);
            sunglassesOverlay.Children.Add(rightLens);
            sunglassesOverlay.Children.Add(shine);
        }
        else if (report.Kind == WeatherKind.Clear && report.IsDay)
        {
            // Gentle sunny sparkle
            badgeHost.Children.Add(new Path
            {
                Data = Geometry.Parse("M 17,13.5 L 18.4,17.6 L 22.5,19 L 18.4,20.4 L 17,24.5 L 15.6,20.4 L 11.5,19 L 15.6,17.6 Z"),
                Fill = GoldBrush,
                Opacity = 0.8,
            });
        }
        else if (report.Kind == WeatherKind.Clear)
        {
            // Clear night star-glint
            badgeHost.Children.Add(new Path
            {
                Data = Geometry.Parse("M 17,14 L 18.2,17.8 L 22,19 L 18.2,20.2 L 17,24 L 15.8,20.2 L 12,19 L 15.8,17.8 Z"),
                Fill = SkyBrush,
                Opacity = 0.75,
            });
        }
        else
        {
            // Subtle soft cloud wisp for cloudy / fog
            badgeHost.Children.Add(new Path
            {
                Data = Geometry.Parse("M 11.5,23 H 22 A 3,3 0 0 0 22,17 A 4.2,4.2 0 0 0 13.5,18 A 2.6,2.6 0 0 0 11.5,23 Z"),
                Fill = SkyBrush,
                Opacity = 0.62,
            });
        }
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private sealed class ShufflePool
    {
        private readonly string[] items;
        private int[] order = Array.Empty<int>();
        private int cursor;
        private string? previous;

        public ShufflePool(string[] items) => this.items = items;

        public string Next()
        {
            if (cursor >= order.Length)
            {
                order = Enumerable.Range(0, items.Length).ToArray();
                Random.Shared.Shuffle(order);
                cursor = 0;
                if (order.Length > 1 && items[order[0]] == previous)
                    (order[0], order[1]) = (order[1], order[0]);
            }
            return previous = items[order[cursor++]];
        }
    }
}
