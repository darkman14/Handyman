#!/usr/bin/env bash
# Writes local config files and docs/local-credentials.md from `supabase status`.
# Machine settings (DOCKER_IP, LAN_IP) come from .env.dev.local; see .env.dev.local.example.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
if [ -f "$root/.env.dev.local" ]; then set -a; . "$root/.env.dev.local"; set +a; fi
docker_ip="${DOCKER_IP:-localhost}"
lan_ip="${LAN_IP:-}"
app_host="${lan_ip:-localhost}"             # how the browser/phone reaches the API on this machine
supa_host="${lan_ip:-$docker_ip}"           # how the browser/phone reaches Supabase

eval "$(npx supabase status -o env)"
ANON_KEY="${ANON_KEY:-${PUBLISHABLE_KEY:-}}"
SERVICE_ROLE_KEY="${SERVICE_ROLE_KEY:-${SECRET_KEY:-}}"
fix() { local v="${1//127.0.0.1/$docker_ip}"; echo "${v//localhost/$docker_ip}"; }
API_URL="$(fix "$API_URL")"; STUDIO_URL="$(fix "$STUDIO_URL")"; DB_URL="$(fix "$DB_URL")"

origins='"http://localhost:8081"'
[ -n "$lan_ip" ] && origins="$origins, \"http://$lan_ip:8081\""

mkdir -p "$root/backend/src/Handyman.Api" "$root/frontend"
cat > "$root/backend/src/Handyman.Api/appsettings.Local.json" <<EOF
{
  "ConnectionStrings": { "Default": "Host=$docker_ip;Port=54322;Database=postgres;Username=postgres;Password=postgres" },
  "Supabase": { "Url": "$API_URL", "ServiceRoleKey": "$SERVICE_ROLE_KEY" },
  "Seed": { "Enabled": true },
  "Cors": { "Origins": [$origins] }
}
EOF

cat > "$root/frontend/.env.local" <<EOF
EXPO_PUBLIC_SUPABASE_URL=http://$supa_host:54321
EXPO_PUBLIC_SUPABASE_ANON_KEY=$ANON_KEY
EXPO_PUBLIC_API_URL=http://$app_host:5080/api
EOF

sed -e "s|{{API_URL}}|$API_URL|g" -e "s|{{STUDIO_URL}}|$STUDIO_URL|g" \
    -e "s|{{DB_URL}}|$DB_URL|g" -e "s|{{ANON_KEY}}|$ANON_KEY|g" \
    -e "s|{{SERVICE_ROLE_KEY}}|$SERVICE_ROLE_KEY|g" -e "s|{{DOCKER_IP}}|$docker_ip|g" \
    "$root/docs/local-credentials.example.md" > "$root/docs/local-credentials.md"

echo "Docker/Supabase host: $docker_ip; app host: $app_host"
echo "Wrote appsettings.Local.json, frontend/.env.local and docs/local-credentials.md"
