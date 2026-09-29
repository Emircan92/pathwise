using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pathwise.Application.Ingestion;
using Pathwise.Application.Interpretation;
using Pathwise.Infrastructure.Persistence;

namespace Pathwise.Api.Tests;

public sealed class MatchReviewEndpointTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"pathwise-review-api-{Guid.NewGuid():N}.db");
    private TestFactory _factory = null!;
    private HttpClient _client = null!;
    private FakeNarrativeProvider _narrativeProvider = null!;

    public async Task InitializeAsync()
    {
        _narrativeProvider = new();
        _factory = new(_databasePath, _narrativeProvider);
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_databasePath + suffix);
    }

    [Fact]
    public async Task ReturnsReviewedContractAndRepresentativeWindowThroughRealComposition()
    {
        var response = await _client.GetAsync("/api/matches/EUW1_1/review");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(
            ["matchId", "mapId", "configuredParticipantId", "participants", "enemyResolution", "versions", "knowledge", "progression", "sourceDataIssues", "windows"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("EUW1_1", root.GetProperty("matchId").GetString());
        Assert.Equal(11, root.GetProperty("mapId").GetInt32());
        Assert.Equal(2, root.GetProperty("configuredParticipantId").GetInt32());
        var participants = root.GetProperty("participants").EnumerateArray().ToArray();
        Assert.Equal(10, participants.Length);
        Assert.Equal(Enumerable.Range(1, 10), participants.Select(value => value.GetProperty("participantId").GetInt32()));
        Assert.Equal("Khazix", participants[1].GetProperty("championName").GetString());
        Assert.Equal(100, participants[1].GetProperty("teamId").GetInt32());
        Assert.Equal("resolved", root.GetProperty("enemyResolution").GetProperty("status").GetString());
        Assert.Equal(7, root.GetProperty("enemyResolution").GetProperty("participantId").GetInt32());
        Assert.Equal("26.18", root.GetProperty("knowledge").GetProperty("publicPatch").GetString());
        Assert.Equal("available", root.GetProperty("knowledge").GetProperty("coverage").GetString());
        Assert.Equal(1, root.GetProperty("versions").GetProperty("encounters").GetInt32());
        Assert.Equal(1, root.GetProperty("versions").GetProperty("progression").GetInt32());
        Assert.Equal([3, 6, 2, 4, 4], root.GetProperty("windows").EnumerateArray()
            .Select(value => value.GetProperty("encounters").GetArrayLength()).ToArray());

        var window = root.GetProperty("windows").EnumerateArray().Single(value => value.GetProperty("requestedStartTimestampMs").GetInt64() == 1_421_654);
        Assert.Equal(1_620_500, window.GetProperty("requestedEndTimestampMs").GetInt64());
        Assert.Equal(23, window.GetProperty("startFrame").GetProperty("frameIndex").GetInt32());
        Assert.Equal(1_380_440, window.GetProperty("startFrame").GetProperty("timestampMs").GetInt64());
        var samples = window.GetProperty("positionSamples").EnumerateArray().ToArray();
        Assert.Equal([23, 24, 25, 26, 27], samples.Select(sample => sample.GetProperty("frameIndex").GetInt32()).ToArray());
        Assert.Equal([1_380_440L, 1_440_464L, 1_500_470L, 1_560_471L, 1_620_500L], samples.Select(sample => sample.GetProperty("timestampMs").GetInt64()).ToArray());
        Assert.Equal((7443, 3036), Position(samples[0].GetProperty("configuredPlayerPosition")));
        Assert.Equal((9665, 5278), Position(samples[0].GetProperty("enemyJunglerPosition")));
        Assert.Equal((463, 692), Position(samples[1].GetProperty("configuredPlayerPosition")));
        Assert.Equal("goldAndXpChange", window.GetProperty("primarySelectionReason").GetString());
        Assert.Equal(
            ["relativeGoldMovement", "relativeXpMovement", "relativeJungleCsMovement", "configuredPlayerCombat", "eliteObjectiveContext"],
            window.GetProperty("observations").EnumerateArray().Select(value => value.GetProperty("kind").GetString()!).ToArray());

        var gold = window.GetProperty("observations")[0];
        Assert.Equal(4_742, gold.GetProperty("relativeStart").GetInt64());
        Assert.Equal(3_182, gold.GetProperty("relativeEnd").GetInt64());
        Assert.Equal(-1_560, gold.GetProperty("signedChange").GetInt64());
        var combat = window.GetProperty("observations")[3];
        Assert.Equal((0, 2, 1, 3), (combat.GetProperty("kills").GetInt32(), combat.GetProperty("deaths").GetInt32(), combat.GetProperty("assists").GetInt32(), combat.GetProperty("distinctEventCount").GetInt32()));
        var annotations = window.GetProperty("knowledgeAnnotations").EnumerateArray().ToArray();
        Assert.Equal(2, annotations.Length);
        Assert.All(annotations, annotation => Assert.Equal("recordedObjectiveContext", annotation.GetProperty("kind").GetString()));
        Assert.Equal((24, 30), Source(annotations[1]));
        Assert.Equal((26, 7), Source(annotations[0]));
        Assert.All(window.GetProperty("observations")[4].GetProperty("events").EnumerateArray(), value => Assert.True(value.TryGetProperty("source", out _)));
        var objectives = window.GetProperty("observations")[4].GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal((9837, 4397), Position(objectives.Single(value => SourceEvent(value) == (24, 30)).GetProperty("position")));
        Assert.Equal((5007, 10471), Position(objectives.Single(value => SourceEvent(value) == (26, 7)).GetProperty("position")));
        Assert.All(combat.GetProperty("events").EnumerateArray(), value => Assert.True(value.TryGetProperty("position", out _)));
        var encounters = window.GetProperty("encounters").EnumerateArray().ToArray();
        Assert.Equal(4, encounters.Length);
        var grouped = encounters[1];
        Assert.Equal(4, grouped.GetProperty("combatEventCount").GetInt32());
        Assert.Equal(4, grouped.GetProperty("combatEvents").GetArrayLength());
        Assert.StartsWith("enc-v1-", grouped.GetProperty("id").GetString());
        Assert.True(grouped.GetProperty("enemyJunglerInvolved").ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.Contains(grouped.GetProperty("associatedObjectiveEvents").EnumerateArray(), value =>
            value.GetProperty("monsterType").GetString() == "BARON_NASHOR");
    }

    [Fact]
    public async Task ExposesTheProgressionCalibrationSequenceWithoutMergingCombatEncounters()
    {
        var response = await _client.GetAsync("/api/matches/EUW1_2/review");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var progression = root.GetProperty("progression");
        var outcome = progression.GetProperty("outcome");
        Assert.Equal(100, outcome.GetProperty("resolvedWinningTeamId").GetInt32());
        Assert.True(outcome.GetProperty("configuredPlayerWon").GetProperty("value").GetBoolean());
        Assert.Equal("match.info.participants[1].win", outcome.GetProperty("configuredPlayerWon").GetProperty("source").GetProperty("jsonPath").GetString());
        Assert.Equal("GameComplete", outcome.GetProperty("endOfGameResult").GetProperty("value").GetString());
        var configuredSummary = outcome.GetProperty("participantResults").EnumerateArray().Single(value =>
            value.GetProperty("participantId").GetInt32() == 2);
        Assert.Equal(1, configuredSummary.GetProperty("nexusKills").GetProperty("value").GetInt32());
        Assert.Equal("match.info.participants[1].nexusKills", configuredSummary.GetProperty("nexusKills").GetProperty("source").GetProperty("jsonPath").GetString());

        var matchEvents = progression.GetProperty("events").EnumerateArray().ToArray();
        Assert.Contains(matchEvents, value => value.GetProperty("kind").GetString() == "riftHeraldKilled" && value.GetProperty("timestampMs").GetInt64() == 982_051);
        Assert.Contains(matchEvents, value => value.GetProperty("kind").GetString() == "itemDestroyed" && value.GetProperty("itemId").GetInt32() == 3513);

        var lateWindow = root.GetProperty("windows").EnumerateArray().Single(value =>
            value.GetProperty("progression").GetProperty("containsGameEnd").GetBoolean());
        var relevant = lateWindow.GetProperty("progression").GetProperty("events").EnumerateArray()
            .Where(value => value.GetProperty("kind").GetString() == "gameEnded" ||
                value.GetProperty("kind").GetString() == "buildingDestroyed" &&
                value.GetProperty("structureOwnerTeam").GetProperty("resolvedTeamId").GetInt32() == 200)
            .ToArray();
        Assert.Equal(
            ["buildingDestroyed", "buildingDestroyed", "buildingDestroyed", "buildingDestroyed", "buildingDestroyed", "buildingDestroyed", "gameEnded"],
            relevant.Select(value => value.GetProperty("kind").GetString()!).ToArray());
        Assert.Equal([1_206_286L, 1_213_961L, 1_221_794L, 1_228_277L, 1_257_369L, 1_264_618L, 1_286_891L],
            relevant.Select(value => value.GetProperty("timestampMs").GetInt64()).ToArray());
        Assert.Equal([(21, 3), (21, 7), (21, 15), (21, 20), (21, 45), (22, 0), (22, 11)],
            relevant.Select(SourceEvent).ToArray());
        Assert.Equal(2, lateWindow.GetProperty("encounters").GetArrayLength());

        var dragon = root.GetProperty("windows").EnumerateArray()
            .SelectMany(window => window.GetProperty("observations").EnumerateArray())
            .Where(observation => observation.GetProperty("kind").GetString() == "eliteObjectiveContext")
            .SelectMany(observation => observation.GetProperty("events").EnumerateArray())
            .Single(value => value.GetProperty("timestampMs").GetInt64() == 1_054_897);
        Assert.Equal(7, dragon.GetProperty("killerParticipantId").GetInt32());
        Assert.Equal(200, dragon.GetProperty("teamAttribution").GetProperty("resolvedTeamId").GetInt32());
        Assert.Contains(2, dragon.GetProperty("assistingParticipantIds").EnumerateArray().Select(value => value.GetInt32()));

        Assert.DoesNotContain("summoned", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("charge", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("base siege", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("push to end", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Maps404ConflictAndUnprocessableFailures()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/matches/OTHER/review")).StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.StoredMatches.Add(new() { MatchId = "EUW1_20", PlayerPuuid = "participant-2-puuid", Regional = "europe", DiscoveredAtUtc = DateTimeOffset.UtcNow, QueueId = 420, TeamPosition = "JUNGLE" });
            db.MatchPayloads.AddRange(new MatchPayloadEntity { MatchId = "EUW1_20", Kind = PayloadKind.Match, State = PayloadState.Missing }, new MatchPayloadEntity { MatchId = "EUW1_20", Kind = PayloadKind.Timeline, State = PayloadState.Missing });
            var broken = Fixture("timeline.json").Replace("\"participantId\": 2", "\"participantId\": \"bad\"", StringComparison.Ordinal);
            db.StoredMatches.Add(new() { MatchId = "EUW1_3", PlayerPuuid = "participant-2-puuid", Regional = "europe", DiscoveredAtUtc = DateTimeOffset.UtcNow, QueueId = 420, TeamPosition = "JUNGLE" });
            db.MatchPayloads.AddRange(
                new MatchPayloadEntity { MatchId = "EUW1_3", Kind = PayloadKind.Match, State = PayloadState.Stored, RawJson = Fixture("match.json").Replace("EUW1_1", "EUW1_3"), RetrievedAtUtc = DateTimeOffset.UtcNow },
                new MatchPayloadEntity { MatchId = "EUW1_3", Kind = PayloadKind.Timeline, State = PayloadState.Stored, RawJson = broken.Replace("EUW1_1", "EUW1_3"), RetrievedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await _client.GetAsync("/api/matches/EUW1_20/review")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.GetAsync("/api/matches/EUW1_3/review")).StatusCode);
    }

    [Fact]
    public async Task ReviewReadDoesNotChangeStoredPayloads()
    {
        string[] before;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            before = await db.MatchPayloads.AsNoTracking().Where(value => value.MatchId == "EUW1_1").OrderBy(value => value.Kind).Select(value => value.RawJson!).ToArrayAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/matches/EUW1_1/review")).StatusCode);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyFactory = verifyScope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
        await using var verify = await verifyFactory.CreateDbContextAsync();
        var after = await verify.MatchPayloads.AsNoTracking().Where(value => value.MatchId == "EUW1_1").OrderBy(value => value.Kind).Select(value => value.RawJson!).ToArrayAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task InterpretsExactSelectedWindowWithServerProjectedEvidenceWithoutPersistence()
    {
        var before = await StoredPayloadsAsync("EUW1_1");

        var response = await _client.PostAsJsonAsync("/api/matches/EUW1_1/review/interpretation", new
        {
            requestedStartTimestampMs = 1_421_654,
            requestedEndTimestampMs = 1_620_500,
            reconstructionVersion = 1,
            detectorVersion = 2
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(_narrativeProvider.LastInput!.InputFingerprint, root.GetProperty("inputFingerprint").GetString());
        Assert.Equal("A selected period was reconstructed from deterministic evidence.", root.GetProperty("overview").GetProperty("text").GetString());
        Assert.Single(root.GetProperty("uncertainties").EnumerateArray());
        Assert.Equal(1, _narrativeProvider.CallCount);
        Assert.Equal((1_421_654L, 1_620_500L),
            (_narrativeProvider.LastInput.Window.RequestedStartTimestampMs, _narrativeProvider.LastInput.Window.RequestedEndTimestampMs));
        Assert.Equal((2, 7), (_narrativeProvider.LastInput.ConfiguredParticipantId, _narrativeProvider.LastInput.EnemyJunglerParticipantId));
        Assert.Contains(_narrativeProvider.LastInput.Participants, value =>
            value.ParticipantId == 2 && value.ChampionName == "Khazix" && value.TeamId == 100 && value.Relationship == "configuredPlayer");
        Assert.Contains(_narrativeProvider.LastInput.Evidence, value => value is NarrativeMetricObservationEvidenceV1 { Id: "observation:relativeGoldMovement" });
        Assert.Contains(_narrativeProvider.LastInput.Evidence, value => value is NarrativeCombatEventEvidenceV1);
        Assert.Contains(_narrativeProvider.LastInput.Evidence, value => value is NarrativeObjectiveEventEvidenceV1);
        Assert.Contains(_narrativeProvider.LastInput.Evidence, value => value is NarrativeEncounterEvidenceV1);
        Assert.Contains(_narrativeProvider.LastInput.Evidence, value => value is NarrativeBuildingDestroyedEvidenceV1);
        Assert.Contains(_narrativeProvider.LastInput.Evidence, value => value is NarrativeKnowledgeAnnotationEvidenceV1);
        Assert.DoesNotContain(_narrativeProvider.LastInput.Evidence, value => value is NarrativeOutcomeEvidenceV1);
        var projected = JsonSerializer.Serialize(_narrativeProvider.LastInput, NarrativeInterpretationJson.Options);
        Assert.DoesNotContain("configuredPlayerPosition", projected, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enemyJunglerPosition", projected, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"x\":", projected, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("participant-2-puuid", projected, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await StoredPayloadsAsync("EUW1_1"));
    }

    [Fact]
    public async Task RejectsStaleAndUnknownInterpretationWindowsBeforeCallingProvider()
    {
        var stale = await _client.PostAsJsonAsync("/api/matches/EUW1_1/review/interpretation", new
        {
            requestedStartTimestampMs = 1_421_654,
            requestedEndTimestampMs = 1_620_500,
            reconstructionVersion = 99,
            detectorVersion = 2
        });
        var unknown = await _client.PostAsJsonAsync("/api/matches/EUW1_1/review/interpretation", new
        {
            requestedStartTimestampMs = 1,
            requestedEndTimestampMs = 2,
            reconstructionVersion = 1,
            detectorVersion = 2
        });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        Assert.Equal(0, _narrativeProvider.CallCount);
    }

    [Fact]
    public async Task RejectsUngroundedProviderOutputWithoutReturningPartialInterpretation()
    {
        _narrativeProvider.ReturnUnknownEvidenceReference = true;

        var response = await _client.PostAsJsonAsync("/api/matches/EUW1_1/review/interpretation", new
        {
            requestedStartTimestampMs = 1_421_654,
            requestedEndTimestampMs = 1_620_500,
            reconstructionVersion = 1,
            detectorVersion = 2
        });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_narrative_output", body, StringComparison.Ordinal);
        Assert.DoesNotContain("A selected period was reconstructed", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslatesProviderFailureToUnavailableResponse()
    {
        _narrativeProvider.Failure = new(
            NarrativeProviderFailureKind.RequestFailed,
            "The narrative provider request failed.",
            429);

        var response = await _client.PostAsJsonAsync("/api/matches/EUW1_1/review/interpretation", new
        {
            requestedStartTimestampMs = 1_421_654,
            requestedEndTimestampMs = 1_620_500,
            reconstructionVersion = 1,
            detectorVersion = 2
        });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("The narrative provider request failed.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private async Task<string[]> StoredPayloadsAsync(string matchId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.MatchPayloads.AsNoTracking()
            .Where(value => value.MatchId == matchId)
            .OrderBy(value => value.Kind)
            .Select(value => value.RawJson!)
            .ToArrayAsync();
    }

    private static (int Frame, int Event) Source(JsonElement annotation)
    {
        var source = annotation.GetProperty("target").GetProperty("event");
        return (source.GetProperty("frameIndex").GetInt32(), source.GetProperty("eventIndex").GetInt32());
    }

    private static (int Frame, int Event) SourceEvent(JsonElement value)
    {
        var source = value.GetProperty("source");
        return (source.GetProperty("frameIndex").GetInt32(), source.GetProperty("eventIndex").GetInt32());
    }

    private static (int X, int Y) Position(JsonElement value) => (value.GetProperty("x").GetInt32(), value.GetProperty("y").GetInt32());

    private static async Task SeedAsync(PathwiseDbContext db)
    {
        db.PlayerAccounts.Add(new() { Puuid = "participant-2-puuid", GameName = "Synthetic Player 2", TagLine = "P02", ConfiguredGameName = "Synthetic Player 2", ConfiguredTagLine = "P02", Platform = "euw1", Regional = "europe", ResolvedAtUtc = DateTimeOffset.UtcNow, RawJson = "{}" });
        db.StoredMatches.Add(new() { MatchId = "EUW1_1", PlayerPuuid = "participant-2-puuid", Regional = "europe", DiscoveredAtUtc = DateTimeOffset.UtcNow, QueueId = 420, ChampionName = "Khazix", TeamPosition = "JUNGLE", DurationSeconds = 1691 });
        db.StoredMatches.Add(new() { MatchId = "EUW1_2", PlayerPuuid = "participant-2-puuid", Regional = "europe", DiscoveredAtUtc = DateTimeOffset.UtcNow, QueueId = 420, ChampionName = "Khazix", TeamPosition = "JUNGLE", DurationSeconds = 1286 });
        db.MatchPayloads.AddRange(
            new MatchPayloadEntity { MatchId = "EUW1_1", Kind = PayloadKind.Match, State = PayloadState.Stored, RawJson = Fixture("match.json"), RetrievedAtUtc = DateTimeOffset.Parse("2026-09-19T18:00:00Z") },
            new MatchPayloadEntity { MatchId = "EUW1_1", Kind = PayloadKind.Timeline, State = PayloadState.Stored, RawJson = Fixture("timeline.json"), RetrievedAtUtc = DateTimeOffset.Parse("2026-09-19T18:00:01Z") },
            new MatchPayloadEntity { MatchId = "EUW1_2", Kind = PayloadKind.Match, State = PayloadState.Stored, RawJson = Fixture("Progression", "match.json"), RetrievedAtUtc = DateTimeOffset.Parse("2026-09-20T18:00:00Z") },
            new MatchPayloadEntity { MatchId = "EUW1_2", Kind = PayloadKind.Timeline, State = PayloadState.Stored, RawJson = Fixture("Progression", "timeline.json"), RetrievedAtUtc = DateTimeOffset.Parse("2026-09-20T18:00:01Z") });
        await db.SaveChangesAsync();
    }

    private static string Fixture(params string[] path) => File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "Fixtures", .. path]));

    private sealed class TestFactory(string databasePath, INarrativeInterpretationProvider narrativeProvider) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextFactory<PathwiseDbContext>>();
                services.AddPooledDbContextFactory<PathwiseDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
                services.RemoveAll<INarrativeInterpretationProvider>();
                services.AddSingleton(narrativeProvider);
            });
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Pathwise"] = $"Data Source={databasePath}",
                ["Riot:Player:GameName"] = "Synthetic Player 2",
                ["Riot:Player:TagLine"] = "P02",
                ["Riot:ApiKey"] = ""
            }));
        }
    }

    private sealed class FakeNarrativeProvider : INarrativeInterpretationProvider
    {
        public int CallCount { get; private set; }
        public NarrativeInterpretationInputV1? LastInput { get; private set; }
        public bool ReturnUnknownEvidenceReference { get; set; }
        public NarrativeProviderException? Failure { get; set; }

        public Task<NarrativeProviderResultV1> InterpretAsync(
            NarrativeInterpretationInputV1 input,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastInput = input;
            if (Failure is not null) throw Failure;
            var evidenceId = ReturnUnknownEvidenceReference ? "event:missing" : input.Evidence[0].Id;
            var output = new NarrativeInterpretationModelOutputV1(
                1,
                input.InputFingerprint,
                new("A selected period was reconstructed from deterministic evidence.", "factSummary", [evidenceId]),
                [],
                [],
                [new("The supplied evidence does not establish why the development occurred.", "notCaptured", [evidenceId])]);
            return Task.FromResult(new NarrativeProviderResultV1(output, "fake", "fake-model", DateTimeOffset.Parse("2026-09-29T12:00:00Z")));
        }
    }
}
