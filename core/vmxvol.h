// The VMX's storage on its memory-unit volume (fatx.h), laid out as VMX World's folder on any Xbox memory unit (the
// root of a memory unit holds title folders, as UDATA does on the hard disk; vmx-world/docs/DESIGN.md):
//   56580001\TitleMeta.xbx                 "TitleName=VMX World" (UTF-16): the dashboard names the folder by it
//   56580001\<game ID>0000\GAME.VXP        an installed game, with SaveMeta.xbx "Name=<title>"
//   56580001\VMX\<KEY>.BIN                 the firmware's plat_load / plat_save keys (settings, recs, casino, ...)
// The same on the VMX itself, in the PC build (an image file) and in World (the VMX in a controller, over USB).
#ifndef VMXVOL_H
#define VMXVOL_H
#include "fatx.h"
#ifdef __cplusplus
extern "C" {
#endif
#define VOL_TITLE "56580001"
int  vol_open(Fatx *v);                                   // mounts it, or formats one that isn't FATX; 1 = ok
int  vol_load(Fatx *v, const char *key, void *buf, int max);      // bytes, 0 = none
int  vol_save(Fatx *v, const char *key, const void *buf, int n);  // 1 = ok
int  vol_games(Fatx *v, uint32_t *ids, int max);          // the installed games' IDs
void vol_game_path(uint32_t id, char out[40]);            // its GAME.VXP
int  vol_install(Fatx *v, uint32_t id, const char *title, const void *vxp, int n);   // 1 = ok
int  vol_install_vxp(Fatx *v, const uint8_t *vxp, int n, uint32_t *id, char title[24]);   // ID + title from the VXP
int  vol_remove(Fatx *v, uint32_t id);                    // its whole save folder; 1 = ok
int  vol_meta(char *out, int cap, const char *key, const char *value);   // a .xbx "Key=Value" file; its length
#ifdef __cplusplus
}
#endif
#endif
