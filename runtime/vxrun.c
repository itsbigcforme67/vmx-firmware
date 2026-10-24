// Runs a VMX game image on the PC for tests: Start(seed, demo), then ticks with a press pattern, printing one line per
// tick: tick number and an FNV-1a hash of the screen after Draw (tools/parity compares these with the JS core).
//   build/vxrun game.vxi SEED DEMO TICKS [PATTERNSEED] [--pgm N out.pgm] [--file NAME=PATH ...]
#include "vxrt.h"
#include "../core/surf.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static uint8_t screen[4096];
static void text(uint8_t *scr, int x, int y, const char *s, int on) { Surf S = { 64, 64, scr }; s_text(&S, x, y, s, on); }
static void report(int s, int a, int b, int c) { (void)s; (void)a; (void)b; (void)c; }
// --file NAME=PATH: a packed data file, as GAME.VXP would carry it
static struct { char name[32]; uint8_t *d; int n; } files[8]; static int nfiles;
static int bank = 100;                                    // a test chip bank (--chips N), as tools/parity/js_run.mjs
static int chips(int op, int n) {
  if (op == 0) return bank;
  if (op == 1) { if (n > bank) return 0; bank -= n; return 1; }
  if (op == 2) { bank += n; return 0; }
  return 0;
}
static int file(const char *name, int off, uint8_t *dst, int n) {
  for (int i = 0; i < nfiles; i++) if (!strcmp(files[i].name, name)) {
    if (!dst) return files[i].n;
    if (off >= files[i].n) return 0; if (n > files[i].n - off) n = files[i].n - off;
    memcpy(dst, files[i].d + off, (size_t)n); return n;
  }
  return dst ? 0 : -1;
}
int main(int argc, char **argv) {
  if (argc < 5) { fprintf(stderr, "usage: vxrun game.vxi SEED DEMO TICKS [PATTERNSEED] [--pgm N out.pgm]\n"); return 2; }
  FILE *f = fopen(argv[1], "rb"); if (!f) { perror(argv[1]); return 1; }
  static uint8_t img[1 << 20]; size_t n = fread(img, 1, sizeof img, f); fclose(f);
  int seed = atoi(argv[2]), demo = atoi(argv[3]), ticks = atoi(argv[4]); unsigned ps = argc > 5 ? (unsigned)atoi(argv[5]) : 1;
  int pgmAt = -1; const char *pgm = NULL;
  for (int i = 1; i < argc - 2; i++) if (!strcmp(argv[i], "--pgm")) { pgmAt = atoi(argv[i + 1]); pgm = argv[i + 2]; }
  for (int i = 1; i < argc - 1; i++) if (!strcmp(argv[i], "--file") && nfiles < 8) {
    char *eq = strchr(argv[i + 1], '='); if (!eq) continue;
    snprintf(files[nfiles].name, sizeof files[nfiles].name, "%.*s", (int)(eq - argv[i + 1]), argv[i + 1]);
    FILE *ff = fopen(eq + 1, "rb"); if (!ff) { perror(eq + 1); return 1; }
    fseek(ff, 0, SEEK_END); files[nfiles].n = (int)ftell(ff); fseek(ff, 0, SEEK_SET);
    files[nfiles].d = malloc((size_t)files[nfiles].n); if (fread(files[nfiles].d, 1, (size_t)files[nfiles].n, ff) != (size_t)files[nfiles].n) return 1;
    fclose(ff); nfiles++;
  }
  for (int i = 1; i < argc - 1; i++) if (!strcmp(argv[i], "--chips")) bank = atoi(argv[i + 1]);
  VxHost host = { screen, NULL, report, NULL, NULL, text, file, chips };
  size_t need = vx_need(img, n, 131072); void *mem = malloc(need); char err[128];
  Vx *vm = vx_load(mem, need, img, n, 131072, &host, err, sizeof err);
  if (!vm) { fprintf(stderr, "load: %s\n", err); return 1; }
  if (vx_start(vm, seed, demo)) { fprintf(stderr, "start: %s\n", vx_error(vm)); return 1; }
  int held = 0; uint32_t worst = 0, worstAt = 0, before = 0;
  for (int t = 1; t <= ticks; t++) {
    // the press pattern: a new random button set every few ticks (same generator as tools/parity/js_run.mjs)
    ps = ps * 1103515245u + 12345u; int nh = (ps >> 16) % 8 == 0 ? (int)((ps >> 20) & 63) : held;
    if (getenv("HOLD")) nh = atoi(getenv("HOLD"));       // HOLD=mask: those buttons held all the time (B_DOWN = 2)
    int pressed = nh & ~held; held = nh;
    vx_stats(vm, &before, NULL, NULL);
    if (vx_tick(vm, held, pressed) || vx_draw(vm)) { fprintf(stderr, "tick %d: %s\n", t, vx_error(vm)); return 1; }
    { uint32_t now; vx_stats(vm, &now, NULL, NULL); if (now - before > worst) { worst = now - before; worstAt = (uint32_t)t; } }
    uint32_t h = 2166136261u; for (int i = 0; i < 4096; i++) { h ^= screen[i]; h *= 16777619u; }
    printf("%d %08x\n", t, h);
    if (t == pgmAt && pgm) { FILE *o = fopen(pgm, "wb"); fprintf(o, "P5 64 64 1\n"); fwrite(screen, 1, 4096, o); fclose(o); }
  }
  uint32_t ins, hu, gcs; vx_stats(vm, &ins, &hu, &gcs);
  fprintf(stderr, "%d ticks: %u instructions (%u a tick, worst %u at tick %u), heap top %u, %u collections\n", ticks, ins, ins / (unsigned)ticks, worst, worstAt, hu, gcs);
  return 0;
}
