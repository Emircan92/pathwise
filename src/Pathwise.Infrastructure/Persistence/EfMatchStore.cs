using Microsoft.EntityFrameworkCore;
using Pathwise.Application.Ingestion;
using Pathwise.Domain.Matches;

namespace Pathwise.Infrastructure.Persistence;

public sealed class EfMatchStore(IDbContextFactory<PathwiseDbContext> dbFactory) : IMatchStore
{
    public async Task<PlayerView?> GetResolvedPlayerAsync(RiotSettingsSnapshot settings, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var candidates = await db.PlayerAccounts.AsNoTracking().Where(x => x.ConfiguredGameName == settings.GameName && x.ConfiguredTagLine == settings.TagLine && x.Platform == settings.Platform && x.Regional == settings.Regional).ToListAsync(ct);
        var p = candidates.OrderByDescending(x => x.ResolvedAtUtc).FirstOrDefault();
        return p is null ? null : new(p.GameName, p.TagLine, p.Platform, p.Regional, settings.RecentMatchCount, true, [], p.Puuid, p.ResolvedAtUtc);
    }

    public async Task SaveResolvedPlayerAsync(RiotAccountResult account, RiotSettingsSnapshot settings, DateTimeOffset resolvedAtUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.PlayerAccounts.FindAsync([account.Puuid], ct);
        if (entity is null)
        {
            entity = new() { Puuid = account.Puuid, GameName = account.GameName, TagLine = account.TagLine, ConfiguredGameName = settings.GameName, ConfiguredTagLine = settings.TagLine, Platform = settings.Platform, Regional = settings.Regional, ResolvedAtUtc = resolvedAtUtc, RawJson = account.RawJson };
            db.Add(entity);
        }
        else
        {
            entity.GameName = account.GameName; entity.TagLine = account.TagLine; entity.ConfiguredGameName = settings.GameName; entity.ConfiguredTagLine = settings.TagLine; entity.Platform = settings.Platform; entity.Regional = settings.Regional; entity.ResolvedAtUtc = resolvedAtUtc; entity.RawJson = account.RawJson;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task EnsureMatchesAsync(string puuid, string regional, IReadOnlyList<string> ids, DateTimeOffset discoveredAtUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.StoredMatches.Where(x => ids.Contains(x.MatchId)).Select(x => x.MatchId).ToListAsync(ct);
        foreach (var id in ids.Except(existing))
        {
            db.StoredMatches.Add(new() { MatchId = id, PlayerPuuid = puuid, Regional = regional, DiscoveredAtUtc = discoveredAtUtc });
            db.MatchPayloads.AddRange(
                new MatchPayloadEntity { MatchId = id, Kind = PayloadKind.Match, State = PayloadState.Missing },
                new MatchPayloadEntity { MatchId = id, Kind = PayloadKind.Timeline, State = PayloadState.Missing });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<StoredMatchState> GetStateAsync(string matchId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var match = await db.StoredMatches.AsNoTracking().Include(x => x.Payloads).SingleAsync(x => x.MatchId == matchId, ct);
        var payloads = match.Payloads.ToDictionary(x => x.Kind, x => new StoredPayloadState(x.Kind, x.State, x.RawJson));
        return new(match.MatchId, match.QueueId.HasValue && match.MetadataErrorCode is null, payloads);
    }

    public async Task SavePayloadAsync(string matchId, PayloadKind kind, RiotPayloadResult result, DateTimeOffset attemptedAtUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.MatchPayloads.SingleAsync(x => x.MatchId == matchId && x.Kind == kind, ct);
        if (p.State == PayloadState.Stored) return;
        p.LastAttemptAtUtc = attemptedAtUtc;
        p.RetryAfterUtc = result.RetryAfterUtc;
        if (result.Success)
        {
            p.RawJson = result.RawJson; p.RetrievedAtUtc = attemptedAtUtc; p.State = PayloadState.Stored; p.FailureCode = null; p.FailureMessage = null; p.FailureHttpStatus = null; p.RetryAfterUtc = null;
        }
        else
        {
            p.RawJson = result.RawJson;
            p.State = result.RawJson is null ? PayloadState.Failed : PayloadState.Invalid;
            p.FailureCode = result.Failure?.Code; p.FailureMessage = result.Failure?.Message; p.FailureHttpStatus = result.Failure?.HttpStatus;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveMetadataAsync(string matchId, MatchFacts facts, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var m = await db.StoredMatches.FindAsync([matchId], ct) ?? throw new InvalidOperationException("Stored match was not found.");
        m.QueueId = facts.QueueId; m.PlayedAtUtc = facts.PlayedAtUtc; m.DurationSeconds = facts.DurationSeconds; m.ChampionName = facts.ChampionName; m.Won = facts.Won; m.TeamPosition = facts.TeamPosition;
        m.MetadataErrorCode = null; m.MetadataErrorMessage = null; m.MetadataErrorAtUtc = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveMetadataFailureAsync(string matchId, string code, string message, DateTimeOffset failedAtUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var m = await db.StoredMatches.FindAsync([matchId], ct) ?? throw new InvalidOperationException("Stored match was not found.");
        m.MetadataErrorCode = code; m.MetadataErrorMessage = message; m.MetadataErrorAtUtc = failedAtUtc;
        await db.SaveChangesAsync(ct);
    }

    public async Task<MatchListView> GetMatchesAsync(string? puuid, int limit, int offset, CancellationToken ct)
    {
        if (puuid is null) return new([], []);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var candidates = await db.StoredMatches.AsNoTracking().Where(x => x.PlayerPuuid == puuid).ToListAsync(ct);
        var matches = candidates.OrderByDescending(x => x.PlayedAtUtc ?? DateTimeOffset.MinValue).ThenByDescending(x => x.DiscoveredAtUtc).Skip(offset).Take(limit).ToList();
        var ids = matches.Select(x => x.MatchId).ToList();
        var payloadRows = await db.MatchPayloads.AsNoTracking().Where(x => ids.Contains(x.MatchId)).Select(p => new { p.MatchId, Payload = new PayloadProjection(p.Kind, p.State, p.FailureCode, p.FailureMessage, p.FailureHttpStatus, p.RetryAfterUtc) }).ToListAsync(ct);
        var rows = matches.Select(match => new MatchProjection(match, payloadRows.Where(x => x.MatchId == match.MatchId).Select(x => x.Payload).ToList())).ToList();
        var views = rows.Select(ToView).ToList();
        return new(views.Where(x => x.ChampionName is not null).ToList(), views.Where(x => x.ChampionName is null).ToList());
    }

    public async Task<MatchView?> GetMatchAsync(string? puuid, string matchId, CancellationToken ct)
    {
        if (puuid is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var match = await db.StoredMatches.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerPuuid == puuid && x.MatchId == matchId, ct);
        if (match is null) return null;
        var payloads = await db.MatchPayloads.AsNoTracking().Where(x => x.MatchId == matchId).Select(p => new PayloadProjection(p.Kind, p.State, p.FailureCode, p.FailureMessage, p.FailureHttpStatus, p.RetryAfterUtc)).ToListAsync(ct);
        return ToView(new MatchProjection(match, payloads));
    }

    private static MatchView ToView(MatchProjection row)
    {
        var matchPayload = row.Payloads.Single(x => x.Kind == PayloadKind.Match);
        var timeline = row.Payloads.Single(x => x.Kind == PayloadKind.Timeline);
        var failures = row.Payloads.Where(x => x.FailureCode is not null).Select(x => new MatchFailureView(x.Kind.ToString().ToLowerInvariant(), x.FailureCode!, x.FailureMessage ?? "Riot request failed.", x.HttpStatus, x.RetryAfterUtc)).ToList();
        if (row.Match.MetadataErrorCode is not null) failures.Add(new("metadata", row.Match.MetadataErrorCode, row.Match.MetadataErrorMessage ?? "Match metadata could not be read.", null, null));
        var complete = row.Match.QueueId.HasValue && row.Match.MetadataErrorCode is null && matchPayload.State == PayloadState.Stored && timeline.State == PayloadState.Stored;
        return new(row.Match.MatchId, row.Match.QueueId, row.Match.PlayedAtUtc, row.Match.DurationSeconds, row.Match.ChampionName, row.Match.Won, row.Match.TeamPosition, MatchSupport.FromTeamPosition(row.Match.TeamPosition), complete ? "complete" : "incomplete", matchPayload.State == PayloadState.Stored, timeline.State == PayloadState.Stored, failures);
    }

    private sealed record MatchProjection(StoredMatchEntity Match, List<PayloadProjection> Payloads);
    private sealed record PayloadProjection(PayloadKind Kind, PayloadState State, string? FailureCode, string? FailureMessage, int? HttpStatus, DateTimeOffset? RetryAfterUtc);
}
