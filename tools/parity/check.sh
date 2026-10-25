#!/usr/bin/env bash
# Parity: the C# port in the VMX runtime against its JS core, frame by frame, demo and random presses, a few seeds.
#   tools/parity/check.sh blox [TICKS]        CHIPS=N: casino games start with N chips in the bank (default 100)
# A game ported from the firmware's C (games/NAME/parity_c names its harness, e.g. build/fold_run) is checked against
# that instead of a JS core, with the seed as the press pattern too (a C game may not use its seed).
set -u; cd "$(dirname "$0")/../.."
G=$1; N=${2:-3000}; NODE=${NODE:-$HOME/.local/node/bin/node}; fail=0
EXTRA=$(cat games/$G/parity_args 2>/dev/null || true)   # e.g. "--file zork1.z3=games/zork/zork1.z3"
REF=$(cat games/$G/parity_c 2>/dev/null || true)
for seed in 1 7 12345; do for demo in 0 1; do
  if [ -n "$REF" ]; then
    diff <(build/vxrun build/$G.vxi $seed $demo $N $seed $EXTRA 2>/dev/null) <($REF $seed $demo $N $seed) > /tmp/parity_$$.txt
  else
    diff <(build/vxrun build/$G.vxi $seed $demo $N 5 --chips ${CHIPS:-100} $EXTRA 2>/dev/null) <(CHIPS=${CHIPS:-100} $NODE tools/parity/js_run.mjs $G $seed $demo $N 5) > /tmp/parity_$$.txt
  fi
  if [ -s /tmp/parity_$$.txt ]; then echo "$G seed $seed demo $demo: DIFFERS from tick $(head -2 /tmp/parity_$$.txt | tail -1 | cut -c3- | cut -d' ' -f1)"; fail=1
  else echo "$G seed $seed demo $demo: same for $N ticks"; fi
done; done
if [ "$G" = fold ]; then   # random presses start on level 1 (no save): play every other level too, from a save
  L=1; NL=$(python3 -c "import struct;print(struct.unpack('<H',open('games/fold/levels.bin','rb').read(2))[0])")
  while [ $L -lt $NL ]; do
    if ! cmp -s <(CLEARED=$L build/fold_run --vxi build/fold.vxi games/fold/levels.bin $L 0 $N $L 2>&1) <(CLEARED=$L build/fold_run $L 0 $N $L); then
      echo "fold level $((L + 1)) random presses: DIFFERS"; fail=1; fi
    L=$((L + 1))
  done
  [ $fail = 0 ] && echo "fold levels 2-$NL random presses: same for $N ticks"
  # levels made in FOLD BUILDER (DATA\LEVELS.BIN, VMX World): the list, two made levels and one too big for the heap,
  # from a save with every built-in level cleared; random presses must never stop the game (no C to compare with)
  python3 - <<'EOF'
import struct
L = [open("../microgames/fold/levels/" + f, "rb").read() for f in ("03-cube.fold", "11-steps.fold")]
big = ["fold 1", "name TEST"] + ["square %d %d 0  1 0 0  0 1 0" % (i % 8, i // 8) for i in range(64)]
for i in range(64): big += ["room %d f" % i, "########"] + ["#..%s...#" % ("p" if i == 0 and r == 0 else ".") for r in range(6)] + ["########"]
L.append(("\n".join(big) + "\n").encode())
off = [2 + 4 * (len(L) + 1)]
for t in L: off.append(off[-1] + len(t))
open("build/fold_made.bin", "wb").write(struct.pack("<H", len(L)) + b"".join(struct.pack("<I", o) for o in off) + b"".join(L))
open("build/fold_save15.bin", "wb").write(struct.pack("<I", 15))
# a walk through the list (up 1 right 8 down 2 left 4 A 16 B 32): play MADE 01, B held back to the list, MADE 02 the
# same, MADE 03 (too big: the message, A away), up to ROOMS and play that
P = []
def tap(m, gap=4): P.extend([m] + [0] * gap)
def walk():
    for i in range(24): tap([1, 8, 2, 4][i % 4], 6)
tap(16, 30); walk(); P.extend([32] * 70 + [0] * 5)
tap(2); tap(16, 30); walk(); P.extend([32] * 70 + [0] * 5)
tap(2); tap(16, 10); tap(16, 10); tap(1); tap(1); tap(1); tap(16, 30); walk()
open("build/fold_walk.txt", "w").write("\n".join(map(str, P)) + "\n")
EOF
  if ! PRESSES=build/fold_walk.txt build/vxrun build/fold.vxi 1 0 1200 1 --file levels=games/fold/levels.bin --data LEVELS.BIN=build/fold_made.bin --save build/fold_save15.bin > /dev/null 2> /tmp/parity_$$.txt; then
    echo "fold made levels, the list walked: $(tail -1 /tmp/parity_$$.txt)"; fail=1
  else echo "fold made levels, the list walked: no game error"; fi
  for seed in 1 7 12345; do
    if ! build/vxrun build/fold.vxi $seed 0 $N $seed --file levels=games/fold/levels.bin --data LEVELS.BIN=build/fold_made.bin --save build/fold_save15.bin > /dev/null 2> /tmp/parity_$$.txt; then
      echo "fold made levels seed $seed: $(tail -1 /tmp/parity_$$.txt)"; fail=1
    else echo "fold made levels seed $seed: $N ticks of random presses, no game error"; fi
  done
fi
rm -f /tmp/parity_$$.txt; exit $fail
