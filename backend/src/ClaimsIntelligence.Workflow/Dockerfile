FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy solution and project files first for layer caching
COPY ClaimsIntelligence.slnx ./
COPY src/ClaimsIntelligence.Domain/ClaimsIntelligence.Domain.csproj           src/ClaimsIntelligence.Domain/
COPY src/ClaimsIntelligence.Infrastructure/ClaimsIntelligence.Infrastructure.csproj src/ClaimsIntelligence.Infrastructure/
COPY src/ClaimsIntelligence.Api/ClaimsIntelligence.Api.csproj                 src/ClaimsIntelligence.Api/
COPY src/ClaimsIntelligence.ContentProcessor/ClaimsIntelligence.ContentProcessor.csproj src/ClaimsIntelligence.ContentProcessor/
COPY src/ClaimsIntelligence.Workflow/ClaimsIntelligence.Workflow.csproj       src/ClaimsIntelligence.Workflow/

RUN dotnet restore src/ClaimsIntelligence.Workflow/ClaimsIntelligence.Workflow.csproj

COPY . .

RUN dotnet publish src/ClaimsIntelligence.Workflow/ClaimsIntelligence.Workflow.csproj \
    -c Release -o /app/publish --no-restore

# ── runtime ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

COPY --from=build /app/publish .

USER appuser
ENTRYPOINT ["dotnet", "ClaimsIntelligence.Workflow.dll"]
