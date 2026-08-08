#!/usr/bin/env bash
# Pulls vendor credentials from the geoassets-otel-kv Key Vault into .env,
# so nobody has to hand-paste them. Requires `az login` and the Key Vault
# Secrets User (or Officer) role on geoassets-otel-kv.
set -euo pipefail

cd "$(dirname "$0")"

VAULT_NAME="geoassets-otel-kv"

if [ ! -f .env ]; then
  cp .env.example .env
fi

NEW_RELIC_LICENSE_KEY=$(az keyvault secret show \
  --vault-name "$VAULT_NAME" \
  --name NEW-RELIC-LICENSE-KEY \
  --query value -o tsv)

if grep -q '^NEW_RELIC_LICENSE_KEY=' .env; then
  sed -i.bak "s|^NEW_RELIC_LICENSE_KEY=.*|NEW_RELIC_LICENSE_KEY=${NEW_RELIC_LICENSE_KEY}|" .env
  rm .env.bak
else
  echo "NEW_RELIC_LICENSE_KEY=${NEW_RELIC_LICENSE_KEY}" >> .env
fi

echo "NEW_RELIC_LICENSE_KEY pulled from Key Vault '$VAULT_NAME' into .env"

AZURE_MONITOR_CONNECTION_STRING=$(az keyvault secret show \
  --vault-name "$VAULT_NAME" \
  --name AZURE-MONITOR-CONNECTION-STRING \
  --query value -o tsv)

if grep -q '^AZURE_MONITOR_CONNECTION_STRING=' .env; then
  sed -i.bak "s|^AZURE_MONITOR_CONNECTION_STRING=.*|AZURE_MONITOR_CONNECTION_STRING=${AZURE_MONITOR_CONNECTION_STRING}|" .env
  rm .env.bak
else
  echo "AZURE_MONITOR_CONNECTION_STRING=${AZURE_MONITOR_CONNECTION_STRING}" >> .env
fi

echo "AZURE_MONITOR_CONNECTION_STRING pulled from Key Vault '$VAULT_NAME' into .env"
