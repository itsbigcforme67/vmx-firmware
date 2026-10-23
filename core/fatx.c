// FATX: see fatx.h. The whole cluster table is kept in RAM (4 KB on a memory unit) and written back sector by sector
// after each change; a change writes the data first, then the directory entry, then the table, so a cut-off write
// leaves the old file or a lost cluster, never a file pointing at someone else's data.
#include "fatx.h"
#include <stdlib.h>
#include <string.h>

#define SECTOR 512
#define CLUSTER 16384
#define PAGE 4096
#define ENT 64

static uint32_t rd32(const uint8_t *p) { return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24; }
static void wr32(uint8_t *p, uint32_t v) { p[0] = (uint8_t)v; p[1] = (uint8_t)(v >> 8); p[2] = (uint8_t)(v >> 16); p[3] = (uint8_t)(v >> 24); }

uint32_t fatx_stamp(int year, int mon, int day, int hour, int min, int sec) {
  if (year < 2000) year = 2000;
  return (uint32_t)(year - 2000) << 25 | (uint32_t)mon << 21 | (uint32_t)day << 16 | (uint32_t)hour << 11 | (uint32_t)min << 5 | (uint32_t)(sec / 2);
}

// ---- the cluster table
static uint32_t get(Fatx *v, uint32_t c) { return v->ent == 2 ? (uint32_t)(v->fat[c * 2] | v->fat[c * 2 + 1] << 8) : rd32(v->fat + c * 4); }
static void set(Fatx *v, uint32_t c, uint32_t x) {
  if (v->ent == 2) { v->fat[c * 2] = (uint8_t)x; v->fat[c * 2 + 1] = (uint8_t)(x >> 8); } else wr32(v->fat + c * 4, x);
  uint32_t s = c * (uint32_t)v->ent / SECTOR; v->fatDirty[s >> 3] |= (uint8_t)(1 << (s & 7));
}
static uint32_t eoc(Fatx *v) { return v->ent == 2 ? 0xFFFF : 0xFFFFFFFF; }
static int last(Fatx *v, uint32_t x) { return v->ent == 2 ? x >= 0xFFF0 : x >= 0xFFFFFFF0; }
static int valid(Fatx *v, uint32_t c) { return c >= 1 && c < v->nclusters; }
static int flush_fat(Fatx *v) {
  int ok = 1;
  for (uint32_t s = 0; s < v->fatBytes / SECTOR; s++)
    if (v->fatDirty[s >> 3] >> (s & 7) & 1) {
      ok &= v->write(v->ctx, PAGE / SECTOR + s, 1, v->fat + s * SECTOR);
      v->fatDirty[s >> 3] &= (uint8_t)~(1 << (s & 7));
    }
  return ok;
}
static uint32_t alloc1(Fatx *v, uint32_t after) {      // a free cluster (searching from after), marked end of chain
  for (uint32_t k = 0; k < v->nclusters; k++) {
    uint32_t c = (after + k) % v->nclusters; if (!c) continue;
    if (!get(v, c)) { set(v, c, eoc(v)); return c; }
  }
  return 0;
}
static void free_chain(Fatx *v, uint32_t c) {
  for (int guard = 0; valid(v, c) && guard < (int)v->nclusters; guard++) { uint32_t n = get(v, c); set(v, c, 0); if (last(v, n)) break; c = n; }
}
static uint32_t sector_of(Fatx *v, uint32_t c) { return v->dataSector + (c - 1) * v->spc; }
static int rd_cl(Fatx *v, uint32_t c) { return v->read(v->ctx, sector_of(v, c), (int)v->spc, v->cl); }
static int wr_cl(Fatx *v, uint32_t c) { return v->write(v->ctx, sector_of(v, c), (int)v->spc, v->cl); }

