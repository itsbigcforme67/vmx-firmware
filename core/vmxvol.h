// The VMX's storage on its memory-unit volume (fatx.h): VMX World's folder, as on any Xbox memory unit (the root of
// a memory unit holds title folders, as UDATA does on the hard disk; vmx-world/docs/DESIGN.md). The Xbox dashboard
// shows "VMX World" with one save per VMX game, plus one shared save for the casino chips:
//   56580001\TitleMeta.xbx                 "TitleName=VMX World" (UTF-16)
//   56580001\VMX.SYS                       marks a VMX (World tells a VMX from other memory units by it)
//   56580001\SETTINGS.BIN                  the VMX's settings (loose: not a save in the dashboard)
//   56580001\<game ID>0000\                a VMX game: SaveMeta.xbx "Name=<title>", GAME.VXP (if installed rather than
//                                          built in), SAVE.BIN (the game's own save), RECORD.BIN (its scores),
//                                          DATA\ (read-only files the Xbox put there for it: FOLD ROOMS' LEVELS.BIN)
//   56580001\56581F000000\                 "VMX CASINO CHIPS": CHIPS.BIN, the stash every casino game shares
// The firmware's plat_load / plat_save keys map onto that (vol_key_path); VMX World uses the same mapping on the
// hard disk (UDATA\...) for its own VMX. The same on the VMX, in the PC build (an image file) and over USB.
#ifndef VMXVOL_H
#define VMXVOL_H
#include "fatx.h"
#ifdef __cplusplus
extern "C" {
#endif
#define VOL_TITLE "56580001"
#define VOL_CHIPS 0x56581F00u                            // the casino chips' save folder (their score record's ID)
// who a VMX is: its serial (from the chip where there is one, so a format keeps it), the owner's name (SETTINGS > NAME)
typedef struct { char magic[4]; uint32_t serial; uint16_t day; char name[10]; } VmxSys;   // "VMX1", 20 bytes
int  vol_open(Fatx *v, uint32_t serial);                  // mounts it (or formats one that isn't FATX); a new VMX.SYS
                                                          // gets this serial (0: none made, the firmware makes one); 1 = ok
int  vol_sys(Fatx *v, VmxSys *out);                       // 1 = it's a VMX (a valid VMX.SYS)
void vol_serial_text(uint32_t serial, char out[10]);      // "1A2B-3C4D"
int  vol_load(Fatx *v, const char *key, void *buf, int max);      // bytes, 0 = none
int  vol_save(Fatx *v, const char *key, const void *buf, int n);  // 1 = ok; buf NULL = delete it
// where a key lives, from the volume's root, with \ between names: "settings", "casino", "g<ID>" (a game's save),
// "r<ID>" (its record), "d<ID>:<NAME>" (a file the Xbox gave it: <ID>0000\DATA\<NAME>, e.g. FOLD BUILDER's levels for
// FOLD ROOMS, d56581010:LEVELS.BIN; the game reads it with Storage.ReadData); anything else is a loose file in the title folder. *folder = the save folder's game ID it
// is in (it needs a SaveMeta.xbx, named by vol_name), or 0
void vol_key_path(const char *key, char out[64], uint32_t *folder);
void vol_name(uint32_t id, const char *title);           // the name a game's save folder gets (VMX_GAMES' titles)
const char *vol_title(uint32_t id);
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
