// Flagpole on Azure Container Apps.
//
// Security choices:
// - PostgreSQL has no public endpoint. It lives in a delegated subnet and is
//   reached over the virtual network through a private DNS zone.
// - Secrets live in Key Vault. The container app reads them with a managed
//   identity, so no credential is stored in the app configuration or in CI.
// - The identity only gets "Key Vault Secrets User" on this one vault.

targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short name used as a prefix for every resource.')
@minLength(3)
@maxLength(12)
param appName string = 'flagpole'

@description('Container image to run, for example ghcr.io/albincode/flagpole:<sha>.')
param containerImage string

@description('Administrator login for PostgreSQL.')
param postgresAdminLogin string = 'flagpoleadmin'

@secure()
@description('Administrator password for PostgreSQL.')
param postgresAdminPassword string

@secure()
@description('API key with permission to manage flags. At least 32 characters.')
@minLength(32)
param adminApiKey string

@secure()
@description('API key that can only evaluate flags. At least 32 characters.')
@minLength(32)
param readerApiKey string

var suffix = uniqueString(resourceGroup().id)
var databaseName = 'flagpole'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

// ---------------------------------------------------------------- Monitoring

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appName}-logs-${suffix}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ------------------------------------------------------------------- Network

resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: '${appName}-vnet'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.20.0.0/16'
      ]
    }
    subnets: [
      {
        name: 'apps'
        properties: {
          addressPrefix: '10.20.0.0/23'
          delegations: [
            {
              name: 'container-apps'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'database'
        properties: {
          addressPrefix: '10.20.2.0/24'
          delegations: [
            {
              name: 'postgres'
              properties: {
                serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
              }
            }
          ]
        }
      }
    ]
  }
}

resource postgresDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: '${appName}-${suffix}.private.postgres.database.azure.com'
  location: 'global'
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: postgresDns
  name: '${appName}-vnet-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

// ------------------------------------------------------------------ Database

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${appName}-pg-${suffix}'
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      delegatedSubnetResourceId: vnet.properties.subnets[1].id
      privateDnsZoneArmResourceId: postgresDns.id
      publicNetworkAccess: 'Disabled'
    }
  }
  dependsOn: [
    postgresDnsLink
  ]
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// ------------------------------------------------------------------- Secrets

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${appName}-identity'
  location: location
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  // Key Vault names are limited to 24 characters.
  name: 'kv-${appName}-${take(suffix, 8)}'
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
    enablePurgeProtection: true
  }
}

resource connectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'db-connection-string'
  properties: {
    value: 'Host=${postgres.properties.fullyQualifiedDomainName};Database=${databaseName};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require'
  }
}

resource adminKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'admin-api-key'
  properties: {
    value: adminApiKey
  }
}

resource readerKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'reader-api-key'
  properties: {
    value: readerApiKey
  }
}

// Least privilege: read secrets in this vault only, nothing else.
resource secretsReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, identity.id, keyVaultSecretsUserRoleId)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ------------------------------------------------------------- Container App

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${appName}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: vnet.properties.subnets[0].id
      internal: false
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${appName}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: [
        {
          name: 'db-connection-string'
          keyVaultUrl: connectionStringSecret.properties.secretUri
          identity: identity.id
        }
        {
          name: 'admin-api-key'
          keyVaultUrl: adminKeySecret.properties.secretUri
          identity: identity.id
        }
        {
          name: 'reader-api-key'
          keyVaultUrl: readerKeySecret.properties.secretUri
          identity: identity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ConnectionStrings__Flagpole'
              secretRef: 'db-connection-string'
            }
            {
              name: 'Database__MigrateOnStartup'
              value: 'true'
            }
            {
              name: 'ApiKeys__Keys__0__Name'
              value: 'admin'
            }
            {
              name: 'ApiKeys__Keys__0__Role'
              value: 'admin'
            }
            {
              name: 'ApiKeys__Keys__0__Key'
              secretRef: 'admin-api-key'
            }
            {
              name: 'ApiKeys__Keys__1__Name'
              value: 'reader'
            }
            {
              name: 'ApiKeys__Keys__1__Role'
              value: 'reader'
            }
            {
              name: 'ApiKeys__Keys__1__Key'
              secretRef: 'reader-api-key'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-load'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
  dependsOn: [
    secretsReader
    database
  ]
}

output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output keyVaultName string = vault.name
