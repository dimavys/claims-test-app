#!/usr/bin/env bash
# Deploys the Claims Module to Azure from this machine.
#
#   ./infra/deploy.sh all        create resources, migrate, deploy API + frontend, smoke-test
#   ./infra/deploy.sh infra      create / update the Azure resources only
#   ./infra/deploy.sh migrate    apply EF migrations to the Azure SQL database
#   ./infra/deploy.sh api        build and deploy the API
#   ./infra/deploy.sh web        build and deploy the frontend
#   ./infra/deploy.sh smoke      run the Newman suite against the deployed API
#   ./infra/deploy.sh info       print URLs and the GitHub Actions values
#   ./infra/deploy.sh destroy    delete the whole resource group
#
# Prerequisites: az (logged in), dotnet 9, node 22+, zip, curl, openssl.
# Override defaults with environment variables: RG (resource group), LOCATION (one region; default tries several),
# SWA_LOCATION (Static Web App region), PLAN_SKU (App Service plan: B1 default; F1 = free tier, see below),
# API_NAME (fixed API name -> <name>.azurewebsites.net; also remembered in infra/.secrets.env).

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RG="${RG:-rg-claims-demo}"
SECRETS_FILE="$ROOT/infra/.secrets.env"
WORK="$ROOT/infra/.publish"
DEPLOYMENT="main"

