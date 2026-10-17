#!/usr/bin/env bash
# Writes local config files and docs/local-credentials.md from `supabase status`.
# Usage: ./scripts/dev-env.sh            (LAN_IP=192.168.1.20 ./scripts/dev-env.sh for phones)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
eval "$(npx supabase status -o env)"
ANON_KEY="${ANON_KEY:-${PUBLISHABLE_KEY:-}}"
SERVICE_ROLE_KEY="${SERVICE_ROLE_KEY:-${SECRET_KEY:-}}"
host="${LAN_IP:-localhost}"
mkdir -p "$root/backend/src/Handyman.Api" "$root/frontend"

cat > "$root/backend/src/Handyman.Api/appsettings.Local.json" <<EOF
{
  "ConnectionStrings": { "Default": "Host=localhost;Port=54322;Database=postgres;Username=postgres;Password=postgres" },
  "Supabase": { "Url": "$API_URL", "ServiceRoleKey": "$SERVICE_ROLE_KEY" },
  "Seed": { "Enabled": true },
  "Cors": { "Origins": ["http://localhost:8081"] }
}
EOF

cat > "$root/frontend/.env.local" <<EOF
EXPO_PUBLIC_SUPABASE_URL=http://$host:54321
EXPO_PUBLIC_SUPABASE_ANON_KEY=$ANON_KEY
EXPO_PUBLIC_API_URL=http://$host:5080/api
EOF

sed -e "s|{{API_URL}}|$API_URL|g" -e "s|{{STUDIO_URL}}|$STUDIO_URL|g" \
    -e "s|{{DB_URL}}|$DB_URL|g" -e "s|{{ANON_KEY}}|$ANON_KEY|g" \
    -e "s|{{SERVICE_ROLE_KEY}}|$SERVICE_ROLE_KEY|g" \
    "$root/docs/local-credentials.example.md" > "$root/docs/local-credentials.md"

echo "Wrote appsettings.Local.json, frontend/.env.local and docs/local-credentials.md"
