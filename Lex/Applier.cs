using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Writes the lex values into the lookup values of the data regulation (Lookup.Key mapping as in the check).
/// Only differing values are replaced in place; descriptions and the formatting of the file are kept.
/// </summary>
public static class Applier
{
    /// <summary>String escaping as written by hand: umlauts, accents and symbols stay readable</summary>
    private static readonly JsonSerializerOptions StringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Replacement of a byte range of a regulation file</summary>
    private sealed class Edit
    {
        /// <summary>File of the replaced range</summary>
        internal RegulationFile File { get; init; }
        /// <summary>Byte offset of the range (file content without byte order mark)</summary>
        internal int Offset { get; init; }
        /// <summary>Byte length of the range, 0 for an insertion</summary>
        internal int Length { get; init; }
        /// <summary>New JSON text of the range</summary>
        internal string Text { get; init; }
        /// <summary>Lex entry that caused the edit, named in conflict messages</summary>
        internal string EntryKey { get; init; }
        /// <summary>Byte offset after the range</summary>
        internal int End => Offset + Length;
    }

    /// <summary>New lookup value for a lex entry without regulation value</summary>
    private sealed class Addition
    {
        /// <summary>Key of the new lookup value</summary>
        internal string Key { get; init; }
        /// <summary>Lex content of the new value</summary>
        internal string Content { get; init; }
        /// <summary>True: JSON content written as valueObject, false: text written as value</summary>
        internal bool Structured { get; init; }
    }

    /// <summary>
    /// Apply one manifest
    /// </summary>
    /// <param name="manifest">Manifest (lex.json)</param>
    /// <param name="lexPath">Full path of lex.json</param>
    /// <param name="dryRun">Report the changes without writing</param>
    public static ApplyResult Apply(Manifest manifest, string lexPath, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var lexDirectory = Path.GetDirectoryName(lexPath)!;
        // lex.json lives in <satellite>/.lex; a manifest outside a .lex folder uses its own folder
        var satelliteDirectory = Directory.GetParent(lexDirectory)?.FullName ?? lexDirectory;
        var result = new ApplyResult { LexPath = lexPath, Satellite = Path.GetFileName(satelliteDirectory) };

        RegulationData regulation;
        try
        {
            regulation = RegulationReader.Read(satelliteDirectory);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            result.Error = "Regulation JSON could not be read: " + exception.Message;
            return result;
        }
        if (regulation.Lookups.Count == 0)
        {
            // lookup values live outside the Regulation folder: nothing to apply
            result.Note = "The regulation JSON holds no lookups, nothing to apply";
            return result;
        }

        var index = new RegulationIndex(regulation);
        var editionTag = EntryMapping.EditionTag(manifest);
        // all changes are collected first and written per file in one pass: offsets refer to the original content
        var edits = new List<Edit>();
        var additions = new Dictionary<RegulationLookup, List<Addition>>();
        foreach (var entry in manifest.Values)
        {
            if (string.IsNullOrEmpty(entry.Key) || Checker.IsReferenceEntry(entry.Key))
            {
                continue;
            }
            // an open entry (rolled over from the previous cycle) must be verified before its value is applied
            if (entry.Open)
            {
                result.Skipped.Add($"{entry.Key}: open entry, verify first");
                continue;
            }
            var target = EntryMapping.Resolve(entry, index, editionTag);
            if (target.Kind == EntryTargetKind.None)
            {
                result.Skipped.Add($"{entry.Key}: no regulation lookup");
                continue;
            }
            string content;
            try
            {
                content = EntryMapping.Content(entry, lexDirectory, out var reason);
                if (content == null)
                {
                    result.Skipped.Add($"{entry.Key}: {reason}");
                    continue;
                }
            }
            catch (IOException exception)
            {
                result.Skipped.Add($"{entry.Key}: {exception.Message}");
                continue;
            }

            switch (target.Kind)
            {
                case EntryTargetKind.Values:
                    foreach (var value in target.Values)
                    {
                        Replace(result, edits, entry.Key, value, target.Remainder, content);
                    }
                    break;
                // a lookup-level entry (content file) can only replace the value of a single-value lookup
                case EntryTargetKind.Lookup:
                    if (target.Lookup.Values.Count == 1)
                    {
                        Replace(result, edits, entry.Key, target.Lookup.Values[0], string.Empty, content);
                    }
                    else
                    {
                        result.Skipped.Add($"{entry.Key}: lookup {target.Lookup.Name} has {target.Lookup.Values.Count} values, expected one");
                    }
                    break;
                // an entry for a missing key of an existing lookup becomes a new value at the end of the values array
                case EntryTargetKind.NewValue:
                    Add(result, additions, entry.Key, target.Lookup, target.Remainder, content);
                    break;
            }
        }

        // the new values of a lookup are inserted together after its last value
        foreach (var (lookup, items) in additions)
        {
            edits.Add(new Edit
            {
                File = lookup.File,
                Offset = lookup.InsertOffset,
                Length = 0,
                Text = AdditionText(lookup, items),
                EntryKey = items[0].Key
            });
        }

        foreach (var group in edits.GroupBy(x => x.File))
        {
            var file = group.Key;
            var bytes = Splice(file, group.OrderBy(x => x.Offset).ToList());
            try
            {
                // safety net: never write a file that is no longer valid JSON
                using var document = JsonDocument.Parse(bytes, LexJson.DocumentOptions);
            }
            catch (JsonException exception)
            {
                result.Error = $"{Path.GetFileName(file.Path)}: generated JSON is invalid ({exception.Message}), nothing written";
                result.WrittenFiles.Clear();
                return result;
            }
            if (!dryRun)
            {
                // the original byte order mark is kept
                File.WriteAllBytes(file.Path, [.. file.Bom, .. bytes]);
                result.WrittenFiles.Add(file.Path);
            }
        }
        return result;
    }

