// Beispiel – deploy.sh übergibt die Werte auf der Kommandozeile, Geheimnisse kommen aus infra/.secrets.<rg>.env.
using 'main.bicep'

param budgetEmail = readEnvironmentVariable('BUDGET_EMAIL')
param deployApps = bool(readEnvironmentVariable('DEPLOY_APPS', 'false'))
param imageTag = readEnvironmentVariable('IMAGE_TAG', 'latest')
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD')
param bffClientSecret = readEnvironmentVariable('BFF_CLIENT_SECRET')
param keycloakAdminPassword = readEnvironmentVariable('KEYCLOAK_ADMIN_PASSWORD')
param piaPassword = readEnvironmentVariable('PIA_PASSWORD')
param fredPassword = readEnvironmentVariable('FRED_PASSWORD')
param friedaPassword = readEnvironmentVariable('FRIEDA_PASSWORD')
