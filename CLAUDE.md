# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build the Function App
dotnet build src/KeyVaultFunction

# Publish for deployment
dotnet publish src/KeyVaultFunction -c Release -o ./publish

# Run locally (requires Azure Functions Core Tools)
cd src/KeyVaultFunction && func start

# Validate Bicep templates
az bicep build --file infra/main.bicep
```

## Architecture Overview

- **src/KeyVaultFunction/** — .NET 8 isolated-worker Azure Function with a single HTTP trigger (`GET /api/GetSecret?secretName=X`) that reads secrets from Azure Key Vault via `SecretClient` (singleton, injected via DI). Uses `DefaultAzureCredential` for auth.
- **infra/** — Bicep IaC: Storage Account, Consumption App Service Plan, Function App (system-assigned Managed Identity), Key Vault (RBAC-enabled), and role assignment granting the Function App "Key Vault Secrets User".
- **.github/workflows/deploy.yml** — GitHub Actions CI/CD using OIDC for Azure login, deploys Bicep then publishes the Function App.

## Key Conventions

- Target framework: .NET 8, Azure Functions v4 isolated worker
- ASP.NET Core integration (`ConfigureFunctionsWebApplication`) for `HttpRequest`/`IActionResult`
- Key Vault uses RBAC authorization (no legacy access policies)
- Error responses return full `ex.ToString()` for debugging (review before production)
- `local.settings.json` is gitignored; set `KEY_VAULT_URI` there for local dev
