# Pathwise

Pathwise is a local-first League of Legends jungle review application. This
repository contains the ASP.NET Core backend and Next.js frontend.

## Prerequisites

- .NET SDK 10.0.401 (pinned by `global.json`)
- Node.js 24.18.0 (pinned by `.nvmrc`)
- pnpm 11.13.1 (recorded in the frontend package manifest)

## Run locally

Start the backend:

```powershell
dotnet run --project src/Pathwise.Api
```

In another terminal, start the frontend:

```powershell
cd src/Pathwise.Web
pnpm install
pnpm dev
```

Open `http://localhost:3000`. The frontend uses the Pathwise API at
`http://localhost:5100` by default. Override it through
`NEXT_PUBLIC_API_BASE_URL`; `.env.example` contains the local default. The API
health endpoint remains available at `http://localhost:5100/health`.

## Configure Riot ingestion

Set the local player identity in `src/Pathwise.Api/appsettings.Development.json`
or with environment variables:

```powershell
$env:Riot__Player__GameName = "Your game name"
$env:Riot__Player__TagLine = "Your tag"
```

Keep the Riot API key in .NET user secrets:

```powershell
dotnet user-secrets set "Riot:ApiKey" "RGAPI-..." --project src/Pathwise.Api
```

Restore the repository-local EF tool and apply the explicit migration before
starting the API for the first time:

```powershell
dotnet tool restore
dotnet ef database update --project src/Pathwise.Infrastructure --startup-project src/Pathwise.Api
```

The defaults use EUW (`euw1`) and the Europe regional route, fetch 50 Ranked
Solo/Duo matches, and store the SQLite database under `src/Pathwise.Api/data/`.
