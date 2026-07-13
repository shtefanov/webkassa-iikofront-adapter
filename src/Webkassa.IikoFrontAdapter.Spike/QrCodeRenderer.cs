using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace Webkassa.IikoFrontAdapter.Spike;

internal static class QrCodeRenderer
{
    private static readonly VersionInfo[] Versions =
    {
        new VersionInfo(1, 19, 7, new[] { 19 }),
        new VersionInfo(2, 34, 10, new[] { 34 }),
        new VersionInfo(3, 55, 15, new[] { 55 }),
        new VersionInfo(4, 80, 20, new[] { 80 }),
        new VersionInfo(5, 108, 26, new[] { 108 }),
        new VersionInfo(6, 136, 18, new[] { 68, 68 }),
        new VersionInfo(7, 156, 20, new[] { 78, 78 }),
        new VersionInfo(8, 194, 24, new[] { 97, 97 }),
        new VersionInfo(9, 232, 30, new[] { 116, 116 }),
        new VersionInfo(10, 274, 18, new[] { 68, 68, 69, 69 }),
    };

    public static Bitmap Render(string value, int targetPixels)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("QR value is empty.", nameof(value));

        var modules = Encode(value);
        var size = modules.GetLength(0);
        const int quietZone = 4;
        var scale = Math.Max(2, targetPixels / (size + quietZone * 2));
        var pixels = (size + quietZone * 2) * scale;
        var bitmap = new Bitmap(pixels, pixels);

