#!/usr/bin/env bash
# Puts a game on the VMX (CoreS3 rig) over Wi-Fi; it restarts with the game in GAMES. No reflash needed.
#   tools/push_game.sh build/games/BLOX.VXP [host]          host defaults to vmx.local
#   tools/push_game.sh --delete BLOX.VXP [host]
set -euo pipefail
cd "$(dirname "$0")/.."
KEY=$(grep '^password' esp32/ota_secret.ini | cut -d= -f2 | tr -d ' ')
if [ "$1" = "--delete" ]; then curl -fsS -X DELETE -H "X-VMX-Key: $KEY" "http://${3:-vmx.local}/game?name=$2"; exit; fi
curl -fsS -H "X-VMX-Key: $KEY" -F "file=@$1;filename=$(basename "$1")" "http://${2:-vmx.local}/game"
