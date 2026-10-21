# VMX firmware: status

## 2026-10-20: first version on the CoreS3

Owner: "put the vmx firmware on my stack core s3 with faces gamepad." There was no C firmware yet (only the web
simulator), so this is the port:
- Boot, main menu, GAMES roulette (A play, Down demo), SAVES (+ erase), SETTINGS (sound, brightness, about),
  CALL (DTMF dial + Bell 202 x3 through the speaker, check code, marks scores sent), attract demos after 30 s idle,
  side button tap/hold. Same timings and sounds as the simulator.
- Games: only FOLD ROOMS so far (the only game with a C core). The roulette shows the one card 5 times until more
  games are ported. The other simulator games are JavaScript and need porting to C (next).
- Flashed to the CoreS3 on /dev/ttyACM0 (MAC 80:45:6b:4d:45:64): boots, Gamepad3 found, 8 MB PSRAM. Main menu and
  FOLD ROOMS checked over the serial hook. NOT checked by me: how it looks on the panel and how loud the beeper is
  (speaker volume 140/255 in main.cpp; note amplitudes in core/sound.c AMP).
- PC test build: `make` + `build/vmxhost`; boot, menus, FOLD demo clear and the CALL list checked by screenshot.

Next: port the JS game cores to C (BLOX, BLOBS, INTERCEPT, INVASION, BLACKJACK, CHECKERS, REVERSI, TRICKY WICKS,
ZORK I's Z-machine), each checked frame for frame against its JS core; the VMX as a USB memory unit later.

## 2026-10-20 later: managed games (.NET-style runtime) + Wi-Fi updates

Owner: VMX games are managed code on a small Microsoft .NET runtime (vmx-world/docs/DESIGN.md "The program").
- `sdk/corlib` (our mscorlib), `sdk/vmx` (Microsoft.Vmx: Game, Screen, Buttons, Beeper, Scores, Storage),
  `sdk/vxlink` (the linker: checks the C# 1.0 subset, writes a .vxi image), `runtime/vxrt.c` (IL interpreter,
  128 KB mark-sweep heap, natives), `tools/vxp.py` (GAME.VXP), `tools/build_game.sh NAME` (all of it).
  .NET 8 SDK lives in ~/tools/dotnet (csc runs with -langversion:ISO-1 -nostdlib).
- BLOX ported to C# (`games/blox/`): `tools/parity/check.sh blox 6000` = same screen as the JS core every tick,
  3 seeds x demo/random presses, through GCs. About 5,700 IL instructions a tick.
- Firmware: managed games come from GAME.VXP (`core/game_vx.c`); built in via `tools/embed_vxp.py`, or uploaded.
  FOLD ROOMS stays built-in C. Unsupported for now: structs, try/catch/finally (and foreach over collections),
  delegates, generics (C# 1.0 anyway), multi-dimensional arrays.
- Wi-Fi on the CoreS3: it was already on the owner's network (credentials left in the chip by older firmware):
  192.168.1.177 / vmx.local. SETTINGS > WIFI shows status; A opens hotspot "VMX-SETUP" (WiFiManager) to change
  network. Firmware: `cd esp32 && pio run -e ota -t upload` (tested). Games: `tools/push_game.sh FILE.VXP`
  (tested; it restarts with the game). Password in esp32/ota_secret.ini (gitignored).

## 2026-10-21: all the simulator's games on the VMX runtime

Ported to C# by parallel helpers, each checked frame for frame against its JS core (`make test` runs all 9):
BLOX, BLOBS, INTERCEPT, INVASION, BLACKJACK AND HOOKERS, CHECKERS, REVERSI, TRICKY WICKS (from the owner's C core in
the v1.5 zip), ZORK I (Z-machine; story packed as a FILE chunk, SAVE to Storage, 16 KB). Not ported: STAR RAIDERS
(needs the player's cart + a 6502; rights). FOLD ROOMS stays built-in C. All 9 + FOLD are built into the CoreS3
firmware (`tools/embed_vxp.py`), playable now.
- Runtime additions: 128 KB heap (owner), `Microsoft.Vmx.Package` (packed data files), saves up to 16 KB (CoreS3:
  LittleFS /saves), fast paths in the interpreter (int and 64-bit ops, locals, fields, arrays, branches), the hot
  loop in IRAM, interpreter state + bytecode in internal RAM. Per-game parity args: games/NAME/parity_args.
- Speed on the ESP32-S3 (device stats, `tools/drive.py --wifi "i"`): 3.4-7 M IL instructions a second for most
  games (BLOX/TRICKY/BLACKJACK look faster because their ticks are light). Earlier "18 M/s" was BLOX-only and
  misleading. The helpers sized the CPU players' per-tick slices for ~400k IL, so on the device a CPU turn in
  CHECKERS / REVERSI has ticks up to ~130 ms (a few dropped frames), and BLOBS's DEMO planner up to ~0.7 s once per
  pair. Normal play is light everywhere. Memory placement (internal vs PSRAM) made no difference; the cost is the
  interpreter itself (~70 cycles an IL op).
- Next for speed (not done): translate IL at load into a compact pre-decoded form (resolved operands, 32-bit slots,
  computed-goto dispatch); expected 2-3x. Then lower the CPU players' slices.
- Known JS bug kept for parity: INTERCEPT's demo() sort is commented out (microgames/ui/games/intercept_core.js).
- Remote testing: the VMX's web server has `/hook` (button presses, screen, stats, 'm' memory, 'P' profiler when
  built with -DVX_PROFILE); `tools/drive.py --wifi`. USB serial was lost on 2026-10-21 (cable?); Wi-Fi works.