// ---- format / mount
static int geometry(Fatx *v, uint32_t spc) {
  uint64_t bytes = (uint64_t)v->sectors * SECTOR;
  v->spc = spc; v->clusterBytes = spc * SECTOR;
  uint32_t n = (uint32_t)(bytes / v->clusterBytes) + 1;          // as nxFormatVolume and the kernel
  v->ent = n < 65520 ? 2 : 4;
  v->fatBytes = (n * (uint32_t)v->ent + PAGE - 1) & ~(uint32_t)(PAGE - 1);
  v->dataSector = (PAGE + v->fatBytes) / SECTOR;
  uint32_t fit = (uint32_t)((bytes - (uint64_t)v->dataSector * SECTOR) / v->clusterBytes);   // clusters that are on the device
  v->nclusters = fit + 1 < n ? fit + 1 : n;
  if (PAGE + v->fatBytes + v->clusterBytes >= bytes) return 0;
  free(v->fat); free(v->fatDirty); free(v->cl);
  v->fat = malloc(v->fatBytes); v->fatDirty = calloc(v->fatBytes / SECTOR / 8 + 1, 1); v->cl = malloc(v->clusterBytes);
  return v->fat && v->fatDirty && v->cl;
}
int fatx_format(Fatx *v, uint32_t volumeId) {
  if (!geometry(v, CLUSTER / SECTOR)) return 0;
  uint8_t sb[PAGE]; memset(sb, 0xFF, sizeof sb);
  memcpy(sb, "FATX", 4); wr32(sb + 4, volumeId); wr32(sb + 8, v->spc); wr32(sb + 12, 1); sb[16] = sb[17] = 0;
  if (!v->write(v->ctx, 0, PAGE / SECTOR, sb)) return 0;
  memset(v->fat, 0, v->fatBytes);
  if (v->ent == 2) { v->fat[0] = 0xF8; v->fat[1] = v->fat[2] = v->fat[3] = 0xFF; } else { wr32(v->fat, 0xFFFFFFF8); wr32(v->fat + 4, 0xFFFFFFFF); }
  for (uint32_t s = 0; s < v->fatBytes / SECTOR; s++) if (!v->write(v->ctx, PAGE / SECTOR + s, 1, v->fat + s * SECTOR)) return 0;
  memset(v->cl, 0xFF, v->clusterBytes); v->root = 1;
  return wr_cl(v, 1);
}
int fatx_mount(Fatx *v) {
  uint8_t sb[SECTOR];
  if (!v->read(v->ctx, 0, 1, sb) || memcmp(sb, "FATX", 4)) return 0;
  uint32_t spc = rd32(sb + 8);
  if (!spc || spc > 128 || (spc & (spc - 1)) || !geometry(v, spc)) return 0;
  v->root = rd32(sb + 12);
  for (uint32_t s = 0; s < v->fatBytes / SECTOR; s++) if (!v->read(v->ctx, PAGE / SECTOR + s, 1, v->fat + s * SECTOR)) return 0;
  return valid(v, v->root);
}
void fatx_unmount(Fatx *v) { free(v->fat); free(v->fatDirty); free(v->cl); v->fat = v->fatDirty = v->cl = NULL; }