    /// <summary>
    /// Replaces the value token (or the property of a split key) when the lex content differs
    /// </summary>
    private static void Replace(ApplyResult result, List<Edit> edits, string entryKey, RegulationValue value,
        string remainder, string content)
    {
        // target label in the change list: Lookup.Key/Property/Path
        var target = value.Lookup + "." + value.Key + (remainder.Length > 0 ? "/" + remainder.Replace('.', '/') : string.Empty);
        // lookup value without value or valueObject property: nothing to replace
        if (value.TokenOffset < 0)
        {
            result.Skipped.Add($"{entryKey}: regulation value {target} has no value");
            return;
        }

        var file = value.Owner.File;
        int offset;
        int length;
        JsonElement element;
        string difference;
        // whole value: the token located by the reader
        if (remainder.Length == 0)
        {
            offset = value.TokenOffset;
            length = value.TokenLength;
            element = value.Token!.Value;
            difference = ValueComparer.Difference(value.Object, value.Scalar, content);
        }
        else
        {
            if (value.Object == null)
            {
                result.Skipped.Add($"{entryKey}: path '{remainder}' but the regulation value is a scalar");
                return;
            }
            var located = Locate(file.Content, value.TokenOffset, value.TokenLength, remainder);
            if (located == null)
            {
                result.Skipped.Add($"{entryKey}: property path '{remainder}' not found in the regulation value");
                return;
            }
            // property of a structured value: compare and replace only this property
            (offset, length, element) = located.Value;
            difference = element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? ValueComparer.Difference(element, null, content)
                : ValueComparer.Difference(null, element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString(), content);
        }

        // equal values (numeric or ignoring case) are not rewritten: the formatting of the file stays unchanged
        if (difference == null)
        {
            result.Unchanged++;
            return;
        }
        var text = TokenText(element, remainder.Length == 0 && value.Object != null, content, out var reason);
        if (text == null)
        {
            result.Skipped.Add($"{entryKey}: {reason}");
            return;
        }
        var edit = new Edit
        {
            File = file,
            Offset = offset,
            Length = length,
            Text = Reindent(text, LineIndent(file.Content, offset), NewLine(file.Content)),
            EntryKey = entryKey
        };
        // two entries must not change overlapping ranges (e.g. a whole value and one of its properties)
        var conflict = edits.FirstOrDefault(x => x.File == file && x.Offset < edit.End && edit.Offset < x.End);
        if (conflict != null)
        {
            result.Skipped.Add($"{entryKey}: overlaps with the change of {conflict.EntryKey}");
            return;
        }
        edits.Add(edit);
        result.Changes.Add(new ApplyChange
        {
            EntryKey = entryKey,
            File = Path.GetFileName(file.Path),
            Target = target,
            Kind = ApplyChangeKind.Changed,
            Detail = difference
        });
    }

