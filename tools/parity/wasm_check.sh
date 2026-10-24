#!/usr/bin/env bash
# The browser build of the runtime (build/vxrt.wasm, runtime/wasm/build.sh) against the PC one: every GAME.VXP in
# build/games through tools/parity/wasm_run.mjs and build/vxrun (its CODE and FILE chunks), a play and a demo;
# FOLD ROOMS also against build/fold_run --vxi on every level (CLEARED saves), play and demo.
#   tools/parity/wasm_check.sh [TICKS]
set -uo pipefail
cd "$(dirname "$0")/../.."
export PATH="$HOME/.local/node/bin:$PATH"
N=${1:-900}; T=$(mktemp -d); trap 'rm -rf "$T"' EXIT; fail=0
for v in build/games/*.VXP; do
  b=$(basename "$v" .VXP)
  args=$(python3 - "$v" "$T/$b" <<'PY'
import sys, struct
d = open(sys.argv[1], 'rb').read(); o = 32; tot = struct.unpack('<I', d[12:16])[0]; out = []
while o + 8 <= tot:
    t = d[o:o+4]; n = struct.unpack('<I', d[o+4:o+8])[0]; p = d[o+8:o+8+n]
    if t == b'CODE': open(sys.argv[2] + '.vxi', 'wb').write(p)
    if t == b'FILE':
        nm = p[1:1+p[0]].decode(); fn = sys.argv[2] + '.' + nm; open(fn, 'wb').write(p[1+p[0]:]); out.append('--file %s=%s' % (nm, fn))
    o += 8 + ((n + 3) & ~3)
print(' '.join(out))
PY
)
  [ -f "$T/$b.vxi" ] || { echo "$b: no CODE (native), skipped"; continue; }
  r=""
  for sd in "11 0" "5 1"; do
    # shellcheck disable=SC2086
    if cmp -s <(node tools/parity/wasm_run.mjs "$v" $sd "$N" 4 2>&1) <(build/vxrun "$T/$b.vxi" $sd "$N" 4 $args 2>/dev/null); then r="$r same"; else r="$r DIFFERS"; fail=1; fi
  done
  echo "$b:$r"
done
if [ -f build/games/FOLDROOM.VXP ] && [ -x build/fold_run ]; then
  NL=$(python3 -c "import struct;print(struct.unpack('<H',open('games/fold/levels.bin','rb').read(2))[0])"); bad=0
  for ((L = 0; L < NL; L++)); do for D in 0 1; do
    if [ $L = 0 ]; then E=(); else E=(CLEARED=$L); fi
    cmp -s <(env "${E[@]}" node tools/parity/wasm_run.mjs build/games/FOLDROOM.VXP $((L * 7 + 3)) $D "$N" $((L + 5)) 2>&1) \
           <(env "${E[@]}" build/fold_run --vxi build/fold.vxi games/fold/levels.bin $((L * 7 + 3)) $D "$N" $((L + 5)) 2>&1) \
      || { echo "FOLD ROOMS level $((L + 1)) demo $D: DIFFERS"; bad=1; fail=1; }
  done; done
  [ $bad = 0 ] && echo "FOLD ROOMS levels 1-$NL, play and demo: same as fold_run --vxi for $N ticks"
fi
exit $fail
