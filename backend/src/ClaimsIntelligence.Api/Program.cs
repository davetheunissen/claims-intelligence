using ClaimsIntelligence.Api.Endpoints;
using ClaimsIntelligence.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register all API services (infrastructure, Cosmos collections, Swagger, health, CORS, OTEL)
builder.Services.AddApi(builder.Configuration);

var app = builder.Build();

// Swagger UI (always enabled; lock behind auth in production if needed)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Claims Intelligence API v1");
    options.RoutePrefix = "swagger";
});

app.UseCors();

// Endpoint groups
app.MapContentProcessorEndpoints();
app.MapClaimProcessorEndpoints();
app.MapSchemaVaultEndpoints();
app.MapSchemaSetVaultEndpoints();
app.MapClaimsDemoEndpoints();

// Health check
app.MapHealthChecks("/health");

await app.RunAsync();

// Expose Program class for WebApplicationFactory<Program> in tests
public partial class Program { }