        using (var graphics = Graphics.FromImage(bitmap))
        using (var white = new SolidBrush(Color.White))
        using (var black = new SolidBrush(Color.Black))
        {
            graphics.FillRectangle(white, 0, 0, pixels, pixels);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (!modules[y, x])
                        continue;

                    graphics.FillRectangle(
                        black,
                        (x + quietZone) * scale,
                        (y + quietZone) * scale,
                        scale,
                        scale);
                }
            }
        }

        return bitmap;
    }

    private static bool[,] Encode(string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        var version = ChooseVersion(data.Length);
        var dataCodewords = BuildDataCodewords(data, version);
        var allCodewords = AddErrorCorrection(dataCodewords, version);

        var bestMatrix = default(bool[,]);
        var bestPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            var matrix = new QrMatrix(version.Version);
            matrix.DrawFunctionPatterns();
            matrix.DrawCodewords(allCodewords, mask);
            matrix.DrawFormatBits(mask);
            if (version.Version >= 7)
                matrix.DrawVersionBits();

            var penalty = matrix.GetPenaltyScore();
            if (penalty >= bestPenalty)
                continue;

            bestPenalty = penalty;
            bestMatrix = matrix.Modules;
        }

        return bestMatrix ?? throw new InvalidOperationException("Unable to render QR code.");
    }

    private static VersionInfo ChooseVersion(int byteLength)
    {
        foreach (var version in Versions)
        {
            var countBits = version.Version <= 9 ? 8 : 16;
            var requiredBits = 4 + countBits + byteLength * 8;
            if (requiredBits <= version.DataCodewords * 8)
                return version;
        }

        throw new InvalidOperationException("Webkassa QR value is too long for the built-in PDF fallback renderer.");
    }

    private static byte[] BuildDataCodewords(byte[] data, VersionInfo version)
    {
        var bits = new List<bool>();
        AppendBits(bits, 0x4, 4);
        AppendBits(bits, data.Length, version.Version <= 9 ? 8 : 16);
        foreach (var value in data)
            AppendBits(bits, value, 8);

        var capacityBits = version.DataCodewords * 8;
        AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count));
        while (bits.Count % 8 != 0)
            bits.Add(false);

        var result = new List<byte>();
        for (var i = 0; i < bits.Count; i += 8)
        {
            var value = 0;
            for (var j = 0; j < 8; j++)
                value = (value << 1) | (bits[i + j] ? 1 : 0);
            result.Add((byte)value);
        }

        for (var pad = 0; result.Count < version.DataCodewords; pad++)
            result.Add((byte)(pad % 2 == 0 ? 0xEC : 0x11));

        return result.ToArray();
    }

    private static byte[] AddErrorCorrection(byte[] dataCodewords, VersionInfo version)
    {
        var blocks = new List<byte[]>();
        var eccBlocks = new List<byte[]>();
        var offset = 0;
        foreach (var length in version.BlockDataCodewords)
        {
            var block = new byte[length];
            Array.Copy(dataCodewords, offset, block, 0, length);
            offset += length;
            blocks.Add(block);
            eccBlocks.Add(ReedSolomon.ComputeRemainder(block, version.EccCodewordsPerBlock));
        }

        var result = new List<byte>();
        var maxDataBlockLength = 0;
        foreach (var block in blocks)
            maxDataBlockLength = Math.Max(maxDataBlockLength, block.Length);

        for (var i = 0; i < maxDataBlockLength; i++)
        {
            foreach (var block in blocks)
            {
                if (i < block.Length)
                    result.Add(block[i]);
            }
        }

        for (var i = 0; i < version.EccCodewordsPerBlock; i++)
        {
            foreach (var block in eccBlocks)
                result.Add(block[i]);
        }

        return result.ToArray();
    }

    private static void AppendBits(ICollection<bool> bits, int value, int length)
    {
        for (var i = length - 1; i >= 0; i--)
            bits.Add(((value >> i) & 1) != 0);
    }

    private sealed class VersionInfo
    {
        public VersionInfo(int version, int dataCodewords, int eccCodewordsPerBlock, int[] blockDataCodewords)
        {
            Version = version;
            DataCodewords = dataCodewords;
            EccCodewordsPerBlock = eccCodewordsPerBlock;
            BlockDataCodewords = blockDataCodewords;
        }

        public int Version { get; }
        public int DataCodewords { get; }
        public int EccCodewordsPerBlock { get; }
        public int[] BlockDataCodewords { get; }
    }

    private sealed class QrMatrix
    {
        private static readonly int[][] AlignmentPatternPositions =
        {
            Array.Empty<int>(),
            Array.Empty<int>(),
            new[] { 6, 18 },
            new[] { 6, 22 },
            new[] { 6, 26 },
            new[] { 6, 30 },
            new[] { 6, 34 },
            new[] { 6, 22, 38 },
            new[] { 6, 24, 42 },
            new[] { 6, 26, 46 },
            new[] { 6, 28, 50 },
        };

        private readonly int version;
        private readonly bool[,] isFunction;

        public QrMatrix(int version)
        {
            this.version = version;
            Size = version * 4 + 17;
            Modules = new bool[Size, Size];
            isFunction = new bool[Size, Size];
        }

        public int Size { get; }
        public bool[,] Modules { get; }

        public void DrawFunctionPatterns()
        {
            DrawFinderPattern(3, 3);
            DrawFinderPattern(Size - 4, 3);
            DrawFinderPattern(3, Size - 4);

            for (var i = 0; i < Size; i++)
            {
                if (!isFunction[6, i])
                    SetFunctionModule(i, 6, i % 2 == 0);
                if (!isFunction[i, 6])
                    SetFunctionModule(6, i, i % 2 == 0);
            }

            foreach (var y in AlignmentPatternPositions[version])
            {
                foreach (var x in AlignmentPatternPositions[version])
                {
                    if (isFunction[y, x])
                        continue;
                    DrawAlignmentPattern(x, y);
                }
            }

            SetFunctionModule(8, Size - 8, true);
            DrawFormatBits(0);
            if (version >= 7)
                DrawVersionBits();
        }

        public void DrawCodewords(byte[] codewords, int mask)
        {
            var bits = new List<bool>(codewords.Length * 8);
            foreach (var value in codewords)
            {
                for (var i = 7; i >= 0; i--)
                    bits.Add(((value >> i) & 1) != 0);
            }

            var bitIndex = 0;
            var upward = true;
            for (var right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                    right--;

                for (var vertical = 0; vertical < Size; vertical++)
                {
                    var y = upward ? Size - 1 - vertical : vertical;
                    for (var column = 0; column < 2; column++)
                    {
                        var x = right - column;
                        if (isFunction[y, x])
                            continue;

                        var bit = bitIndex < bits.Count && bits[bitIndex];
                        bitIndex++;
                        Modules[y, x] = bit ^ GetMaskBit(mask, x, y);
                    }
                }

                upward = !upward;
            }
        }

        public void DrawFormatBits(int mask)
        {
            var data = (1 << 3) | mask; // ECC level L.
            var bits = CalculateBchCode(data, 0x537, 10) ^ 0x5412;
            for (var i = 0; i <= 5; i++)
                SetFunctionModule(8, i, GetBit(bits, i));
            SetFunctionModule(8, 7, GetBit(bits, 6));
            SetFunctionModule(8, 8, GetBit(bits, 7));
            SetFunctionModule(7, 8, GetBit(bits, 8));
            for (var i = 9; i < 15; i++)
                SetFunctionModule(14 - i, 8, GetBit(bits, i));

            for (var i = 0; i < 8; i++)
                SetFunctionModule(Size - 1 - i, 8, GetBit(bits, i));
            for (var i = 8; i < 15; i++)
                SetFunctionModule(8, Size - 15 + i, GetBit(bits, i));
        }

        public void DrawVersionBits()
        {
            var bits = CalculateBchCode(version, 0x1F25, 12);
            for (var i = 0; i < 18; i++)
            {
                var bit = GetBit(bits, i);
                var a = Size - 11 + i % 3;
                var b = i / 3;
                SetFunctionModule(a, b, bit);
                SetFunctionModule(b, a, bit);
            }
        }

        public int GetPenaltyScore()
        {
            var result = 0;
            for (var y = 0; y < Size; y++)
            {
                var runColor = false;
                var runLength = 0;
                for (var x = 0; x < Size; x++)
                    AddRunPenalty(Modules[y, x], ref runColor, ref runLength, ref result);
                AddEndRunPenalty(runLength, ref result);
            }

            for (var x = 0; x < Size; x++)
            {
                var runColor = false;
                var runLength = 0;
                for (var y = 0; y < Size; y++)
                    AddRunPenalty(Modules[y, x], ref runColor, ref runLength, ref result);
                AddEndRunPenalty(runLength, ref result);
            }

            for (var y = 0; y < Size - 1; y++)
            {
                for (var x = 0; x < Size - 1; x++)
                {
                    var color = Modules[y, x];
                    if (color == Modules[y, x + 1] && color == Modules[y + 1, x] && color == Modules[y + 1, x + 1])
                        result += 3;
                }
            }

            var dark = 0;
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    if (Modules[y, x])
                        dark++;
                }
            }

            var total = Size * Size;
            var k = Math.Abs(dark * 20 - total * 10) / total;
            return result + k * 10;
        }

        private void DrawFinderPattern(int centerX, int centerY)
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var x = centerX + dx;
                    var y = centerY + dy;
                    if (x < 0 || x >= Size || y < 0 || y >= Size)
                        continue;

                    var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunctionModule(x, y, distance != 2 && distance != 4);
                }
            }
        }

        private void DrawAlignmentPattern(int centerX, int centerY)
        {
            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    SetFunctionModule(centerX + dx, centerY + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }
        }

        private void SetFunctionModule(int x, int y, bool dark)
        {
            Modules[y, x] = dark;
            isFunction[y, x] = true;
        }

        private static bool GetMaskBit(int mask, int x, int y)
        {
            switch (mask)
            {
                case 0:
                    return (x + y) % 2 == 0;
                case 1:
                    return y % 2 == 0;
                case 2:
                    return x % 3 == 0;
                case 3:
                    return (x + y) % 3 == 0;
                case 4:
                    return (y / 2 + x / 3) % 2 == 0;
                case 5:
                    return (x * y % 2 + x * y % 3) == 0;
                case 6:
                    return (x * y % 2 + x * y % 3) % 2 == 0;
                case 7:
                    return ((x + y) % 2 + x * y % 3) % 2 == 0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mask));
            }
        }

        private static void AddRunPenalty(bool color, ref bool runColor, ref int runLength, ref int result)
        {
            if (runLength == 0)
            {
                runColor = color;
                runLength = 1;
                return;
            }

            if (color == runColor)
            {
                runLength++;
                if (runLength == 5)
                    result += 3;
                else if (runLength > 5)
                    result++;
                return;
            }

            AddEndRunPenalty(runLength, ref result);
            runColor = color;
            runLength = 1;
        }

        private static void AddEndRunPenalty(int runLength, ref int result)
        {
            if (runLength >= 5)
                result += runLength - 2;
        }

        private static int CalculateBchCode(int value, int generator, int degree)
        {
            value <<= degree;
            while (GetBitLength(value) >= degree + 1)
                value ^= generator << (GetBitLength(value) - degree - 1);
            return value;
        }

        private static int GetBitLength(int value)
        {
            var result = 0;
            while (value != 0)
            {
                value >>= 1;
                result++;
            }

            return result;
        }

        private static bool GetBit(int value, int index)
        {
            return ((value >> index) & 1) != 0;
        }
    }

    private static class ReedSolomon
    {
        private static readonly byte[] Exp = new byte[512];
        private static readonly byte[] Log = new byte[256];

        static ReedSolomon()
        {
            var x = 1;
            for (var i = 0; i < 255; i++)
            {
                Exp[i] = (byte)x;
                Log[x] = (byte)i;
                x <<= 1;
                if (x >= 0x100)
                    x ^= 0x11D;
            }

            for (var i = 255; i < Exp.Length; i++)
                Exp[i] = Exp[i - 255];
        }

        public static byte[] ComputeRemainder(byte[] data, int degree)
        {
            var divisor = ComputeDivisor(degree);
            var result = new byte[degree];
            foreach (var value in data)
            {
                var factor = (byte)(value ^ result[0]);
                Array.Copy(result, 1, result, 0, degree - 1);
                result[degree - 1] = 0;
                for (var i = 0; i < degree; i++)
                    result[i] ^= Multiply(divisor[i], factor);
            }

            return result;
        }

        private static byte[] ComputeDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            var root = 1;
            for (var i = 0; i < degree; i++)
            {
                for (var j = 0; j < degree; j++)
                {
                    result[j] = Multiply(result[j], (byte)root);
                    if (j + 1 < degree)
                        result[j] ^= result[j + 1];
                }

                root = Multiply((byte)root, 0x02);
            }

            return result;
        }

        private static byte Multiply(byte x, byte y)
        {
            if (x == 0 || y == 0)
                return 0;
            return Exp[Log[x] + Log[y]];
        }
    }
}
