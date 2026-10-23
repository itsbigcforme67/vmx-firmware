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

## 2026-10-21: JUNCTION (Digidrive-style)
`games/junction/` - JS parity for 30000 ticks; in `make test`; built into the firmware (`tools/embed_vxp.py`), but the
OTA upload failed: vmx.local was off the network. To flash: `cd esp32 && pio run -e ota -t upload`, or push only the
game: `tools/push_game.sh build/games/JUNCTION.VXP`. Worst tick ~180k IL (Auto Bahn spiral) = ~25-50 ms on the device.

## 2026-10-22: 9-BALL (pool)
`games/pool/` - 9-ball vs the computer with a low 3D wireframe shooting view (decisions in microgames/docs/GAMES_LOG.md
"9-BALL"). All-integer physics, so JS parity holds for 30000 ticks (3 seeds, demo + random presses); in `make test`.
~7-16k IL a tick, worst ~300k. Built into the firmware (`tools/embed_vxp.py` + `pio run` OK) but NOT on the rig yet:
vmx.local was off the network. To put it on: `tools/push_game.sh build/games/9-BALL.VXP`, or reflash
`cd esp32 && pio run -e ota -t upload`.

## 2026-10-22: VMX CASINO chip bank
`Microsoft.Vmx.Chips` (Balance / Bet / Pay / Bonus) for games with GAME.VXP flags bit 3 (`casino=1` in game.ini).
The firmware keeps the bank in save `casino` (core/game_vx.c: 100 to start, daily top-up to 25, demo = scratch 100);
CALL sends it as record 56581F00 (score = chips, f1 = peak, f2 = cashed). BLACKJACK and 9-BALL use it; parity
passes at the default 100 and with `CHIPS=15` (going broke). vxrun takes `--chips N`. Firmware builds; not on the rig
(offline). Design: vmx-world/docs/CASINO.md.

## 2026-10-22 later: VIDEO POKER, ROULETTE, SLOTS
`games/poker`, `games/roulette`, `games/slots` (casino=1): JS parity for 30000 ticks and with CHIPS=15; in `make test`.
Limits raised for 14+ games: MAXVX 20 (game_vx.c), VMX_GAMES 24, records/art/CALL arrays 24 (an older 16-entry
"recs" save loads into the front). Host check: GAMES shows 15 cards. 14 built-in VXPs: flash 27%, RAM 42%; firmware
builds; rig offline, so not flashed.

## 2026-10-22 end: 13 more casino tables (27 managed games)
hilo war baccarat threecard reddog letitride craps sicbo chuckaluck keno bigsix scratch frogs: each has JS parity for
30000 ticks (and CHIPS=15); all in `make test`. Limits raised to MAXVX 32 / 36 records. corlib: String.Concat(params
string[]/object[]) (C# turns a + b + c + d + e into one array call). VXP file names are now letters and digits only
(build_game.sh). Owner: not every game needs to be on the VMX at once; the firmware still embeds them all for tests.

## 2026-10-23: the VMX as a USB memory unit (owner: "let's get usb transfers working with VMX")
The VMX's storage is now an Xbox memory unit's volume, and the CoreS3's USB-C is that memory unit:
- `core/fatx.c` (hardware-free FATX, as the Xbox kernel and nxdk's nxFormatVolume lay it out: 4 KB superblock, 16-bit
  cluster table under 65520 clusters, 16 KB clusters) and `core/vmxvol.c` (the VMX's layout on it: `56580001\TitleMeta.xbx`,
  `56580001\<ID>0000\GAME.VXP` + `SaveMeta.xbx` per installed game, `56580001\VMX\<key>.BIN` for every plat_save key).
  The root of a memory unit holds title folders, as UDATA does on the hard disk.
- `build/vmxmu IMG format|ls|tree|get|put|rm|install|test` (host/vmxmu.c): the volume on the PC (an image, or the
  VMX itself as /dev/sdX if you may open it). `make test` runs its self-test too (8 MB volume, nested folders, a
  folder past 256 entries, a full volume, re-mounting, the VMX layout).
- Checked against the real Xbox kernel's FATX driver: vmx-world `tools/kernel_fatx_test.sh` puts a volume we made on
  xemu's hard-disk partition 3, the kernel lists and reads all of it (sizes and checksums match), then writes, replaces
  and deletes files, and we read those back. PASS.
- Firmware: `core/ui.c` has the MEMORY UNIT screen (vmx_link: CONNECTED, DON'T UNPLUG while the host reads/writes,
  B: EJECT). The PC build (`build/vmxhost`) keeps everything in `outdir/vmx.mu` and plays the games installed there
  (script line `link on|off`).
- CoreS3 (`esp32/src/mu.cpp`): the volume lives in the old LittleFS partition ("spiffs", 3.4 MB: about what the real
  VMX's 8 MB flash leaves after the firmware), 512-byte sectors over a one-block 4 KB write-back cache. TinyUSB mass
  storage (USB-OTG mode; `ARDUINO_USB_MODE=0`, no USB serial any more: MSC only, the safest for the Xbox) presents it.
  When a USB host configures the VMX (an Xbox through a controller adapter, or a PC) the VMX becomes its memory unit
  and shows MEMORY UNIT; when the host ejects it, the cable comes out or B is pressed it lets go, and restarts if the
  host wrote anything (so games, records and settings are read again). First start: LittleFS's games and saves and
  the NVS records are moved onto the new volume. Uploads over Wi-Fi (`tools/push_game.sh`) install onto the volume;
  DELETE is now `/game?id=56581001`.
- Builds (flash 33%, RAM 53%) but NOT on the rig: vmx.local was offline. To put it on: `cd esp32 && pio run -e ota -t
  upload`. USB flashing now needs download mode (hold the CoreS3's reset ~3 s) since USB is the memory unit.
- Not tested yet: a real Xbox reading the rig (needs an Xbox controller-port-to-USB adapter, or the VMX's own board),
  and a PC mounting it (Linux sees an 3.4 MB disk with an unknown file system; build/vmxmu reads it).
- Flash wear: no wear levelling yet (the cluster table's 4 KB block is rewritten on every file change; NOR flash
  takes ~100k erases). Fine for testing; a real VMX wants a wear-levelling layer under the volume.

## 2026-10-23 later: saves laid out as the dashboard shows them (owner's model)
Dashboard: "VMX World"; inside it one save per VMX game (`<ID>0000\`: SaveMeta.xbx, GAME.VXP if installed, SAVE.BIN
= the game's own save, RECORD.BIN = its record), and one shared save "VMX CASINO CHIPS" (`56581F000000\CHIPS.BIN`)
written once for every casino game. Settings are a loose `56580001\SETTINGS.BIN`, `VMX.SYS` marks a VMX. Keys
(core/vmxvol.h vol_key_path): settings, casino, g<ID> (FOLD ROOMS' "fold" is now g56581010), r<ID>; the old single
"recs" save is split into r<ID> files on first start. plat_save(key, NULL, 0) deletes. Save folders get their
SaveMeta.xbx name from the games' titles (vol_name, from vmx_init). World uses the same mapping on the hard disk.
Self-test covers it; the host run, World (PC + xemu memory unit) agree. Applets: World content in
E:\TDATA\56580001\$c\5658000156581010\ (vmx-world DESIGN.md). Not made yet: SaveImage.xbx / TitleImage.xbx icons.
