// VMX runtime: runs a VMX game image (.vxi, made by sdk/vxlink from a C# 1.0 assembly). Plain C99, no allocation
// after vx_load, one fixed heap (128 KB on the VMX) for the game's objects (mark-sweep collected). The same code runs on the VMX
// (ESP32-S3), the PC test build, the Xbox simulator and the browser.
//
// Image (little-endian, written by sdk/vxlink/Program.cs Linker.Write):
//   "VXIL" u32 version, then counts: types methods statics strings blobs natives,
//   entry type, entry .ctor, Game vtable slots of Start Tick Draw get_UsesB, type indices of Screen String Object
//   Exception; types (44 bytes each), methods (36 bytes each), static kinds (1 byte each, padded to 4), strings
//   (offset, length into the UTF-16 pool), blobs (offset, length), natives (method, name), then pools with a u32
//   length first: vtables, ref-field offsets, interface map pairs, kinds, code, string pool, blob pool, names.
#ifndef VXRT_H
#define VXRT_H
#include <stdint.h>
#include <stddef.h>
#ifdef __cplusplus
extern "C" {
#endif

// value kinds (storage); the eval stack uses tags (I4 I8 F REF PTR)
enum { VK_VOID, VK_BOOL, VK_I1, VK_U1, VK_I2, VK_U2, VK_I4, VK_U4, VK_I8, VK_U8, VK_R4, VK_R8, VK_REF, VK_PTR };

typedef struct Vx Vx;
typedef struct {                     // what the game can reach: the platform fills these in
  uint8_t *screen;                   // 64 x 64, 1 = lit
  void (*tone)(int hz, int ms, int volume);
  void (*report)(int score, int f1, int f2, int f3);
  int  (*load)(int gameId, uint8_t *buf, int max);   // gameId 0 = this game; returns bytes, -1 = none
  void (*save)(const uint8_t *buf, int n);
  void (*text)(uint8_t *screen, int x, int y, const char *s, int on);   // the VMX font
  // a packed data file: copies up to n bytes from offset into dst, returns how many; dst NULL: returns its length
  // (-1 if there is no such file)
  int  (*file)(const char *name, int offset, uint8_t *dst, int n);
} VxHost;
#define VX_SAVE_MAX 16384

// a runtime in `mem` (it needs vx_need(image) bytes plus the heap); returns NULL with an error in errbuf
size_t vx_need(const uint8_t *image, size_t len, size_t heap);
Vx *vx_load(void *mem, size_t memlen, const uint8_t *image, size_t len, size_t heap, const VxHost *host, char *errbuf, int errlen);
// the same with the game heap in its own block: `mem` (vx_need(image, len, 0) bytes: the interpreter's stacks and
// tables) can then go in fast internal RAM while the heap goes in big, slower RAM (PSRAM on the ESP32-S3)
Vx *vx_load_split(void *mem, size_t memlen, void *heap, size_t heaplen, const uint8_t *image, size_t len, const VxHost *host, char *errbuf, int errlen);
// the game's calls; each returns 0, or -1 after a game error (vx_error says what; the game is stopped)
int vx_start(Vx *vm, int seed, int demo);
int vx_tick(Vx *vm, int held, int pressed);
int vx_draw(Vx *vm);
int vx_uses_b(Vx *vm);
const char *vx_error(Vx *vm);
// for tests: instructions run, heap in use, collections
void vx_stats(Vx *vm, uint32_t *instrs, uint32_t *heapUsed, uint32_t *gcs);

#ifdef __cplusplus
}
#endif
#endif
