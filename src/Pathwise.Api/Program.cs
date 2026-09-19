var builder = WebApplication.CreateBuilder(args);

const string frontendCorsPolicy = "Frontend";
var frontendOrigin = builder.Configuration["FrontendOrigin"] ?? "http://localhost:3000";

builder.Services.AddCors(options =>
{
    options.AddPolicy(frontendCorsPolicy, policy =>
    {
        policy.WithOrigins(frontendOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors(frontendCorsPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

public partial class Program;
