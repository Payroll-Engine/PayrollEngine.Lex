using System;
using System.IO;

namespace PayrollEngine.Lex;

/// <summary>
/// JSON Schema for lex.json (https://payrollengine.org/schemas/lex/v1.json)
/// </summary>
// ReSharper disable once UnusedMember.Global
public static class Schema
{
    /// <summary>Manifest resource name of lex.schema.json (LogicalName in the project file)</summary>
    private const string ResourceName = "PayrollEngine.Lex.lex.schema.json";

    /// <summary>The schema is read once on first access</summary>
    private static readonly Lazy<string> Text = new(Load);

    /// <summary>
    /// Schema text
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public static string Json => Text.Value;

    /// <summary>
    /// Reads the embedded schema resource
    /// </summary>
    private static string Load()
    {
        using var stream = typeof(Schema).Assembly.GetManifestResourceStream(ResourceName) ??
                           throw new InvalidOperationException($"Missing embedded resource {ResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
