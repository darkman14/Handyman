# Writes local config files and docs/local-credentials.md from `supabase status`.
# Machine settings (DOCKER_IP, LAN_IP) come from .env.dev.local; see .env.dev.local.example.
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot/..").Path
Set-Location $root

$cfg = @{}
if (Test-Path "$root/.env.dev.local") {
  Get-Content "$root/.env.dev.local" | ForEach-Object { if ($_ -match '^\s*([A-Z_]+)\s*=\s*(.*?)\s*$') { $cfg[$Matches[1]] = $Matches[2] } }
}
$dockerIp = if ($cfg["DOCKER_IP"]) { $cfg["DOCKER_IP"] } elseif ($env:DOCKER_IP) { $env:DOCKER_IP } else { "localhost" }
$lanIp = if ($cfg["LAN_IP"]) { $cfg["LAN_IP"] } elseif ($env:LAN_IP) { $env:LAN_IP } else { "" }
$appHost = if ($lanIp) { $lanIp } else { "localhost" }
$supaHost = if ($lanIp) { $lanIp } else { $dockerIp }

$vars = @{}
npx supabase status -o env | ForEach-Object {
  if ($_ -match '^([A-Z_]+)="?(.*?)"?$') { $vars[$Matches[1]] = $Matches[2] }
}
function Fix([string]$v) { return $v.Replace("127.0.0.1", $dockerIp).Replace("localhost", $dockerIp) }
$apiUrl = Fix $vars["API_URL"]; $studioUrl = Fix $vars["STUDIO_URL"]; $dbUrl = Fix $vars["DB_URL"]
$anon = if ($vars["ANON_KEY"]) { $vars["ANON_KEY"] } else { $vars["PUBLISHABLE_KEY"] }
$service = if ($vars["SERVICE_ROLE_KEY"]) { $vars["SERVICE_ROLE_KEY"] } else { $vars["SECRET_KEY"] }
$origins = '"http://localhost:8081"'
if ($lanIp) { $origins += ", `"http://${lanIp}:8081`"" }

New-Item -ItemType Directory -Force "$root/backend/src/Handyman.Api", "$root/frontend" | Out-Null
@"
{
  "ConnectionStrings": { "Default": "Host=$dockerIp;Port=54322;Database=postgres;Username=postgres;Password=postgres" },
  "Supabase": { "Url": "$apiUrl", "ServiceRoleKey": "$service" },
  "Seed": { "Enabled": true },
  "Cors": { "Origins": [$origins] }
}
"@ | Set-Content -Encoding utf8 "$root/backend/src/Handyman.Api/appsettings.Local.json"

@"
EXPO_PUBLIC_SUPABASE_URL=http://${supaHost}:54321
EXPO_PUBLIC_SUPABASE_ANON_KEY=$anon
EXPO_PUBLIC_API_URL=http://${appHost}:5080/api
"@ | Set-Content -Encoding utf8 "$root/frontend/.env.local"

(Get-Content -Raw "$root/docs/local-credentials.example.md").
  Replace("{{API_URL}}", $apiUrl).Replace("{{STUDIO_URL}}", $studioUrl).Replace("{{DB_URL}}", $dbUrl).
  Replace("{{ANON_KEY}}", $anon).Replace("{{SERVICE_ROLE_KEY}}", $service).Replace("{{DOCKER_IP}}", $dockerIp) |
  Set-Content -Encoding utf8 "$root/docs/local-credentials.md"

Write-Host "Docker/Supabase host: $dockerIp; app host: $appHost"
Write-Host "Wrote appsettings.Local.json, frontend/.env.local and docs/local-credentials.md"
