// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DIsland;

/// <summary>
/// Self-contained ISO/IEC 18004 QR Code encoder (Byte mode, ECC Level M, Versions 1..10)
/// and crisp aliased WPF vector renderer styled with HoshinoPalette dynamic resources.
/// </summary>
public static class QrCodeVector
{
    private readonly record struct VersionSpec(int TotalCodewords, int EcPerBlock, int Group1Blocks, int Group1DataCw, int Group2Blocks, int Group2DataCw, int[] Alignments);

    // ISO/IEC 18004 Table 9 — Error Correction Level M (~15% recovery), Versions 1..10
    private static readonly VersionSpec[] Specs =
    {
        default,
        new(26, 10, 1, 16, 0, 0, Array.Empty<int>()),
        new(44, 16, 1, 28, 0, 0, new[] { 6, 18 }),
        new(70, 26, 1, 44, 0, 0, new[] { 6, 22 }),
        new(100, 18, 2, 32, 0, 0, new[] { 6, 26 }),
        new(134, 24, 2, 43, 0, 0, new[] { 6, 30 }),
        new(172, 16, 4, 27, 0, 0, new[] { 6, 34 }),
        new(196, 18, 4, 31, 0, 0, new[] { 6, 22, 38 }),
        new(242, 22, 2, 38, 2, 39, new[] { 6, 24, 42 }),
        new(292, 22, 3, 36, 2, 37, new[] { 6, 26, 46 }),
        new(346, 26, 4, 43, 1, 44, new[] { 6, 28, 50 }),
    };

    public static bool[,] Encode(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var version = ChooseVersion(payload.Length);
        var spec = Specs[version];
        var dataCapacity = spec.Group1Blocks * spec.Group1DataCw + spec.Group2Blocks * spec.Group2DataCw;

        var dataCodewords = BuildDataCodewords(payload, version, dataCapacity);
        var allCodewords = AddErrorCorrectionAndInterleave(dataCodewords, spec);

        var size = 17 + 4 * version;
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];

        DrawFunctionPatterns(modules, isFunction, version, spec.Alignments);
        DrawCodewords(modules, isFunction, allCodewords);

        // Pick mask 0..7 with lowest ISO penalty score
        var bestMask = 0;
        var bestPenalty = int.MaxValue;
        bool[,]? bestGrid = null;

