// Managed games: each GAME.VXP the platform adds becomes a VmxGame that runs its CODE chunk in the VMX runtime
// (runtime/vxrt.c). One game runs at a time, so they share one block of memory (the runtime + a 128 KB heap).
// A game error (out of memory, a null reference, an exception) stops the game and shows the message.
#include "vmxfw.h"
#include "surf.h"
#include "sound.h"
#include "../runtime/vxrt.h"
#include <stdio.h>
#include <string.h>

#define MAXVX 12
#define HEAP (128 * 1024)
typedef struct {
  VmxGame g; uint32_t id; const uint8_t *code; size_t codeLen; uint8_t card[72];
  char title[16], l1[8], l2[8]; int best, f[3], changed;
  struct { char name[24]; const uint8_t *d; uint32_t n; } files[4]; int nfiles;
} VxGame;
static VxGame G[MAXVX]; static int nG;
static VxGame *cur; static Vx *vm; static void *mem, *heapMem; static size_t memLen;
static uint8_t *codeFast; static size_t codeFastLen;   // the running game's image copied into internal RAM (if it fits)
static uint8_t scr[4096]; static char err[96];
static uint32_t statUs, statTicks, statWorstUs, statInstr0;   // speed, for the serial 'i' command
uint32_t (*vmx_micros)(void);

static uint32_t rd32(const uint8_t *p) { return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24; }
static uint32_t crc32(const uint8_t *p, size_t n) {
  uint32_t c = 0xFFFFFFFFu;
  while (n--) { c ^= *p++; for (int k = 0; k < 8; k++) c = c & 1 ? (c >> 1) ^ 0xEDB88320u : c >> 1; }
  return ~c;
}

// ---- what a game can reach (VxHost)
static void h_tone(int hz, int ms, int vol) { if (vol < 0) vol = 0; if (vol > 100) vol = 100; snd_tone((float)hz, 0, ms / 1000.0f, 0.07f * vol / 100.0f, 0); }
static void h_report(int s, int a, int b, int c) {
  if (!cur) return;
  if (s > cur->best) { cur->best = s; cur->f[0] = a; cur->f[1] = b; cur->f[2] = c; cur->changed = 1; }
}
static int h_load(int id, uint8_t *buf, int max) {
  char key[16]; uint32_t who = id == 0 && cur ? cur->id : (uint32_t)id;
  snprintf(key, sizeof key, "g%08x", (unsigned)who);
  int n = plat_load(key, buf, max); return n > 0 ? n : -1;
}
static void h_save(const uint8_t *buf, int n) { char key[16]; if (!cur) return; snprintf(key, sizeof key, "g%08x", (unsigned)cur->id); plat_save(key, buf, n); }
static void h_text(uint8_t *s, int x, int y, const char *t, int on) { Surf S = { 64, 64, s }; s_text(&S, x, y, t, on); }
static int h_file(const char *name, int off, uint8_t *dst, int n) {
  if (!cur) return dst ? 0 : -1;
  for (int i = 0; i < cur->nfiles; i++) if (!strcmp(cur->files[i].name, name)) {
    if (!dst) return (int)cur->files[i].n;
    if (off < 0 || (uint32_t)off >= cur->files[i].n) return 0;
    if ((uint32_t)n > cur->files[i].n - (uint32_t)off) n = (int)(cur->files[i].n - (uint32_t)off);
    memcpy(dst, cur->files[i].d + off, (size_t)n); return n;
  }
  return dst ? 0 : -1;
}
static const VxHost HOST = { scr, h_tone, h_report, h_load, h_save, h_text, h_file };

