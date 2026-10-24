// The VMX runtime in the browser: the VMX simulator (POCKET ARCADE vmx/) runs a GAME.VXP here exactly as the VMX
// does (core/game_vx.c): its CODE chunk in runtime/vxrt.c with a 128 KB heap, its FILE chunks as the packed data
// files, the VMX font from core/surf.c, and the same GAME ERROR screen. JavaScript (vmx/vxrt.js) gives the sounds,
// saves, scores and chip bank. Build: runtime/wasm/build.sh.
#include "../vxrt.h"
#include "../../core/surf.h"
#include <stdio.h>
#include <string.h>

#define EXPORT(n) __attribute__((export_name(n)))
#define IMPORT(n) __attribute__((import_module("env"), import_name(n)))
IMPORT("tone") void js_tone(int hz, int ms, int vol);
IMPORT("report") void js_report(int s, int a, int b, int c);
IMPORT("load") int js_load(int id, uint8_t *buf, int max);
IMPORT("save") void js_save(const uint8_t *buf, int n);
IMPORT("chips") int js_chips(int op, int n);
IMPORT("data") int js_data(const char *name, int offset, uint8_t *dst, int n);

#define HEAP (128 * 1024)
static uint8_t vxp[1 << 20];                       // the GAME.VXP, copied in by JavaScript
static uint8_t mem[512 * 1024] __attribute__((aligned(16)));
static uint8_t scr[4096]; static char err[96];
static Vx *vm; static const uint8_t *code; static uint32_t codeLen;
static struct { char name[24]; const uint8_t *d; uint32_t n; } files[4]; static int nfiles;

static uint32_t rd32(const uint8_t *p) { return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24; }
static void h_tone(int hz, int ms, int vol) { js_tone(hz, ms, vol); }
static void h_report(int s, int a, int b, int c) { js_report(s, a, b, c); }
static int h_load(int id, uint8_t *buf, int max) { return js_load(id, buf, max); }
static void h_save(const uint8_t *buf, int n) { js_save(buf, n); }
static int h_chips(int op, int n) { return js_chips(op, n); }
static void h_text(uint8_t *s, int x, int y, const char *t, int on) { Surf S = { 64, 64, s }; s_text(&S, x, y, t, on); }
static int h_file(const char *name, int off, uint8_t *dst, int n) {
  for (int i = 0; i < nfiles; i++) if (!strcmp(files[i].name, name)) {
    if (!dst) return (int)files[i].n;
    if (off < 0 || (uint32_t)off >= files[i].n) return 0;
    if ((uint32_t)n > files[i].n - (uint32_t)off) n = (int)(files[i].n - (uint32_t)off);
    memcpy(dst, files[i].d + off, (size_t)n); return n;
  }
  return dst ? 0 : -1;
}
static int h_data(const char *name, int off, uint8_t *dst, int n) { return js_data(name, off, dst, n); }
static const VxHost HOST = { scr, h_tone, h_report, h_load, h_save, h_text, h_file, h_chips, h_data };

EXPORT("vxw_vxp") uint8_t *vxw_vxp(void) { return vxp; }
EXPORT("vxw_vxp_max") int vxw_vxp_max(void) { return (int)sizeof vxp; }
EXPORT("vxw_screen") uint8_t *vxw_screen(void) { return scr; }
EXPORT("vxw_error") const char *vxw_error(void) { return err; }

// reads the GAME.VXP JavaScript copied in (its header and chunks are checked there): 0, or -1 if it has no CODE
EXPORT("vxw_open") int vxw_open(int len) {
  vm = NULL; code = NULL; codeLen = 0; nfiles = 0; err[0] = 0;
  uint32_t total = rd32(vxp + 12); if (len < 32 || total > (uint32_t)len) return -1;
  for (uint32_t o = 32; o + 8 <= total; ) {
    const uint8_t *tag = vxp + o, *p = vxp + o + 8; uint32_t n = rd32(vxp + o + 4);
    if (o + 8 + n > total) return -1;
    if (!memcmp(tag, "CODE", 4)) { code = p; codeLen = n; }
    else if (!memcmp(tag, "FILE", 4) && n >= 1 && p[0] + 1u <= n && nfiles < 4) {
      int k = nfiles++; snprintf(files[k].name, sizeof files[k].name, "%.*s", p[0], (const char *)p + 1);
      files[k].d = p + 1 + p[0]; files[k].n = n - 1u - p[0];
    }
    o += 8 + ((n + 3) & ~3u);
  }
  return code ? 0 : -1;
}
// the VmxGame calls, as core/game_vx.c's start_ tick_ draw_ (a game error stops the game and shows the message)
EXPORT("vxw_start") int vxw_start(int seed, int demo) {
  err[0] = 0;
  vm = code ? vx_load(mem, sizeof mem, code, codeLen, HEAP, &HOST, err, sizeof err) : NULL;
  if (!vm && !err[0]) snprintf(err, sizeof err, "NO MEMORY");
  if (vm && vx_start(vm, seed & 0x7FFFFFFF, demo)) snprintf(err, sizeof err, "%s", vx_error(vm));
  return err[0] ? -1 : 0;
}
EXPORT("vxw_uses_b") int vxw_uses_b(void) { return !err[0] && vm ? vx_uses_b(vm) : 0; }
EXPORT("vxw_tick") int vxw_tick(int held, int pressed) {
  if (err[0] || !vm) return -1;
  if (vx_tick(vm, held & 63, pressed & 63)) { snprintf(err, sizeof err, "%s", vx_error(vm)); return -1; }
  return 0;
}
static void errorScreen(void) {
  Surf S = { 64, 64, scr }; s_clear(&S, 0);
  s_pill(&S, 2, 2, 60, 9, PILL_FILL, 1); s_centre(&S, 4, "GAME ERROR", 0);
  int len = (int)strlen(err), y = 16;
  for (int i = 0; i < len && y < 50; i += 15, y += 7) { char l[16]; snprintf(l, sizeof l, "%.15s", err + i); s_text(&S, 2, y, l, 1); }
  s_centre(&S, 56, "B: BACK", 1);
}
EXPORT("vxw_draw") uint8_t *vxw_draw(void) {
  if (!err[0] && vm && vx_draw(vm)) snprintf(err, sizeof err, "%s", vx_error(vm));
  if (err[0]) errorScreen();
  return scr;
}
// for tests: instructions run, heap in use, collections
static uint32_t stats[3];
EXPORT("vxw_stats") uint32_t *vxw_stats(void) { if (vm) vx_stats(vm, stats, stats + 1, stats + 2); return stats; }