    /// <summary>
    /// Registers a new lookup value; its form (value or valueObject) must match the existing values of the lookup
    /// </summary>
    private static void Add(ApplyResult result, Dictionary<RegulationLookup, List<Addition>> additions,
        string entryKey, RegulationLookup lookup, string valueKey, string content)
    {
        if (!lookup.HasValues)
        {
            result.Skipped.Add($"{entryKey}: lookup {lookup.Name} has no values array");
            return;
        }
        // a lookup holds either scalar values or structured values, never both
        var structured = EntryMapping.IsStructured(content);
        if (lookup.Values.Count > 0 && lookup.Values.Any(x => x.Object != null) != structured)
        {
            result.Skipped.Add(structured
                ? $"{entryKey}: lookup {lookup.Name} holds scalar values, the lex content is structured"
                : $"{entryKey}: lookup {lookup.Name} holds structured values, the lex content is a scalar");
            return;
        }
        if (!additions.TryGetValue(lookup, out var items))
        {
            additions[lookup] = items = [];
        }
        // several property entries of the same new key cannot be merged into one value
        if (items.Any(x => string.Equals(x.Key, valueKey, StringComparison.Ordinal)))
        {
            result.Skipped.Add($"{entryKey}: value {lookup.Name}.{valueKey} is added by another entry");
            return;
        }
        items.Add(new Addition { Key = valueKey, Content = content, Structured = structured });
        result.Changes.Add(new ApplyChange
        {
            EntryKey = entryKey,
            File = Path.GetFileName(lookup.File.Path),
            Target = lookup.Name + "." + valueKey,
            Kind = ApplyChangeKind.Added,
            Detail = "new value"
        });
    }

