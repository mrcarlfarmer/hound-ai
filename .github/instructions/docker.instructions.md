---
description: "Use when editing Docker Compose files, Dockerfiles, or infrastructure configuration. Covers container networking, dev overrides, and deployment patterns."
applyTo: "{docker-compose*,**/Dockerfile,infra/**}"
---
# Hound AI — Docker & Infrastructure

## Container Architecture
Six containers on `hound-net` bridge network — see [docker-compose.yml](../../docker-compose.yml):
- `ollama` — LLM server with NVIDIA GPU passthrough (port 11434)
- `ravendb` — Document DB (port 8080)
- `trading-pack` — Trading hounds process (depends on ollama, ravendb, hound-api)
- `hound-api` — ASP.NET Core API + SignalR hub (port 5000)
- `hound-ui` — Angular SPA via nginx (port 4200)
- `watchtower` — GitOps auto-deploy from GHCR

## Development vs Production
- **Production**: `docker-compose.yml` — built images, no volume mounts
- **Development**: overlay `docker-compose.dev.yml` — `dotnet watch run` for .NET, `ng serve` for Angular, source volume mounts with node_modules excluded for WSL2 compatibility

```bash
# Dev stack
docker compose -f docker-compose.yml -f docker-compose.dev.yml up
```

## Conventions
- Inter-container communication uses **service names** as hostnames (e.g., `http://ollama:11434`, `http://hound-api:5000`)
- Environment variables in compose override `appsettings.json` (double-underscore syntax: `Ollama__BaseUrl`)
- New packs get their own container + Dockerfile in `src/Hound.{PackName}/Dockerfile`
- Ollama model pulls handled by one-shot `ollama-init` container via `infra/ollama/pull-models.sh`
