using Microsoft.Extensions.Options;
using Pathwise.Api;
using Pathwise.Application.Ingestion;
using Pathwise.Application.Reconstruction;
using Pathwise.Domain.Reconstruction;
using Pathwise.Infrastructure;
using Pathwise.Infrastructure.Riot;

var builder = WebApplication.CreateBuilder(args);
const string frontendCorsPolicy = "Frontend";
var frontendOrigin = builder.Configuration["FrontendOrigin"] ?? "http://localhost:3000";
builder.Services.AddCors(options => options.AddPolicy(frontendCorsPolicy, policy => policy.WithOrigins(frontendOrigin).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<RiotOptions>(builder.Configuration.GetSection(RiotOptions.SectionName));
builder.Services.AddPathwiseInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddScoped<MatchIngestionService>();
builder.Services.AddScoped<ReconstructionService>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors(frontendCorsPolicy);
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

var api = app.MapGroup("/api");
api.MapGet("/player", async (MatchIngestionService service, IOptions<RiotOptions> options, CancellationToken ct) => Results.Ok(await service.GetPlayerAsync(ToSnapshot(options.Value), ct)));
api.MapGet("/matches", async (int? limit, int? offset, MatchIngestionService service, IOptions<RiotOptions> options, CancellationToken ct) => Results.Ok(await service.GetMatchesAsync(ToSnapshot(options.Value), Math.Clamp(limit ?? 50, 1, 100), Math.Max(offset ?? 0, 0), ct)));
api.MapGet("/matches/{matchId}", async (string matchId, MatchIngestionService service, IOptions<RiotOptions> options, CancellationToken ct) =>
{
    var match = await service.GetMatchAsync(ToSnapshot(options.Value), matchId, ct);
    return match is null ? Results.NotFound() : Results.Ok(match);
});
api.MapPost("/matches/fetch", async (MatchIngestionService service, IOptions<RiotOptions> options, CancellationToken ct) =>
{
    try { return Results.Ok(await service.FetchAsync(ToSnapshot(options.Value), ct)); }
    catch (FetchConflictException) { return Results.Conflict(new Microsoft.AspNetCore.Mvc.ProblemDetails { Title = "Match fetching is already in progress.", Status = 409 }); }
    catch (FetchConfigurationException ex) { return Results.Problem(title: "Riot configuration is incomplete.", detail: string.Join(" ", ex.Errors), statusCode: 422); }
    catch (RiotOperationException ex)
    {
        var extensions = ex.RetryAfterUtc is null ? null : new Dictionary<string, object?> { ["retryAfterUtc"] = ex.RetryAfterUtc };
        return Results.Problem(title: "Riot request failed.", detail: ex.Failure.Message, statusCode: ex.Failure.HttpStatus == 429 ? 503 : 502, extensions: extensions);
    }
});

api.MapGet("/matches/{matchId}/reconstruction", async (string matchId, ReconstructionService service, IOptions<RiotOptions> options, CancellationToken ct) =>
{
    try { return Results.Ok(ReconstructionApiMapper.Metadata(await service.GetMetadataAsync(ToSnapshot(options.Value), matchId, ct))); }
    catch (ReconstructionRequestException ex) { return ReconstructionProblem(ex.Failure); }
});

api.MapGet("/matches/{matchId}/reconstruction/state", async (string matchId, long? atMs, ReconstructionService service, IOptions<RiotOptions> options, CancellationToken ct) =>
{
    if (atMs is null) return InvalidTimestamp("atMs is required.");
    try { return Results.Ok(ReconstructionApiMapper.State(await service.GetStateAsync(ToSnapshot(options.Value), matchId, atMs.Value, ct))); }
    catch (ReconstructionRequestException ex) { return ReconstructionProblem(ex.Failure); }
    catch (ReconstructionQueryException ex) { return RangeProblem(ex); }
});

api.MapGet("/matches/{matchId}/reconstruction/changes", async (string matchId, long? fromMs, long? toMs, ReconstructionService service, IOptions<RiotOptions> options, CancellationToken ct) =>
{
    if (fromMs is null || toMs is null) return InvalidTimestamp("fromMs and toMs are required.");
    try { return Results.Ok(ReconstructionApiMapper.Changes(await service.GetChangesAsync(ToSnapshot(options.Value), matchId, fromMs.Value, toMs.Value, ct))); }
    catch (ReconstructionRequestException ex) { return ReconstructionProblem(ex.Failure); }
    catch (ReconstructionQueryException ex) { return RangeProblem(ex); }
});

app.Run();

static RiotSettingsSnapshot ToSnapshot(RiotOptions value) => new(value.Player.GameName, value.Player.TagLine, value.Routing.Platform, value.Routing.Regional, value.RecentMatchCount, value.ApiKey);
static IResult ReconstructionProblem(ReconstructionFailure failure)
{
    var status = failure.Kind switch
    {
        ReconstructionFailureKind.MatchUnavailable => 404,
        ReconstructionFailureKind.PayloadNotReady => 409,
        ReconstructionFailureKind.Unsupported or ReconstructionFailureKind.InvalidSource => 422,
        _ => 500
    };
    return Results.Problem(title: "Game reconstruction unavailable.", detail: failure.Message, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = failure.Code });
}
static IResult InvalidTimestamp(string detail) => Results.Problem(title: "Invalid reconstruction timestamp.", detail: detail, statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "invalid_timestamp" });
static IResult RangeProblem(ReconstructionQueryException exception) => Results.Problem(
    title: "Invalid reconstruction timestamp.",
    detail: exception.Message,
    statusCode: 400,
    extensions: new Dictionary<string, object?>
    {
        ["code"] = exception.Code,
        ["availableFromMs"] = exception.AvailableFromMs,
        ["availableToMs"] = exception.AvailableToMs
    });
public partial class Program;
