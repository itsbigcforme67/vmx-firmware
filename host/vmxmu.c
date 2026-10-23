// A memory unit's FATX volume on the PC (core/fatx.c): an image file, or the VMX itself over USB (/dev/sdX, if you
// may open it). The VMX's own layout is core/vmxvol.h.
//   build/vmxmu IMG format [MB]          an empty volume (makes the file; default 8 MB, as a memory unit)
//   build/vmxmu IMG ls [PATH]            folders end in "\"
//   build/vmxmu IMG tree                 everything, with sizes
//   build/vmxmu IMG get PATH [OUT]       to OUT or stdout
//   build/vmxmu IMG put LOCAL PATH       makes the folders, replaces a file
//   build/vmxmu IMG rm PATH              a file or a whole folder
//   build/vmxmu IMG install GAME.VXP     as the VMX keeps games (56580001\<ID>0000\GAME.VXP + SaveMeta.xbx)
//   build/vmxmu IMG test                 the self-test (on a scratch image)
#include "../core/fatx.h"
#include "../core/vmxvol.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

static int rd(void *ctx, uint32_t s, int n, uint8_t *b) { FILE *f = ctx; return !fseek(f, (long)s * 512, SEEK_SET) && fread(b, 512, (size_t)n, f) == (size_t)n; }
static int wr(void *ctx, uint32_t s, int n, const uint8_t *b) { FILE *f = ctx; return !fseek(f, (long)s * 512, SEEK_SET) && fwrite(b, 512, (size_t)n, f) == (size_t)n; }
static uint8_t *slurp(const char *p, int *n) {
  FILE *f = fopen(p, "rb"); if (!f) return NULL;
  fseek(f, 0, SEEK_END); long k = ftell(f); fseek(f, 0, SEEK_SET);
  uint8_t *d = malloc((size_t)k + 1); if (d && fread(d, 1, (size_t)k, f) != (size_t)k) { free(d); d = NULL; }
  fclose(f); *n = (int)k; return d;
}
static void tree(Fatx *v, const char *path, int depth) {
  FatxEnt e[256]; int n = fatx_list(v, path, e, 256);
  for (int i = 0; i < n; i++) {
    char p[300]; snprintf(p, sizeof p, "%.200s%s%.42s", path, *path ? "\\" : "", e[i].name);
    printf("%*s%s%s", depth * 2, "", e[i].name, e[i].dir ? "\\\n" : "");
    if (e[i].dir) tree(v, p, depth + 1); else printf("  %u\n", (unsigned)e[i].size);
  }
}
static uint32_t now(void) { time_t t = time(NULL); struct tm *m = localtime(&t); return fatx_stamp(m->tm_year + 1900, m->tm_mon + 1, m->tm_mday, m->tm_hour, m->tm_min, m->tm_sec); }

// ---- the self-test: a model of what should be on the volume, checked after every step and after re-mounting
#define T(c) do { if (!(c)) { printf("FAIL line %d: %s\n", __LINE__, #c); return 1; } } while (0)
static unsigned rnd = 12345; static unsigned rn(void) { rnd = rnd * 1103515245u + 12345u; return rnd >> 16; }
static int selftest(const char *img) {
  FILE *f = fopen(img, "w+b"); T(f);
  Fatx v = { .read = rd, .write = wr, .ctx = f, .sectors = FATX_MU_BYTES / 512 };
  T(fatx_format(&v, 1)); fatx_unmount(&v); T(fatx_mount(&v));
  T(v.ent == 2 && v.clusterBytes == 16384 && v.nclusters == 512);           // 511 data clusters on 8 MB
  uint32_t full = fatx_free_bytes(&v); T(full == 510u * 16384);                                     // the root folder has one
  static uint8_t a[300000], b[300000];
  for (int i = 0; i < (int)sizeof a; i++) a[i] = (uint8_t)rn();
  T(fatx_write(&v, "A\\B\\C\\file.bin", a, 40000)); T(fatx_size(&v, "a/b/c/FILE.BIN") == 40000);
  T(fatx_read(&v, "A\\B\\C\\file.bin", b, sizeof b) == 40000 && !memcmp(a, b, 40000));
  T(fatx_size(&v, "A\\B") == -2 && fatx_size(&v, "A\\nope") == -1);
  T(fatx_write(&v, "A\\B\\C\\file.bin", a + 7, 5)); T(fatx_read(&v, "A\\B\\C\\file.bin", b, sizeof b) == 5 && !memcmp(a + 7, b, 5));
  T(fatx_write(&v, "empty", a, 0) && fatx_size(&v, "empty") == 0);
  // many files in one folder: past one cluster of entries (256), so the folder grows
  for (int i = 0; i < 300; i++) { char p[40]; snprintf(p, sizeof p, "many\\f%03d", i); T(fatx_write(&v, p, a + i, 100 + i)); }
  FatxEnt e[400]; T(fatx_list(&v, "many", e, 400) == 300);
  fatx_unmount(&v); T(fatx_mount(&v));
  for (int i = 0; i < 300; i += 37) { char p[40]; snprintf(p, sizeof p, "many\\F%03d", i); T(fatx_read(&v, p, b, sizeof b) == 100 + i && !memcmp(a + i, b, (size_t)(100 + i))); }
  T(fatx_remove(&v, "many")); T(fatx_size(&v, "many") == -1);
  T(fatx_remove(&v, "A")); T(fatx_remove(&v, "empty"));
  T(fatx_free_bytes(&v) == full);                                        // nothing leaked
  // fill it up: the write that doesn't fit fails and leaves the volume as it was
  int k = 0; for (;; k++) { char p[40]; snprintf(p, sizeof p, "fill\\%d", k); if (!fatx_write(&v, p, a, 300000)) break; }
  T(k == 26);                                                            // 19 clusters each, 1 for the folder
  uint32_t left = fatx_free_bytes(&v); T(left < 19u * 16384);
  T(fatx_write(&v, "fill\\0", a + 1, 300000) == 0);                       // replacing needs room for both copies
  T(fatx_read(&v, "fill\\0", b, sizeof b) == 300000);
  T(fatx_remove(&v, "fill") && fatx_free_bytes(&v) == full);
  // the VMX's layout
  T(vol_open(&v)); T(vol_save(&v, "recs", a, 999)); T(vol_load(&v, "recs", b, sizeof b) == 999 && !memcmp(a, b, 999));
  T(vol_install(&v, 0x56581001u, "BLOX", a, 70000)); T(vol_install(&v, 0x56581002u, "BLOBS", a, 9));
  uint32_t ids[8]; T(vol_games(&v, ids, 8) == 2);
  T(vol_remove(&v, 0x56581001u) && vol_games(&v, ids, 8) == 1 && ids[0] == 0x56581002u);
  fatx_unmount(&v); fclose(f);
  printf("fatx self-test ok\n"); return 0;
}

