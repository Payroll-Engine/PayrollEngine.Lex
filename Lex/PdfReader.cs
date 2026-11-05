using System.Collections.Generic;
using System.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace PayrollEngine.Lex;

/// <summary>
/// Reads the text layer of a PDF, one section per page (heading <c>Page N</c>) and the values
/// of the form fields (heading <c>Form fields</c>). Scanned pages without text layer are not recognized (no OCR).
/// </summary>
internal static class PdfReader
{
    /// <summary>Maximum file size in megabytes</summary>
    internal const int MaxMegabytes = 100;

    /// <summary>Maximum number of pages</summary>
    internal const int MaxPages = 3000;

    /// <summary>Heading of the form field section</summary>
    internal const string FormFieldsHeading = "Form fields";

    private static readonly ParsingOptions Options = new() { SkipMissingFonts = true };

    /// <summary>
    /// Text of a PDF: page text in content order with one line per text line, form field values one per line.
    /// A document that requires a user password, exceeds a limit or holds no text throws an
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    /// <param name="stream">The PDF stream (seekable)</param>
    internal static List<(string Heading, string Text)> Read(Stream stream)
    {
        if (stream.Length > MaxMegabytes * 1024L * 1024L)
        {
            throw new InvalidDataException($"PDF exceeds {MaxMegabytes} MB");
        }
        try
        {
            using var document = PdfDocument.Open(stream, Options);
            if (document.NumberOfPages > MaxPages)
            {
                throw new InvalidDataException($"PDF exceeds {MaxPages} pages ({document.NumberOfPages})");
            }
            var sections = new List<(string Heading, string Text)>();
            foreach (var page in document.GetPages())
            {
                var text = HtmlReader.CleanLines(ContentOrderTextExtractor.GetText(page));
                if (text.Length > 0)
                {
                    sections.Add(($"Page {page.Number}", text));
                }
            }
            var fields = FormFieldValues(document);
            if (fields.Count > 0)
            {
                sections.Add((FormFieldsHeading, string.Join('\n', fields)));
            }
            if (sections.Count == 0)
            {
                throw new InvalidDataException("no text layer (scan?)");
            }
            return sections;
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new InvalidDataException("encrypted");
        }
    }

    private static List<string> FormFieldValues(PdfDocument document)
    {
        var values = new List<string>();
        if (document.TryGetForm(out var form))
        {
            foreach (var field in form.Fields)
            {
                AddValues(field, values);
            }
        }
        return values;
    }

    private static void AddValues(AcroFieldBase field, List<string> values)
    {
        switch (field)
        {
            case AcroNonTerminalField parent:
                foreach (var child in parent.Children)
                {
                    AddValues(child, values);
                }
                break;
            case AcroTextField text:
                AddValue(values, text.Value);
                break;
            case AcroComboBoxField comboBox:
                AddOptions(values, comboBox.SelectedOptions);
                break;
            case AcroListBoxField listBox:
                AddOptions(values, listBox.SelectedOptions);
                break;
            // buttons and signatures hold no values
        }
    }

    private static void AddOptions(List<string> values, IEnumerable<string> options)
    {
        foreach (var option in options ?? [])
        {
            AddValue(values, option);
        }
    }

    private static void AddValue(List<string> values, string value)
    {
        var text = HtmlReader.CleanLines(value ?? string.Empty).Replace('\n', ' ');
        if (text.Length > 0)
        {
            values.Add(text);
        }
    }
}
