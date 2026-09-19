using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pathwise.Application.Ingestion;
using Pathwise.Domain.Matches;

namespace Pathwise.Infrastructure.Riot;

public sealed class RiotApiClient(HttpClient httpClient, TimeProvider timeProvider, ILogger<RiotApiClient> logger) : IRiotSource
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRateLimitWait = TimeSpan.FromMinutes(3);
    private readonly SemaphoreSlim _cooldownLock = new(1, 1);
    private DateTimeOffset? _cooldownUntil;
    private TimeSpan _rateLimitWaitThisOperation;

    public async Task<RiotAccountResult> ResolveAccountAsync(RiotSettingsSnapshot settings, CancellationToken ct)
    {
        _rateLimitWaitThisOperation = TimeSpan.Zero;
        var path = $"/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(settings.GameName)}/{Uri.EscapeDataString(settings.TagLine)}";
        var response = await SendAsync(path, settings, ct);
        if (!response.Success) throw new RiotOperationException(response.Failure!, response.RetryAfterUtc);
        try
        {
            var dto = JsonSerializer.Deserialize<AccountDto>(response.RawJson!, JsonOptions) ?? throw new JsonException();
            if (string.IsNullOrWhiteSpace(dto.Puuid)) throw new JsonException();
            return new(dto.Puuid, dto.GameName ?? settings.GameName, dto.TagLine ?? settings.TagLine, response.RawJson!);
        }
        catch (JsonException)
        {
            throw new RiotOperationException(new("invalid_account_response", "Riot returned an invalid account response.", StopOperation: true));
        }
    }

    public async Task<IReadOnlyList<string>> GetRecentRankedMatchIdsAsync(string puuid, RiotSettingsSnapshot settings, CancellationToken ct)
    {
        var path = $"/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(puuid)}/ids?queue=420&start=0&count={settings.RecentMatchCount}";
        var response = await SendAsync(path, settings, ct);
        if (!response.Success) throw new RiotOperationException(response.Failure!, response.RetryAfterUtc);
        try { return JsonSerializer.Deserialize<string[]>(response.RawJson!, JsonOptions) ?? []; }
        catch (JsonException) { throw new RiotOperationException(new("invalid_match_list", "Riot returned an invalid match list.", StopOperation: true)); }
    }

    public async Task<RiotPayloadResult> GetPayloadAsync(string matchId, PayloadKind kind, RiotSettingsSnapshot settings, CancellationToken ct)
    {
        var suffix = kind == PayloadKind.Timeline ? "/timeline" : string.Empty;
        var response = await SendAsync($"/lol/match/v5/matches/{Uri.EscapeDataString(matchId)}{suffix}", settings, ct);
        if (!response.Success) return response;
        try
        {
            using var doc = JsonDocument.Parse(response.RawJson!);
            var returnedId = doc.RootElement.GetProperty("metadata").GetProperty("matchId").GetString();
            if (!string.Equals(returnedId, matchId, StringComparison.Ordinal)) throw new JsonException();
            return response;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new(false, response.RawJson, new("invalid_payload", $"Riot returned an invalid {kind.ToString().ToLowerInvariant()} payload."));
        }
    }

    public MatchFacts ProjectMatch(string rawJson, string expectedMatchId, string playerPuuid)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<MatchDto>(rawJson, JsonOptions) ?? throw new JsonException();
            if (!string.Equals(dto.Metadata?.MatchId, expectedMatchId, StringComparison.Ordinal)) throw new FormatException("Stored match ID does not match its payload.");
            if (dto.Info?.QueueId != 420) throw new FormatException("Stored match is not Ranked Solo/Duo.");
            var participant = dto.Info.Participants?.SingleOrDefault(x => x.Puuid == playerPuuid) ?? throw new FormatException("Configured player is absent from the match.");
            if (dto.Info.GameStartTimestamp <= 0 || dto.Info.GameDuration <= 0 || string.IsNullOrWhiteSpace(participant.ChampionName)) throw new FormatException("Required match facts are missing.");
            return new(dto.Info.QueueId, DateTimeOffset.FromUnixTimeMilliseconds(dto.Info.GameStartTimestamp), dto.Info.GameDuration, participant.ChampionName, participant.Win, participant.TeamPosition);
        }
        catch (JsonException ex) { throw new FormatException("Stored match JSON is malformed.", ex); }
    }

    private async Task<RiotPayloadResult> SendAsync(string path, RiotSettingsSnapshot settings, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var cooldownFailure = await WaitForCooldownAsync(ct);
            if (cooldownFailure is not null) return cooldownFailure;
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{settings.Regional}.api.riotgames.com{path}"));
            request.Headers.Add("X-Riot-Token", settings.ApiKey);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(RequestTimeout);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var wait = GetRetryAfter(response, timeProvider.GetUtcNow()) ?? TimeSpan.FromMinutes(2);
                    if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
                    var until = timeProvider.GetUtcNow() + wait;
                    _cooldownUntil = until;
                    if (_rateLimitWaitThisOperation + wait > MaxRateLimitWait)
                        return new(false, null, new("rate_limited", "Riot rate limit wait exceeded this fetch budget.", 429, true), until);
                    attempt--;
                    continue;
                }

                if (response.IsSuccessStatusCode)
                    return new(true, await response.Content.ReadAsStringAsync(ct), null);

                var status = (int)response.StatusCode;
                if (status >= 500 && attempt < 2) { await DelayForRetryAsync(attempt, ct); continue; }
                var stop = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest;
                var code = response.StatusCode switch { HttpStatusCode.Unauthorized => "unauthorized", HttpStatusCode.Forbidden => "forbidden", HttpStatusCode.NotFound => "not_found", _ when status >= 500 => "riot_unavailable", _ => "riot_request_failed" };
                logger.LogWarning("Riot request {Path} failed with status {StatusCode}", path, status);
                return new(false, null, new(code, $"Riot request failed with HTTP {status}.", status, stop));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < 2) { await DelayForRetryAsync(attempt, ct); }
            catch (HttpRequestException) when (attempt < 2) { await DelayForRetryAsync(attempt, ct); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, null, new("timeout", "Riot request timed out.")); }
            catch (HttpRequestException) { return new(false, null, new("transport_error", "Riot could not be reached.")); }
        }
        return new(false, null, new("riot_unavailable", "Riot request failed after bounded retries."));
    }

    private async Task<RiotPayloadResult?> WaitForCooldownAsync(CancellationToken ct)
    {
        await _cooldownLock.WaitAsync(ct);
        try
        {
            if (_cooldownUntil is not { } until || until <= timeProvider.GetUtcNow()) return null;
            var wait = until - timeProvider.GetUtcNow();
            if (_rateLimitWaitThisOperation + wait > MaxRateLimitWait) return new(false, null, new("rate_limited", "Riot rate limit cooldown is still active.", 429, true), until);
            _rateLimitWaitThisOperation += wait;
            await Task.Delay(wait, timeProvider, ct);
            return null;
        }
        finally { _cooldownLock.Release(); }
    }

    private Task DelayForRetryAsync(int attempt, CancellationToken ct) => Task.Delay(TimeSpan.FromSeconds(attempt + 1), timeProvider, ct);
    private static TimeSpan? GetRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta) return delta;
        if (retry?.Date is { } date) return date - now > TimeSpan.Zero ? date - now : TimeSpan.Zero;
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private sealed record AccountDto(string Puuid, string? GameName, string? TagLine);
    private sealed record MatchDto(MetadataDto? Metadata, InfoDto? Info);
    private sealed record MetadataDto(string? MatchId);
    private sealed record InfoDto(int QueueId, long GameStartTimestamp, int GameDuration, ParticipantDto[]? Participants);
    private sealed record ParticipantDto(string Puuid, string ChampionName, bool Win, string? TeamPosition);
}
