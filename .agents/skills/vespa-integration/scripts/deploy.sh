#!/bin/bash
set -e

# Resolve paths relative to this script
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../../../../" && pwd)"

echo "Packaging Vespa application from $ROOT_DIR/vespa..."
cd "$ROOT_DIR/vespa"

# Zip contents quietly
zip -q -r "$ROOT_DIR/application.zip" services.xml schemas/

echo "Uploading application package to Vespa config server (localhost:19071)..."
RESPONSE=$(curl -s --header "Content-Type: application/zip" --data-binary @"$ROOT_DIR/application.zip" http://localhost:19071/application/v2/tenant/default/prepareandactivate)

# Clean up zip
rm -f "$ROOT_DIR/application.zip"

if echo "$RESPONSE" | grep -q '"activated":true'; then
  echo "Vespa configuration deployed and activated successfully!"
else
  echo "Vespa deployment failed!"
  echo "$RESPONSE"
  exit 1
fi
