using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PwshMcp.Tray;

public enum Glyph { Offline, Idle, Active, App }

/// <summary>The badge mark, drawn as vectors at each pixel size. One source for tray, window and exe icons.</summary>
public static class Glyphs
{
    public static readonly int[] TraySizes = [16, 20, 24, 32, 40, 48];
    public static readonly int[] AppSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    static readonly Color DarkFg = Color.FromArgb(255, 255, 255), LightFg = Color.FromArgb(26, 26, 26);
    static readonly Color AmberOnDark = Color.FromArgb(255, 184, 48), AmberOnLight = Color.FromArgb(196, 116, 0);
    static readonly Color Indigo = Color.FromArgb(51, 70, 200), IndigoDeep = Color.FromArgb(36, 50, 150);

    // Geometry in a 16-unit box; scaled per size.
    static RectangleF Body(float k) => new(3.5f * k, 1.5f * k, 9f * k, 13f * k);
    static RectangleF Slot(float k) => new(6f * k, 3.7f * k, 4f * k, 1.5f * k);

    public static Bitmap Draw(Glyph g, int size, bool lightBar, bool pending)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var gr = Graphics.FromImage(bmp);
        gr.SmoothingMode = SmoothingMode.AntiAlias;
        gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
        gr.Clear(Color.Transparent);
        float k = size / 16f, stroke = MathF.Max(1f, 1.25f * k);
        var fg = lightBar ? LightFg : DarkFg;
        var body = Body(k);
        switch (g)
        {
            case Glyph.Idle:
                Outline(gr, body, k, stroke, fg);
                break;
            case Glyph.Offline:
                var dim = Color.FromArgb(150, fg);
                Outline(gr, body, k, stroke, dim);
                Clear(gr, () => Slash(k), stroke * 2.6f);
                using (var pen = new Pen(dim, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    gr.DrawLine(pen, Slash(k).a, Slash(k).b);
                break;
            case Glyph.Active:
                Filled(gr, body, k, fg, false);
                break;
            case Glyph.App:
                Filled(gr, body, k, Indigo, size >= 24);
                break;
        }
        if (pending) Dot(gr, k, lightBar ? AmberOnLight : AmberOnDark);
        return bmp;
    }

    static (PointF a, PointF b) Slash(float k) => (new PointF(2.2f * k, 13.8f * k), new PointF(13.8f * k, 2.2f * k));

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    static void Outline(Graphics gr, RectangleF body, float k, float stroke, Color c)
    {
        var inset = RectangleF.Inflate(body, -stroke / 2, -stroke / 2);
        using var path = Round(inset, 2.2f * k);
        using var pen = new Pen(c, stroke) { LineJoin = LineJoin.Round };
        gr.DrawPath(pen, path);
        using var slot = Round(Slot(k), 0.75f * k);
        using var b = new SolidBrush(c);
        gr.FillPath(b, slot);
    }

    static void Filled(Graphics gr, RectangleF body, float k, Color c, bool prompt)
    {
        using var path = Round(body, 2.2f * k);
        path.AddPath(Round(Slot(k), 0.75f * k), false);
        path.FillMode = FillMode.Alternate;
        if (prompt)
        {
            using var lg = new LinearGradientBrush(body, c, IndigoDeep, 90f);
            gr.FillPath(lg, path);
            using var pen = new Pen(Color.White, 1.1f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            gr.DrawLines(pen, [new PointF(6f * k, 8f * k), new PointF(8.1f * k, 9.7f * k), new PointF(6f * k, 11.4f * k)]);
            gr.DrawLine(pen, 8.9f * k, 11.6f * k, 10.4f * k, 11.6f * k);
        }
        else
        {
            using var b = new SolidBrush(c);
            gr.FillPath(b, path);
        }
    }

    static void Dot(Graphics gr, float k, Color c)
    {
        float r = 3.1f * k, ring = 1.3f * k, cx = 12.4f * k, cy = 3.6f * k;
        var old = gr.CompositingMode;
        gr.CompositingMode = CompositingMode.SourceCopy;
        using (var clear = new SolidBrush(Color.Transparent)) gr.FillEllipse(clear, cx - r - ring, cy - r - ring, (r + ring) * 2, (r + ring) * 2);
        gr.CompositingMode = old;
        using var b = new SolidBrush(c);
        gr.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
    }

    static void Clear(Graphics gr, Func<(PointF a, PointF b)> line, float width)
    {
        var old = gr.CompositingMode;
        gr.CompositingMode = CompositingMode.SourceCopy;
        var (a, b) = line();
        using var pen = new Pen(Color.Transparent, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        gr.DrawLine(pen, a, b);
        gr.CompositingMode = old;
    }

    public static byte[] Png(Glyph g, int size, bool lightBar = false, bool pending = false)
    {
        using var bmp = Draw(g, size, lightBar, pending);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Packs PNG frames into an .ico container (Vista+ PNG entries).</summary>
    public static byte[] Ico(IReadOnlyList<(int size, byte[] png)> frames)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0); w.Write((short)1); w.Write((short)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
            w.Write(png.Length); w.Write(offset); offset += png.Length;
        }
        foreach (var (_, png) in frames) w.Write(png);
        return ms.ToArray();
    }

    public static Icon TrayIcon(Glyph g, bool lightBar, bool pending)
    {
        var frames = TraySizes.Select(s => (s, Png(g, s, lightBar, pending))).ToList();
        using var ms = new MemoryStream(Ico(frames));
        return new Icon(ms, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    public static byte[] AppIco() => Ico(AppSizes.Select(s => (s, Png(Glyph.App, s))).ToList());

    /// <summary>Writes app.ico and every tray state PNG for review and packaging.</summary>
    public static void Export(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, Names.IcoFile), AppIco());
        foreach (var g in new[] { Glyph.Offline, Glyph.Idle, Glyph.Active })
            foreach (var light in new[] { false, true })
                foreach (var pending in new[] { false, true })
                    foreach (var s in TraySizes)
                    {
                        var name = $"{g.ToString().ToLowerInvariant()}{(pending ? "-pending" : "")}-{(light ? "light" : "dark")}";
                        File.WriteAllBytes(Path.Combine(dir, string.Format(Names.PngPattern, name, s)), Png(g, s, light, pending));
                    }
    }
}
