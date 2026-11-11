using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ProcessRequestEndPoint.Pdf
{
    public enum PdfFont { Regular, Bold, Mono }

    /// <summary>
    /// Minimal flowing-layout PDF writer: headings, wrapped paragraphs, two-column key/value tables,
    /// rules, a coloured title band and "Page x of y" footers on US Letter pages.
    ///
    /// It uses only the PDF standard Type 1 fonts (Helvetica, Helvetica-Bold, Courier), which every PDF
    /// reader provides, so no font files are needed on the server and nothing is embedded. Text is
    /// WinAnsi-encoded: Latin-1 plus common punctuation; other characters are replaced with '?'.
    /// </summary>
    public sealed class SimplePdfDocument
    {
        public const double PageWidth = 612, PageHeight = 792;
        private const double Margin = 54, FooterHeight = 36;

        private readonly List<StringBuilder> _pages = new();
        private StringBuilder _page = null!;
        private double _y;

        public string Title { get; }
        public string FooterText { get; set; } = string.Empty;

        public SimplePdfDocument(string title)
        {
            Title = title;
            NewPage();
        }

        private static double ContentWidth => PageWidth - 2 * Margin;

        public void NewPage()
        {
            _page = new StringBuilder();
            _pages.Add(_page);
            _y = PageHeight - Margin;
        }

        private void Ensure(double height)
        {
            if (_y - height < Margin + FooterHeight) NewPage();
        }

        // ---------------- building blocks ----------------

        /// <summary>A filled band across the top of the current page with a title and subtitle.</summary>
        public void TitleBand(string title, string? subtitle, (double R, double G, double B) color)
        {
            const double height = 64;
            var top = PageHeight - Margin + 18;
            Rect(Margin - 18, top - height, ContentWidth + 36, height, color);
            Text(Margin, top - 28, title, PdfFont.Bold, 18, (1, 1, 1));
            if (!string.IsNullOrWhiteSpace(subtitle)) Text(Margin, top - 48, subtitle!, PdfFont.Regular, 11, (0.92, 0.95, 1));
            _y = top - height - 22;
        }

        public void Heading(string text, double size = 13)
        {
            Ensure(size + 30);
            _y -= 8;
            Text(Margin, _y - size, text, PdfFont.Bold, size, (0.10, 0.20, 0.38));
            _y -= size + 6;
            Line(Margin, _y, Margin + ContentWidth, _y, 0.8, (0.75, 0.80, 0.88));
            _y -= 10;
        }

        public void Paragraph(string text, double size = 10, PdfFont font = PdfFont.Regular, (double R, double G, double B)? color = null)
        {
            foreach (var line in Wrap(text, font, size, ContentWidth))
            {
                Ensure(size * 1.4);
                Text(Margin, _y - size, line, font, size, color ?? (0.12, 0.12, 0.12));
                _y -= size * 1.4;
            }
            _y -= 4;
        }

        public void Spacer(double height) => _y -= height;

        /// <summary>Letterhead: a brand-coloured rule, the company name on the left and contact lines on the right.</summary>
        public void Letterhead(string company, IReadOnlyList<string> contactLines, (double R, double G, double B) color)
        {
            var top = PageHeight - Margin + 10;
            Rect(Margin, top, ContentWidth, 4, color);
            Text(Margin, top - 30, company, PdfFont.Bold, 20, color);
            var y = top - 18;
            foreach (var line in contactLines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var w = Measure(line, PdfFont.Regular, 8.5);
                Text(Margin + ContentWidth - w, y, line, PdfFont.Regular, 8.5, (0.38, 0.40, 0.46));
                y -= 11;
            }
            _y = Math.Min(top - 46, y - 4);
            Line(Margin, _y, Margin + ContentWidth, _y, 0.6, (0.82, 0.84, 0.88));
            _y -= 26;
        }

        /// <summary>A single right-aligned line (e.g. the letter date).</summary>
        public void RightAligned(string text, double size = 10)
        {
            Ensure(size * 1.4);
            Text(Margin + ContentWidth - Measure(text, PdfFont.Regular, size), _y - size, text, PdfFont.Regular, size, (0.12, 0.12, 0.12));
            _y -= size * 1.4;
        }

        /// <summary>Consecutive lines with no paragraph gap (address blocks, signatures).</summary>
        public void Lines(IEnumerable<string> lines, double size = 10, PdfFont font = PdfFont.Regular)
        {
            foreach (var line in lines)
            {
                foreach (var wrapped in Wrap(line, font, size, ContentWidth))
                {
                    Ensure(size * 1.35);
                    Text(Margin, _y - size, wrapped, font, size, (0.12, 0.12, 0.12));
                    _y -= size * 1.35;
                }
            }
        }

        /// <summary>
        /// Label / value rows with alternate shading. Values wrap within their column; a row is never split
        /// across pages (callers keep values short enough to fit on a page).
        /// </summary>
        public void KeyValueTable(IEnumerable<(string Label, string? Value)> rows, double labelWidth = 150, double size = 10)
        {
            var valueX = Margin + labelWidth + 10;
            var valueWidth = ContentWidth - labelWidth - 10;
            var lineHeight = size * 1.35;
            var shade = false;
            foreach (var (label, value) in rows)
            {
                var valueLines = Wrap(string.IsNullOrWhiteSpace(value) ? "—" : value!, PdfFont.Regular, size, valueWidth);
                var labelLines = Wrap(label, PdfFont.Bold, size, labelWidth);
                var lineCount = Math.Max(valueLines.Count, labelLines.Count);
                var rowHeight = lineCount * lineHeight + 6;
                Ensure(rowHeight);
                if (shade) Rect(Margin - 4, _y - rowHeight, ContentWidth + 8, rowHeight, (0.95, 0.96, 0.98));
                shade = !shade;
                var baseline = _y - 3 - size;
                for (var i = 0; i < lineCount; i++)
                {
                    if (i < labelLines.Count) Text(Margin, baseline, labelLines[i], PdfFont.Bold, size, (0.30, 0.34, 0.42));
                    if (i < valueLines.Count) Text(valueX, baseline, valueLines[i], PdfFont.Regular, size, (0.10, 0.10, 0.10));
                    baseline -= lineHeight;
                }
                _y -= rowHeight;
            }
            _y -= 8;
        }

        // ---------------- drawing primitives ----------------

        private void Text(double x, double y, string text, PdfFont font, double size, (double R, double G, double B) color)
        {
            _page.Append(F(color.R)).Append(' ').Append(F(color.G)).Append(' ').Append(F(color.B)).Append(" rg BT /")
                 .Append(FontResource(font)).Append(' ').Append(F(size)).Append(" Tf ")
                 .Append(F(x)).Append(' ').Append(F(y)).Append(" Td (").Append(Escape(text)).Append(") Tj ET\n");
        }

        private void Rect(double x, double y, double w, double h, (double R, double G, double B) color)
        {
            _page.Append(F(color.R)).Append(' ').Append(F(color.G)).Append(' ').Append(F(color.B)).Append(" rg ")
                 .Append(F(x)).Append(' ').Append(F(y)).Append(' ').Append(F(w)).Append(' ').Append(F(h)).Append(" re f\n");
        }

        private void Line(double x1, double y1, double x2, double y2, double width, (double R, double G, double B) color)
        {
            _page.Append(F(color.R)).Append(' ').Append(F(color.G)).Append(' ').Append(F(color.B)).Append(" RG ")
                 .Append(F(width)).Append(" w ").Append(F(x1)).Append(' ').Append(F(y1)).Append(" m ")
                 .Append(F(x2)).Append(' ').Append(F(y2)).Append(" l S\n");
        }

        private static string FontResource(PdfFont font) => font switch { PdfFont.Bold => "F2", PdfFont.Mono => "F3", _ => "F1" };

        private static string F(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

        // ---------------- text measurement and wrapping ----------------

        /// <summary>Width of <paramref name="text"/> in points (standard Helvetica / Courier metrics).</summary>
        public static double Measure(string text, PdfFont font, double size)
        {
            if (font == PdfFont.Mono) return text.Length * 600 * size / 1000;
            var widths = font == PdfFont.Bold ? HelveticaBoldWidths : HelveticaWidths;
            double total = 0;
            foreach (var ch in text)
            {
                var code = ch;
                total += code >= 32 && code <= 126 ? widths[code - 32] : 556;
            }
            return total * size / 1000;
        }

        /// <summary>Greedy word wrap; words longer than the width (ids, base64) are broken by character.</summary>
        public static List<string> Wrap(string text, PdfFont font, double size, double width)
        {
            var lines = new List<string>();
            foreach (var paragraph in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                var line = new StringBuilder();
                foreach (var word in paragraph.Split(' '))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (Measure(candidate, font, size) <= width) { line.Clear().Append(candidate); continue; }
                    if (line.Length > 0) { lines.Add(line.ToString()); line.Clear(); }
                    var rest = word;
                    while (Measure(rest, font, size) > width && rest.Length > 1)
                    {
                        var take = rest.Length;
                        while (take > 1 && Measure(rest.Substring(0, take), font, size) > width) take--;
                        lines.Add(rest.Substring(0, take));
                        rest = rest.Substring(take);
                    }
                    line.Append(rest);
                }
                lines.Add(line.ToString());
            }
            return lines;
        }

        /// <summary>PDF literal-string body: WinAnsi bytes, with ( ) \ escaped and bytes ≥ 128 as octal.</summary>
        private static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            foreach (var ch in text)
            {
                var b = ToWinAnsi(ch);
                if (b == '(' || b == ')' || b == '\\') sb.Append('\\').Append((char)b);
                else if (b < 32 || b >= 127) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                else sb.Append((char)b);
            }
            return sb.ToString();
        }

        private static readonly Dictionary<char, byte> WinAnsiExtras = new()
        {
            ['€'] = 0x80, ['‚'] = 0x82, ['„'] = 0x84, ['…'] = 0x85, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94,
            ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['™'] = 0x99, ['·'] = 0xB7
        };

        private static byte ToWinAnsi(char ch)
        {
            if (ch == '\t') return (byte)' ';
            if (WinAnsiExtras.TryGetValue(ch, out var b)) return b;
            if (ch < 32) return (byte)' ';
            if (ch <= 126 || (ch >= 160 && ch <= 255)) return (byte)ch;
            return (byte)'?';
        }

        // ---------------- serialization ----------------

        private bool _written;

        /// <summary>Adds the footers and serializes the document. Call once.</summary>
        public byte[] ToBytes(DateTime createdUtc)
        {
            if (_written) throw new InvalidOperationException("The document has already been written.");
            _written = true;
            // Footers last, once the page count is known.
            for (var i = 0; i < _pages.Count; i++)
            {
                _page = _pages[i]; // drawing primitives write to the current page
                var footer = (FooterText.Length > 0 ? FooterText + "   ·   " : string.Empty) + $"Page {i + 1} of {_pages.Count}";
                _pages[i].Append("0.8 0.82 0.86 RG 0.6 w ").Append(F(Margin)).Append(' ').Append(F(Margin + 18)).Append(" m ")
                    .Append(F(Margin + ContentWidth)).Append(' ').Append(F(Margin + 18)).Append(" l S\n");
                Text(Margin, Margin + 4, footer, PdfFont.Regular, 8, (0.42, 0.45, 0.52));
            }

            var objects = new List<byte[]>();
            int Add(byte[] body) { objects.Add(body); return objects.Count; }
            byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

            var catalogId = Add(Array.Empty<byte>()); // placeholders filled below
            var pagesId = Add(Array.Empty<byte>());
            var helvetica = Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
            var helveticaBold = Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
            var courier = Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"));

            var pageIds = new List<int>();
            foreach (var page in _pages)
            {
                var raw = Encoding.Latin1.GetBytes(page.ToString());
                byte[] compressed;
                using (var ms = new MemoryStream())
                {
                    using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
                    compressed = ms.ToArray();
                }
                var header = Ascii($"<< /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n");
                var content = Add(header.Concat(compressed).Concat(Ascii("\nendstream")).ToArray());
                pageIds.Add(Add(Ascii(
                    $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] " +
                    $"/Resources << /Font << /F1 {helvetica} 0 R /F2 {helveticaBold} 0 R /F3 {courier} 0 R >> >> /Contents {content} 0 R >>")));
            }

            objects[pagesId - 1] = Ascii($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => id + " 0 R"))}] /Count {pageIds.Count} >>");
            objects[catalogId - 1] = Ascii($"<< /Type /Catalog /Pages {pagesId} 0 R >>");
            var info = Add(Encoding.Latin1.GetBytes(
                $"<< /Title ({Escape(Title)}) /Producer (ECCO Correspondence Manager) /CreationDate (D:{createdUtc:yyyyMMddHHmmss}Z) >>"));

            using var output = new MemoryStream();
            void Write(byte[] bytes) => output.Write(bytes, 0, bytes.Length);
            Write(Ascii("%PDF-1.4\n"));
            Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }); // binary marker
            var offsets = new List<long>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(output.Position);
                Write(Ascii($"{i + 1} 0 obj\n"));
                Write(objects[i]);
                Write(Ascii("\nendobj\n"));
            }
            var xref = output.Position;
            var table = new StringBuilder($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (var o in offsets) table.Append(o.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            table.Append($"trailer\n<< /Size {objects.Count + 1} /Root {catalogId} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            Write(Ascii(table.ToString()));
            return output.ToArray();
        }

        // Standard Adobe AFM advance widths for characters 32..126 (units of 1/1000 em).
        private static readonly int[] HelveticaWidths =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
        };

        private static readonly int[] HelveticaBoldWidths =
        {
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
        };
    }
}
