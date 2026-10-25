# PC build of the firmware core, for tests (the CoreS3 build is esp32/, PlatformIO).
FOLD = ../microgames/fold
SRC = core/ui.c core/surf.c core/sound.c core/records.c core/games.c core/game_vx.c core/fatx.c core/vmxvol.c runtime/vxrt.c
CFLAGS = -O2 -Wall -Wextra -Wno-unused-parameter -std=gnu11
build/vmxhost: $(SRC) host/main.c core/*.h runtime/*.h
	mkdir -p build
	$(CC) $(CFLAGS) -o $@ $(SRC) host/main.c -lm
test: build/vmxhost build/vxrun build/vmxmu build/fold_run
	for g in blox blobs intercept invasion blackjack checkers reversi tricky zork junction pool poker roulette slots hilo war reddog baccarat keno threecard craps scratch bigsix letitride frogs sicbo chuckaluck fold; do tools/parity/check.sh $$g 3000 || exit 1; done
	build/vmxmu build/mutest.img test
.PHONY: test
build/vmxmu: host/vmxmu.c core/fatx.c core/vmxvol.c core/fatx.h core/vmxvol.h
	mkdir -p build
	$(CC) $(CFLAGS) -o $@ host/vmxmu.c core/fatx.c core/vmxvol.c
build/vxrun: runtime/vxrun.c runtime/vxrt.c runtime/vxrt.h core/surf.c
	$(CC) $(CFLAGS) -o $@ runtime/vxrun.c runtime/vxrt.c core/surf.c -lm
# FOLD ROOMS' parity harness: the C game (tools/parity/fold_ref.c, from core/game_fold.c) and the runtime, side by side
build/fold_run: tools/parity/fold_run.c tools/parity/fold_ref.c core/surf.c runtime/vxrt.c runtime/vxrt.h $(FOLD)/core/fold.c $(FOLD)/app/vmx.c
	$(CC) $(CFLAGS) -o $@ tools/parity/fold_run.c tools/parity/fold_ref.c core/surf.c runtime/vxrt.c $(FOLD)/core/fold.c $(FOLD)/app/vmx.c -lm
