# PC build of the firmware core, for tests (the CoreS3 build is esp32/, PlatformIO).
FOLD = ../microgames/fold
SRC = core/ui.c core/surf.c core/sound.c core/records.c core/games.c core/game_fold.c $(FOLD)/core/fold.c $(FOLD)/app/vmx.c
CFLAGS = -O2 -Wall -Wextra -Wno-unused-parameter -std=gnu11
build/vmxhost: $(SRC) host/main.c core/*.h
	mkdir -p build
	$(CC) $(CFLAGS) -o $@ $(SRC) host/main.c -lm
test: build/vmxhost
	bash tools/host_test.sh
.PHONY: test
