# Writes local config files and docs/local-credentials.md from `supabase status`.
# Usage: ./scripts/dev-env.ps1     ($env:LAN_IP = "192.168.1.20"; ./scripts/dev-env.ps1 for phones)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot/..").Path
Set-Location $root

$vars = @{}
npx supabase status -o env | ForEach-Object {
  if ($_ -match '^([A-Z_]+)="?(.*?)"?$') { $vars[$Matches[1]] = $Matches[2] }
}
$anon = if ($vars["ANON_KEY"]) { $vars["ANON_KEY"] } else { $vars["PUBLISHABLE_KEY"] }
$service = if ($vars["SERVICE_ROLE_KEY"]) { $vars["SERVICE_ROLE_KEY"] } else { $vars["SECRET_KEY"] }
$hostName = if ($env:LAN_IP) { $env:LAN_IP } else { "localhost" }
New-Item -ItemType Directory -Force "$root/backend/src/Handyman.Api", "$root/frontend" | Out-Null

@"
{
  "ConnectionStrings": { "Default": "Host=localhost;Port=54322;Database=postgres;Username=postgres;Password=postgres" },
  "Supabase": { "Url": "$($vars["API_URL"])", "ServiceRoleKey": "$service" },
  "Seed": { "Enabled": true },
  "Cors": { "Origins": ["http://localhost:8081"] }
}
"@ | Set-Content -Encoding utf8 "$root/backend/src/Handyman.Api/appsettings.Local.json"

@"
EXPO_PUBLIC_SUPABASE_URL=http://${hostName}:54321
EXPO_PUBLIC_SUPABASE_ANON_KEY=$anon
EXPO_PUBLIC_API_URL=http://${hostName}:5080/api
"@ | Set-Content -Encoding utf8 "$root/frontend/.env.local"

(Get-Content -Raw "$root/docs/local-credentials.example.md").
  Replace("{{API_URL}}", $vars["API_URL"]).
  Replace("{{STUDIO_URL}}", $vars["STUDIO_URL"]).
  Replace("{{DB_URL}}", $vars["DB_URL"]).
  Replace("{{ANON_KEY}}", $anon).
  Replace("{{SERVICE_ROLE_KEY}}", $service) |
  Set-Content -Encoding utf8 "$root/docs/local-credentials.md"

Write-Host "Wrote appsettings.Local.json, frontend/.env.local and docs/local-credentials.md"
