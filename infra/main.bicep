targetScope = 'resourceGroup'

@description('Base name for all resources (e.g. myapp).')
param baseName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

// ── Storage Account ──────────────────────────────────────────────
module storage 'modules/storageAccount.bicep' = {
  name: 'storageAccount'
  params: {
    baseName: baseName
    location: location
  }
}

// ── App Service Plan (Consumption / Dynamic) ─────────────────────
module plan 'modules/appServicePlan.bicep' = {
  name: 'appServicePlan'
  params: {
    baseName: baseName
    location: location
  }
}

// ── Key Vault ────────────────────────────────────────────────────
module kv 'modules/keyVault.bicep' = {
  name: 'keyVault'
  params: {
    baseName: baseName
    location: location
  }
}

// ── Function App ─────────────────────────────────────────────────
module func 'modules/functionApp.bicep' = {
  name: 'functionApp'
  params: {
    baseName: baseName
    location: location
    appServicePlanId: plan.outputs.appServicePlanId
    storageAccountConnectionString: storage.outputs.primaryConnectionString
    keyVaultUri: kv.outputs.keyVaultUri
  }
}

// ── Key Vault Role Assignment ────────────────────────────────────
module kvRole 'modules/keyVaultRoleAssignment.bicep' = {
  name: 'keyVaultRoleAssignment'
  params: {
    keyVaultId: kv.outputs.keyVaultId
    principalId: func.outputs.functionAppPrincipalId
  }
}

// ── Outputs ──────────────────────────────────────────────────────
output functionAppName string = func.outputs.functionAppName
output functionAppUrl string = 'https://${func.outputs.functionAppDefaultHostName}'
output keyVaultName string = kv.outputs.keyVaultName
output keyVaultUri string = kv.outputs.keyVaultUri
