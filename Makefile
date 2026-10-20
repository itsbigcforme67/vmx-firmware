# PC build of the firmware core, for tests (the CoreS3 build is esp32/, PlatformIO).
FOLD = ../microgames/fold
SRC = core/ui.c core/surf.c core/sound.c core/records.c core/games.c core/game_fold.c core/game_vx.c runtime/vxrt.c $(FOLD)/core/fold.c $(FOLD)/app/vmx.c
CFLAGS = -O2 -Wall -Wextra -Wno-unused-parameter -std=gnu11
build/vmxhost: $(SRC) host/main.c core/*.h runtime/*.h
	mkdir -p build
	$(CC) $(CFLAGS) -o $@ $(SRC) host/main.c -lm
test: build/vmxhost build/vxrun
	bash tools/host_test.sh
.PHONY: test
build/vxrun: runtime/vxrun.c runtime/vxrt.c runtime/vxrt.h core/surf.c
	$(CC) $(CFLAGS) -o $@ runtime/vxrun.c runtime/vxrt.c core/surf.c -lm
