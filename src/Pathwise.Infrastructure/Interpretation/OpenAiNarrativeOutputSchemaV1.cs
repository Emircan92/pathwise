using System.Globalization;

namespace Pathwise.Infrastructure.Interpretation;

internal static class OpenAiNarrativeOutputSchemaV1
{
    public static string Create(long requestedStartTimestampMs, long requestedEndTimestampMs)
    {
        if (requestedStartTimestampMs > requestedEndTimestampMs)
            throw new ArgumentException("The requested narrative window bounds are reversed.");

        return Template
            .Replace("NARRATIVE_TIMESTAMP_MINIMUM", requestedStartTimestampMs.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("NARRATIVE_TIMESTAMP_MAXIMUM", requestedEndTimestampMs.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private const string Template = """
        {
          "type": "object",
          "properties": {
            "version": { "type": "integer", "const": 1 },
            "inputFingerprint": { "type": "string", "minLength": 64, "maxLength": 64 },
            "overview": { "$ref": "#/$defs/claim" },
            "threads": {
              "type": "array",
              "maxItems": 6,
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string", "minLength": 1, "maxLength": 160 },
                  "startTimestampMs": { "type": "integer", "minimum": NARRATIVE_TIMESTAMP_MINIMUM, "maximum": NARRATIVE_TIMESTAMP_MAXIMUM },
                  "endTimestampMs": { "type": "integer", "minimum": NARRATIVE_TIMESTAMP_MINIMUM, "maximum": NARRATIVE_TIMESTAMP_MAXIMUM },
                  "summary": { "$ref": "#/$defs/claim" },
                  "significantDevelopments": { "type": "array", "maxItems": 6, "items": { "$ref": "#/$defs/claim" } }
                },
                "required": ["title", "startTimestampMs", "endTimestampMs", "summary", "significantDevelopments"],
                "additionalProperties": false
              }
            },
            "momentsWorthInvestigating": {
              "type": "array",
              "maxItems": 6,
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string", "minLength": 1, "maxLength": 160 },
                  "startTimestampMs": { "type": "integer", "minimum": NARRATIVE_TIMESTAMP_MINIMUM, "maximum": NARRATIVE_TIMESTAMP_MAXIMUM },
                  "endTimestampMs": { "type": "integer", "minimum": NARRATIVE_TIMESTAMP_MINIMUM, "maximum": NARRATIVE_TIMESTAMP_MAXIMUM },
                  "whyItStandsOut": { "$ref": "#/$defs/claim" },
                  "question": { "type": "string", "minLength": 1, "maxLength": 500 },
                  "uncertainties": { "type": "array", "minItems": 1, "maxItems": 6, "items": { "$ref": "#/$defs/uncertainty" } }
                },
                "required": ["title", "startTimestampMs", "endTimestampMs", "whyItStandsOut", "question", "uncertainties"],
                "additionalProperties": false
              }
            },
            "uncertainties": { "type": "array", "minItems": 1, "maxItems": 8, "items": { "$ref": "#/$defs/uncertainty" } }
          },
          "required": ["version", "inputFingerprint", "overview", "threads", "momentsWorthInvestigating", "uncertainties"],
          "additionalProperties": false,
          "$defs": {
            "claim": {
              "type": "object",
              "properties": {
                "text": { "type": "string", "minLength": 1, "maxLength": 1000 },
                "basis": { "type": "string", "enum": ["factSummary", "crossEvidenceSynthesis"] },
                "evidenceIds": { "type": "array", "minItems": 1, "maxItems": 16, "items": { "type": "string" } }
              },
              "required": ["text", "basis", "evidenceIds"],
              "additionalProperties": false
            },
            "uncertainty": {
              "type": "object",
              "properties": {
                "statement": { "type": "string", "minLength": 1, "maxLength": 700 },
                "reason": { "type": "string", "enum": ["notCaptured", "missingSource", "ambiguousAttribution", "samplingGap", "conflictingSources", "outsideNarrativeScope"] },
                "relatedEvidenceIds": { "type": "array", "maxItems": 16, "items": { "type": "string" } }
              },
              "required": ["statement", "reason", "relatedEvidenceIds"],
              "additionalProperties": false
            }
          }
        }
        """;
}