// ---- the VmxGame calls, for game number n
static void errorScreen(void) {
  Surf S = { 64, 64, scr }; s_clear(&S, 0);
  s_pill(&S, 2, 2, 60, 9, PILL_FILL, 1); s_centre(&S, 4, "GAME ERROR", 0);
  // the message, 15 characters a line
  int len = (int)strlen(err), y = 16;
  for (int i = 0; i < len && y < 50; i += 15, y += 7) { char l[16]; snprintf(l, sizeof l, "%.15s", err + i); s_text(&S, 2, y, l, 1); }
  s_centre(&S, 56, "B: BACK", 1);
}
static void art_(VxGame *g, void *p) {
  Surf *s = p;
  for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++) s_px(s, x, y, (g->card[y * 3 + x / 8] >> (7 - x % 8)) & 1);
}
static void start_(VxGame *g, uint32_t seed, int demo) {
  cur = g; err[0] = 0; statInstr0 = 0;
  if (!mem || !heapMem) { snprintf(err, sizeof err, "NO MEMORY"); g->g.usesB = 0; return; }
  const uint8_t *code = g->code;
  if (codeFast && g->codeLen <= codeFastLen) { memcpy(codeFast, g->code, g->codeLen); code = codeFast; }
  vm = mem && heapMem ? vx_load_split(mem, memLen, heapMem, HEAP + 8, code, g->codeLen, &HOST, err, sizeof err) : NULL;
  if (vm && vx_start(vm, (int)(seed & 0x7FFFFFFF), demo)) snprintf(err, sizeof err, "%s", vx_error(vm));
  if (vm && !err[0]) g->g.usesB = vx_uses_b(vm);
  if (err[0]) g->g.usesB = 0;
}
static void tick_(VxGame *g, const VmxIn *in, int demo) {
  if (err[0] || !vm) return;
  cur = g;
  uint32_t t0 = vmx_micros ? vmx_micros() : 0;
  if (vx_tick(vm, in->held & 63, in->edges & 63)) { snprintf(err, sizeof err, "%s", vx_error(vm)); g->g.usesB = 0; }
  if (vmx_micros) { uint32_t us = vmx_micros() - t0; statUs += us; statTicks++; if (us > statWorstUs) statWorstUs = us; }
}
static const uint8_t *draw_(VxGame *g) {
  if (!err[0] && vm && vx_draw(vm)) { snprintf(err, sizeof err, "%s", vx_error(vm)); g->g.usesB = 0; }
  if (err[0]) errorScreen();
  return scr;
}
static int record_(VxGame *g, VmxRec *r) {
  if (!g->changed) return 0;
  g->changed = 0;
  if (g->g.wld) { r->w = g->f[0]; r->l = g->f[1]; r->d = g->f[2]; return 1; }
  if (g->best > r->best) { r->best = g->best; return 1; }
  return 0;
}
#define SLOT(n) \
  static void art##n(void *s) { art_(&G[n], s); } \
  static void start##n(uint32_t a, int d) { start_(&G[n], a, d); } \
  static void tick##n(const VmxIn *i, int d) { tick_(&G[n], i, d); } \
  static const uint8_t *draw##n(void) { return draw_(&G[n]); } \
  static int record##n(VmxRec *r) { return record_(&G[n], r); }
SLOT(0) SLOT(1) SLOT(2) SLOT(3) SLOT(4) SLOT(5) SLOT(6) SLOT(7) SLOT(8) SLOT(9) SLOT(10) SLOT(11)
#define FN(n) { art##n, start##n, tick##n, draw##n, record##n }
static const struct { void (*art)(void *); void (*start)(uint32_t, int); void (*tick)(const VmxIn *, int);
  const uint8_t *(*draw)(void); int (*record)(VmxRec *); } FNS[MAXVX] = {
  FN(0), FN(1), FN(2), FN(3), FN(4), FN(5), FN(6), FN(7), FN(8), FN(9), FN(10), FN(11) };

