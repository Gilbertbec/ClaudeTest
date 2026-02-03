@description('Base name used to derive resource names.')
param baseName string

@description('Azure region for the resource.')
param location string = resourceGroup().location

var uniqueSuffix = uniqueString(resourceGroup().id)
var rawName = replace(toLower('st${baseName}${uniqueSuffix}'), '-', '')
var storageAccountName = substring(rawName, 0, min(length(rawName), 24))

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

@description('The storage account name.')
output storageAccountName string = storageAccount.name

@description('The storage account resource ID.')
output storageAccountId string = storageAccount.id

@description('The primary connection string (contains secret).')
#disable-next-line outputs-should-not-contain-secrets
output primaryConnectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
