#!/bin/bash
set -e

PROJECT_FILE="src/Aether/Aether.csproj"
OUTPUT_DIR="releases/latest"
BINARY_NAME="Aether"

echo "Starting Aether Rebuild..."

# Set version
NEW_VERSION="3.0.1"
if grep -q "<Version>" "$PROJECT_FILE"; then
    sed -i "s/<Version>.*<\/Version>/<Version>$NEW_VERSION<\/Version>/" "$PROJECT_FILE"
else
    sed -i "/<\/PropertyGroup>/i \    <Version>$NEW_VERSION<\/Version>" "$PROJECT_FILE"
fi

dotnet publish "$PROJECT_FILE" -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true /p:PublishReadyToRun=true -o "$OUTPUT_DIR"

# Build and Deploy Plugins
echo "🔨 Building MariaMemory Plugin..."
PLUGIN_PROJECT="src/Plugins/Aether.Plugins.MariaMemory/Aether.Plugins.MariaMemory.csproj"
PLUGIN_DEPLOY_DIR="/home/thoor/.aether/plugins/maria-memory"
dotnet build "$PLUGIN_PROJECT" -c Release
mkdir -p "$PLUGIN_DEPLOY_DIR"
cp src/Plugins/Aether.Plugins.MariaMemory/bin/Release/net10.0/Aether.Plugins.MariaMemory.dll "$PLUGIN_DEPLOY_DIR/"

# Ensure Data directory and Schema.sql are in the right place
mkdir -p "$OUTPUT_DIR/Data"
cp src/Aether/Data/Schema.sql "$OUTPUT_DIR/Data/"

systemctl --user restart aether.service
echo "Rebuild complete. Version: $NEW_VERSION"