say()  { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
fail() { printf '\033[1;31mError: %s\033[0m\n' "$*" >&2; exit 1; }

preflight() {
  for tool in az dotnet node npm zip curl openssl; do
    command -v "$tool" >/dev/null || fail "'$tool' is not installed or not on PATH."
  done
  az account show >/dev/null 2>&1 || fail "Not logged in to Azure. Run: az login"
  # Brand-new subscriptions need these resource providers registered once (no-op when already registered).
  for ns in Microsoft.Web Microsoft.Sql Microsoft.Storage; do
    state="$(az provider show -n "$ns" --query registrationState -o tsv 2>/dev/null || echo NotRegistered)"
    if [ "$state" != "Registered" ]; then
      say "Registering resource provider $ns (first use on this subscription)"
      az provider register -n "$ns" --wait
    fi
  done
}

# Secrets are generated once and reused, so re-running the script never rotates them under a live app.
load_secrets() {
  if [ ! -f "$SECRETS_FILE" ]; then
    say "Generating deployment secrets -> infra/.secrets.env (git-ignored)"
    {
      echo "SQL_ADMIN_PASSWORD='$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 20)Aa1!'"
      echo "JWT_SIGNING_KEY='$(openssl rand -hex 32)'"
      echo "FILE_SIGNING_KEY='$(openssl rand -hex 16)'"
    } > "$SECRETS_FILE"
    chmod 600 "$SECRETS_FILE"
  fi
  # shellcheck disable=SC1090
  source "$SECRETS_FILE"
}

out() { az deployment group show -g "$RG" -n "$DEPLOYMENT" --query "properties.outputs.$1.value" -o tsv; }

# Regions tried in order when LOCATION is not set. New free subscriptions are often blocked in the busiest regions
# ("not accepting new customers"), so the script falls through to the next one automatically.
DEFAULT_REGIONS="northeurope uksouth swedencentral francecentral germanywestcentral norwayeast switzerlandnorth eastus2 centralus"

cmd_infra() {
  preflight; load_secrets
  local candidates swa deployed="" loc log
  candidates="${LOCATION:-}"
  if [ -z "$candidates" ]; then
    # Re-runs stay in the region of the existing plan instead of probing every region again.
    # Azure returns display names ("Sweden Central"); region ids are the lower-case name without spaces.
    candidates="$(az appservice plan list -g "$RG" --query '[0].location' -o tsv 2>/dev/null | tr -d ' ' | tr '[:upper:]' '[:lower:]' || true)"
    candidates="${candidates:-$DEFAULT_REGIONS}"
  fi
  swa="${SWA_LOCATION:-centralus}"   # Static Web Apps only exist in: westus2 centralus eastus2 westeurope eastasia
  local sku="${PLAN_SKU:-B1}"
  local api_name="${API_NAME:-}"
  if [ "$sku" = "F1" ]; then
    echo "NOTE: the F1 (free) plan has no Always On and sleeps when idle. The API still works, but background jobs"
    echo "      (GL posting, the 15-minute SLA scan) only run while the app is awake. Use B1 or better for a live review."
  fi

  # The resource group's own region does not restrict where its resources go, so an existing one is reused.
  if [ "$(az group exists -n "$RG")" != "true" ]; then
    say "Creating resource group $RG"
    az group create -n "$RG" -l "${candidates%% *}" -o none
  fi

  say "Validating the template"
  az bicep build --file "$ROOT/infra/main.bicep" --stdout >/dev/null

  for loc in $candidates; do
    say "Deploying to $loc (plan $sku, Static Web App region $swa). SQL + App Service take a few minutes"
    log="$(mktemp)"
    if az deployment group create -g "$RG" -n "$DEPLOYMENT" -f "$ROOT/infra/main.bicep" \
        -p location="$loc" staticWebAppLocation="$swa" appServicePlanSku="$sku" apiNameOverride="$api_name" sqlAdminPassword="$SQL_ADMIN_PASSWORD" \
           jwtSigningKey="$JWT_SIGNING_KEY" fileSigningKey="$FILE_SIGNING_KEY" \
        --query properties.outputs -o none 2>"$log"; then
      deployed="$loc"; rm -f "$log"; break
    fi

    # Region/quota refusals are reported by Azure's pre-flight check, before anything is created, so moving on is safe.
    if grep -qiE 'locationineligible|not accepting new customers|RequestDisallowedByAzure|SubscriptionIsOverQuotaForSku|additional quota' "$log"; then
      echo "Region $loc cannot host plan $sku for this subscription (region restriction or App Service quota); trying the next one."
      rm -f "$log"
      continue
    fi
    cat "$log" >&2; rm -f "$log"
    fail "Deployment failed for a reason other than region availability (details above)."
  done

  [ -n "$deployed" ] || fail "None of these regions accepted plan $sku: $candidates

New subscriptions often have an App Service quota of 0 for paid tiers. Options:
  1. Free tier (works, but no Always On so background jobs only run while awake):   PLAN_SKU=F1 ./infra/deploy.sh all
  2. Request quota: portal -> Subscriptions -> Usage + quotas -> Microsoft.Web -> Basic VMs (B1) -> +1
  3. Upgrade the account to pay-as-you-go (your remaining credit still applies); quota is usually granted at once.
If the Static Web App region was the problem instead, try SWA_LOCATION=eastus2 (or westus2, eastasia)."

  say "Resources ready in $deployed"
  echo "API:      $(out apiUrl)"
  echo "Frontend: $(out staticWebAppUrl)"
}

cmd_migrate() {
  preflight; load_secrets
  local server fqdn ip rule
  server="$(out sqlServerName)"; fqdn="$(out sqlServerFqdn)"
  rule="deploy-$(whoami)"
  ip="$(curl -fsS https://api.ipify.org)"

  say "Opening the SQL firewall for $ip (removed afterwards)"
  az sql server firewall-rule create -g "$RG" -s "$server" -n "$rule" --start-ip-address "$ip" --end-ip-address "$ip" -o none
  trap 'az sql server firewall-rule delete -g "$RG" -s "$server" -n "$rule" -y -o none || true' EXIT

  say "Applying EF Core migrations (schema + seed data)"
  ( cd "$ROOT/backend" && dotnet tool restore >/dev/null &&
    ConnectionStrings__ClaimsDb="Server=tcp:$fqdn,1433;Initial Catalog=ClaimsDb;User ID=claimsadmin;Password=$SQL_ADMIN_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;" \
    dotnet ef database update --project src/ClaimsModule.Persistence )
}

# Waits for a healthy API. With an expected version it waits for THAT build, so a smoke test never races the old
# container while it is being replaced.
wait_for_health() {
  local url="$1/health" expect="${2:-}" i body
  say "Waiting for $url${expect:+ to report build ${expect:0:7}}"
  for i in $(seq 1 72); do
    body="$(curl -s "$url" || true)"
    if [[ "$body" == *'"status":"Healthy"'* ]] && { [ -z "$expect" ] || [[ "$body" == *"$expect"* ]]; }; then echo "healthy: $body"; return 0; fi
    sleep 5
  done
  fail "The API did not become healthy. Check: az webapp log tail -g $RG -n $(out apiName)"
}

cmd_api() {
  preflight
  mkdir -p "$WORK"; rm -rf "$WORK/api" "$WORK/api.zip"
  local rev; rev="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || true)"
  say "Publishing the API${rev:+ (build ${rev:0:7})}"
  dotnet publish "$ROOT/backend/src/ClaimsModule.API" -c Release -o "$WORK/api" --nologo -v quiet ${rev:+-p:SourceRevisionId="$rev"}
  ( cd "$WORK/api" && zip -qr "$WORK/api.zip" . )
  say "Deploying to App Service $(out apiName)"
  az webapp deploy -g "$RG" -n "$(out apiName)" --src-path "$WORK/api.zip" --type zip -o none
  wait_for_health "$(out apiUrl)" "$rev"
}

cmd_web() {
  preflight
  local api_url token
  api_url="$(out apiUrl)"
  say "Building the frontend"
  ( cd "$ROOT/frontend" && npm ci --no-audit --no-fund && npm run build )
  local dist="$ROOT/frontend/dist/claims-ui/browser"
  [ -f "$dist/index.html" ] || fail "Build output not found at $dist"

  # Runtime config: the same build can point at any API without rebuilding.
  printf '{ "apiBaseUrl": "%s" }\n' "$api_url" > "$dist/config.json"

  say "Deploying to Static Web App $(out staticWebAppName)"
  token="$(az staticwebapp secrets list -g "$RG" -n "$(out staticWebAppName)" --query properties.apiKey -o tsv)"
  ( cd "$ROOT/frontend" && npx --yes @azure/static-web-apps-cli deploy "$dist" --deployment-token "$token" --env production )
  echo "Frontend: $(out staticWebAppUrl)"
}

cmd_smoke() {
  preflight
  say "Running the Newman suite against $(out apiUrl)"
  ( cd "$ROOT/tests/postman" && npm ci --no-audit --no-fund && npm run build:collection >/dev/null &&
    npx newman run claims-api.postman_collection.json -e local.postman_environment.json \
      --env-var "baseUrl=$(out apiUrl)" --working-dir . --color on )
}

cmd_info() {
  preflight
  local api swa
  api="$(out apiUrl)"; swa="$(out staticWebAppName)"
  cat <<INFO

Frontend : $(out staticWebAppUrl)
API      : $api
Swagger  : $api/swagger
Health   : $api/health

Test credentials: handler / Handler#2026, supervisor / Supervisor#2026, manager / Manager#2026

GitHub Actions (repo Settings -> Secrets and variables -> Actions):
  Variables: AZURE_WEBAPP_NAME=$(out apiName)   API_URL=$api
  Secrets:
    AZURE_WEBAPP_PUBLISH_PROFILE   az webapp deployment list-publishing-profiles -g $RG -n $(out apiName) --xml
    AZURE_STATIC_WEB_APPS_API_TOKEN  az staticwebapp secrets list -g $RG -n $swa --query properties.apiKey -o tsv
    SQL_CONNECTION_STRING          see: ./infra/deploy.sh sqlconn
INFO
}

cmd_sqlconn() {
  preflight; load_secrets
  echo "Server=tcp:$(out sqlServerFqdn),1433;Initial Catalog=ClaimsDb;User ID=claimsadmin;Password=$SQL_ADMIN_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"
}

cmd_destroy() {
  preflight
  read -r -p "Delete resource group '$RG' and EVERYTHING in it (database included)? Type the name to confirm: " answer
  [ "$answer" = "$RG" ] || fail "Cancelled."
  az group delete -n "$RG" --yes --no-wait
  say "Deletion started (runs in the background, a few minutes)"
}

case "${1:-}" in
  all)     cmd_infra; cmd_migrate; cmd_api; cmd_web; cmd_smoke; cmd_info ;;
  infra)   cmd_infra ;;
  migrate) cmd_migrate ;;
  api)     cmd_api ;;
  web)     cmd_web ;;
  smoke)   cmd_smoke ;;
  info)    cmd_info ;;
  sqlconn) cmd_sqlconn ;;
  destroy) cmd_destroy ;;
  *) sed -n '2,14p' "$0"; exit 1 ;;
esac
