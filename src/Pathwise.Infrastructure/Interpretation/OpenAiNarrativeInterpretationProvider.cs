using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pathwise.Application.Interpretation;

namespace Pathwise.Infrastructure.Interpretation;

public sealed class OpenAiNarrativeInterpretationProvider(
    HttpClient httpClient,
    IOptions<NarrativeInterpretationOptions> options,
    TimeProvider timeProvider,
    ILogger<OpenAiNarrativeInterpretationProvider> logger) : INarrativeInterpretationProvider
{
    private static readonly HashSet<string> ReasoningEfforts = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "minimal", "low", "medium", "high", "xhigh", "max"
    };

    public async Task<NarrativeProviderResultV1> InterpretAsync(
        NarrativeInterpretationInputV1 input,
        CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        Validate(configuration);
        var endpoint = $"{configuration.BaseUrl.TrimEnd('/')}/responses";
        using var schema = JsonDocument.Parse(OpenAiNarrativeOutputSchemaV1.Create(
            input.Window.RequestedStartTimestampMs,
            input.Window.RequestedEndTimestampMs));
        var requestBody = new
        {
            model = configuration.Model,
            store = false,
            reasoning = new { effort = configuration.ReasoningEffort },
            max_output_tokens = configuration.MaximumOutputTokens,
            instructions = NarrativeInterpretationPromptPolicyV1.Instructions,
            input = JsonSerializer.Serialize(input, NarrativeInterpretationJson.Options),
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "pathwise_narrative_interpretation_v1",
                    strict = true,
                    schema = schema.RootElement.Clone()
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(requestBody, options: NarrativeInterpretationJson.Options)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);

        logger.LogInformation(
            "Narrative interpretation request started for input {InputFingerprint} using model {Model}.",
            input.InputFingerprint,
            configuration.Model);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NarrativeProviderException(
                NarrativeProviderFailureKind.RequestFailed,
                "The OpenAI narrative interpretation request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new NarrativeProviderException(
                NarrativeProviderFailureKind.RequestFailed,
                $"The OpenAI narrative interpretation request failed: {exception.Message}");
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new NarrativeProviderException(
                    NarrativeProviderFailureKind.RequestFailed,
                    ProviderError(responseText, response.StatusCode),
                    (int)response.StatusCode);
            }

            NarrativeInterpretationModelOutputV1 output;
            string returnedModel;
            try
            {
                using var document = JsonDocument.Parse(responseText);
                var root = document.RootElement;
                var status = root.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null;
                if (!string.Equals(status, "completed", StringComparison.Ordinal))
                    throw new NarrativeProviderException(
                        NarrativeProviderFailureKind.InvalidResponse,
                        $"OpenAI returned a narrative response with status '{status ?? "unknown"}'.");
                returnedModel = root.TryGetProperty("model", out var modelValue) && !string.IsNullOrWhiteSpace(modelValue.GetString())
                    ? modelValue.GetString()!
                    : configuration.Model;
                var structuredText = ExtractOutputText(root) ?? throw new NarrativeProviderException(
                    NarrativeProviderFailureKind.InvalidResponse,
                    "OpenAI returned no structured narrative output.");
                output = JsonSerializer.Deserialize<NarrativeInterpretationModelOutputV1>(
                    structuredText,
                    NarrativeInterpretationJson.Options) ?? throw new NarrativeProviderException(
                        NarrativeProviderFailureKind.InvalidResponse,
                        "OpenAI returned an empty structured narrative output.");
            }
            catch (NarrativeProviderException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw new NarrativeProviderException(
                    NarrativeProviderFailureKind.InvalidResponse,
                    $"OpenAI returned malformed narrative JSON: {exception.Message}");
            }

            logger.LogInformation(
                "Narrative interpretation request completed for input {InputFingerprint} using model {Model}.",
                input.InputFingerprint,
                returnedModel);
            return new(output, "openai", returnedModel, timeProvider.GetUtcNow());
        }
    }

    private static void Validate(NarrativeInterpretationOptions value)
    {
        if (!value.Enabled)
            throw new NarrativeProviderException(NarrativeProviderFailureKind.Disabled, "Narrative interpretation is disabled.");
        if (!string.Equals(value.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
            throw new NarrativeProviderException(NarrativeProviderFailureKind.InvalidConfiguration, "Narrative interpretation provider must be OpenAI for V1.");
        if (string.IsNullOrWhiteSpace(value.ApiKey))
            throw new NarrativeProviderException(NarrativeProviderFailureKind.InvalidConfiguration, "The OpenAI API key is not configured.");
        if (string.IsNullOrWhiteSpace(value.Model))
            throw new NarrativeProviderException(NarrativeProviderFailureKind.InvalidConfiguration, "The OpenAI model is not configured.");
        if (!ReasoningEfforts.Contains(value.ReasoningEffort))
            throw new NarrativeProviderException(NarrativeProviderFailureKind.InvalidConfiguration, "The configured OpenAI reasoning effort is not supported.");
        if (value.TimeoutSeconds is < 1 or > 300)
            throw new NarrativeProviderException(NarrativeProviderFailureKind.InvalidConfiguration, "Narrative interpretation timeout must be between 1 and 300 seconds.");
        if (value.MaximumOutputTokens is < 256 or > 32_000)
            throw new NarrativeProviderException(NarrativeProviderFailureKind.InvalidConfiguration, "Narrative interpretation maximum output tokens must be between 256 and 32000.");
        if (!Uri.TryCreate(value.BaseUrl, UriKind.Absolute, out _))
            throw new NarrativeProviderException(NarrativeProviderFailureKind.InvalidConfiguration, "The OpenAI base URL is invalid.");
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemType) || itemType.GetString() != "message") continue;
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var partType) && partType.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text)) return text.GetString();
                if (part.TryGetProperty("type", out partType) && partType.GetString() == "refusal")
                    throw new NarrativeProviderException(NarrativeProviderFailureKind.InvalidResponse, "OpenAI refused the narrative interpretation request.");
            }
        }
        return null;
    }

    private static string ProviderError(string responseText, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message) &&
                !string.IsNullOrWhiteSpace(message.GetString()))
                return $"OpenAI request failed ({(int)statusCode}): {message.GetString()}";
        }
        catch (JsonException)
        {
        }
        return $"OpenAI request failed with HTTP {(int)statusCode}.";
    }
}