    /// <summary>
    /// Locate a property path inside a JSON object token
    /// </summary>
    private static (int Offset, int Length, JsonElement Element)? Locate(byte[] content, int offset, int length, string path)
    {
        var reader = new Utf8JsonReader(content.AsSpan(offset, length), RegulationReader.ReaderOptions);
        if (!reader.Read())
        {
            return null;
        }
        // walk the property path level by level; property names are compared ignoring case
        var parts = path.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }
            var found = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var match = string.Equals(reader.GetString(), parts[i], StringComparison.OrdinalIgnoreCase);
                reader.Read();
                if (!match)
                {
                    reader.Skip();
                    continue;
                }
                found = true;
                if (i == parts.Length - 1)
                {
                    var start = (int)reader.TokenStartIndex;
                    var element = JsonElement.ParseValue(ref reader);
                    // offsets of the slice reader are relative to the token: add the token offset
                    return (offset + start, (int)reader.BytesConsumed - start, element);
                }
                break;
            }
            if (!found)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// JSON text of the new value token, in the form of the existing token
    /// </summary>
    private static string TokenText(JsonElement existing, bool valueObject, string content, out string reason)
    {
        reason = null;
        if (valueObject || existing.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            if (!EntryMapping.IsStructured(content))
            {
                reason = "lex content is not JSON but the regulation value is structured";
                return null;
            }
            return content.Trim();
        }
        // the new token keeps the JSON type of the existing token: number stays number, boolean stays boolean, else string
        switch (existing.ValueKind)
        {
            case JsonValueKind.Number:
                if (Literal(content) is JsonValueKind.Number)
                {
                    return content.Trim();
                }
                reason = $"lex content '{ValueComparer.Shorten(content)}' is not a number";
                return null;
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (Literal(content) is JsonValueKind.True or JsonValueKind.False)
                {
                    return content.Trim();
                }
                reason = $"lex content '{ValueComparer.Shorten(content)}' is not a boolean";
                return null;
            default:
                return JsonString(content);
        }
    }

    /// <summary>
    /// JSON kind of literal content (number, boolean, ...), null if the content is no JSON literal
    /// </summary>
    private static JsonValueKind? Literal(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content.Trim());
            return document.RootElement.ValueKind;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// JSON text of new lookup values, indented like the existing values
    /// </summary>
    private static string AdditionText(RegulationLookup lookup, List<Addition> items)
    {
        var content = lookup.File.Content;
        var newLine = NewLine(content);
        // an empty values array has no value to copy the indentation from: indent one level below the "values" property
        var empty = lookup.LastValueOffset < 0;
        var itemIndent = empty
            ? LineIndent(content, lookup.ValuesOffset) + "  "
            : LineIndent(content, lookup.LastValueOffset);
        var inner = itemIndent + "  ";
        var builder = new StringBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!empty || i > 0)
            {
                builder.Append(',');
            }
            builder.Append(newLine).Append(itemIndent).Append('{').Append(newLine);
            builder.Append(inner).Append("\"key\": ").Append(JsonString(item.Key)).Append(',').Append(newLine);
            var value = item.Structured
                ? Reindent(item.Content.Trim(), inner, newLine)
                : JsonString(item.Content);
            builder.Append(inner).Append(item.Structured ? "\"valueObject\": " : "\"value\": ").Append(value).Append(newLine);
            builder.Append(itemIndent).Append('}');
        }
        if (empty)
        {
            // the closing bracket of a previously empty array moves to its own line
            builder.Append(newLine).Append(LineIndent(content, lookup.ValuesOffset));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Content of the file with all edits applied; the edits are sorted by offset and do not overlap
    /// </summary>
    private static byte[] Splice(RegulationFile file, List<Edit> edits)
    {
        using var stream = new MemoryStream();
        var position = 0;
        foreach (var edit in edits)
        {
            stream.Write(file.Content, position, edit.Offset - position);
            var bytes = Encoding.UTF8.GetBytes(edit.Text);
            stream.Write(bytes, 0, bytes.Length);
            position = edit.End;
        }
        stream.Write(file.Content, position, file.Content.Length - position);
        return stream.ToArray();
    }

    /// <summary>
    /// JSON string literal of a text
    /// </summary>
    private static string JsonString(string text) => JsonSerializer.Serialize(text, StringOptions);

    /// <summary>
    /// Continuation lines of a multi-line value are indented like the line of the token
    /// </summary>
    private static string Reindent(string text, string indent, string newLine)
    {
        var lines = text.Split('\n').Select(x => x.TrimEnd('\r')).ToArray();
        if (lines.Length == 1)
        {
            return text;
        }
        return lines[0] + newLine + string.Join(newLine, lines.Skip(1).Select(x => indent + x));
    }

    /// <summary>
    /// Leading whitespace of the line that contains the offset
    /// </summary>
    private static string LineIndent(byte[] content, int offset)
    {
        var start = offset;
        while (start > 0 && content[start - 1] != (byte)'\n')
        {
            start--;
        }
        var end = start;
        while (end < content.Length && content[end] is (byte)' ' or (byte)'\t')
        {
            end++;
        }
        return Encoding.UTF8.GetString(content, start, end - start);
    }

    /// <summary>
    /// Line break of the file: CRLF if the file contains one, else LF
    /// </summary>
    private static string NewLine(byte[] content) =>
        content.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n" : "\n";
}