int main(int argc, char **argv) {
  if (argc < 3) { fprintf(stderr, "build/vmxmu IMG format|ls|tree|get|put|rm|install|test ...\n"); return 2; }
  const char *img = argv[1], *cmd = argv[2];
  if (!strcmp(cmd, "test")) return selftest(img);
  FILE *f;
  if (!strcmp(cmd, "format")) {
    long mb = argc > 3 ? atol(argv[3]) : 8;
    f = fopen(img, "r+b"); if (!f) f = fopen(img, "w+b"); if (!f) { perror(img); return 1; }
    fseek(f, 0, SEEK_END); if (ftell(f) < mb << 20) { fseek(f, (mb << 20) - 1, SEEK_SET); fputc(0, f); }
    Fatx v = { .read = rd, .write = wr, .ctx = f, .sectors = (uint32_t)(mb << 11) }; v.stamp = now();
    int ok = fatx_format(&v, (uint32_t)time(NULL)); printf(ok ? "%s: FATX, %u clusters of 16 KB\n" : "%s: format failed\n", img, (unsigned)v.nclusters - 1);
    fatx_unmount(&v); fclose(f); return !ok;
  }
  int write = !strcmp(cmd, "put") || !strcmp(cmd, "rm") || !strcmp(cmd, "install");
  f = fopen(img, write ? "r+b" : "rb"); if (!f) { perror(img); return 1; }
  fseek(f, 0, SEEK_END); long size = ftell(f);
  Fatx v = { .read = rd, .write = wr, .ctx = f, .sectors = (uint32_t)(size / 512) }; v.stamp = now();
  if (!fatx_mount(&v)) { fprintf(stderr, "%s: not a FATX volume\n", img); return 1; }
  int ok = 1;
  if (!strcmp(cmd, "ls")) {
    FatxEnt e[256]; int n = fatx_list(&v, argc > 3 ? argv[3] : "", e, 256); if (n < 0) { fprintf(stderr, "no such folder\n"); return 1; }
    for (int i = 0; i < n; i++) printf(e[i].dir ? "%s\\\n" : "%s  %u\n", e[i].name, (unsigned)e[i].size);
    printf("(%u KB free)\n", (unsigned)(fatx_free_bytes(&v) / 1024));
  } else if (!strcmp(cmd, "tree")) { tree(&v, "", 0); printf("(%u KB free)\n", (unsigned)(fatx_free_bytes(&v) / 1024)); }
  else if (!strcmp(cmd, "get") && argc > 3) {
    int n = fatx_size(&v, argv[3]); if (n < 0) { fprintf(stderr, "no such file\n"); return 1; }
    uint8_t *d = malloc((size_t)n + 1); fatx_read(&v, argv[3], d, n);
    FILE *o = argc > 4 ? fopen(argv[4], "wb") : stdout; fwrite(d, 1, (size_t)n, o); if (o != stdout) fclose(o);
  } else if (!strcmp(cmd, "put") && argc > 4) {
    int n; uint8_t *d = slurp(argv[3], &n); if (!d) { perror(argv[3]); return 1; }
    ok = fatx_write(&v, argv[4], d, n);
  } else if (!strcmp(cmd, "rm") && argc > 3) ok = fatx_remove(&v, argv[3]);
  else if (!strcmp(cmd, "install") && argc > 3) {
    int n; uint8_t *d = slurp(argv[3], &n); if (!d) { perror(argv[3]); return 1; }
    char title[24]; uint32_t id;
    ok = vol_open(&v) && vol_install_vxp(&v, d, n, &id, title);
    if (ok) printf("%s: %08X %s\n", argv[3], (unsigned)id, title);
  } else { fprintf(stderr, "bad command\n"); return 2; }
  fatx_unmount(&v); fclose(f);
  if (!ok) fprintf(stderr, "failed\n");
  return !ok;
}
