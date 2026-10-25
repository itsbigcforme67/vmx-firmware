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

## 2026-10-24: won/lost records for managed games
game.ini `wld=1` (GAME.VXP flags bit 5): every `Scores.Report` is the game's lifetime won/lost/drawn, taken into the
SAVES record as is (CHECKERS, REVERSI). `wld=2` (bits 5 + 6): a won/lost game that reports one match's tally
`(you, you, CPU)` from 0 each match, once per game; each report is one game, lost when the second field went up by
one or it is a new match's 0-1, else won, added to the lifetime WON/LOST (9-BALL; the simulator's games.js "pool"
record does the same). SAVES shows WON/LOST/DRAWN (wld=2: WON/LOST). `readVxp` (runtime/wasm/vxrt.js) gives `wld`.
Checked on `build/vmxhost` with a throwaway test game (five reports -> WON 3 LOST 2, again -> 6/4; wld=1 takes the
last report) and CHECKERS/9-BALL's SAVES pages; parity (checkers, reversi, pool) and wasm_check unchanged.

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

## 2026-10-23 later: telling VMXs apart (owner: serial number; up to 8 plugged in at once)
- `56580001\VMX.SYS` (VmxSys, 20 bytes): "VMX1", serial, the day it was first set up, the owner's name. On the CoreS3
  the serial comes from the chip's factory MAC (FNV hash to 32 bits), so formatting keeps it; elsewhere (PC build,
  World's hard-disk VMX) the firmware makes one at first start (an older VMX keeps its SETTINGS serial). Shown as
  `1A2B-3C4D` in SETTINGS > ABOUT; CALL sends it with the owner's name.
- SETTINGS > NAME (new; SETTINGS has 5 rows now): 8 letters, up/down the letter, left/right which one, A keeps it.
- The casino stash carries the serial (VmxBank.serial): a stash copied onto another VMX through the dashboard doesn't
  count there (that VMX starts its own 100). Not covered: restoring an old copy onto the same VMX (needs a counter
  the volume can't hold, e.g. in the chip's NVS).
- `build/vmxmu IMG id [NAME]` shows or sets who a VMX is; `install` makes a plain memory unit a VMX (new serial).
- VMX World handles up to 8 (vmx-world xbox/vmxlink.c): listed by controller and slot, X opens CHOOSE A VMX, and
  downloads ask DOWNLOAD TO when there's more than one place.

## 2026-10-24: on the rig
Flashed over Wi-Fi (`pio run -e ota -t upload`): boots, all 27 games built in, the LittleFS storage moved onto the
memory-unit volume. The first flash wrote an empty RECORD.BIN (and its save folder) for every game while splitting
the old records: 2016 KB free. Fixed (records are only written when they hold something; empty ones and folders
left with only their SaveMeta.xbx are removed at start) and flashed again: 2976 KB free (186 blocks). Not tried yet:
plugging the rig's USB-C into a PC or an Xbox (MEMORY UNIT mode).

## 2026-10-24: FOLD ROOMS in C# (the last built-in C game)
`games/fold/`: FoldLevel.cs (fold_parse, with glibc's sscanf / atoi / strtol rules), FoldGame.cs (microgames/fold
core/fold.c: surface, moves, signals + logic, journal undo, what a tile says), FoldRooms.cs (core/game_fold.c + the
64x64 picture from app/vmx.c). `tools/build_game.sh fold` -> build/games/FOLDROOM.VXP (CODE + FILE "levels", no NATV).
Levels: `games/fold/levels.bin` (u16 count, offsets, the .fold texts), made by `python3 games/fold/levels.py` from
microgames/fold/levels; run it again after a level changes. Same save as before (g56581010, 4 bytes: levels cleared).
- Parity (`tools/parity/check.sh fold`, in `make test`): the C game is the reference (`tools/parity/fold_ref.c`, a frozen
  copy of core/game_fold.c that reads the same levels.bin, so it outlives the firmware's copy; checked identical to
  core/game_fold.c on every run below). `build/fold_run` runs it, or the C# image with `--vxi` (CLEARED=n: a save with n
  levels cleared; PRESSES=file: scripted buttons). Same every tick: 3 seeds x demo/random presses for 30000 ticks (the
  seed is also the press pattern, since FOLD ignores its seed), random presses on every level from a save, the demo
  through all 15 levels several times (40000 ticks), and scripted undo runs (45) with both journals cut to 4 entries.
