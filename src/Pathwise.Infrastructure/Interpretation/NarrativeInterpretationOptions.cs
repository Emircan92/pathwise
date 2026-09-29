namespace Pathwise.Infrastructure.Interpretation;

public sealed class NarrativeInterpretationOptions
{
    public const string SectionName = "NarrativeInterpretation";

    public bool Enabled { get; set; }
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-5.6-sol";
    public string ReasoningEffort { get; set; } = "medium";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaximumOutputTokens { get; set; } = 6_000;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
}
