// Runs a VMX game image on the PC for tests: Start(seed, demo), then ticks with a press pattern, printing one line per
// tick: tick number and an FNV-1a hash of the screen after Draw (tools/parity compares these with the JS core).
//   build/vxrun game.vxi SEED DEMO TICKS [PATTERNSEED] [--pgm N out.pgm] [--file NAME=PATH ...] [--data NAME=PATH ...]
//               [--save PATH]
// --data: a file in the game's DATA\ folder (Storage.ReadData); --save: its save (Storage.Load); HEAP=bytes: a smaller
// game heap than the VMX's 128 KB (to find what a game needs); PRESSES=file: the buttons held each tick, one mask a
// line (as tools/parity/fold_run), instead of the pattern
#include "vxrt.h"
#include "../core/surf.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static uint8_t screen[4096];
static void text(uint8_t *scr, int x, int y, const char *s, int on) { Surf S = { 64, 64, scr }; s_text(&S, x, y, s, on); }
static void report(int s, int a, int b, int c) { (void)s; (void)a; (void)b; (void)c; }
// --file NAME=PATH: a packed data file, as GAME.VXP would carry it (--data: kind 1)
static struct { char name[32]; uint8_t *d; int n, kind; } files[8]; static int nfiles;
static uint8_t *save; static int saveN = -1;
static int load(int id, uint8_t *buf, int max) { if (id || saveN < 0) return -1; int n = saveN < max ? saveN : max; memcpy(buf, save, (size_t)n); return n; }
static int bank = 100;                                    // a test chip bank (--chips N), as tools/parity/js_run.mjs
static int chips(int op, int n) {
  if (op == 0) return bank;
  if (op == 1) { if (n > bank) return 0; bank -= n; return 1; }
  if (op == 2) { bank += n; return 0; }
  return 0;
}
static int fileOf(int kind, const char *name, int off, uint8_t *dst, int n) {
  for (int i = 0; i < nfiles; i++) if (files[i].kind == kind && !strcmp(files[i].name, name)) {
    if (!dst) return files[i].n;
    if (off >= files[i].n) return 0; if (n > files[i].n - off) n = files[i].n - off;
    memcpy(dst, files[i].d + off, (size_t)n); return n;
  }
  return dst ? 0 : -1;
}
static int file(const char *name, int off, uint8_t *dst, int n) { return fileOf(0, name, off, dst, n); }
static int data(const char *name, int off, uint8_t *dst, int n) { return fileOf(1, name, off, dst, n); }
static uint8_t *slurp(const char *p, int *n) {
  FILE *ff = fopen(p, "rb"); if (!ff) { perror(p); exit(1); }
  fseek(ff, 0, SEEK_END); *n = (int)ftell(ff); fseek(ff, 0, SEEK_SET);
  uint8_t *d = malloc((size_t)*n + 1); if (fread(d, 1, (size_t)*n, ff) != (size_t)*n) exit(1);
  fclose(ff); return d;
}
int main(int argc, char **argv) {
  if (argc < 5) { fprintf(stderr, "usage: vxrun game.vxi SEED DEMO TICKS [PATTERNSEED] [--pgm N out.pgm]\n"); return 2; }
  FILE *f = fopen(argv[1], "rb"); if (!f) { perror(argv[1]); return 1; }
  static uint8_t img[1 << 20]; size_t n = fread(img, 1, sizeof img, f); fclose(f);
  int seed = atoi(argv[2]), demo = atoi(argv[3]), ticks = atoi(argv[4]); unsigned ps = argc > 5 ? (unsigned)atoi(argv[5]) : 1;
  int pgmAt = -1; const char *pgm = NULL;
  for (int i = 1; i < argc - 2; i++) if (!strcmp(argv[i], "--pgm")) { pgmAt = atoi(argv[i + 1]); pgm = argv[i + 2]; }
  for (int i = 1; i < argc - 1; i++) if ((!strcmp(argv[i], "--file") || !strcmp(argv[i], "--data")) && nfiles < 8) {
    char *eq = strchr(argv[i + 1], '='); if (!eq) continue;
    snprintf(files[nfiles].name, sizeof files[nfiles].name, "%.*s", (int)(eq - argv[i + 1]), argv[i + 1]);
    files[nfiles].d = slurp(eq + 1, &files[nfiles].n); files[nfiles].kind = !strcmp(argv[i], "--data"); nfiles++;
  }
  for (int i = 1; i < argc - 1; i++) if (!strcmp(argv[i], "--save")) save = slurp(argv[i + 1], &saveN);
  for (int i = 1; i < argc - 1; i++) if (!strcmp(argv[i], "--chips")) bank = atoi(argv[i + 1]);
  VxHost host = { screen, NULL, report, load, NULL, text, file, chips, data };
  size_t heap = getenv("HEAP") ? (size_t)atoi(getenv("HEAP")) : 131072;
  size_t need = vx_need(img, n, heap); void *mem = malloc(need); char err[128];
  Vx *vm = vx_load(mem, need, img, n, heap, &host, err, sizeof err);
  if (!vm) { fprintf(stderr, "load: %s\n", err); return 1; }
  if (vx_start(vm, seed, demo)) { fprintf(stderr, "start: %s\n", vx_error(vm)); return 1; }
  int held = 0; uint32_t worst = 0, worstAt = 0, before = 0;
  FILE *pf = getenv("PRESSES") ? fopen(getenv("PRESSES"), "r") : NULL;
  for (int t = 1; t <= ticks; t++) {
    // the press pattern: a new random button set every few ticks (same generator as tools/parity/js_run.mjs)
    ps = ps * 1103515245u + 12345u; int nh = (ps >> 16) % 8 == 0 ? (int)((ps >> 20) & 63) : held;
    if (getenv("HOLD")) nh = atoi(getenv("HOLD"));       // HOLD=mask: those buttons held all the time (B_DOWN = 2)
    if (pf && fscanf(pf, "%d", &nh) != 1) nh = 0;
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
