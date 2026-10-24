#!/usr/bin/env bash
# Builds the VMX runtime for the browser: build/vxrt.wasm (runtime/vxrt.c + core/surf.c + vxwasm.c + libc.c), for
# the VMX simulator (POCKET ARCADE vmx/, copied there by its tools/vmx-sync.sh). Needs clang 18 + wasm-ld
# (~/xboxdev/env.sh has them). setjmp/longjmp become WebAssembly exceptions (every current browser has them).
set -euo pipefail
cd "$(dirname "$0")/../.."
[ -f ~/xboxdev/env.sh ] && . ~/xboxdev/env.sh >/dev/null 2>&1 || true
CC=${CC:-clang-18}; LD=${WASM_LD:-wasm-ld-18}
command -v "$CC" >/dev/null || CC=clang; command -v "$LD" >/dev/null || LD=wasm-ld
OUT=build/wasm; mkdir -p "$OUT"
FLAGS="--target=wasm32 -O2 -nostdlib -ffreestanding -isystem runtime/wasm/include -mexception-handling -mllvm -wasm-enable-sjlj -Wall -Wno-unused-function"
for f in runtime/vxrt.c core/surf.c runtime/wasm/vxwasm.c runtime/wasm/libc.c; do
  $CC $FLAGS -c "$f" -o "$OUT/$(basename "${f%.c}").o"
done
$LD --no-entry --export-memory --strip-all -z stack-size=262144 --initial-memory=4194304 -o build/vxrt.wasm "$OUT"/*.o
echo "build/vxrt.wasm: $(stat -c %s build/vxrt.wasm) bytes"
