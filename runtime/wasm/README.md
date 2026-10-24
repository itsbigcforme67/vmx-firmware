# The VMX runtime in the browser

`build.sh` compiles `runtime/vxrt.c` + `core/surf.c` + `vxwasm.c` (the host: a GAME.VXP's CODE and FILE chunks,
the GAME ERROR screen, as `core/game_vx.c`) + `libc.c` (the few libc calls, no libc on wasm32) into
`build/vxrt.wasm` (~56 KB). `vxrt.js` loads it and runs a GAME.VXP: see its header. clang 18 lowers setjmp/longjmp
to WebAssembly exceptions; sin/cos/atan2/pow/fmod come from JavaScript's Math.

Used by the VMX simulator (`POCKET ARCADE/pocket-arcade/vmx/`, copied by its `tools/vmx-sync.sh`): FOLD ROOMS
there is `build/games/FOLDROOM.VXP`. Check: `tools/parity/wasm_check.sh` (every GAME.VXP against build/vxrun, FOLD
ROOMS on every level against build/fold_run --vxi); `tools/parity/wasm_run.mjs` is the one-game runner.
