using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pathwise.Infrastructure.Persistence;

namespace Pathwise.Api.Tests;

public sealed class ProductEndpointTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"pathwise-api-{Guid.NewGuid():N}.db");
    private TestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestFactory(_databasePath);
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PathwiseDbContext>>();
        await using var db = await contextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    [Fact]
    public async Task PlayerNeverReturnsApiKeyAndReportsMissingConfiguration()
    {
        var response = await _client.GetAsync("/api/player");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("fetchReady\":false", body);
        Assert.DoesNotContain("ApiKey", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchWithMissingIdentityReturnsUnprocessableProblemDetails()
    {
        var response = await _client.PostAsync("/api/matches/fetch", null);
        var problem = await response.Content.ReadFromJsonAsync<Problem>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Riot configuration is incomplete.", problem?.Title);
    }

    [Fact]
    public async Task MatchReadsAreLocalAndInitiallyEmpty()
    {
        var response = await _client.GetAsync("/api/matches");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"totalStored\":0,\"limit\":50,\"offset\":0,\"matches\":[],\"incompleteImports\":[]}", body);
    }

    private sealed record Problem(string Title);

    private sealed class TestFactory(string databasePath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Pathwise"] = $"Data Source={databasePath}",
                ["Riot:Player:GameName"] = "",
                ["Riot:Player:TagLine"] = "",
                ["Riot:ApiKey"] = ""
            }));
        }
    }
}