- Speed: ~15k IL a tick on average (idle 12k; ~19k on the bigger levels), a move ~20-40k, worst ~650k when the
  24-square level loads or restarts (~0.1-0.2 s on the device, once). Heap: ~35 KB still free at that load.
- Needed special handling: the change journal is 4096 entries here (the C's 65536 = 256 KB won't fit the heap); a step
  logs nothing, a push 2, a room move up to ~35, so only very long undo chains differ (same drop-the-oldest rule as the
  C, checked with both cut to 4). Text in rooms/talk boxes is drawn with the FOLD renderer's font rule (unknown
  character = blank; Screen.Text would draw '?'). The cells are sized for the level (C: room for 64 squares); boxes off
  target are counted as they change and the signal loops skip levels with no buttons / driven tiles (same results).
  The screen is drawn straight onto Screen (talk boxes over the last frame, as the C does into its buffer).
- Limits: a level needs about 1 KB of heap a square plus its text: the 64-square levels the FOLD builder allows won't
  fit (an estimate, untested: up to ~50 should). Not done: room transitions (the Xbox playtest's slide / corner turn) and Xbox-made levels on
  the VMX; both are follow-ups.

## 2026-10-24: levels made in FOLD BUILDER play on the VMX
Owner's goal: any level built in the FOLD builder on the Xbox plays on the VMX (and in the simulator). How they get
there: VMX World packs the builder's saved levels (`user-*.fold` in its levels folder, name order) into FOLD ROOMS'
save folder, `56580001\565810100000\DATA\LEVELS.BIN` (games/fold/levels.py's layout), on the selected VMX or World's
own; it does this each time it looks at a VMX's games and writes only when they changed (vmx-world/xbox/hdd.c).
- `DATA\` is new: read-only files the Xbox gives a game (vmxvol key `d<ID>:<NAME>`). Games read them with
  `Storage.DataLength(name)` / `Storage.ReadData(name, offset, dst, index, count)` (sdk/vmx/vmx.cs), as Package. The
  runtime's VxHost has a last field `data` (NULL = no such files); game_vx.c reads the file whole on first use (64 KB
  at most, one buffer made once) and forgets it at the next start.
- FOLD ROOMS: with made levels it opens on a list, ROOMS NN (the built-in ones, from the next to clear) then MADE 01..
  (a dot = cleared; no names or anything else that could hint at a shape). Clearing one: A goes to the next made one,
  then the list; holding B 2 s (past the 0.7 s restart) goes back to the list. The save keeps the C's 4 bytes and adds
  a u32 FNV-1a hash of each made level cleared (so re-sent or reordered levels keep their dots). Without LEVELS.BIN
  the game is the C's exactly (parity untouched).
- Heap (measured with `HEAP=bytes build/vxrun ... --data LEVELS.BIN=... --save ...`, load + restart + back to the
  list): ~62 KB + ~1.1 KB a square + 2 bytes a byte of text. A plain 56-square level fits, 58 doesn't survive a
  restart, 64 doesn't load; text-heavy (31 signs + a 4000-step solution) runs out at 48. The rule is
  `1100 * squares + 2 * bytes <= 64000` (FoldRooms.FitsVmx = microgames/fold/core/vmxfit.h): the builder says
  "SAVED. TOO BIG FOR THE VMX" when a save fails it, World leaves such levels off ("FOLD: 1 LEVEL SENT, 1 TOO BIG FOR
  THE VMX") and FOLD ROOMS shows TOO BIG FOR THE VMX instead of running out of memory.
- Checked: builder (headless) saves 03-cube as user-001 and a 64-square level as user-002 -> World (PC build, a VMX
  image as build/vmx.mu) writes LEVELS.BIN with the one that fits -> `build/vmxhost` on that image: the list, MADE 01
  played through its solution to CLEARED!, its dot after. Hard-disk VMX writes the same file. `check.sh fold` (in
  make test) now also walks the list (PRESSES: both made levels, B back, the too-big one, ROOMS) and runs random
  presses with made levels, no game error allowed. vxrun gained `--data`, `--save`, `HEAP=` and `PRESSES=`.
- Not done: the VMX simulator (web) has no `data` hook yet, so it plays only the built-in levels; the World and
  builder XBEs build with these changes (nxdk) but weren't run in xemu or on the Xbox for this.

## 2026-10-24: FOLD ROOMS turns into the next room (the builder's transitions)
Owner: the VMX's FOLD ROOMS should have the Xbox builder's room transitions. Walking through a doorway now turns the
view over to the next room as the FOLD BUILDER playtest does (microgames/fold/app/app.c `room_anim_draw`): a flat join
slides, an outside corner tips away like a cube, an inside corner folds up towards you. 12 ticks, smoothstep, the eye
2.6 rooms away (the playtest's numbers); not on teleporters, undo or restart; moves aren't held up by it (as the
playtest). Only the edge you just crossed shows; pop-ups unchanged ("A ROOM FLIPPED", drawn over the turn).
Screenshots: `docs/fold_turns.png` (top: `build/vmxhost` in the demo, crossing up and back; below: flat left / up,
outside corner, inside corner left / up, from the C# image).
- How (FoldRooms.cs DoMove / Cells / Face): 64x64 1-bit, all integer. The two rooms are kept as 7x7 cell masks (one
  `CellCode` now decides each cell's sprites for both DrawRoom and the masks). The turn is about the screen axis
  across the move, so each screen line across it shows one picture line scaled: one long division a line, then runs of
  lit pixels a cell at a time (`Fill`). Sides turned away are dithered (Bayer, like the simulator's JS turn); the
  outline and crease are drawn; the HUD stays.
- Parity: the C reference has the same transition (`tools/parity/fold_ref.c` do_move / anim_*), built from the real
  `vmx_room` pixels every frame, so `check.sh fold` checks the turn picture, the C#'s cell masks and its redraw
  bookkeeping every tick. All checks same: 3 seeds x demo/random for 30000 ticks, the demo through all 15 levels
  (40000), random presses on every level (20000 on 4, 10, 15; 7-184 turns each); `make test` passes.
- Cost: ~45-65k IL a tick while a turn runs (~15k idle); the tick you cross ~70-115k, of which the move itself is
  40-100k on the big levels (it was already). Room for more: skipping empty cells per line (~10k).
- Web simulator: its old JS FOLD ROOMS (`microgames/ui/games/foldrooms_core.js` roomTurn) already had a VMX-sized
  turn (same 3 kinds, 12 ticks, dithered and outlined); this one follows the Xbox playtest's geometry instead (its
  eye is 2 rooms away, quadratic ease). The simulator gets this version when it runs the C# game (a sibling session is
  on that).
- Not done: on the rig (vmx.local offline here); `cd esp32 && pio run -e ota -t upload` or
  `tools/push_game.sh build/games/FOLDROOM.VXP` puts it on.

## 2026-10-24: no games in the firmware (owner)
"It shouldn't come with any games as part of a default firmware." A VMX's games are exactly the GAME.VXPs installed
on its memory unit, the same list VMX World shows: the CoreS3's 27 embedded games are gone (tools/embed_vxp.py,
esp32/src/games_builtin.h), and FOLD ROOMS's C version (core/game_fold.c, fold_levels.h, tools/embed_levels.py) is
replaced by the C# port (games/fold, its own section above; tools/parity/fold_ref.c keeps a frozen copy of the C game
for the parity check). GAMES and SAVES on an empty VMX say NO GAMES YET / GET THEM IN VMX WORLD, and the idle demos
wait for a game (they divided by zero). Firmware 2.2 MB -> 1.3 MB. Not yet on the rig (offline when flashed):
`cd esp32 && pio run -e ota -t upload`; after that it starts empty, and games go on from World (GET GAMES with the
VMX plugged in; it restarts with them when unplugged or ejected) or `tools/push_game.sh`.

## 2026-10-24 later: on the rig (firmware without games + FOLD ROOMS with turns)
Flashed over Wi-Fi (`pio run -e ota -t upload`, ~/.platformio/penv/bin/pio), then `tools/push_game.sh
build/games/FOLDROOM.VXP`: FOLD ROOMS is the one game installed (the other 27 aren't on it: add them from World or
push_game.sh). Running in its demo: ~90 M IL/s while ticking, avg 0.38 ms a tick, worst 5.2 ms (`drive.py --wifi i`),
so the room turns are far inside a 33 ms tick. Not looked at by me: how the turns look on the panel.
