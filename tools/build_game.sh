#!/usr/bin/env bash
# Builds a VMX game: C# 1.0 -> .dll (csc, against the VMX libraries) -> .vxi (sdk/vxlink) -> GAME.VXP (tools/vxp.py).
#   tools/build_game.sh blox        -> build/games/BLOX.VXP
set -euo pipefail
cd "$(dirname "$0")/.."
G=$1; export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
D=${DOTNET:-$HOME/tools/dotnet/dotnet}; CSC="$D $(ls -d $HOME/tools/dotnet/sdk/*/Roslyn/bincore/csc.dll | tail -1)"
mkdir -p build/sdk build/games
if [ ! -f build/sdk/mscorlib.dll ] || [ sdk/corlib/corlib.cs -nt build/sdk/mscorlib.dll ] || [ sdk/vmx/vmx.cs -nt build/sdk/Microsoft.Vmx.dll ]; then
  $CSC -nologo -nostdlib -noconfig -runtimemetadataversion:v4.0.30319 -langversion:ISO-1 -target:library -out:build/sdk/mscorlib.dll sdk/corlib/corlib.cs
  $CSC -nologo -nostdlib -noconfig -langversion:ISO-1 -target:library -r:build/sdk/mscorlib.dll -out:build/sdk/Microsoft.Vmx.dll sdk/vmx/vmx.cs
fi
if [ ! -f build/vxlink/vxlink.dll ] || [ sdk/vxlink/Program.cs -nt build/vxlink/vxlink.dll ]; then $D build sdk/vxlink -c Release -o build/vxlink -v q >/dev/null; fi
$CSC -nologo -nostdlib -noconfig -optimize+ -langversion:ISO-1 -target:library -r:build/sdk/mscorlib.dll -r:build/sdk/Microsoft.Vmx.dll \
  -out:build/$G.dll games/$G/*.cs
$D build/vxlink/vxlink.dll build/$G.vxi build/sdk/mscorlib.dll build/sdk/Microsoft.Vmx.dll build/$G.dll
ini() { grep "^$1=" games/$G/game.ini | cut -d= -f2-; }
FILES=(); while read -r f; do [ -n "$f" ] && FILES+=(--file "$f"); done < <(grep '^file=' games/$G/game.ini | cut -d= -f2- || true)
T=$(ini title)
python3 tools/vxp.py pack "build/games/$(echo "$T" | tr -d ' ' | cut -c1-8).VXP" --id "$(ini id)" --title "$T" --lines "$(ini lines)" \
  --publisher "$(ini publisher)" --blurb "$(ini blurb)" --card games/$G/card.txt --code build/$G.vxi "${FILES[@]}"
