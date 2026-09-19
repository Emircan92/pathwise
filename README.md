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

Open `http://localhost:3000`. The landing page checks the backend health endpoint
at `http://localhost:5100/health`. Override the frontend URL through
`NEXT_PUBLIC_API_BASE_URL`; `.env.example` contains the local default.
