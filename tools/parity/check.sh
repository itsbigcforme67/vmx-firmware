#!/usr/bin/env bash
# Parity: the C# port in the VMX runtime against its JS core, frame by frame, demo and random presses, a few seeds.
#   tools/parity/check.sh blox [TICKS]        CHIPS=N: casino games start with N chips in the bank (default 100)
set -u; cd "$(dirname "$0")/../.."
G=$1; N=${2:-3000}; NODE=${NODE:-$HOME/.local/node/bin/node}; fail=0
EXTRA=$(cat games/$G/parity_args 2>/dev/null || true)   # e.g. "--file zork1.z3=games/zork/zork1.z3"
for seed in 1 7 12345; do for demo in 0 1; do
  diff <(build/vxrun build/$G.vxi $seed $demo $N 5 --chips ${CHIPS:-100} $EXTRA 2>/dev/null) <(CHIPS=${CHIPS:-100} $NODE tools/parity/js_run.mjs $G $seed $demo $N 5) > /tmp/parity_$$.txt
  if [ -s /tmp/parity_$$.txt ]; then echo "$G seed $seed demo $demo: DIFFERS from tick $(head -2 /tmp/parity_$$.txt | tail -1 | cut -c3- | cut -d' ' -f1)"; fail=1
  else echo "$G seed $seed demo $demo: same for $N ticks"; fi
done; done; rm -f /tmp/parity_$$.txt; exit $fail