// ---- folders
typedef struct { uint32_t cl; int off; } Slot;          // where an entry is: its cluster and byte offset
static int name_eq(const uint8_t *e, const char *name, int len) {
  if (e[0] != len) return 0;
  for (int i = 0; i < len; i++) { int a = e[2 + i], b = (unsigned char)name[i]; if (a >= 'a' && a <= 'z') a -= 32; if (b >= 'a' && b <= 'z') b -= 32; if (a != b) return 0; }
  return 1;
}
// finds name (len bytes) in the folder starting at dir; copies its entry to e. 1 = found
static int find_in(Fatx *v, uint32_t dir, const char *name, int len, uint8_t e[ENT], Slot *at) {
  for (int guard = 0; valid(v, dir) && guard < (int)v->nclusters; guard++) {
    if (!rd_cl(v, dir)) return 0;
    for (uint32_t o = 0; o < v->clusterBytes; o += ENT) {
      const uint8_t *p = v->cl + o;
      if (p[0] == 0x00 || p[0] == 0xFF) return 0;
      if (p[0] != 0xE5 && p[0] <= 42 && name_eq(p, name, len)) { memcpy(e, p, ENT); if (at) { at->cl = dir; at->off = (int)o; } return 1; }
    }
    uint32_t n = get(v, dir); if (last(v, n)) break; dir = n;
  }
  return 0;
}
// walks path; *dir = the folder holding the last part, *name / *len = the last part. 1 = the folders exist
static int walk(Fatx *v, const char *path, uint32_t *dir, const char **name, int *len) {
  uint32_t d = v->root;
  for (;;) {
    while (*path == '/' || *path == '\\') path++;
    const char *s = path; while (*s && *s != '/' && *s != '\\') s++;
    const char *t = s; while (*t == '/' || *t == '\\') t++;
    if (!*t) { *dir = d; *name = path; *len = (int)(s - path); return 1; }
    uint8_t e[ENT]; if (!find_in(v, d, path, (int)(s - path), e, NULL) || !(e[1] & 0x10)) return 0;
    d = rd32(e + 0x2C); path = t;
  }
}
static int put_entry(Fatx *v, uint32_t dir, const uint8_t e[ENT]) {   // into a free slot, growing the folder if full
  uint32_t prev = 0;
  for (int guard = 0; valid(v, dir) && guard < (int)v->nclusters; guard++) {
    if (!rd_cl(v, dir)) return 0;
    for (uint32_t o = 0; o < v->clusterBytes; o += ENT) {
      uint8_t *p = v->cl + o;
      if (p[0] == 0x00 || p[0] == 0xFF || p[0] == 0xE5) { memcpy(p, e, ENT); return wr_cl(v, dir); }
    }
    prev = dir; uint32_t n = get(v, dir); if (last(v, n)) break; dir = n;
  }
  if (!prev) return 0;
  uint32_t c = alloc1(v, prev); if (!c) return 0;
  set(v, prev, c);
  memset(v->cl, 0xFF, v->clusterBytes); memcpy(v->cl, e, ENT);
  return wr_cl(v, c) && flush_fat(v);
}
static void make_entry(Fatx *v, uint8_t e[ENT], const char *name, int len, int attr, uint32_t first, uint32_t size) {
  memset(e, 0xFF, ENT); e[0] = (uint8_t)len; e[1] = (uint8_t)attr; memcpy(e + 2, name, (size_t)len);
  uint32_t t = v->stamp ? v->stamp : fatx_stamp(2001, 1, 1, 0, 0, 0);
  wr32(e + 0x2C, first); wr32(e + 0x30, size); wr32(e + 0x34, t); wr32(e + 0x38, t); wr32(e + 0x3C, t);
}
static int ok_name(const char *name, int len) {
  if (len < 1 || len > 42) return 0;
  for (int i = 0; i < len; i++) if (strchr("<>:\"|?*", name[i]) || (unsigned char)name[i] < 32 || (unsigned char)name[i] > 126) return 0;
  return 1;
}
static int mkdir_in(Fatx *v, uint32_t dir, const char *name, int len, uint32_t *out) {
  uint8_t e[ENT];
  if (find_in(v, dir, name, len, e, NULL)) { *out = rd32(e + 0x2C); return (e[1] & 0x10) != 0; }
  if (!ok_name(name, len)) return 0;
  uint32_t c = alloc1(v, dir); if (!c) return 0;
  memset(v->cl, 0xFF, v->clusterBytes);
  if (!wr_cl(v, c)) return 0;
  make_entry(v, e, name, len, 0x10, c, 0);
  if (!put_entry(v, dir, e)) { set(v, c, 0); return 0; }
  *out = c; return flush_fat(v);
}
int fatx_mkdir(Fatx *v, const char *path) {
  uint32_t d = v->root;
  for (;;) {
    while (*path == '/' || *path == '\\') path++;
    if (!*path) return 1;
    const char *s = path; while (*s && *s != '/' && *s != '\\') s++;
    if (!mkdir_in(v, d, path, (int)(s - path), &d)) return 0;
    path = s;
  }
}

