using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Pathwise.Application.Ingestion;
using Pathwise.Domain.Matches;
using Pathwise.Infrastructure.Persistence;

namespace Pathwise.Infrastructure.Tests;

public sealed class IngestionPersistenceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"pathwise-{Guid.NewGuid():N}.db");
    private ServiceProvider _provider = null!;
    private IDbContextFactory<PathwiseDbContext> _factory = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<PathwiseDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    [Fact]
    public async Task RepeatedFetchSkipsPayloadRequestsForCompleteMatch()
    {
        var riot = new FakeRiotSource();
        var service = new MatchIngestionService(riot, new EfMatchStore(_factory), TimeProvider.System);

        var first = await service.FetchAsync(Settings(), CancellationToken.None);
        var second = await service.FetchAsync(Settings(), CancellationToken.None);

        Assert.Equal("succeeded", first.Outcome);
        Assert.Equal(["EUW1_1"], first.NewlyCompleted);
        Assert.Equal(["EUW1_1"], second.AlreadyComplete);
        Assert.Equal(2, riot.PayloadCalls);
    }

    [Fact]
    public async Task RepeatedFetchRepairsOnlyFailedTimeline()
    {
        var riot = new FakeRiotSource { FailFirstTimeline = true };
        var service = new MatchIngestionService(riot, new EfMatchStore(_factory), TimeProvider.System);

        var first = await service.FetchAsync(Settings(), CancellationToken.None);
        var second = await service.FetchAsync(Settings(), CancellationToken.None);

        Assert.Equal("failed", first.Outcome);
        Assert.Equal(["EUW1_1"], first.Incomplete);
        Assert.Equal(["EUW1_1"], second.Repaired);
        Assert.Equal(3, riot.PayloadCalls);

        await using var db = await _factory.CreateDbContextAsync();
        var payloads = await db.MatchPayloads.AsNoTracking().ToListAsync();
        Assert.All(payloads, payload => Assert.Equal(PayloadState.Stored, payload.State));
        Assert.Contains(payloads, payload => payload.Kind == PayloadKind.Match && payload.RawJson!.Contains("Kha'Zix"));
    }

    [Fact]
    public async Task MigrationEnforcesUniqueMatchIdentifiers()
    {
        await using (var first = await _factory.CreateDbContextAsync())
        {
            first.PlayerAccounts.Add(new() { Puuid = "p", GameName = "Player", TagLine = "EUW", ConfiguredGameName = "Player", ConfiguredTagLine = "EUW", Platform = "euw1", Regional = "europe", ResolvedAtUtc = DateTimeOffset.UtcNow, RawJson = "{}" });
            first.StoredMatches.Add(new StoredMatchEntity { MatchId = "EUW1_DUP", PlayerPuuid = "p", Regional = "europe" });
            await first.SaveChangesAsync();
        }
        await using var second = await _factory.CreateDbContextAsync();
        second.StoredMatches.Add(new StoredMatchEntity { MatchId = "EUW1_DUP", PlayerPuuid = "p", Regional = "europe" });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    private static RiotSettingsSnapshot Settings() => new("Player", "EUW", "euw1", "europe", 50, "secret");

    private sealed class FakeRiotSource : IRiotSource
    {
        public bool FailFirstTimeline { get; init; }
        public int PayloadCalls { get; private set; }
        private bool _timelineFailed;
        public Task<RiotAccountResult> ResolveAccountAsync(RiotSettingsSnapshot settings, CancellationToken ct) => Task.FromResult(new RiotAccountResult("player-puuid", "Player", "EUW", "{\"puuid\":\"player-puuid\"}"));
        public Task<IReadOnlyList<string>> GetRecentRankedMatchIdsAsync(string puuid, RiotSettingsSnapshot settings, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["EUW1_1"]);
        public Task<RiotPayloadResult> GetPayloadAsync(string matchId, PayloadKind kind, RiotSettingsSnapshot settings, CancellationToken ct)
        {
            PayloadCalls++;
            if (kind == PayloadKind.Timeline && FailFirstTimeline && !_timelineFailed)
            {
                _timelineFailed = true;
                return Task.FromResult(new RiotPayloadResult(false, null, new RiotFailure("riot_unavailable", "Timeline unavailable.", 503)));
            }
            var raw = kind == PayloadKind.Match
                ? "{\"metadata\":{\"matchId\":\"EUW1_1\"},\"info\":{\"queueId\":420,\"gameStartTimestamp\":1760000000000,\"gameDuration\":1900,\"participants\":[{\"puuid\":\"player-puuid\",\"championName\":\"Kha'Zix\",\"win\":true,\"teamPosition\":\"JUNGLE\"}]}}"
                : "{\"metadata\":{\"matchId\":\"EUW1_1\"},\"info\":{\"frames\":[]}}";
            return Task.FromResult(new RiotPayloadResult(true, raw, null));
        }
        public MatchFacts ProjectMatch(string rawJson, string expectedMatchId, string playerPuuid) => new(420, DateTimeOffset.FromUnixTimeMilliseconds(1760000000000), 1900, "Kha'Zix", true, "JUNGLE");
    }
}
