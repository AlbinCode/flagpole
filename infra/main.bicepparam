using 'main.bicep'

// Secrets are read from environment variables at deploy time, so they are
// never written to this file or to Git. The deploy workflow sets them from
// GitHub secrets.
param containerImage = readEnvironmentVariable('FLAGPOLE_IMAGE', 'ghcr.io/albincode/flagpole:latest')
param postgresAdminPassword = readEnvironmentVariable('FLAGPOLE_DB_PASSWORD')
param adminApiKey = readEnvironmentVariable('FLAGPOLE_ADMIN_KEY')
param readerApiKey = readEnvironmentVariable('FLAGPOLE_READER_KEY')
