// The VMX's storage: see vmxvol.h.
#include "vmxvol.h"
#include <stdio.h>
#include <string.h>

int vol_meta(char *out, int cap, const char *key, const char *value) {   // UTF-16 LE with a byte-order mark, CR LF
  char line[120]; int n = snprintf(line, sizeof line, "%s=%s\r\n", key, value), k = 0;
  if (n > (int)sizeof line - 1) n = (int)sizeof line - 1;
  if (cap < 2 + 2 * n) return 0;
  out[k++] = (char)0xFF; out[k++] = (char)0xFE;
  for (int i = 0; i < n; i++) { out[k++] = line[i]; out[k++] = 0; }
  return k;
}
int vol_open(Fatx *v) {
  if (!fatx_mount(v) && !fatx_format(v, 0x564D5801u)) return 0;          // "VMX" + 1
  if (fatx_size(v, VOL_TITLE "\\TitleMeta.xbx") < 0) {
    char m[260]; int n = vol_meta(m, sizeof m, "TitleName", "VMX World");
    if (!fatx_write(v, VOL_TITLE "\\TitleMeta.xbx", m, n)) return 0;
  }
  return fatx_mkdir(v, VOL_TITLE "\\VMX");
}
static void key_path(const char *key, char out[64]) { snprintf(out, 64, VOL_TITLE "\\VMX\\%.20s.BIN", key); }
int vol_load(Fatx *v, const char *key, void *buf, int max) { char p[64]; key_path(key, p); int n = fatx_read(v, p, buf, max); return n > 0 ? n : 0; }
int vol_save(Fatx *v, const char *key, const void *buf, int n) { char p[64]; key_path(key, p); return fatx_write(v, p, buf, n); }
static int hexid(const char *s, uint32_t *id) {          // a save folder's name: 8 hex digits + 0000
  if (strlen(s) != 12 || strcmp(s + 8, "0000")) return 0;
  uint32_t x = 0;
  for (int i = 0; i < 8; i++) { int c = s[i], d = c >= '0' && c <= '9' ? c - '0' : c >= 'A' && c <= 'F' ? c - 55 : c >= 'a' && c <= 'f' ? c - 87 : -1; if (d < 0) return 0; x = x << 4 | (uint32_t)d; }
  *id = x; return 1;
}
int vol_games(Fatx *v, uint32_t *ids, int max) {
  static FatxEnt e[64]; int n = fatx_list(v, VOL_TITLE, e, 64), k = 0;
  for (int i = 0; i < n && k < max; i++) {
    uint32_t id; char p[40];
    if (!e[i].dir || !hexid(e[i].name, &id)) continue;
    vol_game_path(id, p); if (fatx_size(v, p) > 0) ids[k++] = id;
  }
  return k;
}
void vol_game_path(uint32_t id, char out[40]) { snprintf(out, 40, VOL_TITLE "\\%08X0000\\GAME.VXP", (unsigned)id); }
int vol_install(Fatx *v, uint32_t id, const char *title, const void *vxp, int n) {
  char p[40], m[260]; vol_game_path(id, p);
  if (!fatx_write(v, p, vxp, n)) return 0;
  snprintf(p, sizeof p, VOL_TITLE "\\%08X0000\\SaveMeta.xbx", (unsigned)id);
  int k = vol_meta(m, sizeof m, "Name", title);
  return fatx_write(v, p, m, k);
}
int vol_install_vxp(Fatx *v, const uint8_t *d, int n, uint32_t *id, char title[24]) {
  if (n < 32 || memcmp(d, "VXP1", 4)) return 0;
  *id = (uint32_t)d[4] | (uint32_t)d[5] << 8 | (uint32_t)d[6] << 16 | (uint32_t)d[7] << 24;
  snprintf(title, 24, "VMX GAME");
  for (int o = 32; o + 8 <= n; ) {                       // the NAME chunk's first line is the title
    int len = d[o + 4] | d[o + 5] << 8 | d[o + 6] << 16;
    if (len < 0 || o + 8 + len > n) break;
    if (!memcmp(d + o, "NAME", 4)) { int k = 0; while (k < len && k < 23 && d[o + 8 + k] != '\n') { title[k] = (char)d[o + 8 + k]; k++; } title[k] = 0; break; }
    o += 8 + ((len + 3) & ~3);
  }
  return vol_install(v, *id, title, d, n);
}
int vol_remove(Fatx *v, uint32_t id) { char p[32]; snprintf(p, sizeof p, VOL_TITLE "\\%08X0000", (unsigned)id); return fatx_remove(v, p); }
