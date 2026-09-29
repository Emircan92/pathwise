using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pathwise.Application.Interpretation;
using Pathwise.Infrastructure.Interpretation;

namespace Pathwise.Infrastructure.Tests;

public sealed class OpenAiNarrativeInterpretationProviderTests
{
    [Fact]
    public async Task SendsResponsesStructuredOutputRequestWithoutStoringResponse()
    {
        var input = Input();
        var output = Output(input);
        var structured = JsonSerializer.Serialize(output, NarrativeInterpretationJson.Options);
        var response = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-sol-2026-09-01",
            output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = structured } } } }
        });
        var handler = new RecordingHandler(new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        var provider = Provider(handler, EnabledOptions());

        var result = await provider.InterpretAsync(input, CancellationToken.None);

        Assert.Equal("openai", result.Provider);
        Assert.Equal("gpt-5.6-sol-2026-09-01", result.Model);
        Assert.Equal("Bearer test-key", handler.Authorization);
        using var request = JsonDocument.Parse(handler.Body!);
        var root = request.RootElement;
        Assert.Equal("gpt-5.6-sol", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("medium", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("json_schema", root.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.True(root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        var schema = root.GetProperty("text").GetProperty("format").GetProperty("schema");
        var threadProperties = schema.GetProperty("properties").GetProperty("threads").GetProperty("items").GetProperty("properties");
        var momentProperties = schema.GetProperty("properties").GetProperty("momentsWorthInvestigating").GetProperty("items").GetProperty("properties");
        AssertTimestampBounds(threadProperties.GetProperty("startTimestampMs"));
        AssertTimestampBounds(threadProperties.GetProperty("endTimestampMs"));
        AssertTimestampBounds(momentProperties.GetProperty("startTimestampMs"));
        AssertTimestampBounds(momentProperties.GetProperty("endTimestampMs"));
        var serializedInput = root.GetProperty("input").GetString()!;
        Assert.Contains(input.InputFingerprint, serializedInput);
        Assert.Contains("\"timestampMs\":1860547", serializedInput, StringComparison.Ordinal);
        Assert.DoesNotContain("configuredPlayerPosition", serializedInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enemyJunglerPosition", serializedInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"x\":", serializedInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"y\":", serializedInput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsMalformedStructuredOutput()
    {
        var response = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-sol",
            output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = "{" } } } }
        });
        var provider = Provider(new RecordingHandler(new(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json")
        }), EnabledOptions());

        var exception = await Assert.ThrowsAsync<NarrativeProviderException>(() => provider.InterpretAsync(Input(), CancellationToken.None));

        Assert.Equal(NarrativeProviderFailureKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task RejectsStructuredOutputWithUnknownSchemaMembers()
    {
        var input = Input();
        var structured = JsonSerializer.Serialize(Output(input), NarrativeInterpretationJson.Options);
        structured = structured[..^1] + ",\"recommendation\":\"Do something else.\"}";
        var response = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-sol",
            output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = structured } } } }
        });
        var provider = Provider(new RecordingHandler(new(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json")
        }), EnabledOptions());

        var exception = await Assert.ThrowsAsync<NarrativeProviderException>(() => provider.InterpretAsync(input, CancellationToken.None));

        Assert.Equal(NarrativeProviderFailureKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task TranslatesProviderFailureWithoutExposingAuthorization()
    {
        var handler = new RecordingHandler(new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"error\":{\"message\":\"Rate limit reached.\"}}", Encoding.UTF8, "application/json")
        });
        var provider = Provider(handler, EnabledOptions());

        var exception = await Assert.ThrowsAsync<NarrativeProviderException>(() => provider.InterpretAsync(Input(), CancellationToken.None));

        Assert.Equal(NarrativeProviderFailureKind.RequestFailed, exception.Kind);
        Assert.Equal(429, exception.HttpStatus);
        Assert.DoesNotContain("test-key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledProviderDoesNotSendRequest()
    {
        var handler = new RecordingHandler(new(HttpStatusCode.OK));
        var options = EnabledOptions();
        options.Enabled = false;
        var provider = Provider(handler, options);

        var exception = await Assert.ThrowsAsync<NarrativeProviderException>(() => provider.InterpretAsync(Input(), CancellationToken.None));

        Assert.Equal(NarrativeProviderFailureKind.Disabled, exception.Kind);
        Assert.Equal(0, handler.CallCount);
    }

    private static OpenAiNarrativeInterpretationProvider Provider(RecordingHandler handler, NarrativeInterpretationOptions options) => new(
        new HttpClient(handler),
        Options.Create(options),
        TimeProvider.System,
        NullLogger<OpenAiNarrativeInterpretationProvider>.Instance);

    private static NarrativeInterpretationOptions EnabledOptions() => new()
    {
        Enabled = true,
        ApiKey = "test-key",
        Model = "gpt-5.6-sol",
        ReasoningEffort = "medium",
        BaseUrl = "https://api.openai.com/v1"
    };

    private static void AssertTimestampBounds(JsonElement timestampSchema)
    {
        Assert.Equal("integer", timestampSchema.GetProperty("type").GetString());
        Assert.Equal(1_910_458, timestampSchema.GetProperty("minimum").GetInt64());
        Assert.Equal(2_100_622, timestampSchema.GetProperty("maximum").GetInt64());
    }

    private static NarrativeInterpretationInputV1 Input()
    {
        var input = new NarrativeInterpretationInputV1(
            1,
            string.Empty,
            new(1, 2, 1, 1, 1, 1),
            2,
            7,
            [new(2, "Khazix", 100, "configuredPlayer"), new(7, "Kayn", 200, "enemyJungler")],
            new(1_910_458, 2_100_622, new(31, 1_860_547), new(35, 2_100_622), "goldAndXpChange", ["goldDifferenceChange", "xpDifferenceChange"], []),
            new(null, "unknownPatch"),
            [
                new NarrativeWindowSelectionEvidenceV1("window:selection", "goldAndXpChange", ["goldDifferenceChange", "xpDifferenceChange"], []),
                new NarrativeMetricObservationEvidenceV1(
                    "observation:relativeGoldMovement",
                    "relativeGoldMovement",
                    new(31, 1_860_547),
                    new(35, 2_100_622),
                    6_926,
                    5_746,
                    -1_180,
                    new(20_608, 22_842),
                    new(13_682, 17_096))
            ],
            ["No periodic position samples, raw map coordinates, inferred paths, or inferred map regions are supplied."]);
        return input with { InputFingerprint = NarrativeInterpretationFingerprint.Compute(input) };
    }

    private static NarrativeInterpretationModelOutputV1 Output(NarrativeInterpretationInputV1 input) => new(
        1,
        input.InputFingerprint,
        new("The selected period contains a gold-change signal.", "factSummary", ["window:selection"]),
        [],
        [],
        [new("The signal does not establish why the change occurred.", "notCaptured", ["window:selection"])]);

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString();
            return response;
        }
    }
}