        for (var mask = 0; mask < 8; mask++)
        {
            var candidate = (bool[,])modules.Clone();
            ApplyMask(candidate, isFunction, mask);
            DrawFormatBits(candidate, isFunction, mask);
            var penalty = ComputePenalty(candidate);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMask = mask;
                bestGrid = candidate;
            }
        }

        _ = bestMask;
        return bestGrid!;
    }

    /// <summary>Creates a sharp vector QR code pad using HoshinoPalette (#EAB0CF on dark background by default).</summary>
    public static Border CreateVisual(string content, double sizeDip = 128, bool lightPad = false)
    {
        var matrix = Encode(content);
        var n = matrix.GetLength(0);
        const int quiet = 3;
        var total = n + quiet * 2;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (var r = 0; r < n; r++)
            {
                var c = 0;
                while (c < n)
                {
                    if (!matrix[r, c]) { c++; continue; }
                    var start = c;
                    while (c < n && matrix[r, c]) c++;
                    double x = quiet + start, y = quiet + r, w = c - start;
                    ctx.BeginFigure(new Point(x, y), isFilled: true, isClosed: true);
                    ctx.LineTo(new Point(x + w, y), false, false);
                    ctx.LineTo(new Point(x + w, y + 1), false, false);
                    ctx.LineTo(new Point(x, y + 1), false, false);
                }
            }
        }
        geo.Freeze();

        var path = new Path { Data = geo };
        RenderOptions.SetEdgeMode(path, EdgeMode.Aliased);

        var canvas = new Canvas { Width = total, Height = total };
        canvas.Children.Add(path);

        var pad = new Border
        {
            Width = sizeDip,
            Height = sizeDip,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = canvas },
        };
        pad.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");

        if (lightPad)
        {
            var lightBg = new SolidColorBrush(Color.FromRgb(0xF4, 0xED, 0xF6));
            lightBg.Freeze();
            var darkFg = new SolidColorBrush(Color.FromRgb(0x18, 0x14, 0x1F));
            darkFg.Freeze();
            pad.Background = lightBg;
            path.Fill = darkFg;
        }
        else if (HoshinoPalette.IsLight)
        {
            pad.SetResourceReference(Border.BackgroundProperty, "HoshinoSurface");
            path.SetResourceReference(Shape.FillProperty, "HoshinoText");
        }
        else
        {
            pad.SetResourceReference(Border.BackgroundProperty, "HoshinoBackground");
            path.SetResourceReference(Shape.FillProperty, "HoshinoAccent");
        }
        return pad;
    }

    private static int ChooseVersion(int byteCount)
    {
        for (var v = 1; v <= 10; v++)
        {
            var s = Specs[v];
            var capBytes = s.Group1Blocks * s.Group1DataCw + s.Group2Blocks * s.Group2DataCw;
            var headerBits = 4 + (v <= 9 ? 8 : 16);
            if (headerBits + byteCount * 8 <= capBytes * 8) return v;
        }
        throw new ArgumentException("QR payload exceeds Version 10 capacity.");
    }

    private static byte[] BuildDataCodewords(byte[] payload, int version, int capacityCodewords)
    {
        var bits = new List<bool>(capacityCodewords * 8);
        AppendBits(bits, 0b0100, 4); // Byte mode
        AppendBits(bits, payload.Length, version <= 9 ? 8 : 16);
        foreach (var b in payload) AppendBits(bits, b, 8);

        var capacityBits = capacityCodewords * 8;
        var terminator = Math.Min(4, capacityBits - bits.Count);
        AppendBits(bits, 0, terminator);
        while (bits.Count % 8 != 0) bits.Add(false);

        var padBytes = new[] { 0xEC, 0x11 };
        var padIdx = 0;
        while (bits.Count < capacityBits)
        {
            AppendBits(bits, padBytes[padIdx & 1], 8);
            padIdx++;
        }

        var result = new byte[capacityCodewords];
        for (var i = 0; i < bits.Count; i++)
            if (bits[i]) result[i >> 3] |= (byte)(1 << (7 - (i & 7)));
        return result;
    }

    private static void AppendBits(List<bool> bits, int val, int len)
    {
        for (var i = len - 1; i >= 0; i--)
            bits.Add(((val >> i) & 1) != 0);
    }

    private static byte[] AddErrorCorrectionAndInterleave(byte[] data, VersionSpec spec)
    {
        var totalBlocks = spec.Group1Blocks + spec.Group2Blocks;
        var dataBlocks = new byte[totalBlocks][];
        var ecBlocks = new byte[totalBlocks][];
        var divisor = ReedSolomonDivisor(spec.EcPerBlock);

        var offset = 0;
        for (var i = 0; i < totalBlocks; i++)
        {
            var len = i < spec.Group1Blocks ? spec.Group1DataCw : spec.Group2DataCw;
            var block = new byte[len];
            Array.Copy(data, offset, block, 0, len);
            offset += len;
            dataBlocks[i] = block;
            ecBlocks[i] = ReedSolomonRemainder(block, divisor);
        }

        var result = new byte[spec.TotalCodewords];
        var pos = 0;
        var maxDataLen = Math.Max(spec.Group1DataCw, spec.Group2DataCw);
        for (var col = 0; col < maxDataLen; col++)
            for (var b = 0; b < totalBlocks; b++)
                if (col < dataBlocks[b].Length) result[pos++] = dataBlocks[b][col];

        for (var col = 0; col < spec.EcPerBlock; col++)
            for (var b = 0; b < totalBlocks; b++)
                result[pos++] = ecBlocks[b][col];

        return result;
    }

    private static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        byte root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                result[j] = GfMultiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = GfMultiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
                result[i] ^= GfMultiply(divisor[i], factor);
        }
        return result;
    }

    private static byte GfMultiply(byte x, byte y)
    {
        int z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction, int version, int[] align)
    {
        var size = modules.GetLength(0);

        for (var i = 0; i < size; i++)
        {
            SetFunc(modules, isFunction, 6, i, i % 2 == 0);
            SetFunc(modules, isFunction, i, 6, i % 2 == 0);
        }

        DrawFinder(modules, isFunction, 3, 3);
        DrawFinder(modules, isFunction, 3, size - 4);
        DrawFinder(modules, isFunction, size - 4, 3);

        for (var i = 0; i < align.Length; i++)
        {
            for (var j = 0; j < align.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == align.Length - 1) || (i == align.Length - 1 && j == 0))
                    continue;
                DrawAlignment(modules, isFunction, align[i], align[j]);
            }
        }

        // Reserve format bits + dark module
        DrawFormatBits(modules, isFunction, 0);

        // Version bits for version >= 7
        if (version >= 7)
        {
            var rem = version;
            for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            var bits = (version << 12) | rem;
            for (var i = 0; i < 18; i++)
            {
                var bit = ((bits >> i) & 1) != 0;
                var a = size - 11 + i % 3;
                var b = i / 3;
                SetFunc(modules, isFunction, b, a, bit);
                SetFunc(modules, isFunction, a, b, bit);
            }
        }
    }

    private static void DrawFinder(bool[,] modules, bool[,] isFunction, int cr, int cc)
    {
        var size = modules.GetLength(0);
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                int r = cr + dy, c = cc + dx;
                if (r < 0 || r >= size || c < 0 || c >= size) continue;
                var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                SetFunc(modules, isFunction, r, c, dist != 2 && dist != 4);
            }
        }
    }

    private static void DrawAlignment(bool[,] modules, bool[,] isFunction, int cr, int cc)
    {
        for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
                SetFunc(modules, isFunction, cr + dy, cc + dx, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
    }

    private static void DrawFormatBits(bool[,] modules, bool[,] isFunction, int mask)
    {
        var size = modules.GetLength(0);
        // ECC Level M format indicator = 00
        var data = mask; // (0b00 << 3) | mask
        var rem = data;
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = ((data << 10) | rem) ^ 0x5412;

        for (var i = 0; i <= 5; i++) SetFunc(modules, isFunction, i, 8, ((bits >> i) & 1) != 0);
        SetFunc(modules, isFunction, 7, 8, ((bits >> 6) & 1) != 0);
        SetFunc(modules, isFunction, 8, 8, ((bits >> 7) & 1) != 0);
        SetFunc(modules, isFunction, 8, 7, ((bits >> 8) & 1) != 0);
        for (var i = 9; i < 15; i++) SetFunc(modules, isFunction, 8, 14 - i, ((bits >> i) & 1) != 0);

        for (var i = 0; i < 8; i++) SetFunc(modules, isFunction, 8, size - 1 - i, ((bits >> i) & 1) != 0);
        for (var i = 8; i < 15; i++) SetFunc(modules, isFunction, size - 15 + i, 8, ((bits >> i) & 1) != 0);
        SetFunc(modules, isFunction, size - 8, 8, true);
    }

    private static void SetFunc(bool[,] modules, bool[,] isFunction, int r, int c, bool dark)
    {
        modules[r, c] = dark;
        isFunction[r, c] = true;
    }

    private static void DrawCodewords(bool[,] modules, bool[,] isFunction, byte[] data)
    {
        var size = modules.GetLength(0);
        var bitIdx = 0;
        var totalBits = data.Length * 8;

        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            var upward = ((right + 1) & 2) == 0;
            for (var vert = 0; vert < size; vert++)
            {
                var r = upward ? size - 1 - vert : vert;
                for (var j = 0; j < 2; j++)
                {
                    var c = right - j;
                    if (isFunction[r, c]) continue;
                    var dark = bitIdx < totalBits && ((data[bitIdx >> 3] >> (7 - (bitIdx & 7))) & 1) != 0;
                    modules[r, c] = dark;
                    bitIdx++;
                }
            }
        }
    }

    private static void ApplyMask(bool[,] modules, bool[,] isFunction, int mask)
    {
        var size = modules.GetLength(0);
        for (var r = 0; r < size; r++)
        {
            for (var c = 0; c < size; c++)
            {
                if (isFunction[r, c]) continue;
                var invert = mask switch
                {
                    0 => (r + c) % 2 == 0,
                    1 => r % 2 == 0,
                    2 => c % 3 == 0,
                    3 => (r + c) % 3 == 0,
                    4 => (r / 2 + c / 3) % 2 == 0,
                    5 => r * c % 2 + r * c % 3 == 0,
                    6 => (r * c % 2 + r * c % 3) % 2 == 0,
                    _ => ((r + c) % 2 + r * c % 3) % 2 == 0,
                };
                if (invert) modules[r, c] = !modules[r, c];
            }
        }
    }

    private static int ComputePenalty(bool[,] m)
    {
        var n = m.GetLength(0);
        var penalty = 0;

        // N1: runs of >= 5 same-colored modules in rows and columns
        for (var i = 0; i < n; i++)
        {
            int runR = 1, runC = 1;
            for (var j = 1; j < n; j++)
            {
                if (m[i, j] == m[i, j - 1]) { if (++runR == 5) penalty += 3; else if (runR > 5) penalty++; }
                else runR = 1;

                if (m[j, i] == m[j - 1, i]) { if (++runC == 5) penalty += 3; else if (runC > 5) penalty++; }
                else runC = 1;
            }
        }

        // N2: 2x2 blocks
        for (var r = 0; r < n - 1; r++)
            for (var c = 0; c < n - 1; c++)
                if (m[r, c] == m[r, c + 1] && m[r, c] == m[r + 1, c] && m[r, c] == m[r + 1, c + 1])
                    penalty += 3;

        // N4: dark/light balance
        var dark = 0;
        for (var r = 0; r < n; r++)
            for (var c = 0; c < n; c++)
                if (m[r, c]) dark++;
        var total = n * n;
        var k = Math.Abs(dark * 20 - total * 10) / total;
        penalty += k * 10;
        return penalty;
    }
}
