using System.Net;
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
using Pathwise.Infrastructure.Persistence;

namespace Pathwise.Api.Tests;

public sealed class MatchReviewEndpointTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"pathwise-review-api-{Guid.NewGuid():N}.db");
    private TestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new(_databasePath);
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
            ["matchId", "mapId", "configuredParticipantId", "participants", "enemyResolution", "versions", "knowledge", "sourceDataIssues", "windows"],
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
    public async Task Maps404ConflictAndUnprocessableFailures()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/matches/OTHER/review")).StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.StoredMatches.Add(new() { MatchId = "EUW1_2", PlayerPuuid = "participant-2-puuid", Regional = "europe", DiscoveredAtUtc = DateTimeOffset.UtcNow, QueueId = 420, TeamPosition = "JUNGLE" });
            db.MatchPayloads.AddRange(new MatchPayloadEntity { MatchId = "EUW1_2", Kind = PayloadKind.Match, State = PayloadState.Missing }, new MatchPayloadEntity { MatchId = "EUW1_2", Kind = PayloadKind.Timeline, State = PayloadState.Missing });
            var broken = Fixture("timeline.json").Replace("\"participantId\": 2", "\"participantId\": \"bad\"", StringComparison.Ordinal);
            db.StoredMatches.Add(new() { MatchId = "EUW1_3", PlayerPuuid = "participant-2-puuid", Regional = "europe", DiscoveredAtUtc = DateTimeOffset.UtcNow, QueueId = 420, TeamPosition = "JUNGLE" });
            db.MatchPayloads.AddRange(
                new MatchPayloadEntity { MatchId = "EUW1_3", Kind = PayloadKind.Match, State = PayloadState.Stored, RawJson = Fixture("match.json").Replace("EUW1_1", "EUW1_3"), RetrievedAtUtc = DateTimeOffset.UtcNow },
                new MatchPayloadEntity { MatchId = "EUW1_3", Kind = PayloadKind.Timeline, State = PayloadState.Stored, RawJson = broken.Replace("EUW1_1", "EUW1_3"), RetrievedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await _client.GetAsync("/api/matches/EUW1_2/review")).StatusCode);
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
        db.MatchPayloads.AddRange(
            new MatchPayloadEntity { MatchId = "EUW1_1", Kind = PayloadKind.Match, State = PayloadState.Stored, RawJson = Fixture("match.json"), RetrievedAtUtc = DateTimeOffset.Parse("2026-09-19T18:00:00Z") },
            new MatchPayloadEntity { MatchId = "EUW1_1", Kind = PayloadKind.Timeline, State = PayloadState.Stored, RawJson = Fixture("timeline.json"), RetrievedAtUtc = DateTimeOffset.Parse("2026-09-19T18:00:01Z") });
        await db.SaveChangesAsync();
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private sealed class TestFactory(string databasePath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextFactory<PathwiseDbContext>>();
                services.AddPooledDbContextFactory<PathwiseDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
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
}
