using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// 用法:
//   analyze <png>                       打印非白像素包围盒 + 粗密度图，用来定位水印
//   build <src.png> <out.ico> [inset]   按包围盒裁掉边距后缩多尺寸打成 .ico，inset 再往里收几个像素
//   png <src.png> <out.png> <边长> [inset]  出单张方形 PNG

var mode = args.Length > 0 ? args[0] : "";
switch (mode)
{
    case "analyze":
        return Analyze(args[1]);
    case "build":
        return Build(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 0);
    case "png":
        return Single(args[1], args[2], int.Parse(args[3]), args.Length > 4 ? int.Parse(args[4]) : 0);
    default:
        Console.Error.WriteLine("用法: analyze <png> | build <src.png> <out.ico> [inset] | png <src> <out> <size> [inset]");
        return 1;
}

static int Analyze(string path)
{
    using var bmp = new Bitmap(path);
    var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
    var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    var pixels = new int[data.Stride * data.Height / 4];
    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
    bmp.UnlockBits(data);

    var minX = bmp.Width;
    var minY = bmp.Height;
    var maxX = -1;
    var maxY = -1;
    var nonWhite = 0;
    for (var y = 0; y < bmp.Height; y++)
    {
        for (var x = 0; x < bmp.Width; x++)
        {
            var v = pixels[y * data.Stride / 4 + x];
            var r = v & 0xFF;
            var g = (v >> 8) & 0xFF;
            var b = (v >> 16) & 0xFF;
            if (Math.Min(r, Math.Min(g, b)) >= 243)
            {
                continue;
            }

            nonWhite++;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
    }

    Console.WriteLine($"{bmp.Width}x{bmp.Height} 非白像素 {nonWhite} 包围盒 x {minX}..{maxX} y {minY}..{maxY}");

    const int Cols = 64;
    const int Rows = 32;
    for (var ry = 0; ry < Rows; ry++)
    {
        var line = new char[Cols];
        for (var cx = 0; cx < Cols; cx++)
        {
            var x0 = cx * bmp.Width / Cols;
            var x1 = (cx + 1) * bmp.Width / Cols;
            var y0 = ry * bmp.Height / Rows;
            var y1 = (ry + 1) * bmp.Height / Rows;
            var dark = 0;
            for (var y = y0; y < y1; y += 4)
            {
                for (var x = x0; x < x1; x += 4)
                {
                    var v = pixels[y * data.Stride / 4 + x];
                    if (Math.Min(v & 0xFF, Math.Min((v >> 8) & 0xFF, (v >> 16) & 0xFF)) < 200)
                    {
                        dark++;
                    }
                }
            }

            line[cx] = dark switch { 0 => '.', < 3 => '-', < 8 => '+', < 16 => '#', _ => '@' };
        }

        Console.WriteLine(new string(line));
    }

    return 0;
}

static Bitmap CropToBadge(string path, int inset)
{
    using var src = new Bitmap(path);
    var box = BadgeBox(src);
    box = new Rectangle(box.X + inset, box.Y + inset, box.Width - inset * 2, box.Height - inset * 2);
    if (box.Width <= 0 || box.Height <= 0)
    {
        throw new InvalidOperationException($"裁剪框无效：{box}");
    }

    // 图标必须是正方形，取短边居中，免得拉伸变形。
    var side = Math.Min(box.Width, box.Height);
    var square = new Rectangle(
        box.X + (box.Width - side) / 2,
        box.Y + (box.Height - side) / 2,
        side,
        side);
    var crop = new Bitmap(side, side, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(crop))
    {
        g.DrawImage(src, new Rectangle(0, 0, side, side), square, GraphicsUnit.Pixel);
    }

    return crop;
}

static Rectangle BadgeBox(Bitmap src)
{
    var rect = new Rectangle(0, 0, src.Width, src.Height);
    var data = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    var pixels = new int[data.Stride * data.Height / 4];
    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
    src.UnlockBits(data);

    var stride = data.Stride / 4;
    var rowHits = new int[src.Height];
    var colHits = new int[src.Width];
    for (var y = 0; y < src.Height; y++)
    {
        for (var x = 0; x < src.Width; x++)
        {
            var v = pixels[y * stride + x];
            if (Math.Min(v & 0xFF, Math.Min((v >> 8) & 0xFF, (v >> 16) & 0xFF)) >= 243)
            {
                continue;
            }

            rowHits[y]++;
            colHits[x]++;
        }
    }

    // 水印只占角上一小撮像素，按整行整列的覆盖率切，就能把徽章本体单独框出来。
    var rowGate = src.Width / 4;
    var colGate = src.Height / 4;
    var minX = src.Width;
    var minY = src.Height;
    var maxX = -1;
    var maxY = -1;
    for (var y = 0; y < src.Height; y++)
    {
        if (rowHits[y] > rowGate)
        {
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
    }

    for (var x = 0; x < src.Width; x++)
    {
        if (colHits[x] > colGate)
        {
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
        }
    }

    Console.WriteLine(
        $"徽章 {maxX - minX + 1}x{maxY - minY + 1} @ ({minX},{minY})，整图非白包围盒另算");
    return new Rectangle(minX, minY, Math.Max(0, maxX - minX + 1), Math.Max(0, maxY - minY + 1));
}

static Bitmap Scale(Bitmap src, int size)
{
    var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(dst);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.SmoothingMode = SmoothingMode.HighQuality;
    g.DrawImage(src, new Rectangle(0, 0, size, size));
    return dst;
}

static byte[] PngOf(Image image)
{
    using var ms = new MemoryStream();
    image.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

static int Single(string src, string outPath, int size, int inset)
{
    using var crop = CropToBadge(src, inset);
    using var scaled = Scale(crop, size);
    scaled.Save(outPath, ImageFormat.Png);
    Console.WriteLine($"{outPath} {size}x{size}（裁剪自 {crop.Width}x{crop.Height}）");
    return 0;
}

static int Build(string srcPath, string outPath, int inset)
{
    var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
    using var crop = CropToBadge(srcPath, inset);
    var blobs = new List<byte[]>();
    using (crop)
    {
        foreach (var size in sizes)
        {
            using var scaled = Scale(crop, size);
            blobs.Add(PngOf(scaled));
        }
    }

    using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
    using var w = new BinaryWriter(fs);
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)sizes.Length);

    var offset = 6 + sizes.Length * 16;
    for (var i = 0; i < sizes.Length; i++)
    {
        var size = sizes[i];
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write((uint)blobs[i].Length);
        w.Write((uint)offset);
        offset += blobs[i].Length;
    }

    foreach (var blob in blobs)
    {
        w.Write(blob);
    }

    Console.WriteLine($"{outPath} 写入 {sizes.Length} 个尺寸，共 {fs.Length} 字节");
    return 0;
}
