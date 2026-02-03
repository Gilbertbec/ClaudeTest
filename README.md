# Azure Function + Key Vault (Managed Identity)

A C# .NET 8 Azure Function (isolated worker) that reads secrets from Azure Key Vault using Managed Identity. Infrastructure is defined in Bicep and deployed via GitHub Actions with OIDC.

## Architecture

- **Azure Function** (Consumption plan, system-assigned Managed Identity)
- **Azure Key Vault** (RBAC-enabled, Function App granted "Key Vault Secrets User")
- **Storage Account** (required by Azure Functions runtime)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- An Azure subscription

## Local Development

1. Update `src/KeyVaultFunction/local.settings.json` with your Key Vault URI.
2. Ensure you are authenticated (`az login`).
3. Start the function:
   ```bash
   cd src/KeyVaultFunction
   func start
   ```
4. Test:
   ```bash
   curl "http://localhost:7071/api/GetSecret?secretName=MySecret"
   ```

## Deploy Infrastructure

### Create the resource group

```bash
az group create --name rg-kvfuncapp --location eastus2
```

### Deploy Bicep

```bash
az deployment group create \
  --resource-group rg-kvfuncapp \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam
```

### Add a secret to Key Vault

```bash
az keyvault secret set --vault-name kv-kvfuncapp --name MySecret --value "Hello from Key Vault"
```

## Deploy Function Code

```bash
cd src/KeyVaultFunction
dotnet publish -c Release -o ./publish
func azure functionapp publish func-kvfuncapp
```

## GitHub Actions CI/CD

The workflow at `.github/workflows/deploy.yml` deploys infrastructure and code on push to `main`.

### OIDC Setup (Recommended)

1. Create an Azure AD app registration and federated credential:
   ```bash
   az ad app create --display-name github-deploy
   APP_ID=$(az ad app list --display-name github-deploy --query "[0].appId" -o tsv)
   az ad sp create --id $APP_ID
   SP_OID=$(az ad sp show --id $APP_ID --query id -o tsv)

   # Grant Contributor on the resource group
   az role assignment create --assignee $SP_OID --role Contributor \
     --scope /subscriptions/<SUB_ID>/resourceGroups/rg-kvfuncapp

   # Create federated credential for GitHub Actions
   az ad app federated-credential create --id $APP_ID --parameters '{
     "name": "github-main",
     "issuer": "https://token.actions.githubusercontent.com",
     "subject": "repo:<OWNER>/<REPO>:ref:refs/heads/main",
     "audiences": ["api://AzureADTokenExchange"]
   }'
   ```
2. Set GitHub repository secrets:
   - `AZURE_CLIENT_ID` — App registration Application (client) ID
   - `AZURE_TENANT_ID` — Azure AD tenant ID
   - `AZURE_SUBSCRIPTION_ID` — Azure subscription ID

### Service Principal Fallback

If OIDC is not available, create a service principal secret and replace the login step:

```yaml
- name: Azure Login (SP)
  uses: azure/login@v2
  with:
    creds: ${{ secrets.AZURE_CREDENTIALS }}
```

Where `AZURE_CREDENTIALS` is the JSON output from:
```bash
az ad sp create-for-rbac --name github-deploy --role Contributor \
  --scopes /subscriptions/<SUB_ID>/resourceGroups/rg-kvfuncapp --sdk-auth
```

## Testing

```bash
# After deploy, get the function URL with key
FUNC_URL=$(az functionapp function show \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp \
  --function-name GetSecret \
  --query invokeUrlTemplate -o tsv)

FUNC_KEY=$(az functionapp function keys list \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp \
  --function-name GetSecret \
  --query default -o tsv)

# Retrieve a secret
curl "${FUNC_URL}?secretName=MySecret&code=${FUNC_KEY}"

# Missing secretName → 400
curl "${FUNC_URL}?code=${FUNC_KEY}"

# Non-existent secret → 500 with stack trace
curl "${FUNC_URL}?secretName=DoesNotExist&code=${FUNC_KEY}"
```

## Cleanup

```bash
az group delete --name rg-kvfuncapp --yes --no-wait
```
