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
  64 KB mark-sweep heap, natives), `tools/vxp.py` (GAME.VXP), `tools/build_game.sh NAME` (all of it).
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
