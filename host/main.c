// The firmware on the PC, for tests: runs a script of presses at 60 frames a second, writes screenshots (PGM, 4x)
// and the sound (WAV). Never opens a window. Its storage is a memory unit image, outdir/vmx.mu (core/vmxvol.h; made
// if missing; build/vmxmu reads it), and the games installed there are played as on the VMX.
//   build/vmxhost [-o outdir] [-g GAME.VXP]... < script
//   script lines: wait MS | press BTN | hold BTN MS | shot NAME | link on | link off
//   BTN: up down left right a b side
#include "../core/vmxfw.h"
#include "../core/vmxvol.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static char outdir[256] = ".";
static uint32_t ms;
static int held;
static FILE *wav; static long wavN;

void *plat_alloc_fast(size_t n) { return calloc(1, n); }
void *plat_alloc(size_t n) { void *p = calloc(1, n); if (!p) { fprintf(stderr, "out of memory\n"); exit(1); } return p; }
static FILE *muf; static Fatx mu;
static int mu_rd(void *c, uint32_t sec, int n, uint8_t *b) { return !fseek(muf, (long)sec * 512, SEEK_SET) && fread(b, 512, (size_t)n, muf) == (size_t)n; }
static int mu_wr(void *c, uint32_t sec, int n, const uint8_t *b) { return !fseek(muf, (long)sec * 512, SEEK_SET) && fwrite(b, 512, (size_t)n, muf) == (size_t)n; }
static void mu_open(void) {
  char p[400]; snprintf(p, sizeof p, "%s/vmx.mu", outdir);
  muf = fopen(p, "r+b"); if (!muf) { muf = fopen(p, "w+b"); if (muf) { fseek(muf, FATX_MU_BYTES - 1, SEEK_SET); fputc(0, muf); } }
  if (!muf) { perror(p); exit(1); }
  mu = (Fatx){ .read = mu_rd, .write = mu_wr, .sectors = FATX_MU_BYTES / 512, .stamp = fatx_stamp(2002, 11, 15, 12, 0, 0) };
  if (!vol_open(&mu)) { fprintf(stderr, "%s: can't open the volume\n", p); exit(1); }
}
int plat_load(const char *key, void *buf, int max) { return vol_load(&mu, key, buf, max); }
void plat_save(const char *key, const void *buf, int n) { vol_save(&mu, key, buf, n); fflush(muf); }
void plat_link_eject(void) { vmx_link(0); }
static void mu_games(void) {                       // the games installed on the volume
  uint32_t ids[32]; int n = vol_games(&mu, ids, 32);
  for (int i = 0; i < n; i++) {
    char p[40]; vol_game_path(ids[i], p); int len = fatx_size(&mu, p); uint8_t *d = malloc((size_t)len);
    if (d && fatx_read(&mu, p, d, len) == len && !vmx_add_game(d, (size_t)len)) continue;
    fprintf(stderr, "%s: not a valid VXP\n", p); free(d);
  }
}
int plat_day(void) { return 400; }
const char *plat_name(void) { return "PC TEST"; }
int plat_wifi(char lines[4][16]) { snprintf(lines[0], 16, "PC TEST"); return 1; }
void plat_wifi_setup(void) {}

static void frame(void) {
  ms += 1000 / 60 + ((ms / 1000 * 60) % 3 == 0);  // ~16.67 ms
  vmx_frame(ms, held);
  int16_t a[VMX_RATE / 60 + 1]; int n = VMX_RATE / 60; vmx_audio(a, n);
  if (wav) { fwrite(a, 2, (size_t)n, wav); wavN += n; }
}
static void run(int t) { uint32_t end = ms + (uint32_t)t; while (ms < end) frame(); }
static int bit(const char *b) {
  const char *N[] = { "up", "down", "left", "right", "a", "b", "side" };
  for (int i = 0; i < 7; i++) if (!strcmp(b, N[i])) return 1 << i;
  fprintf(stderr, "unknown button %s\n", b); return 0;
}
static void shot(const char *name) {
  char p[400]; snprintf(p, sizeof p, "%s/%s.pgm", outdir, name);
  FILE *f = fopen(p, "wb"); if (!f) return;
  const uint8_t *s = vmx_screen(); fprintf(f, "P5 256 256 255\n");
  for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) fputc(s[(y / 4) * 64 + x / 4] ? 255 : 0, f);
  fclose(f);
}
static void wav_hdr(FILE *f, long n) {
  unsigned char h[44] = "RIFF....WAVEfmt \x10\0\0\0\1\0\1\0........\2\0\x10\0data....";
  long rate = VMX_RATE, by = rate * 2, sz = n * 2;
  for (int i = 0; i < 4; i++) { h[4 + i] = (unsigned char)((36 + sz) >> (8 * i)); h[24 + i] = (unsigned char)(rate >> (8 * i));
    h[28 + i] = (unsigned char)(by >> (8 * i)); h[40 + i] = (unsigned char)(sz >> (8 * i)); }
  fseek(f, 0, SEEK_SET); fwrite(h, 1, 44, f);
}
int main(int argc, char **argv) {
  for (int i = 1; i < argc; i++) if (!strcmp(argv[i], "-o") && i + 1 < argc) snprintf(outdir, sizeof outdir, "%s", argv[++i]);
  char p[400]; snprintf(p, sizeof p, "%s/sound.wav", outdir); wav = fopen(p, "wb"); if (wav) fseek(wav, 44, SEEK_SET);
  for (int i = 1; i < argc; i++) if (!strcmp(argv[i], "-g") && i + 1 < argc) {     // -g GAME.VXP: add a managed game
    FILE *f = fopen(argv[++i], "rb"); if (!f) { perror(argv[i]); return 1; }
    fseek(f, 0, SEEK_END); long n = ftell(f); fseek(f, 0, SEEK_SET); uint8_t *d = malloc((size_t)n);
    if (fread(d, 1, (size_t)n, f) != (size_t)n) return 1; fclose(f);
    if (vmx_add_game(d, (size_t)n)) { fprintf(stderr, "%s: not a valid VXP\n", argv[i]); return 1; }
  }
  mu_open(); mu_games();
  vmx_init();
  char line[256], a[64]; int v;
  while (fgets(line, sizeof line, stdin)) {
    if (sscanf(line, "wait %d", &v) == 1) run(v);
    else if (sscanf(line, "hold %63s %d", a, &v) == 2) { held |= bit(a); run(v); held &= ~bit(a); frame(); }
    else if (sscanf(line, "press %63s", a) == 1) { held |= bit(a); frame(); frame(); held &= ~bit(a); frame(); }
    else if (sscanf(line, "shot %63s", a) == 1) shot(a);
    else if (sscanf(line, "link %63s", a) == 1) vmx_link(!strcmp(a, "on"));
  }
  if (wav) { wav_hdr(wav, wavN); fclose(wav); }
  return 0;
}
