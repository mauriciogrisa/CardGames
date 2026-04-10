#!/bin/bash
# Deploy CardGames to Azure
# Usage: ./infra/deploy.sh [resource-group] [app-name]
#
# Prerequisites:
#   az login
#   dotnet SDK installed

RESOURCE_GROUP=${1:-"cardgames-rg"}
APP_NAME=${2:-"jogosdecartas"}
LOCATION="brazilsouth"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "=== 1. Create resource group (if needed) ==="
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

echo "=== 2. Provision infrastructure via Bicep ==="
az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file "$SCRIPT_DIR/main.bicep" \
  --parameters appName="$APP_NAME" location="$LOCATION" \
  --output none

echo "=== 3. Publish app ==="
PUBLISH_DIR="$PROJECT_ROOT/../publish-output"
dotnet publish "$PROJECT_ROOT/CardGames.csproj" -c Release -o "$PUBLISH_DIR"

echo "=== 4. Zip and deploy ==="
DEPLOY_ZIP="$PROJECT_ROOT/../deploy.zip"
if command -v zip &>/dev/null; then
  (cd "$PUBLISH_DIR" && zip -r "$DEPLOY_ZIP" .)
else
  powershell.exe -Command "Compress-Archive -Path '$PUBLISH_DIR/*' -DestinationPath '$DEPLOY_ZIP' -Force"
fi

az webapp deploy \
  --resource-group "$RESOURCE_GROUP" \
  --name "$APP_NAME" \
  --src-path "$DEPLOY_ZIP" \
  --type zip \
  --async false

echo ""
echo "Done. App URL: https://$APP_NAME.azurewebsites.net"
