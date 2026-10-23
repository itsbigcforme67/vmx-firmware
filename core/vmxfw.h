// VMX firmware core: the boot, the menus (GAMES / SAVES / SETTINGS / CALL), the games and the beeper, with no
// hardware code. A platform (host/main.c for tests, esp32/src/main.cpp for the CoreS3) calls vmx_frame() with the
// buttons held and draws vmx_screen(); it pulls sound with vmx_audio() and keeps the saves with the plat_* calls.
// It follows the simulator (POCKET ARCADE vmx/vmx.js, branch vmx-call) screen for screen.
#ifndef VMXFW_H
#define VMXFW_H
#include <stdint.h>
#include <stddef.h>
#ifdef __cplusplus
extern "C" {
#endif

enum { B_UP = 1, B_DOWN = 2, B_LEFT = 4, B_RIGHT = 8, B_A = 16, B_B = 32, B_SIDE = 64 };
#define VMX_RATE 16000                      // sound sample rate

void vmx_init(void);
void vmx_frame(uint32_t ms, int held);      // ms = time now; held = B_* bits
const uint8_t *vmx_screen(void);            // 64 x 64, 1 = lit
int  vmx_bright(void);                      // 0..2
int  vmx_on(void);                          // 0 = powered off (screen dark)
void vmx_audio(int16_t *out, int n);        // the next n samples (mono, VMX_RATE)

// provided by the platform
void *plat_alloc(size_t n);                 // big, long-lived buffers (PSRAM on the CoreS3)
void *plat_alloc_fast(size_t n);            // small, busy buffers: fast internal RAM if there is room (else NULL)
int  plat_load(const char *key, void *buf, int max);       // bytes read, 0 = none
void plat_save(const char *key, const void *buf, int n);
int  plat_day(void);                        // days since 15 Nov 2001, or -1 if the clock isn't set
const char *plat_name(void);                // what ABOUT says the VMX is running on
int  plat_wifi(char lines[4][16]);          // SETTINGS > WIFI: status lines; returns how many (0 = no Wi-Fi here)
void plat_wifi_setup(void);                 // SETTINGS > WIFI, A: start Wi-Fi setup

// games in GAME.VXP files (managed code, runtime/vxrt.c): add them before vmx_init (the data must stay put)
int  vmx_add_game(const uint8_t *vxp, size_t len);   // 0 = added, -1 = not a valid VXP
void vmx_clear_games(void);                          // forget them all (then add again and call vmx_init)

// VMX CASINO's shared chip bank (core/game_vx.c; vmx-world/docs/CASINO.md)
typedef struct { uint32_t magic; int32_t chips, peak, staked, paid, cashed, sent; uint16_t stipendDay; uint8_t sentOk, pad; } VmxBank;
const VmxBank *vmx_bank(void);
void vmx_bank_sent(int32_t chips);          // CALL sent this stash

// a game (core/games.c lists them)
typedef struct { int held, edges; } VmxIn;
typedef struct { int32_t best, w, l, d; uint16_t day; uint8_t sentOk; int32_t sent[4]; } VmxRec;
typedef struct VmxGame {
  const char *id, *title, *name[2];
  uint32_t tid;                             // game ID for the score records (vmx-world/docs/DESIGN.md)
  int usesB, wld;                           // usesB 0: B goes back; wld: score = won/lost/drawn
  void (*art)(void *surf);                  // 24 x 24 card picture (a Surf *)
  void (*start)(uint32_t seed, int demo);
  void (*tick)(const VmxIn *in, int demo);  // 30 times a second
  const uint8_t *(*draw)(void);
  int  (*record)(VmxRec *r);                // update r from the game; 1 = changed
} VmxGame;
extern VmxGame *VMX_GAMES[36];
extern int VMX_NGAMES;
void games_init(void);                      // the managed games added so far, then the built-in ones

#ifdef __cplusplus
}
#endif
#endif
