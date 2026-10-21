# VMX firmware

The VMX's firmware in C: the boot (goo, the X, the chime), the main menu (GAMES / SAVES / SETTINGS / CALL), saves,
settings, the phone score call, and the games. It follows the web simulator (POCKET ARCADE `vmx/vmx.js`, branch
`vmx-call`) screen for screen.

- `core/`: the firmware, no hardware code (`vmxfw.h` is the API). Games: FOLD ROOMS (from `../microgames/fold`).
- `host/` + `Makefile`: the PC test build. `make`, then `build/vmxhost -o OUTDIR < script` writes screenshots and a WAV.
- `esp32/`: the M5Stack CoreS3 + Faces Bottom3 + Gamepad3 test rig (the VMX's chip, ESP32-S3).
  `cd esp32 && pio run -t upload --upload-port /dev/ttyACM0`
- Games: `tools/build_game.sh NAME` (C# in games/NAME -> build/games/NAME.VXP), `tools/parity/check.sh NAME` (same screens
  as the JS core), `python3 tools/embed_vxp.py A.VXP B.VXP ...` (the firmware's built-in games; run before building esp32/).
- `tools/embed_levels.py` builds FOLD's levels into the firmware; `tools/drive.py` presses buttons and prints the
  screen over USB serial.

## Controls on the CoreS3

| Gamepad3 | VMX |
|---|---|
| D-pad, A, B | D-pad, A, B |
| SELECT or START | the side button: tap = back, hold 0.7 s = power off/on (replays the boot) |

Without the pad: touch the left edge for the D-pad, right top = B, right bottom = A, the top strip = side button.
FOLD ROOMS: D-pad moves, A talks to a tile, B undoes (hold = restart), side button = back to GAMES.
