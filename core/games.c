// The games on this VMX: the ones installed (GAME.VXP files, core/game_vx.c, added by the platform), nothing else.
// A VMX's firmware has no games of its own (owner, 24 Oct 2026); FOLD ROOMS is a managed game like the rest.
#include "vmxfw.h"
VmxGame *VMX_GAMES[36];
int VMX_NGAMES;
int vx_games(VmxGame **out, int max);       // core/game_vx.c
void games_init(void) { VMX_NGAMES = vx_games(VMX_GAMES, 36); }
