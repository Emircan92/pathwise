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
using Pathwise.Infrastructure.Persistence;

namespace Pathwise.Api.Tests;

public sealed class ReconstructionEndpointTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"pathwise-reconstruction-api-{Guid.NewGuid():N}.db");
    private TestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestFactory(_databasePath);
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await SeedCompleteAsync(db);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_databasePath + suffix);
    }

    [Fact]
    public async Task ThreeEndpointsReturnCamelCaseContractsAndReviewedProvenance()
    {
        var metadataResponse = await _client.GetAsync("/api/matches/EUW1_1/reconstruction");
        var stateResponse = await _client.GetAsync("/api/matches/EUW1_1/reconstruction/state?atMs=900000");
        var changesResponse = await _client.GetAsync("/api/matches/EUW1_1/reconstruction/changes?fromMs=900315&toMs=1200393");

        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changesResponse.StatusCode);
        using var metadata = JsonDocument.Parse(await metadataResponse.Content.ReadAsStringAsync());
        using var state = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
        using var changes = JsonDocument.Parse(await changesResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, metadata.RootElement.GetProperty("context").GetProperty("reconstructionVersion").GetInt32());
        Assert.Equal("v5", metadata.RootElement.GetProperty("context").GetProperty("source").GetProperty("timelineEndpointVersion").GetString());
        Assert.Equal(1691676, metadata.RootElement.GetProperty("coverage").GetProperty("availableToMs").GetInt64());
        Assert.Equal(840296, state.RootElement.GetProperty("state").GetProperty("selectedFrameTimestampMs").GetInt64());
        Assert.Equal(4131, changes.RootElement.GetProperty("changes").GetProperty("configuredPlayerDelta").GetProperty("totalGold").GetInt64());
        var firstEvent = changes.RootElement.GetProperty("changes").GetProperty("events")[0];
        Assert.Equal(JsonValueKind.String, firstEvent.GetProperty("kind").ValueKind);
        Assert.True(firstEvent.TryGetProperty("source", out _));
        Assert.True(firstEvent.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task InvalidAndOutOfRangeQueriesReturnProblemDetailsWithBounds()
    {
        var missing = await _client.GetAsync("/api/matches/EUW1_1/reconstruction/state");
        var reversed = await _client.GetAsync("/api/matches/EUW1_1/reconstruction/changes?fromMs=10&toMs=0");
        var outOfRange = await _client.GetAsync("/api/matches/EUW1_1/reconstruction/state?atMs=1691677");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
        using var problem = JsonDocument.Parse(await outOfRange.Content.ReadAsStringAsync());
        Assert.Equal("timestamp_out_of_range", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, problem.RootElement.GetProperty("availableFromMs").GetInt64());
        Assert.Equal(1691676, problem.RootElement.GetProperty("availableToMs").GetInt64());
    }

    [Fact]
    public async Task AccountScopeMissingPayloadAndCoreFailureMapToExpectedStatuses()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/matches/OTHER/reconstruction")).StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.StoredMatches.Add(new() { MatchId = "EUW1_2", PlayerPuuid = "participant-2-puuid", Regional = "europe", DiscoveredAtUtc = DateTimeOffset.UtcNow, QueueId = 420, TeamPosition = "JUNGLE" });
            db.MatchPayloads.AddRange(
                new MatchPayloadEntity { MatchId = "EUW1_2", Kind = PayloadKind.Match, State = PayloadState.Missing },
                new MatchPayloadEntity { MatchId = "EUW1_2", Kind = PayloadKind.Timeline, State = PayloadState.Missing });
            await db.SaveChangesAsync();

            var brokenTimeline = JsonDocument.Parse(Fixture("timeline.json")).RootElement.GetRawText().Replace("\"participantId\": 2", "\"participantId\": \"bad\"", StringComparison.Ordinal);
            db.StoredMatches.Add(new() { MatchId = "EUW1_3", PlayerPuuid = "participant-2-puuid", Regional = "europe", DiscoveredAtUtc = DateTimeOffset.UtcNow, QueueId = 420, TeamPosition = "JUNGLE" });
            db.MatchPayloads.AddRange(
                new MatchPayloadEntity { MatchId = "EUW1_3", Kind = PayloadKind.Match, State = PayloadState.Stored, RawJson = Fixture("match.json").Replace("EUW1_1", "EUW1_3"), RetrievedAtUtc = DateTimeOffset.UtcNow },
                new MatchPayloadEntity { MatchId = "EUW1_3", Kind = PayloadKind.Timeline, State = PayloadState.Stored, RawJson = brokenTimeline.Replace("EUW1_1", "EUW1_3"), RetrievedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await _client.GetAsync("/api/matches/EUW1_2/reconstruction")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.GetAsync("/api/matches/EUW1_3/reconstruction")).StatusCode);
    }

    [Fact]
    public async Task ReconstructionReadLeavesPayloadAndIngestionMetadataUnchangedAndReadsCommittedWalRow()
    {
        string beforeMatch;
        string beforeTimeline;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var payloads = await db.MatchPayloads.AsNoTracking().Where(x => x.MatchId == "EUW1_1").OrderBy(x => x.Kind).ToArrayAsync();
            beforeMatch = payloads[0].RawJson!;
            beforeTimeline = payloads[1].RawJson!;
        }

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/matches/EUW1_1/reconstruction")).StatusCode);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyFactory = verifyScope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
        await using var verify = await verifyFactory.CreateDbContextAsync();
        var after = await verify.MatchPayloads.AsNoTracking().Where(x => x.MatchId == "EUW1_1").OrderBy(x => x.Kind).ToArrayAsync();
        Assert.Equal(beforeMatch, after[0].RawJson);
        Assert.Equal(beforeTimeline, after[1].RawJson);
        Assert.All(after, x => Assert.Equal(PayloadState.Stored, x.State));
    }

    private static async Task SeedCompleteAsync(PathwiseDbContext db)
    {
        db.PlayerAccounts.Add(new()
        {
            Puuid = "participant-2-puuid",
            GameName = "Synthetic Player 2",
            TagLine = "P02",
            ConfiguredGameName = "Synthetic Player 2",
            ConfiguredTagLine = "P02",
            Platform = "euw1",
            Regional = "europe",
            ResolvedAtUtc = DateTimeOffset.UtcNow,
            RawJson = "{}"
        });
        db.StoredMatches.Add(new()
        {
            MatchId = "EUW1_1",
            PlayerPuuid = "participant-2-puuid",
            Regional = "europe",
            DiscoveredAtUtc = DateTimeOffset.UtcNow,
            QueueId = 420,
            ChampionName = "Khazix",
            TeamPosition = "JUNGLE",
            DurationSeconds = 1691
        });
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
