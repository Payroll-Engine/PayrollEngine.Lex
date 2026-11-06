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
              "description": "Documented exclusions from the coverage, sourceLocation, orphan and automatic verification rules, and derived properties of entries. Excluded items are listed in the check report.",
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
                  "enum": ["coverage", "sourceLocation", "orphan", "autoVerification", "derived"],
                  "description": "coverage: regulation values without lex entry. sourceLocation: lex entries without sourceLocation. orphan: lex entries without regulation value (evidence of a rule implemented in code). autoVerification: lex entries verified manually only (LexVerifySource writes no source location and no automatic verification record). derived: property of a lex entry that is derived from other values and is not in the source (LexVerifySource does not look up its numbers)."
                },
                "pattern": {
                  "type": "string",
                  "description": "Key pattern: Lookup, Lookup.Key, or a prefix with trailing '*'. A name also matches all keys below it. derived: entry key and property path of the JSON content (Lookup.Key.Property, nested properties separated by '.')."
                },
                "reason": {
                  "type": "string",
                  "description": "Mandatory justification for the exclusion; derived: formula and legal reference",
                  "examples": ["5% of BaseRate (0.06), Example Act s. 12(2)"]
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
                  "description": "Review rule for the values of this source (values whose sourceUrls list the source url). FourEyes: manual verifications require reviewedBy and reviewedOn."
                },
                "official": {
                  "type": "boolean",
                  "default": true,
                  "description": "Official source of the issuing authority. false: secondary source (e.g. a commercial summary); it may be a further source of a value, but not the main source (first URL of sourceUrls)."
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
                },
                "derived": {
                  "type": "array",
                  "minItems": 1,
                  "description": "Properties of the value that are derived from other values and not taken from the source (auto mode only, see exclusion kind derived)",
                  "items": {
                    "type": "string"
                  },
                  "examples": [["CreditReduction"]]
                }
              },
              "dependentRequired": {
                "reviewedBy": ["reviewedOn"],
                "reviewedOn": ["reviewedBy"]
              },
              "allOf": [
                {
                  "if": { "properties": { "mode": { "const": "manual" } } },
                  "then": {
                    "required": ["verifiedBy", "evidence"],
                    "not": { "anyOf": [ { "required": ["method"] }, { "required": ["derived"] } ] }
                  }
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
                "sourceUrls": {
                  "type": "array",
                  "description": "URLs where this specific value can be found. A value combined from several authorities lists one URL per authority; the first one is the main source.",
                  "items": {
                    "type": "string",
                    "format": "uri"
                  }
                },
                "sourceLocation": {
                  "type": "string",
                  "description": "Precise location within the source document (free text). Examples: 'Calculation guide 2026, Page 12, Section 3.2.1', 'Income Tax Act §12(1) sentence 2', 'File tariff-a.txt'"
                },
                "sourceQuote": {
                  "type": "string",
                  "minLength": 1,
                  "description": "Verbatim quote from the source that holds the value. LexVerifySource looks up the numbers of the value in the section of the quote; numbers in words within the quote count (e.g. 'one and one-half times')."
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
                "sourceUrls": {
                  "type": "array",
                  "description": "Source URLs — when sourcePath is empty, LexSnapshot downloads the file from the first URL that answers",
                  "items": {
                    "type": "string",
                    "format": "uri"
                  }
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
