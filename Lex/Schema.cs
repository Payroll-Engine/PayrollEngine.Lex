namespace PayrollEngine.Lex;

/// <summary>
/// JSON Schema for lex.json (https://payrollengine.org/schemas/lex/v1.json)
/// </summary>
public static class Schema
{
    /// <summary>
    /// Schema text
    /// </summary>
    public const string Json = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://payrollengine.org/schemas/lex/v1.json",
          "title": "lex.json — PayrollEngine Compliance Source Manifest",
          "description": "Source manifest documenting the provenance of compliance values in a data regulation satellite.",
          "type": "object",
          "required": ["authority", "regulation", "validFrom"],
          "additionalProperties": false,
          "properties": {
            "$schema": {
              "type": "string",
              "description": "JSON Schema URI for IDE validation"
            },
            "authority": {
              "type": "string",
              "description": "Country/jurisdiction code (e.g. XX)",
              "examples": ["XX"]
            },
            "regulation": {
              "type": "string",
              "description": "Regulation identifier — typically the Data satellite name (e.g. Data.Tax, Data.Rates)",
              "examples": ["Data.Tax", "Data.Rates", "Data.Brackets"]
            },
            "validFrom": {
              "type": "string",
              "pattern": "^\\d{4}-\\d{2}-\\d{2}$",
              "description": "Effective date in YYYY-MM-DD format",
              "examples": ["2026-01-01", "2022-01-01"]
            },
            "provider": {
              "type": "string",
              "description": "Issuing authority or data provider (e.g. Tax authority)",
              "examples": ["Tax authority", "Social security agency"]
            },
            "sources": {
              "type": "array",
              "description": "Document-level source references — general provenance for the entire manifest",
              "items": {
                "$ref": "#/$defs/source"
              }
            },
            "values": {
              "type": "array",
              "description": "Compliance values with content and precise provenance",
              "items": {
                "$ref": "#/$defs/value"
              }
            },
            "files": {
              "type": "array",
              "description": "Source documents to copy into the .lex folder",
              "items": {
                "$ref": "#/$defs/file"
              }
            },
            "exclusions": {
              "type": "array",
              "description": "Documented exclusions from the coverage and sourceLocation checks. Excluded items are listed in the check report.",
              "items": {
                "$ref": "#/$defs/exclusion"
              }
            }
          },
          "$defs": {
            "exclusion": {
              "type": "object",
              "required": ["kind", "pattern", "reason"],
              "additionalProperties": false,
              "properties": {
                "kind": {
                  "type": "string",
                  "enum": ["coverage", "sourceLocation", "orphan"],
                  "description": "coverage: regulation values without lex entry. sourceLocation: lex entries without sourceLocation. orphan: lex entries without regulation value (evidence of a rule implemented in code)."
                },
                "pattern": {
                  "type": "string",
                  "description": "Key pattern: Lookup, Lookup.Key, or a prefix with trailing '*'. A name also matches all keys below it."
                },
                "reason": {
                  "type": "string",
                  "description": "Mandatory justification for the exclusion"
                }
              }
            },
            "source": {
              "type": "object",
              "required": ["url"],
              "additionalProperties": false,
              "properties": {
                "url": {
                  "type": "string",
                  "format": "uri",
                  "description": "URL to the source document or page"
                },
                "description": {
                  "type": "string",
                  "description": "Human-readable description of the source"
                },
                "legalBasis": {
                  "type": "string",
                  "description": "Legal basis (e.g. Income Tax Act §12, Act 123/2025 Art. 4)",
                  "examples": ["Income Tax Act §12", "Act 123/2025 Art. 4", "Employment Act s.38"]
                },
                "review": {
                  "type": "string",
                  "enum": ["SingleAuthor", "FourEyes"],
                  "default": "SingleAuthor",
                  "description": "Review rule for the values of this source (values whose sourceUrl equals the source url). FourEyes: manual verifications require reviewedBy and reviewedOn."
                }
              }
            },
            "verification": {
              "type": "object",
              "required": ["mode", "verifiedOn"],
              "additionalProperties": false,
              "properties": {
                "mode": {
                  "type": "string",
                  "enum": ["auto", "manual"],
                  "description": "auto: verified by a tool against the source. manual: verified by a person with an evidence document."
                },
                "verifiedOn": {
                  "type": "string",
                  "pattern": "^\\d{4}-\\d{2}-\\d{2}$",
                  "description": "Verification date in YYYY-MM-DD format",
                  "examples": ["2026-01-15"]
                },
                "verifiedBy": {
                  "type": "string",
                  "description": "Person who verified the value (manual mode only)",
                  "examples": ["Jane Doe"]
                },
                "evidence": {
                  "type": "string",
                  "description": "Evidence document declared in the files[] array (mandatory in manual mode)",
                  "examples": ["tax-rates-2026.pdf"]
                },
                "method": {
                  "type": "string",
                  "description": "Tool and version that verified the value (auto mode only)",
                  "examples": ["LexVerifySource 1.2"]
                },
                "reviewedBy": {
                  "type": "string",
                  "description": "Second person who confirmed a manual verification (sources with review FourEyes)",
                  "examples": ["John Doe"]
                },
                "reviewedOn": {
                  "type": "string",
                  "pattern": "^\\d{4}-\\d{2}-\\d{2}$",
                  "description": "Review date in YYYY-MM-DD format (sources with review FourEyes)",
                  "examples": ["2026-01-16"]
                }
              },
              "dependentRequired": {
                "reviewedBy": ["reviewedOn"],
                "reviewedOn": ["reviewedBy"]
              },
              "allOf": [
                {
                  "if": { "properties": { "mode": { "const": "manual" } } },
                  "then": { "required": ["verifiedBy", "evidence"], "not": { "required": ["method"] } }
                },
                {
                  "if": { "properties": { "mode": { "const": "auto" } } },
                  "then": { "required": ["method"], "not": { "required": ["verifiedBy"] } }
                }
              ]
            },
            "value": {
              "type": "object",
              "required": ["key"],
              "additionalProperties": false,
              "properties": {
                "key": {
                  "type": "string",
                  "description": "Unique value identifier within this manifest"
                },
                "content": {
                  "type": "string",
                  "description": "Inline compliance value — scalar, JSON object, or JSON array. Mutually exclusive with contentFile."
                },
                "contentFile": {
                  "type": "string",
                  "description": "Relative path to a source file to copy into the .lex folder. Mutually exclusive with content."
                },
                "documentRef": {
                  "type": "string",
                  "description": "Reference to a local evidence document declared in the files[] array (e.g. 'tax-rates-2026.pdf'). The referenced fileName must exist in files[]."
                },
                "unit": {
                  "type": "string",
                  "description": "Unit of measure",
                  "examples": ["EUR", "%", "CHF", "SGD", "months", "days"]
                },
                "sourceUrl": {
                  "type": "string",
                  "format": "uri",
                  "description": "URL where this specific value can be found"
                },
                "sourceLocation": {
                  "type": "string",
                  "description": "Precise location within the source document (free text). Examples: 'Calculation guide 2026, Page 12, Section 3.2.1', 'Income Tax Act §12(1) sentence 2', 'File tariff-a.txt'"
                },
                "legalReference": {
                  "type": "string",
                  "description": "Legal norm reference",
                  "examples": ["Income Tax Act §12(1)", "Act 123/2025 Art. 4", "Employment Act s.35(1)"]
                },
                "description": {
                  "type": "string",
                  "description": "Human-readable description of this value"
                },
                "verification": {
                  "$ref": "#/$defs/verification"
                },
                "open": {
                  "type": "boolean",
                  "description": "Open entry: value taken from the previous cycle by LexRollover, needs a new verification. A new verification record removes the flag."
                }
              }
            },
            "file": {
              "type": "object",
              "required": ["fileName"],
              "additionalProperties": false,
              "properties": {
                "fileName": {
                  "type": "string",
                  "description": "Target file name in the .lex folder"
                },
                "sourcePath": {
                  "type": "string",
                  "description": "Local path to the source file (copied into .lex by LexSnapshot)"
                },
                "sourceUrl": {
                  "type": "string",
                  "format": "uri",
                  "description": "Source URL — file is downloaded automatically by LexSnapshot when sourcePath is empty"
                },
                "description": {
                  "type": "string",
                  "description": "Human-readable description of this file"
                }
              }
            }
          }
        }
        """;
}
