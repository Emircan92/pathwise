using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Pathwise.Application.Ingestion;
using Pathwise.Infrastructure.Riot;

namespace Pathwise.Infrastructure.Tests;

public sealed class RiotApiClientTests
{
    [Fact]
    public async Task MatchListUsesRegionalRouteRankedQueueAndConfiguredCount()
    {
        HttpRequestMessage? observed = null;
        var handler = new StubHandler(request => { observed = request; return Json("[\"EUW1_7\"]"); });
        var client = new RiotApiClient(new HttpClient(handler), TimeProvider.System, NullLogger<RiotApiClient>.Instance);

        var result = await client.GetRecentRankedMatchIdsAsync("p uuid", Settings(), CancellationToken.None);

        Assert.Equal(["EUW1_7"], result);
        Assert.Equal("https://europe.api.riotgames.com/lol/match/v5/matches/by-puuid/p%20uuid/ids?queue=420&start=0&count=50", observed!.RequestUri!.AbsoluteUri);
        Assert.Equal("secret", observed.Headers.GetValues("X-Riot-Token").Single());
    }

    [Fact]
    public void MappingSelectsPlayerByPuuidAndUsesCurrentDurationSeconds()
    {
        var client = new RiotApiClient(new HttpClient(new StubHandler(_ => Json("{}"))), TimeProvider.System, NullLogger<RiotApiClient>.Instance);
        const string raw = "{\"metadata\":{\"matchId\":\"EUW1_7\"},\"info\":{\"queueId\":420,\"gameStartTimestamp\":1760000000000,\"gameDuration\":1901,\"participants\":[{\"puuid\":\"other\",\"championName\":\"Ahri\",\"win\":false,\"teamPosition\":\"MIDDLE\"},{\"puuid\":\"mine\",\"championName\":\"Nocturne\",\"win\":true,\"teamPosition\":\"JUNGLE\"}]}}";

        var facts = client.ProjectMatch(raw, "EUW1_7", "mine");

        Assert.Equal(1901, facts.DurationSeconds);
        Assert.Equal("Nocturne", facts.ChampionName);
        Assert.Equal("JUNGLE", facts.TeamPosition);
        Assert.True(facts.Won);
    }

    [Fact]
    public async Task InvalidSuccessfulPayloadRetainsRawBodyForRecovery()
    {
        const string raw = "{\"unexpected\":true}";
        var client = new RiotApiClient(new HttpClient(new StubHandler(_ => Json(raw))), TimeProvider.System, NullLogger<RiotApiClient>.Instance);

        var result = await client.GetPayloadAsync("EUW1_7", PayloadKind.Match, Settings(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(raw, result.RawJson);
        Assert.Equal("invalid_payload", result.Failure?.Code);
    }

    [Fact]
    public async Task TransientServerFailureIsRetried()
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json("[\"EUW1_7\"]"));
        var client = new RiotApiClient(new HttpClient(handler), TimeProvider.System, NullLogger<RiotApiClient>.Instance);

        var result = await client.GetRecentRankedMatchIdsAsync("puuid", Settings(), CancellationToken.None);

        Assert.Equal(["EUW1_7"], result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RateLimitHonorsRetryAfterBeforeRetrying()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            if (++calls > 1) return Json("[\"EUW1_7\"]");
            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return limited;
        });
        var client = new RiotApiClient(new HttpClient(handler), TimeProvider.System, NullLogger<RiotApiClient>.Instance);
        var started = DateTimeOffset.UtcNow;

        var result = await client.GetRecentRankedMatchIdsAsync("puuid", Settings(), CancellationToken.None);

        Assert.Equal(["EUW1_7"], result);
        Assert.Equal(2, calls);
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(900));
    }

    private static RiotSettingsSnapshot Settings() => new("Player Name", "EUW", "euw1", "europe", 50, "secret");
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
}
