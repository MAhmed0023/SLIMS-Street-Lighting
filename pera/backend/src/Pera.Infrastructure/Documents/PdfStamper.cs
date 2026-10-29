using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;
using Pera.Application.Interfaces;

namespace Pera.Infrastructure.Documents;

/// <summary>Draws visible signature blocks on a copy of a PDF with PDFsharp (MIT). The input bytes are never modified.
/// A PDF that cannot be opened (encrypted, damaged) returns null and the signature is recorded without a stamp.</summary>
public sealed class PdfStamper : IPdfStamper
{
    static readonly object Gate = new();
    static bool _fontsReady;

    static void EnsureFonts()
    {
        lock (Gate)
        {
            if (_fontsReady) return;
            if (OperatingSystem.IsWindows()) GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            else if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new FileFontResolver();
            _fontsReady = true;
        }
    }

    public int? PageCount(byte[] pdf)
    {
        try { using var ms = new MemoryStream(pdf); using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import); return doc.PageCount; }
        catch { return null; }
    }

    public byte[]? Stamp(byte[] pdf, IReadOnlyList<StampSpec> stamps)
    {
        try
        {
            EnsureFonts();
            using var input = new MemoryStream(pdf);
            using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            var ink = XColor.FromArgb(255, 91, 60, 83);
            foreach (var s in stamps)
            {
                var page = doc.Pages[Math.Clamp(s.Page, 1, doc.PageCount) - 1];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                double pw = page.Width.Point, ph = page.Height.Point;
                var rect = new XRect(s.X * pw, s.Y * ph, Math.Max(40, s.W * pw), Math.Max(18, s.H * ph));
                gfx.DrawRectangle(new XPen(ink, 0.8), new XSolidBrush(XColor.FromArgb(28, 113, 75, 103)), rect);
                var size = Math.Clamp(rect.Height / (s.Lines.Count * 1.3 + 0.6), 4.5, 9.5);
                var bold = new XFont("PeraSans", size, XFontStyleEx.Bold);
                var regular = new XFont("PeraSans", size, XFontStyleEx.Regular);
                var y = rect.Top + size * 1.15;
                for (var i = 0; i < s.Lines.Count; i++)
                {
                    var text = Fit(gfx, s.Lines[i], i == 0 ? bold : regular, rect.Width - 8);
                    gfx.DrawString(text, i == 0 ? bold : regular, new XSolidBrush(ink), new XPoint(rect.Left + 4, y));
                    y += size * 1.3;
                }
            }
            using var output = new MemoryStream();
            doc.Save(output);
            return output.ToArray();
        }
        catch { return null; }
    }

    static string Fit(XGraphics g, string text, XFont font, double width)
    {
        if (g.MeasureString(text, font).Width <= width) return text;
        while (text.Length > 4 && g.MeasureString(text + "…", font).Width > width) text = text[..^1];
        return text + "…";
    }

    /// <summary>Any family name resolves to a sans-serif font found on the server (DejaVu or Liberation on Linux).</summary>
    sealed class FileFontResolver : IFontResolver
    {
        static readonly string[] Regular = ["/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/dejavu/DejaVuSans.ttf", "/Library/Fonts/Arial.ttf"];
        static readonly string[] Bold = ["/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf", "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf", "/Library/Fonts/Arial Bold.ttf"];
        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(isBold ? "pera-bold" : "pera-regular");
        public byte[]? GetFont(string faceName)
        {
            var path = (faceName == "pera-bold" ? Bold : Regular).Concat(Regular).FirstOrDefault(File.Exists);
            return path is null ? null : File.ReadAllBytes(path);
        }
    }
}
