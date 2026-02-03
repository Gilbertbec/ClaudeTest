@description('Base name used to derive resource names.')
param baseName string

@description('Azure region for the resource.')
param location string = resourceGroup().location

@description('Resource ID of the App Service Plan.')
param appServicePlanId string

@description('Connection string for the Storage Account.')
param storageAccountConnectionString string

@description('URI of the Key Vault (e.g. https://myvault.vault.azure.net/).')
param keyVaultUri string

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-${baseName}'
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v9.0'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageAccountConnectionString }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED', value: '1' }
        { name: 'KEY_VAULT_URI', value: keyVaultUri }
      ]
    }
  }
}

output functionAppName string = functionApp.name
output functionAppPrincipalId string = functionApp.identity.principalId
output functionAppDefaultHostName string = functionApp.properties.defaultHostName
