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