// ---- reading a GAME.VXP
int vmx_add_game(const uint8_t *d, size_t len) {
  if (nG >= MAXVX || len < 32 || memcmp(d, "VXP1", 4)) return -1;
  uint32_t total = rd32(d + 12);
  if (total > len || crc32(d + 32, total - 32) != rd32(d + 16)) return -1;
  VxGame *g = &G[nG]; memset(g, 0, sizeof *g);
  g->id = rd32(d + 4);
  for (uint32_t o = 32; o + 8 <= total; ) {
    const uint8_t *tag = d + o; uint32_t n = rd32(d + o + 4); const uint8_t *p = d + o + 8;
    if (o + 8 + n > total) return -1;
    if (!memcmp(tag, "CODE", 4)) { g->code = p; g->codeLen = n; }
    else if (!memcmp(tag, "CARD", 4) && n >= 72) memcpy(g->card, p, 72);
    else if (!memcmp(tag, "FILE", 4) && n >= 1 && p[0] + 1u <= n && g->nfiles < 4) {   // u8 name length, name, data
      int k = g->nfiles++; snprintf(g->files[k].name, sizeof g->files[k].name, "%.*s", p[0], (const char *)p + 1);
      g->files[k].d = p + 1 + p[0]; g->files[k].n = n - 1u - p[0];
    }
    else if (!memcmp(tag, "NAME", 4)) {                     // title \n publisher \n line1|line2 \n blurb
      char tmp[160]; size_t k = n < sizeof tmp - 1 ? n : sizeof tmp - 1; memcpy(tmp, p, k); tmp[k] = 0;
      char *title = tmp, *pub = strchr(title, '\n'); if (pub) *pub++ = 0;
      char *lines = pub ? strchr(pub, '\n') : NULL; if (lines) *lines++ = 0;
      char *end = lines ? strchr(lines, '\n') : NULL; if (end) *end = 0;
      snprintf(g->title, sizeof g->title, "%s", title);
      if (lines) { char *bar = strchr(lines, '|'); if (bar) { *bar = 0; snprintf(g->l2, sizeof g->l2, "%s", bar + 1); } snprintf(g->l1, sizeof g->l1, "%s", lines); }
      else snprintf(g->l1, sizeof g->l1, "%.6s", title);
    }
    o += 8 + ((n + 3) & ~3u);
  }
  if (!g->code || !g->title[0]) return -1;
  int at = nG;                                             // a newer copy of a game replaces the old one
  for (int i = 0; i < nG; i++) if (G[i].id == g->id) at = i;
  if (at != nG) G[at] = G[nG];
  g = &G[at];
  static char ids[MAXVX][12]; snprintf(ids[at], sizeof ids[at], "x%08x", (unsigned)g->id);
  g->g = (VmxGame){ ids[at], g->title, { g->l1, g->l2[0] ? g->l2 : NULL }, g->id, 1, 0,
                    FNS[at].art, FNS[at].start, FNS[at].tick, FNS[at].draw, FNS[at].record };
  if (at == nG) nG++;
  return 0;
}
// "TICKS n, AVG us, WORST us, INSTR/S n" since the last call (ticks only; draws not counted)
void vmx_stats_text(char *b, int n) {
  uint32_t ins = 0; if (vm) vx_stats(vm, &ins, NULL, NULL);
  int k = snprintf(b, (size_t)n, "[mem %p heap %p code %p] ", mem, heapMem, (void *)codeFast); b += k; n -= k;
  snprintf(b, (size_t)n, "%s: %u ticks, avg %u us, worst %u us, %u instructions in all (%u/s while ticking)",
           cur ? cur->title : "-", (unsigned)statTicks, (unsigned)(statTicks ? statUs / statTicks : 0), (unsigned)statWorstUs,
           (unsigned)ins, (unsigned)(statUs ? (uint64_t)(ins - statInstr0) * 1000000ull / statUs : 0));
  statUs = statTicks = statWorstUs = 0; statInstr0 = ins;
}
const char *vx_method_name(Vx *vm, int m);
const char *vmx_method_name(int m) { return vm ? vx_method_name(vm, m) : "?"; }
// one block for the interpreter (sized for the biggest game) and one for the game heap, made once: fast internal RAM
// for whichever fits (VX_FAST_HEAP: the heap first; search-heavy games walk their arrays), PSRAM for the other
static void vx_memory(void) {
  size_t big = 0; for (int i = 0; i < nG; i++) { size_t n = vx_need(G[i].code, G[i].codeLen, 0) + 64; if (n > big) big = n; }
  if (!big) return;
#ifdef VX_FAST_HEAP
  heapMem = plat_alloc_fast(HEAP + 8); if (!heapMem) heapMem = plat_alloc(HEAP + 8);
  mem = plat_alloc_fast(big); if (!mem) mem = plat_alloc(big);
#else
  mem = plat_alloc_fast(big); if (!mem) mem = plat_alloc(big);
  heapMem = plat_alloc_fast(HEAP + 8); if (!heapMem) heapMem = plat_alloc(HEAP + 8);
#endif
  memLen = mem ? big : 0;
  size_t bigCode = 0; for (int i = 0; i < nG; i++) if (G[i].codeLen > bigCode && G[i].codeLen <= 48 * 1024) bigCode = G[i].codeLen;
  codeFast = bigCode ? plat_alloc_fast(bigCode) : NULL; codeFastLen = codeFast ? bigCode : 0;
}
int vx_games(VmxGame **out, int max) {
  if (!mem) vx_memory(); int n = nG < max ? nG : max; for (int i = 0; i < n; i++) out[i] = &G[i].g; return n; }
