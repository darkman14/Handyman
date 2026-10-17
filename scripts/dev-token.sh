#!/usr/bin/env bash
# Prints a Supabase access token for a seeded local user (test code 123456).
# Usage: ./scripts/dev-token.sh +381600000201
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
set -a; source "$root/frontend/.env.local"; set +a
phone="${1:?phone number, e.g. +381600000201}"
url="$EXPO_PUBLIC_SUPABASE_URL/auth/v1"
key="$EXPO_PUBLIC_SUPABASE_ANON_KEY"

curl -fsS -X POST "$url/otp" -H "apikey: $key" -H "Content-Type: application/json" \
  -d "{\"phone\":\"$phone\"}" > /dev/null
curl -fsS -X POST "$url/verify" -H "apikey: $key" -H "Content-Type: application/json" \
  -d "{\"phone\":\"$phone\",\"token\":\"123456\",\"type\":\"sms\"}" |
  node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>console.log(JSON.parse(s).access_token))'
