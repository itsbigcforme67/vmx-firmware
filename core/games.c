// The games on this VMX. Each is a C core with the simulator's adapter rules (POCKET ARCADE vmx/games.js).
#include "vmxfw.h"
extern const VmxGame GAME_FOLD;
const VmxGame *const VMX_GAMES[] = { &GAME_FOLD };
const int VMX_NGAMES = (int)(sizeof VMX_GAMES / sizeof VMX_GAMES[0]);
