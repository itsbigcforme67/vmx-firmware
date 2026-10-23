#!/usr/bin/env bash
# Puts a game on the VMX (CoreS3 rig) over Wi-Fi; it restarts with the game in GAMES. No reflash needed.
#   tools/push_game.sh build/games/BLOX.VXP [host]          host defaults to vmx.local
#   tools/push_game.sh --delete build/games/BLOX.VXP [host]   (or the game's ID, e.g. 56581001)
# Games go onto the VMX's memory unit (56580001\<ID>0000\GAME.VXP), as the Xbox puts them there over USB.
set -euo pipefail
cd "$(dirname "$0")/.."
KEY=$(grep '^password' esp32/ota_secret.ini | cut -d= -f2 | tr -d ' ')
if [ "$1" = "--delete" ]; then
    ID=$2; [ -f "$ID" ] && ID=$(od -An -tx4 -j4 -N4 "$ID" | tr -d ' ')
    curl -fsS -X DELETE -H "X-VMX-Key: $KEY" "http://${3:-vmx.local}/game?id=$ID"; exit
fi
curl -fsS -H "X-VMX-Key: $KEY" -F "file=@$1;filename=$(basename "$1")" "http://${2:-vmx.local}/game"
