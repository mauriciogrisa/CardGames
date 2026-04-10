#!/bin/bash
# Deploy CardGames to Azure
# Usage: ./infra/deploy.sh [resource-group] [app-name]
#
# Prerequisites:
#   az login
#   dotnet SDK installed

RESOURCE_GROUP=${1:-"cardgames-rg"}
APP_NAME=${2:-"cardgames-mauricio"}
LOCATION="brazilsouth"

echo "=== 1. Create resource group (if needed) ==="
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

echo "=== 2. Provision infrastructure via Bicep ==="
az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file "$(dirname "$0")/main.bicep" \
  --parameters appName="$APP_NAME" location="$LOCATION" \
  --output none

echo "=== 3. Build and deploy app ==="
az webapp up \
  --name "$APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --runtime "DOTNETCORE:10.0" \
  --os-type linux

echo ""
echo "✓ Done. App URL: https://$APP_NAME.azurewebsites.net"
