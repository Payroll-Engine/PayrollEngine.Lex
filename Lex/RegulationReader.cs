using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Reads the lookup values of the regulation JSON files of a satellite with the byte location of each value.
/// The JSON entries of ZIP archives in the Regulation folder are read like files.
/// </summary>
internal static class RegulationReader
{
    /// <summary>Reader options: regulation files may contain comments and trailing commas</summary>
    internal static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Root properties of a file that holds lookups only</summary>
    private static readonly HashSet<string> LookupRootProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "$schema", "createdObjectDate", "tenants"
    };

    /// <summary>Tenant properties of a file that holds lookups only</summary>
    private static readonly HashSet<string> LookupTenantProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "identifier", "updateMode", "culture", "calendar", "attributes", "regulations"
    };

    /// <summary>Regulation properties of a file that holds lookups only</summary>
    private static readonly HashSet<string> LookupRegulationProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "namespace", "updateMode", "validFrom", "version", "description", "descriptionLocalizations",
        "sharedRegulation", "attributes", "baseRegulations", "lookups"
    };

    /// <summary>State of reading one regulation file</summary>
    private sealed class Context
    {
        /// <summary>Collected lookups and values of all files of the satellite</summary>
        internal RegulationData Data { get; init; }
        /// <summary>File being read, owner of the byte offsets</summary>
        internal RegulationFile File { get; init; }
        /// <summary>First namespace declared by a regulation of the file</summary>
        internal string Namespace { get; set; }
    }

    /// <summary>
    /// Read all regulation files (*.json and the JSON entries of *.zip) of the Regulation folder below the
    /// satellite directory, in the order of the file names
    /// </summary>
    /// <exception cref="InvalidDataException">A ZIP file is no valid archive</exception>
    internal static RegulationData Read(string satelliteDirectory)
    {
        var data = new RegulationData();
        var regulationDirectory = Path.Combine(satelliteDirectory, "Regulation");
        if (!Directory.Exists(regulationDirectory))
        {
            return data;
        }

        // the first declared namespace applies to all files of the satellite
        string satelliteNamespace = null;
        foreach (var file in Files(regulationDirectory))
        {
            var context = new Context { Data = data, File = file };
            var reader = new Utf8JsonReader(file.Content, ReaderOptions);
            ReadRoot(ref reader, context);
            satelliteNamespace ??= context.Namespace;
        }

        // the lookup files of one satellite may omit the namespace that another file of the same satellite declares
        foreach (var lookup in data.Lookups)
        {
            data.LookupNames.Add(lookup.Name);
        }
        if (!string.IsNullOrEmpty(satelliteNamespace))
        {
            // a lookup named with the namespace prefix is also found by its short name (alias)
            var prefix = satelliteNamespace + ".";
            foreach (var lookup in data.Lookups.Where(x => x.Name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                lookup.Alias = lookup.Name[prefix.Length..];
                data.LookupNames.Add(lookup.Alias);
            }
            foreach (var value in data.Values.Where(x => x.Lookup.StartsWith(prefix, StringComparison.Ordinal)))
            {
                value.LookupAlias = value.Lookup[prefix.Length..];
            }
        }
        return data;
    }

    /// <summary>
    /// Regulation files of the folder: JSON files and the JSON entries of ZIP archives (entries by name)
    /// </summary>
    private static IEnumerable<RegulationFile> Files(string regulationDirectory)
    {
        var paths = Directory.GetFiles(regulationDirectory)
            .Where(x => IsExtension(x, ".json") || IsExtension(x, ".zip"))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (IsExtension(path, ".json"))
            {
                yield return ToRegulationFile(path, null, File.ReadAllBytes(path));
                continue;
            }
            foreach (var (name, bytes) in ArchiveWriter.ReadEntries(path).Where(x => IsExtension(x.Name, ".json")))
            {
                yield return ToRegulationFile(path, name, bytes);
            }
        }
    }

    /// <summary>
    /// Regulation file of the content bytes; offsets are counted without the byte order mark, it is kept to
    /// write the file back unchanged
    /// </summary>
    private static RegulationFile ToRegulationFile(string path, string entryName, byte[] bytes)
    {
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return new RegulationFile
        {
            Path = path,
            EntryName = entryName,
            Bom = bom ? bytes[..3] : [],
            Content = bom ? bytes[3..] : bytes
        };
    }

    /// <summary>
    /// Root object: reads the tenants array, skips all other properties
    /// </summary>
    private static void ReadRoot(ref Utf8JsonReader reader, Context context)
    {
        // a file without root object (e.g. an array) holds no regulation
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            context.File.LookupsOnly = false;
            return;
        }
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            Observe(context, LookupRootProperties, name);
            reader.Read();
            if (Is(name, "tenants") && reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        ReadTenant(ref reader, context);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }
            else
            {
                reader.Skip();
            }
        }
    }

    /// <summary>
    /// Tenant object: reads the regulations array, skips all other properties
    /// </summary>
    private static void ReadTenant(ref Utf8JsonReader reader, Context context)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            Observe(context, LookupTenantProperties, name);
            reader.Read();
            if (Is(name, "regulations") && reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        ReadRegulation(ref reader, context);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }
            else
            {
                reader.Skip();
            }
        }
    }

    /// <summary>
    /// Regulation object: reads its lookups, name, validFrom and namespace; the first regulation with a
    /// name or validFrom sets the satellite values
    /// </summary>
    private static void ReadRegulation(ref Utf8JsonReader reader, Context context)
    {
        string regulationName = null;
        string validFrom = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            Observe(context, LookupRegulationProperties, name);
            reader.Read();
            if (Is(name, "lookups") && reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        ReadLookup(ref reader, context);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }
            else if (Is(name, "name"))
            {
                regulationName = Text(ref reader);
            }
            else if (Is(name, "validFrom"))
            {
                // the validFrom token location allows LexApply/Rollover to replace the date in place
                var offset = (int)reader.TokenStartIndex;
                var isString = reader.TokenType == JsonTokenType.String;
                validFrom = Text(ref reader);
                if (isString)
                {
                    context.Data.ValidFromTokens.Add(new RegulationToken
                    {
                        File = context.File,
                        Value = validFrom,
                        Offset = offset,
                        Length = (int)reader.BytesConsumed - offset
                    });
                }
            }
            else if (Is(name, "namespace"))
            {
                var ns = Text(ref reader);
                if (!string.IsNullOrEmpty(ns))
                {
                    // several regulations in one file: the first namespace wins
                    context.Namespace ??= ns;
                }
            }
            else
            {
                reader.Skip();
            }
        }
        context.Data.Name ??= regulationName;
        context.Data.ValidFrom ??= validFrom;
    }

    /// <summary>
    /// Lookup value read before its lookup name is known (the name may follow the values array)
    /// </summary>
    private sealed class PendingValue
    {
        /// <summary>Lookup value key, null without key</summary>
        internal string Key { get; set; }
        /// <summary>Key values array, null without</summary>
        internal JsonElement? KeyValues { get; set; }
        /// <summary>Range value, used as key of range lookups without key</summary>
        internal string RangeValue { get; set; }
        /// <summary>Scalar or JSON text value of the "value" property</summary>
        internal JsonElement? Value { get; set; }
        /// <summary>Byte offset of the "value" token in the file</summary>
        internal int ValueOffset { get; set; }
        /// <summary>Byte length of the "value" token</summary>
        internal int ValueLength { get; set; }
        /// <summary>Structured value of the "valueObject" property</summary>
        internal JsonElement? ValueObject { get; set; }
        /// <summary>Byte offset of the "valueObject" token in the file</summary>
        internal int ValueObjectOffset { get; set; }
        /// <summary>Byte length of the "valueObject" token</summary>
        internal int ValueObjectLength { get; set; }
    }

    /// <summary>
    /// Lookup object: reads its name and values with the byte locations needed to replace or append values
    /// </summary>
    private static void ReadLookup(ref Utf8JsonReader reader, Context context)
    {
        string lookupName = null;
        var hasValues = false;
        // insertOffset: end of the last value (or of the opening bracket), where LexApply appends a new value
        var insertOffset = 0;
        var valuesOffset = 0;
        var lastValueOffset = -1;
        var arrayOffset = -1;
        var arrayLength = 0;
        var pending = new List<PendingValue>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            var nameOffset = (int)reader.TokenStartIndex;
            reader.Read();
            if (Is(name, "values") && reader.TokenType == JsonTokenType.StartArray)
            {
                hasValues = true;
                valuesOffset = nameOffset;
                arrayOffset = (int)reader.TokenStartIndex;
                insertOffset = (int)reader.BytesConsumed;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        lastValueOffset = (int)reader.TokenStartIndex;
                        pending.Add(ReadValue(ref reader));
                        insertOffset = (int)reader.BytesConsumed;
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                // the whole array is replaced by a text table
                arrayLength = (int)reader.BytesConsumed - arrayOffset;
            }
            else if (Is(name, "name"))
            {
                lookupName = Text(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }
        // a lookup without name cannot be addressed by a lex key
        if (lookupName == null)
        {
            return;
        }

        var lookup = new RegulationLookup
        {
            Name = lookupName,
            File = context.File,
            HasValues = hasValues,
            InsertOffset = insertOffset,
            ValuesOffset = valuesOffset,
            LastValueOffset = lastValueOffset,
            ArrayOffset = arrayOffset,
            ArrayLength = arrayLength
        };
        context.Data.Lookups.Add(lookup);
        context.Data.LookupCount++;
        foreach (var item in pending)
        {
            // valueObject takes precedence over value; the token of the used property is the one to replace
            var useObject = item.ValueObject is { ValueKind: not JsonValueKind.Null };
            var value = new RegulationValue
            {
                Lookup = lookupName,
                // range lookups may have no key: the range value identifies the entry
                Key = item.Key ?? item.RangeValue ?? string.Empty,
                ValueKey = item.Key,
                KeyValues = item.KeyValues,
                RangeValue = item.RangeValue,
                Object = useObject ? item.ValueObject : null,
                Scalar = useObject || item.Value == null ? null : ScalarText(item.Value.Value),
                Owner = lookup,
                Token = useObject ? item.ValueObject : item.Value,
                TokenOffset = useObject ? item.ValueObjectOffset : item.Value != null ? item.ValueOffset : -1,
                TokenLength = useObject ? item.ValueObjectLength : item.ValueLength
            };
            lookup.Values.Add(value);
            context.Data.Values.Add(value);
        }
    }

    /// <summary>
    /// Lookup value object: reads key, keyValues, rangeValue, value and valueObject with their token locations
    /// </summary>
    private static PendingValue ReadValue(ref Utf8JsonReader reader)
    {
        var value = new PendingValue();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            // the token starts after reading the property name; its length is known after parsing
            var offset = (int)reader.TokenStartIndex;
            if (Is(name, "key"))
            {
                value.Key = ScalarText(JsonElement.ParseValue(ref reader), nullAsNull: true);
            }
            else if (Is(name, "keyValues"))
            {
                var keyValues = JsonElement.ParseValue(ref reader);
                value.KeyValues = keyValues.ValueKind == JsonValueKind.Array ? keyValues : null;
            }
            else if (Is(name, "rangeValue"))
            {
                value.RangeValue = JsonElement.ParseValue(ref reader).ToString();
            }
            else if (Is(name, "value"))
            {
                value.Value = JsonElement.ParseValue(ref reader);
                value.ValueOffset = offset;
                value.ValueLength = (int)reader.BytesConsumed - offset;
            }
            else if (Is(name, "valueObject"))
            {
                value.ValueObject = JsonElement.ParseValue(ref reader);
                value.ValueObjectOffset = offset;
                value.ValueObjectLength = (int)reader.BytesConsumed - offset;
            }
            else
            {
                reader.Skip();
            }
        }
        return value;
    }

    /// <summary>
    /// A property outside the header and the lookups: the file holds more than lookups
    /// </summary>
    private static void Observe(Context context, HashSet<string> lookupProperties, string name)
    {
        if (!lookupProperties.Contains(name))
        {
            context.File.LookupsOnly = false;
        }
    }

    /// <summary>
    /// Text of the current token, null for a JSON null
    /// </summary>
    private static string Text(ref Utf8JsonReader reader)
    {
        var element = JsonElement.ParseValue(ref reader);
        return ScalarText(element, nullAsNull: true);
    }

    /// <summary>
    /// Text of a scalar element: strings unquoted, numbers and booleans in their JSON spelling
    /// </summary>
    private static string ScalarText(JsonElement element, bool nullAsNull = false) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null when nullAsNull => null,
            _ => element.ToString()
        };

    /// <summary>
    /// File name with the extension, ignoring case
    /// </summary>
    private static bool IsExtension(string path, string extension) =>
        string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Property names are compared ignoring case (regulation files are written by hand)
    /// </summary>
    private static bool Is(string name, string expected) =>
        string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
}
