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
    private static readonly JsonSerializerOptions StringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class Edit
    {
        internal RegulationFile File { get; init; }
        internal int Offset { get; init; }
        internal int Length { get; init; }
        internal string Text { get; init; }
        internal string EntryKey { get; init; }
        internal int End => Offset + Length;
    }

    private sealed class Addition
    {
        internal string Key { get; init; }
        internal string Content { get; init; }
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
        var edits = new List<Edit>();
        var additions = new Dictionary<RegulationLookup, List<Addition>>();
        foreach (var entry in manifest.Values)
        {
            if (string.IsNullOrEmpty(entry.Key) || Checker.IsReferenceEntry(entry.Key))
            {
                continue;
            }
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
                case EntryTargetKind.NewValue:
                    Add(result, additions, entry.Key, target.Lookup, target.Remainder, content);
                    break;
            }
        }

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
                File.WriteAllBytes(file.Path, [.. file.Bom, .. bytes]);
                result.WrittenFiles.Add(file.Path);
            }
        }
        return result;
    }

    private static void Replace(ApplyResult result, List<Edit> edits, string entryKey, RegulationValue value,
        string remainder, string content)
    {
        var target = value.Lookup + "." + value.Key + (remainder.Length > 0 ? "/" + remainder.Replace('.', '/') : string.Empty);
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
            (offset, length, element) = located.Value;
            difference = element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? ValueComparer.Difference(element, null, content)
                : ValueComparer.Difference(null, element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString(), content);
        }

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

    private static void Add(ApplyResult result, Dictionary<RegulationLookup, List<Addition>> additions,
        string entryKey, RegulationLookup lookup, string valueKey, string content)
    {
        if (!lookup.HasValues)
        {
            result.Skipped.Add($"{entryKey}: lookup {lookup.Name} has no values array");
            return;
        }
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

    private static string AdditionText(RegulationLookup lookup, List<Addition> items)
    {
        var content = lookup.File.Content;
        var newLine = NewLine(content);
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
            builder.Append(newLine).Append(LineIndent(content, lookup.ValuesOffset));
        }
        return builder.ToString();
    }

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

    private static string NewLine(byte[] content) =>
        content.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n" : "\n";
}
