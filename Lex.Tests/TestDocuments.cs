using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace PayrollEngine.Lex.Tests;

/// <summary>
/// Test documents created in memory
/// </summary>
internal static class TestDocuments
{
    /// <summary>
    /// ZIP archive with text entries, in the given order
    /// </summary>
    internal static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        return stream.ToArray();
    }

    /// <summary>
    /// PDF with one page per item; a page holds text lines, a line holds cells placed side by side.
    /// A page without lines has no text.
    /// </summary>
    internal static byte[] Pdf(params string[][][] pages)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            for (var line = 0; line < lines.Length; line++)
            {
                for (var cell = 0; cell < lines[line].Length; cell++)
                {
                    page.AddText(lines[line][cell], 11, new PdfPoint(50 + cell * 200, 780 - line * 20), font);
                }
            }
        }
        return builder.Build();
    }

    /// <summary>
    /// PDF with one page of text lines
    /// </summary>
    internal static byte[] TextPdf(params string[] lines) =>
        Pdf(lines.Select(x => new[] { x }).ToArray());

    /// <summary>
    /// Handwritten PDF: object 1 is the catalog, the objects are numbered in order; the trailer holds
    /// the root and the additional entries
    /// </summary>
    internal static byte[] RawPdf(string trailerEntries, params string[] objects)
    {
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            // ReSharper disable once StringLiteralTypo
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }
        // ReSharper disable once StringLiteralTypo
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R {trailerEntries} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>
    /// PDF with one empty page and a text field
    /// </summary>
    internal static byte[] PdfWithTextField(string name, string value, string trailerEntries = "",
        params string[] additionalObjects) =>
        RawPdf(trailerEntries,
        [
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            // ReSharper disable once StringLiteralTypo
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Annots [4 0 R] >>",
            $"<< /Type /Annot /Subtype /Widget /FT /Tx /T ({name}) /V ({value}) /Rect [50 700 250 720] /P 3 0 R >>",
            .. additionalObjects
        ]);
}
