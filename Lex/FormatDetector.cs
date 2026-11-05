using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PayrollEngine.Lex;

/// <summary>
/// Detects the format of a source document from its content, independent of file extension and media type
/// </summary>
internal static class FormatDetector
{
    /// <summary>The PDF header may follow leading bytes within the first kilobyte</summary>
    private const int PdfHeaderRange = 1024;

    /// <summary>Length of the content sample of the binary check</summary>
    private const int TextSampleLength = 8192;

    /// <summary>Maximum length of the mimetype entry of an OpenDocument file</summary>
    private const int MimeTypeMaxLength = 200;

    private static readonly byte[] PdfHeader = "%PDF-"u8.ToArray();
    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] EmptyZipSignature = [0x50, 0x4B, 0x05, 0x06];

    private static readonly Dictionary<string, string> Names = new()
    {
        [".pdf"] = "PDF",
        [".xls"] = "Excel 97-2003 workbook",
        [".xlsx"] = "Excel workbook",
        [".xlsb"] = "Excel binary workbook",
        [".docx"] = "Word document",
        [".ods"] = "OpenDocument spreadsheet",
        [".odt"] = "OpenDocument text",
        [".zip"] = "ZIP archive"
    };

    /// <summary>
    /// Format of the content as lower-case file extension: <c>.pdf</c>, <c>.xls</c> (OLE compound file),
    /// <c>.xlsx</c>, <c>.xlsb</c>, <c>.docx</c>, <c>.ods</c>, <c>.odt</c> or <c>.zip</c> (other ZIP archive);
    /// null when the content has no known signature (text, HTML or an unknown binary format)
    /// </summary>
    internal static string Detect(byte[] bytes)
    {
        if (StartsWith(bytes, OleSignature))
        {
            return ".xls";
        }
        if (StartsWith(bytes, ZipSignature) || StartsWith(bytes, EmptyZipSignature))
        {
            return ZipFormat(bytes);
        }
        if (bytes.AsSpan(0, Math.Min(bytes.Length, PdfHeaderRange)).IndexOf(PdfHeader) >= 0)
        {
            return ".pdf";
        }
        return null;
    }

    /// <summary>
    /// Display name of a detected format
    /// </summary>
    internal static string Name(string format) =>
        Names.GetValueOrDefault(format, format);

    /// <summary>
    /// True if the content is not text: a zero byte or more than 1% control characters in the first bytes.
    /// The encoding is not validated, so that text in a legacy code page stays text.
    /// </summary>
    internal static bool IsBinary(byte[] bytes)
    {
        // UTF-16 text holds zero bytes
        if (StartsWith(bytes, [0xFF, 0xFE]) || StartsWith(bytes, [0xFE, 0xFF]))
        {
            return false;
        }
        var sample = bytes.AsSpan(0, Math.Min(bytes.Length, TextSampleLength));
        var controls = 0;
        foreach (var value in sample)
        {
            if (value == 0)
            {
                return true;
            }
            // tab, line feed, form feed, carriage return and escape occur in text
            if (value < 0x20 && value is not (0x09 or 0x0A or 0x0C or 0x0D or 0x1B))
            {
                controls++;
            }
        }
        return controls * 100 > sample.Length;
    }

    /// <summary>
    /// Text of a text document; a byte order mark selects the encoding, otherwise UTF-8
    /// </summary>
    internal static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string ZipFormat(byte[] bytes)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var mimeType = archive.GetEntry("mimetype");
            if (mimeType != null)
            {
                switch (ReadMimeType(mimeType))
                {
                    case "application/vnd.oasis.opendocument.spreadsheet":
                        return ".ods";
                    case "application/vnd.oasis.opendocument.text":
                        return ".odt";
                }
            }
            if (archive.GetEntry("xl/workbook.xml") != null)
            {
                return ".xlsx";
            }
            if (archive.GetEntry("xl/workbook.bin") != null)
            {
                return ".xlsb";
            }
            if (archive.GetEntry("word/document.xml") != null)
            {
                return ".docx";
            }
        }
        catch (InvalidDataException)
        {
            // damaged archive
        }
        return ".zip";
    }

    private static string ReadMimeType(ZipArchiveEntry entry)
    {
        if (entry.Length > MimeTypeMaxLength)
        {
            return null;
        }
        using var reader = new StreamReader(entry.Open(), Encoding.ASCII);
        return reader.ReadToEnd().Trim();
    }

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> signature) =>
        bytes.AsSpan().StartsWith(signature);
}
