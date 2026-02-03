@description('Base name used to derive resource names.')
param baseName string

@description('Azure region for the resource.')
param location string = resourceGroup().location

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'asp-${baseName}'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: false
  }
}

output appServicePlanId string = appServicePlan.id
