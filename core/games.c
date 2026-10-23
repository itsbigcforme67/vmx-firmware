// The games on this VMX: managed games from GAME.VXP files (core/game_vx.c, added by the platform), then the
// built-in ones with C cores (FOLD ROOMS, until it is ported to C#).
#include "vmxfw.h"
extern VmxGame GAME_FOLD;
VmxGame *VMX_GAMES[24];
int VMX_NGAMES;
int vx_games(VmxGame **out, int max);       // core/game_vx.c
void games_init(void) {
  VMX_NGAMES = vx_games(VMX_GAMES, 23);
  VMX_GAMES[VMX_NGAMES++] = &GAME_FOLD;
}
