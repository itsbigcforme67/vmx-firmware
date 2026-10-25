// FOLD ROOMS' parity check, both sides with the same press pattern, HOLD and "tick hash" output as runtime/vxrun.c:
//   build/fold_run SEED DEMO TICKS [PATTERNSEED]                 the C game (GAME_FOLD: fold_ref.c, core/game_fold.c's copy)
//   build/fold_run --vxi build/fold.vxi LEVELS SEED DEMO TICKS [PATTERNSEED]   the C# port in the VMX runtime, with
//     LEVELS = games/fold/levels.bin as its "levels" file; prints instructions a tick on stderr when ILS=1
// PRESSES=file: the buttons held each tick from a file (one mask a line) instead of the pattern (undo tests).
// CLEARED=n: both start with a save that says n levels are cleared (so random presses play level n + 1); without it
// there is no save, as vxrun's. tools/parity/check.sh fold runs vxrun against the C, then this for every level.
#include "../../core/vmxfw.h"
#include "../../runtime/vxrt.h"
#include "../../core/surf.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int cleared(uint8_t *buf, int max) {          // the save, as plat_load / Storage.Load see it
  const char *e = getenv("CLEARED"); if (!e || max < 4) return -1;
  int n = atoi(e); memcpy(buf, &n, 4); return 4;
}
void *plat_alloc(size_t n) { return calloc(1, n); }
int plat_load(const char *key, void *buf, int max) { (void)key; int n = cleared(buf, max); return n < 0 ? 0 : n; }
void plat_save(const char *key, const void *buf, int n) { (void)key; (void)buf; (void)n; }
extern VmxGame GAME_FOLD;

static uint8_t screen[4096], *levels; static int nlevels;
static void text(uint8_t *scr, int x, int y, const char *s, int on) { Surf S = { 64, 64, scr }; s_text(&S, x, y, s, on); }
static void report(int s, int a, int b, int c) { (void)s; (void)a; (void)b; (void)c; }
static int load(int id, uint8_t *buf, int max) { (void)id; return cleared(buf, max); }
static void save(const uint8_t *buf, int n) { (void)buf; (void)n; }
static int file(const char *name, int off, uint8_t *dst, int n) {
  if (strcmp(name, "levels")) return dst ? 0 : -1;
  if (!dst) return nlevels;
  if (off < 0 || off >= nlevels) return 0;
  if (n > nlevels - off) n = nlevels - off;
  memcpy(dst, levels + off, (size_t)n); return n;
}
static uint8_t *slurp(const char *path, int *n) {
  FILE *f = fopen(path, "rb"); if (!f) { perror(path); exit(1); }
  fseek(f, 0, SEEK_END); *n = (int)ftell(f); fseek(f, 0, SEEK_SET);
  uint8_t *d = malloc((size_t)*n + 1); if (fread(d, 1, (size_t)*n, f) != (size_t)*n) exit(1); fclose(f); return d;
}

int main(int argc, char **argv) {
  Vx *vm = NULL; int a = 1;
  if (argc > 3 && !strcmp(argv[1], "--vxi")) {
    int n; uint8_t *img = slurp(argv[2], &n); levels = slurp(argv[3], &nlevels); a = 4;
    static VxHost host = { screen, NULL, report, load, save, text, file, NULL };
    size_t need = vx_need(img, (size_t)n, 131072); char err[128];
    vm = vx_load(malloc(need), need, img, (size_t)n, 131072, &host, err, sizeof err);
    if (!vm) { fprintf(stderr, "load: %s\n", err); return 1; }
  }
  if (argc < a + 3) { fprintf(stderr, "usage: fold_run [--vxi IMAGE LEVELS] SEED DEMO TICKS [PATTERNSEED]\n"); return 2; }
  int seed = atoi(argv[a]), demo = atoi(argv[a + 1]), ticks = atoi(argv[a + 2]); unsigned ps = argc > a + 3 ? (unsigned)atoi(argv[a + 3]) : 1;
  int ils = getenv("ILS") != NULL;
  if (vm ? vx_start(vm, seed, demo) != 0 : (GAME_FOLD.start((uint32_t)seed, demo), 0)) { fprintf(stderr, "start: %s\n", vx_error(vm)); return 1; }
  int held = 0; uint32_t before = 0, now = 0;
  FILE *pf = getenv("PRESSES") ? fopen(getenv("PRESSES"), "r") : NULL;
  for (int t = 1; t <= ticks; t++) {
    ps = ps * 1103515245u + 12345u; int nh = (ps >> 16) % 8 == 0 ? (int)((ps >> 20) & 63) : held;
    if (getenv("HOLD")) nh = atoi(getenv("HOLD"));
    if (pf && fscanf(pf, "%d", &nh) != 1) nh = 0;
    int pressed = nh & ~held; held = nh;
    const uint8_t *scr;
    if (vm) {
      vx_stats(vm, &before, NULL, NULL);
      if (vx_tick(vm, held, pressed) || vx_draw(vm)) { fprintf(stderr, "tick %d: %s\n", t, vx_error(vm)); return 1; }
      vx_stats(vm, &now, NULL, NULL); if (ils) fprintf(stderr, "%d %u\n", t, now - before);
      scr = screen;
    } else { VmxIn in = { held, pressed }; GAME_FOLD.tick(&in, demo); scr = GAME_FOLD.draw(); }
    uint32_t h = 2166136261u; for (int i = 0; i < 4096; i++) { h ^= scr[i]; h *= 16777619u; }
    printf("%d %08x\n", t, h);
  }
  return 0;
}
