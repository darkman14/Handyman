# Prints a Supabase access token for a seeded local user (test code 123456).
# Usage: ./scripts/dev-token.ps1 +381600000201
param([Parameter(Mandatory = $true)][string]$Phone)
$ErrorActionPreference = "Stop"
$envFile = Join-Path $PSScriptRoot "../frontend/.env.local"
$cfg = @{}
Get-Content $envFile | ForEach-Object { if ($_ -match '^([A-Z_]+)=(.*)$') { $cfg[$Matches[1]] = $Matches[2] } }
$url = "$($cfg["EXPO_PUBLIC_SUPABASE_URL"])/auth/v1"
$headers = @{ apikey = $cfg["EXPO_PUBLIC_SUPABASE_ANON_KEY"] }

Invoke-RestMethod -Method Post -Uri "$url/otp" -Headers $headers -ContentType "application/json" `
  -Body (@{ phone = $Phone } | ConvertTo-Json) | Out-Null
$res = Invoke-RestMethod -Method Post -Uri "$url/verify" -Headers $headers -ContentType "application/json" `
  -Body (@{ phone = $Phone; token = "123456"; type = "sms" } | ConvertTo-Json)
$res.access_token