// ---- files
int fatx_size(Fatx *v, const char *path) {
  uint32_t dir; const char *nm; int len; uint8_t e[ENT];
  if (!walk(v, path, &dir, &nm, &len) || !find_in(v, dir, nm, len, e, NULL)) return -1;
  return e[1] & 0x10 ? -2 : (int)rd32(e + 0x30);
}
int fatx_read(Fatx *v, const char *path, void *buf, int max) {
  uint32_t dir; const char *nm; int len; uint8_t e[ENT];
  if (!walk(v, path, &dir, &nm, &len) || !find_in(v, dir, nm, len, e, NULL) || (e[1] & 0x10)) return -1;
  uint32_t size = rd32(e + 0x30), c = rd32(e + 0x2C); int n = (int)size < max ? (int)size : max, got = 0;
  while (got < n && valid(v, c)) {
    if (!rd_cl(v, c)) return -1;
    int k = n - got < (int)v->clusterBytes ? n - got : (int)v->clusterBytes;
    memcpy((uint8_t *)buf + got, v->cl, (size_t)k); got += k;
    uint32_t nx = get(v, c); if (last(v, nx)) break; c = nx;
  }
  return got;
}
int fatx_write(Fatx *v, const char *path, const void *buf, int n) {
  uint32_t dir; const char *nm; int len; uint8_t e[ENT]; Slot at;
  {                                                   // the folders first
    const char *s = path + strlen(path); while (s > path && s[-1] != '/' && s[-1] != '\\') s--;
    if (s > path) { char d[256]; size_t k = (size_t)(s - path) < sizeof d ? (size_t)(s - path) : sizeof d - 1; memcpy(d, path, k); d[k] = 0; if (!fatx_mkdir(v, d)) return 0; }
  }
  if (!walk(v, path, &dir, &nm, &len) || !ok_name(nm, len)) return 0;
  int had = find_in(v, dir, nm, len, e, &at);
  if (had && (e[1] & 0x10)) return 0;
  uint32_t first = 0, prev = 0, hint = dir;            // the new data, in fresh clusters
  for (int done = 0; done < n; ) {
    uint32_t c = alloc1(v, hint); if (!c) { if (first) free_chain(v, first); return 0; }
    if (prev) set(v, prev, c); else first = c;
    int k = n - done < (int)v->clusterBytes ? n - done : (int)v->clusterBytes;
    memset(v->cl, 0, v->clusterBytes); memcpy(v->cl, (const uint8_t *)buf + done, (size_t)k);
    if (!wr_cl(v, c)) { free_chain(v, first); return 0; }
    done += k; prev = hint = c;
  }
  uint32_t old = had ? rd32(e + 0x2C) : 0;
  make_entry(v, e, nm, len, 0, first, (uint32_t)n);
  if (had) {                                          // the entry in place, keeping its created stamp
    if (!rd_cl(v, at.cl)) return 0;
    memcpy(e + 0x34, v->cl + at.off + 0x34, 4);
    memcpy(v->cl + at.off, e, ENT);
    if (!wr_cl(v, at.cl)) return 0;
  } else if (!put_entry(v, dir, e)) { if (first) free_chain(v, first); flush_fat(v); return 0; }
  if (old) free_chain(v, old);
  return flush_fat(v);
}
static int remove_tree(Fatx *v, uint32_t dir, int depth) {   // everything inside a folder's chain
  if (depth > 16) return 0;
  uint32_t c = dir;
  for (int guard = 0; valid(v, c) && guard < (int)v->nclusters; guard++) {
    if (!rd_cl(v, c)) return 0;
    for (uint32_t o = 0; o < v->clusterBytes; o += ENT) {
      const uint8_t *p = v->cl + o;
      if (p[0] == 0x00 || p[0] == 0xFF) return 1;
      if (p[0] == 0xE5 || p[0] > 42) continue;
      uint32_t f = rd32(p + 0x2C);
      if (p[1] & 0x10) { if (!remove_tree(v, f, depth + 1) || !rd_cl(v, c)) return 0; }   // (it used the buffer)
      if (f) free_chain(v, f);
    }
    uint32_t n = get(v, c); if (last(v, n)) break; c = n;
  }
  return 1;
}
int fatx_remove(Fatx *v, const char *path) {
  uint32_t dir; const char *nm; int len; uint8_t e[ENT]; Slot at;
  if (!walk(v, path, &dir, &nm, &len) || !len || !find_in(v, dir, nm, len, e, &at)) return 0;
  uint32_t first = rd32(e + 0x2C);
  if ((e[1] & 0x10) && !remove_tree(v, first, 0)) return 0;
  if (!rd_cl(v, at.cl)) return 0;
  v->cl[at.off] = 0xE5;
  if (!wr_cl(v, at.cl)) return 0;
  if (first) free_chain(v, first);
  return flush_fat(v);
}
int fatx_list(Fatx *v, const char *path, FatxEnt *out, int max) {
  uint32_t d = v->root;
  const char *p = path; while (*p == '/' || *p == '\\') p++;
  if (*p) {
    uint32_t dir; const char *nm; int len; uint8_t e[ENT];
    if (!walk(v, path, &dir, &nm, &len)) return -1;
    if (len) { if (!find_in(v, dir, nm, len, e, NULL) || !(e[1] & 0x10)) return -1; d = rd32(e + 0x2C); } else d = dir;
  }
  int n = 0;
  for (int guard = 0; valid(v, d) && guard < (int)v->nclusters; guard++) {
    if (!rd_cl(v, d)) return -1;
    for (uint32_t o = 0; o < v->clusterBytes; o += ENT) {
      const uint8_t *q = v->cl + o;
      if (q[0] == 0x00 || q[0] == 0xFF) return n;
      if (q[0] == 0xE5 || q[0] > 42 || n >= max) continue;
      FatxEnt *f = &out[n++]; memcpy(f->name, q + 2, q[0]); f->name[q[0]] = 0;
      f->dir = (q[1] & 0x10) != 0; f->first = rd32(q + 0x2C); f->size = rd32(q + 0x30); f->stamp = rd32(q + 0x38);
    }
    uint32_t nx = get(v, d); if (last(v, nx)) break; d = nx;
  }
  return n;
}
uint32_t fatx_free_bytes(Fatx *v) {
  uint32_t n = 0; for (uint32_t c = 1; c < v->nclusters; c++) if (!get(v, c)) n++;
  return n * v->clusterBytes;
}
