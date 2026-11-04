using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Regulation JSON file with its content (without byte order mark)
/// </summary>
internal sealed class RegulationFile
{
    internal string Path { get; init; }
    internal byte[] Bom { get; init; }
    internal byte[] Content { get; init; }
}

/// <summary>
/// Lookup of a regulation file
/// </summary>
internal sealed class RegulationLookup
{
    internal string Name { get; init; }
    internal string Alias { get; set; }
    internal RegulationFile File { get; init; }

    /// <summary>Lookup has a values array</summary>
    internal bool HasValues { get; set; }

    /// <summary>Offset behind the last value object, or behind the opening bracket of an empty values array</summary>
    internal int InsertOffset { get; set; }

    /// <summary>Offset of the values property name (indentation of new values)</summary>
    internal int ValuesOffset { get; set; }

    /// <summary>Offset of the last value object</summary>
    internal int LastValueOffset { get; set; } = -1;

    internal List<RegulationValue> Values { get; } = [];
}

/// <summary>
/// Lookup value of a regulation file with the location of its value token
/// </summary>
internal sealed class RegulationValue
{
    internal string Lookup { get; init; }
    internal string LookupAlias { get; set; }
    internal string Key { get; init; }
    internal string Scalar { get; init; }
    internal JsonElement? Object { get; init; }
    internal bool Covered { get; set; }
    internal RegulationLookup Owner { get; init; }

    /// <summary>Value token (value or valueObject), null without value</summary>
    internal JsonElement? Token { get; init; }
    internal int TokenOffset { get; init; } = -1;
    internal int TokenLength { get; init; }
}

/// <summary>
/// Lookup values of the Regulation folder of a satellite
/// </summary>
internal sealed class RegulationData
{
    internal string Name { get; set; }
    internal string ValidFrom { get; set; }
    internal int LookupCount { get; set; }
    internal List<RegulationValue> Values { get; } = [];
    internal List<RegulationLookup> Lookups { get; } = [];

    /// <summary>Lookup names including the names without satellite namespace</summary>
    internal HashSet<string> LookupNames { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Reads the lookup values of the regulation JSON files of a satellite with the byte location of each value
/// </summary>
internal static class RegulationReader
{
    internal static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class Context
    {
        internal RegulationData Data { get; init; }
        internal RegulationFile File { get; init; }
        internal string Namespace { get; set; }
    }

    /// <summary>
    /// Read all regulation files (*.json) of the Regulation folder below the satellite directory
    /// </summary>
    internal static RegulationData Read(string satelliteDirectory)
    {
        var data = new RegulationData();
        var regulationDirectory = Path.Combine(satelliteDirectory, "Regulation");
        if (!Directory.Exists(regulationDirectory))
        {
            return data;
        }

        string satelliteNamespace = null;
        foreach (var path in Directory.GetFiles(regulationDirectory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var bytes = File.ReadAllBytes(path);
            var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var file = new RegulationFile
            {
                Path = path,
                Bom = bom ? bytes[..3] : [],
                Content = bom ? bytes[3..] : bytes
            };
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

    private static void ReadRoot(ref Utf8JsonReader reader, Context context)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return;
        }
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
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

    private static void ReadTenant(ref Utf8JsonReader reader, Context context)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
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

    private static void ReadRegulation(ref Utf8JsonReader reader, Context context)
    {
        string regulationName = null;
        string validFrom = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
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
                validFrom = Text(ref reader);
            }
            else if (Is(name, "namespace"))
            {
                var ns = Text(ref reader);
                if (!string.IsNullOrEmpty(ns))
                {
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

    private sealed class PendingValue
    {
        internal string Key { get; set; }
        internal string RangeValue { get; set; }
        internal JsonElement? Value { get; set; }
        internal int ValueOffset { get; set; }
        internal int ValueLength { get; set; }
        internal JsonElement? ValueObject { get; set; }
        internal int ValueObjectOffset { get; set; }
        internal int ValueObjectLength { get; set; }
    }

    private static void ReadLookup(ref Utf8JsonReader reader, Context context)
    {
        string lookupName = null;
        var hasValues = false;
        var insertOffset = 0;
        var valuesOffset = 0;
        var lastValueOffset = -1;
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
            LastValueOffset = lastValueOffset
        };
        context.Data.Lookups.Add(lookup);
        context.Data.LookupCount++;
        foreach (var item in pending)
        {
            var useObject = item.ValueObject is { ValueKind: not JsonValueKind.Null };
            var value = new RegulationValue
            {
                Lookup = lookupName,
                Key = item.Key ?? item.RangeValue ?? string.Empty,
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

    private static PendingValue ReadValue(ref Utf8JsonReader reader)
    {
        var value = new PendingValue();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            var offset = (int)reader.TokenStartIndex;
            if (Is(name, "key"))
            {
                value.Key = ScalarText(JsonElement.ParseValue(ref reader), nullAsNull: true);
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

    private static string Text(ref Utf8JsonReader reader)
    {
        var element = JsonElement.ParseValue(ref reader);
        return ScalarText(element, nullAsNull: true);
    }

    private static string ScalarText(JsonElement element, bool nullAsNull = false) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null when nullAsNull => null,
            _ => element.ToString()
        };

    private static bool Is(string name, string expected) =>
        string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
}
