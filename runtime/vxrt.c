// VMX runtime: the IL interpreter, objects, the collector and the native methods. See vxrt.h.
#include "vxrt.h"
#include <math.h>
#include <setjmp.h>
#include <stdio.h>
#include <string.h>

// on the ESP32 the interpreter's hot code goes in instruction RAM (flash-cache misses made it several times slower)
#if defined(ESP_PLATFORM) || defined(ARDUINO)
#include "esp_attr.h"
#define VX_HOT IRAM_ATTR
#else
#define VX_HOT
#endif
#define STACK_SLOTS 2048
#define MAX_FRAMES 256
enum { T_NONE, T_I4, T_I8, T_F, T_REF, T_PTR };
enum { SP_HEAP = 1, SP_STACK = 2, SP_STATIC = 3 };
#define HDR 12                       // object header: size|flags, type (or array element), length
#define F_MARK 0x80000000u
#define F_FREE 0x40000000u
#define F_ARRAY 0x20000000u
#define SIZE_MASK 0x00FFFFFFu
#define NO_TYPE 0xFFFFFFu

typedef union { int32_t i; uint32_t u; int64_t l; uint64_t ul; double f; } Slot;
typedef struct {
  int32_t parent; uint16_t flags; uint8_t prim; uint32_t instSize, vtOff, nVirt, refOff, nRefs, imapOff, nImap; int32_t cctor; uint32_t name;
} VxType;
typedef void (*Native)(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt);
typedef struct {
  uint32_t codeOff, codeLen; uint16_t nArgs, nLocals, maxStack, flags; int32_t vslot; uint8_t ret; uint32_t kindsOff, owner, name; Native native;
} VxMethod;
enum { MF_STATIC = 1, MF_VIRTUAL = 2, MF_ABSTRACT = 4, MF_NATIVE = 8, MF_IFACE = 16, MF_VALUE = 32 };
typedef struct { int32_t m; uint32_t ip, bp, lp; uint32_t pushObj; } Frame;

struct Vx {
  const VxHost *host; jmp_buf jb; char err[96]; int running;
  uint32_t nTypes, nMethods, nStatics, nStrings, nBlobs;
  uint32_t entry, entryCtor, slotStart, slotTick, slotDraw, slotUsesB, tScreen, tString, tObject, tException;
  VxType *types; VxMethod *methods; uint8_t *staticKinds; Slot *statics; uint8_t *staticTags;
  const uint32_t *strOff, *strLen, *blobOff, *blobLen; uint32_t *strRef;
  const uint8_t *vt, *refs, *imap, *kinds, *code, *strPool, *blobPool; const char *names;
  uint8_t *heap; uint32_t heapSize, top; uint32_t *markStack; uint32_t markCap;
  Slot st[STACK_SLOTS]; uint8_t tg[STACK_SLOTS]; uint32_t sp;
  Frame fr[MAX_FRAMES]; int nfr;
  uint32_t game, screen;
  uint32_t instrs, gcs;
};

static uint16_t r16(const uint8_t *p) { return (uint16_t)(p[0] | p[1] << 8); }
static uint32_t r32(const uint8_t *p) { return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24; }
static uint32_t vtAt(Vx *vm, uint32_t i) { return r32(vm->vt + 4 * i); }

static void fail(Vx *vm, const char *fmt, const char *a) {
  snprintf(vm->err, sizeof vm->err, fmt, a ? a : "");
  longjmp(vm->jb, 1);
}

// ---------------------------------------------------------------- heap
static uint32_t *hw(Vx *vm, uint32_t ref) { return (uint32_t *)(vm->heap + ref); }    // header words (heap is 4-aligned)
static uint32_t objType(Vx *vm, uint32_t ref) { return hw(vm, ref)[1]; }
static int isArray(Vx *vm, uint32_t ref) { return (hw(vm, ref)[0] & F_ARRAY) != 0; }
static uint32_t arrLen(Vx *vm, uint32_t ref) { return hw(vm, ref)[2]; }
static uint8_t *data(Vx *vm, uint32_t ref) { return vm->heap + ref + HDR; }
static int ksize(int k) { switch (k) { case VK_BOOL: case VK_I1: case VK_U1: return 1; case VK_I2: case VK_U2: return 2;
  case VK_I8: case VK_U8: case VK_R8: return 8; default: return 4; } }

static void gc(Vx *vm);
static uint32_t alloc_raw(Vx *vm, uint32_t bytes) {
  uint32_t need = (HDR + bytes + 3) & ~3u;
  for (int pass = 0; pass < 2; pass++) {
    if (vm->top + need <= vm->heapSize) { uint32_t r = vm->top; vm->top += need; memset(vm->heap + r, 0, need); hw(vm, r)[0] = need; return r; }
    for (uint32_t r = 4; r < vm->top; ) {                               // first fit in the freed blocks
      uint32_t w = hw(vm, r)[0], sz = w & SIZE_MASK;
      if ((w & F_FREE) && sz >= need) {
        if (sz - need >= HDR + 4) { hw(vm, r + need)[0] = (sz - need) | F_FREE; sz = need; }
        memset(vm->heap + r, 0, sz); hw(vm, r)[0] = sz; return r;
      }
      r += sz;
    }
    if (pass == 0) gc(vm);
  }
  fail(vm, "OUT OF MEMORY%s", NULL); return 0;
}
static uint32_t newObj(Vx *vm, uint32_t type) {
  uint32_t r = alloc_raw(vm, vm->types[type].instSize < 8 ? 8 : vm->types[type].instSize);
  hw(vm, r)[1] = type; return r;
}
static uint32_t newArr(Vx *vm, uint32_t desc, int32_t n) {
  if (n < 0) fail(vm, "NEGATIVE ARRAY SIZE%s", NULL);
  int k = desc >> 24; uint32_t r = alloc_raw(vm, (uint32_t)n * ksize(k));
  hw(vm, r)[0] |= F_ARRAY; hw(vm, r)[1] = desc; hw(vm, r)[2] = (uint32_t)n; return r;
}
static uint32_t newStr(Vx *vm, const uint16_t *c, uint32_t n) {
  uint32_t r = alloc_raw(vm, n * 2);
  hw(vm, r)[1] = vm->tString; hw(vm, r)[2] = n;
  if (c) memcpy(data(vm, r), c, n * 2);
  return r;
}
static uint32_t newStrA(Vx *vm, const char *s) {
  uint32_t n = (uint32_t)strlen(s), r = newStr(vm, NULL, n);
  uint16_t *d = (uint16_t *)data(vm, r); for (uint32_t i = 0; i < n; i++) d[i] = (uint8_t)s[i];
  return r;
}

// collection: mark from the eval stack, statics, interned strings, the game and the screen; then sweep
static void markPush(Vx *vm, uint32_t r) {
  if (!r || r >= vm->top) return;
  uint32_t *h = hw(vm, r); if (h[0] & (F_MARK | F_FREE)) return;
  h[0] |= F_MARK;
  if (vm->markCap) vm->markStack[--vm->markCap] = r;
}
static void gc(Vx *vm) {
  vm->gcs++;
  uint32_t cap = vm->markCap;
  for (uint32_t i = 0; i < vm->sp; i++) {
    if (vm->tg[i] == T_REF) markPush(vm, vm->st[i].u);
    else if (vm->tg[i] == T_PTR && (vm->st[i].ul >> 48 & 0xFF) == SP_HEAP) markPush(vm, (uint32_t)vm->st[i].ul);
  }
  for (uint32_t i = 0; i < vm->nStatics; i++) if (vm->staticTags[i] == T_REF) markPush(vm, vm->statics[i].u);
  for (uint32_t i = 0; i < vm->nStrings; i++) markPush(vm, vm->strRef[i]);
  markPush(vm, vm->game); markPush(vm, vm->screen);
  for (int i = 0; i < vm->nfr; i++) if (vm->fr[i].pushObj) markPush(vm, vm->fr[i].pushObj);
  while (vm->markCap < cap) {
    uint32_t r = vm->markStack[vm->markCap++], *h = hw(vm, r);
    if (h[0] & F_ARRAY) {
      if ((h[1] >> 24) == VK_REF) for (uint32_t i = 0; i < h[2]; i++) markPush(vm, r32(data(vm, r) + 4 * i));
    } else if (h[1] != vm->tString) {
      const VxType *t = &vm->types[h[1]];
      for (uint32_t i = 0; i < t->nRefs; i++) markPush(vm, r32(data(vm, r) + r32(vm->refs + 4 * (t->refOff + i))));
    }
  }
  if (vm->markCap != cap) fail(vm, "GC MARK OVERFLOW%s", NULL);
  uint32_t r = 4, lastFree = 0;
  while (r < vm->top) {                                                    // sweep, joining neighbouring free blocks
    uint32_t *h = hw(vm, r), sz = h[0] & SIZE_MASK;
    if (h[0] & F_MARK) { h[0] &= ~F_MARK; lastFree = 0; }
    else {
      if (lastFree) { hw(vm, lastFree)[0] = ((hw(vm, lastFree)[0] & SIZE_MASK) + sz) | F_FREE; }
      else { h[0] = sz | F_FREE; lastFree = r; }
    }
    r += sz;
  }
  if (lastFree) vm->top = lastFree;                                        // a free tail goes back to the bump space
}

// ---------------------------------------------------------------- values
static VX_HOT void push(Vx *vm, uint8_t tag, Slot v) { if (vm->sp >= STACK_SLOTS) fail(vm, "STACK OVERFLOW%s", NULL); vm->st[vm->sp] = v; vm->tg[vm->sp++] = tag; }
static void pushI(Vx *vm, int32_t i) { Slot s; s.l = 0; s.i = i; push(vm, T_I4, s); }
static void pushR(Vx *vm, uint32_t r) { Slot s; s.l = 0; s.u = r; push(vm, T_REF, s); }
static void pushF(Vx *vm, double f) { Slot s; s.f = f; push(vm, T_F, s); }
static void pushL(Vx *vm, int64_t l) { Slot s; s.l = l; push(vm, T_I8, s); }
static uint8_t tagOf(int k) { switch (k) { case VK_I8: case VK_U8: return T_I8; case VK_R4: case VK_R8: return T_F; case VK_REF: return T_REF; case VK_PTR: return T_PTR; default: return T_I4; } }
// read / write a value of kind k at p (any alignment)
static VX_HOT void loadK(int k, const uint8_t *p, Slot *v, uint8_t *t) {
  v->l = 0; *t = tagOf(k);
  switch (k) {
    case VK_BOOL: case VK_U1: v->i = p[0]; break;
    case VK_I1: v->i = (int8_t)p[0]; break;
    case VK_I2: { int16_t x; memcpy(&x, p, 2); v->i = x; } break;
    case VK_U2: { uint16_t x; memcpy(&x, p, 2); v->i = x; } break;
    case VK_R4: { float x; memcpy(&x, p, 4); v->f = x; } break;
    case VK_R8: memcpy(&v->f, p, 8); break;
    case VK_I8: case VK_U8: memcpy(&v->l, p, 8); break;
    default: memcpy(&v->u, p, 4); break;
  }
}
static VX_HOT void storeK(int k, uint8_t *p, Slot v) {
  switch (k) {
    case VK_BOOL: case VK_U1: case VK_I1: p[0] = (uint8_t)v.i; break;
    case VK_I2: case VK_U2: { uint16_t x = (uint16_t)v.i; memcpy(p, &x, 2); } break;
    case VK_R4: { float x = (float)v.f; memcpy(p, &x, 4); } break;
    case VK_R8: memcpy(p, &v.f, 8); break;
    case VK_I8: case VK_U8: memcpy(p, &v.l, 8); break;
    default: memcpy(p, &v.u, 4); break;
  }
}
// a slot (local / arg / static) keeps its kind's normal form: small ints truncated, R4 rounded
static VX_HOT Slot norm(int k, Slot v) {
  Slot o; o.l = 0;
  switch (k) {
    case VK_BOOL: case VK_U1: o.i = (uint8_t)v.i; break; case VK_I1: o.i = (int8_t)v.i; break;
    case VK_I2: o.i = (int16_t)v.i; break; case VK_U2: o.i = (uint16_t)v.i; break;
    case VK_R4: o.f = (float)v.f; break;
    default: o = v; break;
  }
  return o;
}
static Slot mkptr(int space, uint32_t base, uint32_t off, int kind) { Slot s; s.ul = (uint64_t)base | (uint64_t)(off & 0xFFFF) << 32 | (uint64_t)space << 48 | (uint64_t)kind << 56; return s; }
static uint8_t *ptrBytes(Vx *vm, Slot p) {             // heap targets only
  uint32_t base = (uint32_t)p.ul, off = (uint32_t)(p.ul >> 32 & 0xFFFF);
  if (!base) fail(vm, "NULL REFERENCE%s", NULL);
  return vm->heap + base + off;
}
static void ptrLoad(Vx *vm, Slot p, int k, Slot *v, uint8_t *t) {
  int sp = (int)(p.ul >> 48 & 0xFF); uint32_t base = (uint32_t)p.ul;
  if (sp == SP_HEAP) { loadK(k, ptrBytes(vm, p), v, t); return; }
  Slot s = sp == SP_STACK ? vm->st[base] : vm->statics[base];
  *t = tagOf(k); *v = norm(k, s); if (k == VK_REF) *t = T_REF;
}
static void ptrStore(Vx *vm, Slot p, int k, Slot v) {
  int sp = (int)(p.ul >> 48 & 0xFF); uint32_t base = (uint32_t)p.ul;
  if (sp == SP_HEAP) { storeK(k, ptrBytes(vm, p), v); return; }
  int pk = (int)(p.ul >> 56); if (k == VK_VOID) k = pk;
  if (sp == SP_STACK) { vm->st[base] = norm(pk ? pk : k, v); }
  else { vm->statics[base] = norm(vm->staticKinds[base], v); }
}

static int isInst(Vx *vm, uint32_t r, uint32_t type) {
  if (!r) return 0;
  if (type == 0xFFFFFE) return isArray(vm, r);
  if (type == vm->tObject) return 1;
  if (isArray(vm, r)) return 0;
  uint32_t t = objType(vm, r);
  if (vm->types[type].flags & 1) {                       // an interface: does the type map any of its methods?
    const VxType *T = &vm->types[t];
    for (uint32_t i = 0; i < T->nImap; i++) if (vm->methods[r32(vm->imap + 8 * (T->imapOff + i))].owner == type) return 1;
    return 0;
  }
  for (int32_t p = (int32_t)t; p >= 0; p = vm->types[p].parent) if ((uint32_t)p == type) return 1;
  return 0;
}

// ---------------------------------------------------------------- natives
static const uint16_t *strChars(Vx *vm, uint32_t r) { return (const uint16_t *)data(vm, r); }
static void needObj(Vx *vm, uint32_t r) { if (!r) fail(vm, "NULL REFERENCE%s", NULL); }
static Slot thisValue(Vx *vm, Slot *a, uint8_t *at, int k) {   // `this` of a primitive: a pointer, or a boxed value
  Slot v; uint8_t t;
  if (at[0] == T_PTR) ptrLoad(vm, a[0], k, &v, &t);
  else if (at[0] == T_REF) { needObj(vm, a[0].u); loadK(k, data(vm, a[0].u), &v, &t); }
  else v = a[0];
  return v;
}
#define RET_R(x) do { ret->l = 0; ret->u = (x); *rt = T_REF; } while (0)
#define RET_I(x) do { ret->l = 0; ret->i = (x); *rt = T_I4; } while (0)
#define RET_F(x) do { ret->f = (x); *rt = T_F; } while (0)
static void n_i4ToString(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { char b[16]; snprintf(b, sizeof b, "%d", (int)thisValue(vm, a, at, VK_I4).i); RET_R(newStrA(vm, b)); }
static void n_u4ToString(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { char b[16]; snprintf(b, sizeof b, "%u", (unsigned)thisValue(vm, a, at, VK_U4).u); RET_R(newStrA(vm, b)); }
static void n_u1ToString(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { char b[8]; snprintf(b, sizeof b, "%u", (unsigned)thisValue(vm, a, at, VK_U1).u); RET_R(newStrA(vm, b)); }
static void n_charToString(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { uint16_t c = (uint16_t)thisValue(vm, a, at, VK_U2).u; RET_R(newStr(vm, &c, 1)); }
static void n_arrLen(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { needObj(vm, a[0].u); RET_I((int32_t)arrLen(vm, a[0].u)); }
static void n_arrCopy(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  uint32_t s = a[0].u, d = a[2].u; int32_t si = a[1].i, di = a[3].i, n = a[4].i; needObj(vm, s); needObj(vm, d);
  if (hw(vm, s)[1] >> 24 != hw(vm, d)[1] >> 24) fail(vm, "ARRAY.COPY: DIFFERENT TYPES%s", NULL);
  if (si < 0 || di < 0 || n < 0 || (uint32_t)(si + n) > arrLen(vm, s) || (uint32_t)(di + n) > arrLen(vm, d)) fail(vm, "INDEX OUT OF RANGE%s", NULL);
  int k = ksize(hw(vm, s)[1] >> 24); memmove(data(vm, d) + di * k, data(vm, s) + si * k, (size_t)n * k);
}
static void n_arrClear(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  uint32_t s = a[0].u; int32_t i = a[1].i, n = a[2].i; needObj(vm, s);
  if (i < 0 || n < 0 || (uint32_t)(i + n) > arrLen(vm, s)) fail(vm, "INDEX OUT OF RANGE%s", NULL);
  int k = ksize(hw(vm, s)[1] >> 24); memset(data(vm, s) + i * k, 0, (size_t)n * k);
}
static void n_strLen(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { needObj(vm, a[0].u); RET_I((int32_t)arrLen(vm, a[0].u)); }
static void n_strChar(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  needObj(vm, a[0].u); if (a[1].u >= arrLen(vm, a[0].u)) fail(vm, "INDEX OUT OF RANGE%s", NULL);
  RET_I(strChars(vm, a[0].u)[a[1].u]);
}
static uint32_t concat(Vx *vm, uint32_t *s, int n) {
  uint32_t len = 0; for (int i = 0; i < n; i++) if (s[i]) len += arrLen(vm, s[i]);
  // the parts stay on the eval stack (rooted) while this allocates
  uint32_t r = newStr(vm, NULL, len), o = 0;
  for (int i = 0; i < n; i++) if (s[i]) { uint32_t l = arrLen(vm, s[i]); memcpy(data(vm, r) + 2 * o, data(vm, s[i]), l * 2); o += l; }
  return r;
}
static void n_concat2(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { uint32_t s[2] = { a[0].u, a[1].u }; RET_R(concat(vm, s, 2)); }
static void n_concat3(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { uint32_t s[3] = { a[0].u, a[1].u, a[2].u }; RET_R(concat(vm, s, 3)); }
static void n_strEq(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  uint32_t x = a[0].u, y = a[1].u;
  if (x == y) { RET_I(1); return; } if (!x || !y) { RET_I(0); return; }
  RET_I(arrLen(vm, x) == arrLen(vm, y) && !memcmp(data(vm, x), data(vm, y), arrLen(vm, x) * 2));
}
static void n_substr(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  needObj(vm, a[0].u); int32_t s = a[1].i, n = a[2].i;
  if (s < 0 || n < 0 || (uint32_t)(s + n) > arrLen(vm, a[0].u)) fail(vm, "SUBSTRING OUT OF RANGE%s", NULL);
  uint32_t r = newStr(vm, NULL, (uint32_t)n); memcpy(data(vm, r), data(vm, a[0].u) + 2 * s, (size_t)n * 2); RET_R(r);
}
static void pad(Vx *vm, Slot *a, Slot *ret, uint8_t *rt, int left) {
  needObj(vm, a[0].u); uint32_t l = arrLen(vm, a[0].u); int32_t w = a[1].i; uint16_t c = (uint16_t)a[2].i;
  if (w <= (int32_t)l) { RET_R(a[0].u); return; }
  uint32_t r = newStr(vm, NULL, (uint32_t)w); uint16_t *d = (uint16_t *)data(vm, r); const uint16_t *s = strChars(vm, a[0].u);
  for (int32_t i = 0; i < w; i++) d[i] = c;
  memcpy(d + (left ? w - (int32_t)l : 0), s, l * 2); RET_R(r);
}
static void n_padL(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { pad(vm, a, ret, rt, 1); }
static void n_padR(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { pad(vm, a, ret, rt, 0); }
static void n_indexOf(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  needObj(vm, a[0].u); const uint16_t *s = strChars(vm, a[0].u);
  for (uint32_t i = 0; i < arrLen(vm, a[0].u); i++) if (s[i] == (uint16_t)a[1].i) { RET_I((int32_t)i); return; }
  RET_I(-1);
}
static void n_upper(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  needObj(vm, a[0].u); uint32_t n = arrLen(vm, a[0].u), r = newStr(vm, strChars(vm, a[0].u), n); uint16_t *d = (uint16_t *)data(vm, r);
  for (uint32_t i = 0; i < n; i++) if (d[i] >= 'a' && d[i] <= 'z') d[i] -= 32;
  RET_R(r);
}
static void n_fromChars(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  needObj(vm, a[0].u); int32_t s = a[1].i, n = a[2].i;
  if (s < 0 || n < 0 || (uint32_t)(s + n) > arrLen(vm, a[0].u)) fail(vm, "INDEX OUT OF RANGE%s", NULL);
  uint32_t r = newStr(vm, NULL, (uint32_t)n); memcpy(data(vm, r), data(vm, a[0].u) + 2 * s, (size_t)n * 2); RET_R(r);
}
static void n_floor(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { RET_F(floor(a[0].f)); }
static void n_ceil(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { RET_F(ceil(a[0].f)); }
static void n_round(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { RET_F(floor(a[0].f + 0.5)); }
static void n_sqrt(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { RET_F(sqrt(a[0].f)); }
static void n_sin(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { RET_F(sin(a[0].f)); }
static void n_cos(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { RET_F(cos(a[0].f)); }
static void n_atan2(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { RET_F(atan2(a[0].f, a[1].f)); }
static void n_pow(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { RET_F(pow(a[0].f, a[1].f)); }
static void n_initArray(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  uint32_t arr = a[0].u, b = a[1].u; needObj(vm, arr);
  if (b >= vm->nBlobs) fail(vm, "BAD ARRAY DATA%s", NULL);
  uint32_t n = arrLen(vm, arr) * ksize(hw(vm, arr)[1] >> 24); if (n > vm->blobLen[b]) n = vm->blobLen[b];
  memcpy(data(vm, arr), vm->blobPool + vm->blobOff[b], n);
}
static void n_scrClear(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { memset(vm->host->screen, 0, 4096); }
static void n_scrSet(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  int32_t x = a[1].i, y = a[2].i; if (x >= 0 && y >= 0 && x < 64 && y < 64) vm->host->screen[y * 64 + x] = a[3].i ? 1 : 0;
}
static void n_scrGet(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  int32_t x = a[1].i, y = a[2].i; RET_I(x >= 0 && y >= 0 && x < 64 && y < 64 ? vm->host->screen[y * 64 + x] : 0);
}
static void n_scrFill(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  for (int32_t y = a[2].i; y < a[2].i + a[4].i; y++) for (int32_t x = a[1].i; x < a[1].i + a[3].i; x++)
    if (x >= 0 && y >= 0 && x < 64 && y < 64) vm->host->screen[y * 64 + x] = a[5].i ? 1 : 0;
}
static void n_scrText(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  uint32_t s = a[3].u; if (!s) return;
  char b[80]; uint32_t n = arrLen(vm, s); if (n > 79) n = 79;
  for (uint32_t i = 0; i < n; i++) { uint16_t c = strChars(vm, s)[i]; b[i] = c < 128 ? (char)c : '?'; } b[n] = 0;
  if (vm->host->text) vm->host->text(vm->host->screen, a[1].i, a[2].i, b, a[4].i);
}
static void n_tone(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { if (vm->host->tone) vm->host->tone(a[0].i, a[1].i, a[2].i); }
static void n_report(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { if (vm->host->report) vm->host->report(a[0].i, a[1].i, a[2].i, a[3].i); }
static void loadSave(Vx *vm, int id, Slot *ret, uint8_t *rt) {
  static uint8_t buf[VX_SAVE_MAX]; int n = vm->host->load ? vm->host->load(id, buf, sizeof buf) : -1;
  if (n < 0) { RET_R(0); return; }
  uint32_t r = newArr(vm, (uint32_t)VK_U1 << 24 | NO_TYPE, n); memcpy(data(vm, r), buf, (size_t)n); RET_R(r);
}
static void n_load(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { loadSave(vm, 0, ret, rt); }
static void n_loadOther(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) { loadSave(vm, a[0].i ? a[0].i : -2, ret, rt); }
static void n_save(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  uint32_t r = a[0].u; if (!r || !vm->host->save) return;
  uint32_t n = arrLen(vm, r); if (n > VX_SAVE_MAX) fail(vm, "SAVE BIGGER THAN 16 KB%s", NULL);
  vm->host->save(data(vm, r), (int)n);
}
static void strArg(Vx *vm, uint32_t s, char *b, int n) {     // a string argument as plain ASCII
  needObj(vm, s); uint32_t l = arrLen(vm, s); if (l > (uint32_t)n - 1) l = (uint32_t)n - 1;
  for (uint32_t i = 0; i < l; i++) { uint16_t c = strChars(vm, s)[i]; b[i] = c < 128 ? (char)c : '?'; } b[l] = 0;
}
static void n_pkgLen(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  char nm[32]; strArg(vm, a[0].u, nm, sizeof nm); RET_I(vm->host->file ? vm->host->file(nm, 0, NULL, 0) : -1);
}
static void n_pkgRead(Vx *vm, Slot *a, uint8_t *at, Slot *ret, uint8_t *rt) {
  char nm[32]; strArg(vm, a[0].u, nm, sizeof nm);
  uint32_t d = a[2].u; int32_t off = a[1].i, idx = a[3].i, n = a[4].i; needObj(vm, d);
  if (idx < 0 || n < 0 || off < 0 || (uint32_t)(idx + n) > arrLen(vm, d) || (hw(vm, d)[1] >> 24) != VK_U1) fail(vm, "PACKAGE.READ: BAD BUFFER%s", NULL);
  RET_I(vm->host->file ? vm->host->file(nm, off, data(vm, d) + idx, n) : 0);
}
static const struct { const char *name; Native fn; } NATIVES[] = {
  { "Microsoft.Vmx.Package::Length/1", n_pkgLen }, { "Microsoft.Vmx.Package::Read/5", n_pkgRead },
  { "System.Int32::ToString/0", n_i4ToString }, { "System.UInt32::ToString/0", n_u4ToString }, { "System.Byte::ToString/0", n_u1ToString },
  { "System.Char::ToString/0", n_charToString }, { "System.Array::get_Length/0", n_arrLen }, { "System.Array::Copy/5", n_arrCopy },
  { "System.Array::Clear/3", n_arrClear }, { "System.String::get_Length/0", n_strLen }, { "System.String::get_Chars/1", n_strChar },
  { "System.String::Concat/2", n_concat2 }, { "System.String::Concat/3", n_concat3 }, { "System.String::Equals/2", n_strEq },
  { "System.String::Substring/2", n_substr }, { "System.String::PadLeft/2", n_padL }, { "System.String::PadRight/2", n_padR },
  { "System.String::IndexOf/1", n_indexOf }, { "System.String::ToUpper/0", n_upper }, { "System.String::FromChars/3", n_fromChars },
  { "System.Math::Floor/1", n_floor }, { "System.Math::Ceiling/1", n_ceil }, { "System.Math::Round/1", n_round },
  { "System.Math::Sqrt/1", n_sqrt }, { "System.Math::Sin/1", n_sin }, { "System.Math::Cos/1", n_cos },
  { "System.Math::Atan2/2", n_atan2 }, { "System.Math::Pow/2", n_pow },
  { "System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray/2", n_initArray },
  { "Microsoft.Vmx.Screen::Clear/0", n_scrClear }, { "Microsoft.Vmx.Screen::Set/3", n_scrSet }, { "Microsoft.Vmx.Screen::Get/2", n_scrGet },
  { "Microsoft.Vmx.Screen::Fill/5", n_scrFill }, { "Microsoft.Vmx.Screen::Text/4", n_scrText },
  { "Microsoft.Vmx.Beeper::Tone/3", n_tone }, { "Microsoft.Vmx.Scores::Report/4", n_report },
  { "Microsoft.Vmx.Storage::Load/0", n_load }, { "Microsoft.Vmx.Storage::Save/1", n_save }, { "Microsoft.Vmx.Storage::LoadOther/1", n_loadOther },
};

// ---------------------------------------------------------------- loading
#define R8(n) (((size_t)(n) + 7) & ~(size_t)7)
size_t vx_need(const uint8_t *img, size_t len, size_t heap) {
  if (len < 72 || memcmp(img, "VXIL", 4)) return 0;
  uint32_t nT = r32(img + 8), nM = r32(img + 12), nS = r32(img + 16), nStr = r32(img + 20), nB = r32(img + 24);
  return 8 + R8(sizeof(Vx)) + R8(nT * sizeof(VxType)) + R8(nM * sizeof(VxMethod)) + 3 * R8(nStr * 4) + 2 * R8(nB * 4 + 4)
       + R8(nS * sizeof(Slot)) + R8(nS) + R8(1024 * 4) + R8(heap);
}
static void *carve(uint8_t **p, size_t n) { void *r = *p; *p += (n + 7) & ~(size_t)7; return r; }
Vx *vx_load(void *mem, size_t memlen, const uint8_t *img, size_t len, size_t heap, const VxHost *host, char *eb, int el) {
  size_t small = vx_need(img, len, 0);
  if (!small || memlen < vx_need(img, len, heap)) { snprintf(eb, (size_t)el, "not a VMX image, or too little memory"); return NULL; }
  uint8_t *h = (uint8_t *)(((uintptr_t)mem + 7) & ~(uintptr_t)7) + small;
  return vx_load_split(mem, small, h, heap, img, len, host, eb, el);
}
Vx *vx_load_split(void *mem, size_t memlen, void *heapMem, size_t heap, const uint8_t *img, size_t len, const VxHost *host, char *eb, int el) {
  if (!vx_need(img, len, 0) || memlen < vx_need(img, len, 0) || !heapMem) { snprintf(eb, (size_t)el, "not a VMX image, or too little memory"); return NULL; }
  uint8_t *p = (uint8_t *)(((uintptr_t)mem + 7) & ~(uintptr_t)7);
  Vx *vm = carve(&p, sizeof(Vx)); memset(vm, 0, sizeof *vm); vm->host = host;
  const uint8_t *q = img + 8;
  vm->nTypes = r32(q); vm->nMethods = r32(q + 4); vm->nStatics = r32(q + 8); vm->nStrings = r32(q + 12); vm->nBlobs = r32(q + 16);
  uint32_t nNat = r32(q + 20); q += 24;
  vm->entry = r32(q); vm->entryCtor = r32(q + 4); vm->slotStart = r32(q + 8); vm->slotTick = r32(q + 12); vm->slotDraw = r32(q + 16);
  vm->slotUsesB = r32(q + 20); vm->tScreen = r32(q + 24); vm->tString = r32(q + 28); vm->tObject = r32(q + 32); vm->tException = r32(q + 36); q += 40;
  vm->types = carve(&p, vm->nTypes * sizeof(VxType));
  for (uint32_t i = 0; i < vm->nTypes; i++, q += 44) {
    VxType *t = &vm->types[i];
    t->parent = (int32_t)r32(q); t->flags = r16(q + 4); t->prim = q[6]; t->instSize = r32(q + 8); t->vtOff = r32(q + 12); t->nVirt = r32(q + 16);
    t->refOff = r32(q + 20); t->nRefs = r32(q + 24); t->imapOff = r32(q + 28); t->nImap = r32(q + 32); t->cctor = (int32_t)r32(q + 36); t->name = r32(q + 40);
  }
  vm->methods = carve(&p, vm->nMethods * sizeof(VxMethod));
  for (uint32_t i = 0; i < vm->nMethods; i++, q += 36) {
    VxMethod *m = &vm->methods[i];
    m->codeOff = r32(q); m->codeLen = r32(q + 4); m->nArgs = r16(q + 8); m->nLocals = r16(q + 10); m->maxStack = r16(q + 12);
    m->flags = r16(q + 14); m->vslot = (int32_t)r32(q + 16); m->ret = q[20]; m->kindsOff = r32(q + 24); m->owner = r32(q + 28); m->name = r32(q + 32);
    m->native = NULL;
  }
  vm->staticKinds = (uint8_t *)q; q += (vm->nStatics + 3) & ~3u;
  vm->strOff = (const uint32_t *)0; const uint8_t *strTab = q; q += vm->nStrings * 8;
  const uint8_t *blobTab = q; q += vm->nBlobs * 8;
  const uint8_t *natTab = q; q += nNat * 8;
  uint32_t n;
  n = r32(q); vm->vt = q + 4; q += 4 + 4 * n;
  n = r32(q); vm->refs = q + 4; q += 4 + 4 * n;
  n = r32(q); vm->imap = q + 4; q += 4 + 4 * n;
  n = r32(q); vm->kinds = q + 4; q += (4 + n + 3) & ~3u;
  n = r32(q); vm->code = q + 4; q += 4 + n;
  n = r32(q); vm->strPool = q + 4; q += (4 + n + 3) & ~3u;
  n = r32(q); vm->blobPool = q + 4; q += 4 + n;
  n = r32(q); vm->names = (const char *)q + 4; q += (4 + n + 3) & ~3u;
  if (q > img + len) { snprintf(eb, (size_t)el, "image is cut short"); return NULL; }
  // tables we read often go to RAM as plain arrays
  uint32_t *so = carve(&p, vm->nStrings * 4), *sl = carve(&p, vm->nStrings * 4), *bo = carve(&p, vm->nBlobs * 4 + 4), *bl = carve(&p, vm->nBlobs * 4 + 4);
  for (uint32_t i = 0; i < vm->nStrings; i++) { so[i] = r32(strTab + 8 * i); sl[i] = r32(strTab + 8 * i + 4); }
  for (uint32_t i = 0; i < vm->nBlobs; i++) { bo[i] = r32(blobTab + 8 * i); bl[i] = r32(blobTab + 8 * i + 4); }
  vm->strOff = so; vm->strLen = sl; vm->blobOff = bo; vm->blobLen = bl;
  vm->strRef = carve(&p, vm->nStrings * 4); memset(vm->strRef, 0, vm->nStrings * 4);
  vm->statics = carve(&p, vm->nStatics * sizeof(Slot)); vm->staticTags = carve(&p, vm->nStatics);
  for (uint32_t i = 0; i < vm->nStatics; i++) { vm->statics[i].l = 0; vm->staticTags[i] = tagOf(vm->staticKinds[i]); }
  for (uint32_t i = 0; i < nNat; i++) {
    uint32_t mi = r32(natTab + 8 * i); const char *nm = vm->names + r32(natTab + 8 * i + 4);
    for (unsigned k = 0; k < sizeof NATIVES / sizeof NATIVES[0]; k++) if (!strcmp(NATIVES[k].name, nm)) vm->methods[mi].native = NATIVES[k].fn;
    // a native nobody calls can stay missing; calling it is an error
  }
  vm->markCap = 1024; vm->markStack = carve(&p, vm->markCap * 4);
  vm->heap = (uint8_t *)(((uintptr_t)heapMem + 3) & ~(uintptr_t)3); vm->heapSize = (uint32_t)(heap & ~(size_t)3) - 4; vm->top = 4;
  return vm;
}
const char *vx_error(Vx *vm) { return vm->err; }
void vx_stats(Vx *vm, uint32_t *i, uint32_t *h, uint32_t *g) { if (i) *i = vm->instrs; if (h) *h = vm->top; if (g) *g = vm->gcs; }

// ---------------------------------------------------------------- the interpreter
static const char *mname(Vx *vm, uint32_t m) { return vm->names + vm->methods[m].name; }
static uint32_t ldstr(Vx *vm, uint32_t i) {
  if (!vm->strRef[i]) vm->strRef[i] = newStr(vm, (const uint16_t *)(vm->strPool + 2 * vm->strOff[i]), vm->strLen[i]);
  return vm->strRef[i];
}
// virtual dispatch: the method the object's type runs for m
static VX_HOT uint32_t dispatch(Vx *vm, uint32_t m, uint32_t obj) {
  const VxMethod *M = &vm->methods[m];
  if (!(M->flags & MF_VIRTUAL)) return m;
  uint32_t t = isArray(vm, obj) ? vm->tObject : objType(vm, obj);
  const VxType *T = &vm->types[t];
  if (M->flags & MF_IFACE) {
    for (uint32_t i = 0; i < T->nImap; i++) if (r32(vm->imap + 8 * (T->imapOff + i)) == m) return r32(vm->imap + 8 * (T->imapOff + i) + 4);
    fail(vm, "INTERFACE METHOD NOT FOUND: %s", mname(vm, m));
  }
  if (M->vslot < 0 || (uint32_t)M->vslot >= T->nVirt) fail(vm, "BAD VIRTUAL CALL: %s", mname(vm, m));
  return vtAt(vm, T->vtOff + (uint32_t)M->vslot);
}
// enter method m with its arguments on top of the eval stack
static VX_HOT void enter(Vx *vm, uint32_t m, uint32_t pushObj) {
  const VxMethod *M = &vm->methods[m];
  uint32_t bp = vm->sp - M->nArgs;
  if (M->flags & MF_NATIVE) {
    if (!M->native) fail(vm, "MISSING NATIVE %s", mname(vm, m));
    Slot ret; uint8_t rt = T_NONE; ret.l = 0;
    M->native(vm, &vm->st[bp], &vm->tg[bp], &ret, &rt);
    vm->sp = bp;
    if (M->ret != VK_VOID) push(vm, rt ? rt : tagOf(M->ret), ret);
    if (pushObj) pushR(vm, pushObj);
    return;
  }
  if (M->flags & MF_ABSTRACT) fail(vm, "ABSTRACT CALL %s", mname(vm, m));
  if (vm->nfr >= MAX_FRAMES) fail(vm, "TOO DEEP (RECURSION?)%s", NULL);
  const uint8_t *k = vm->kinds + M->kindsOff;
  for (uint32_t i = 0; i < M->nArgs; i++) { if (k[i] != VK_PTR && k[i] != VK_REF) vm->st[bp + i] = norm(k[i], vm->st[bp + i]); }
  uint32_t lp = bp + M->nArgs;
  if (lp + M->nLocals + M->maxStack + 4 > STACK_SLOTS) fail(vm, "STACK OVERFLOW%s", NULL);
  for (uint32_t i = 0; i < M->nLocals; i++) { vm->st[lp + i].l = 0; vm->tg[lp + i] = tagOf(k[M->nArgs + i]); }
  vm->sp = lp + M->nLocals;
  Frame *f = &vm->fr[vm->nfr++]; f->m = (int32_t)m; f->ip = 0; f->bp = bp; f->lp = lp; f->pushObj = pushObj;
}

#define POP() (vm->sp--)
#define TOP (vm->sp - 1)
static VX_HOT int cmpv(Vx *vm, uint32_t a, uint32_t b, int un) {     // -1 0 1, or 2 for unordered
  uint8_t ta = vm->tg[a], tb = vm->tg[b]; Slot x = vm->st[a], y = vm->st[b];
  if (ta == T_F || tb == T_F) { double p = ta == T_F ? x.f : ta == T_I8 ? (double)x.l : x.i, q = tb == T_F ? y.f : tb == T_I8 ? (double)y.l : y.i;
    if (p != p || q != q) return 2;
    return p < q ? -1 : p > q ? 1 : 0; }
  if (ta == T_I8 || tb == T_I8) { int64_t p = ta == T_I8 ? x.l : x.i, q = tb == T_I8 ? y.l : y.i;
    if (un) return (uint64_t)p < (uint64_t)q ? -1 : (uint64_t)p > (uint64_t)q ? 1 : 0;
    return p < q ? -1 : p > q ? 1 : 0; }
  if (ta == T_PTR || tb == T_PTR) return x.ul == y.ul ? 0 : x.ul < y.ul ? -1 : 1;
  if (un || ta == T_REF || tb == T_REF) return x.u < y.u ? -1 : x.u > y.u ? 1 : 0;
  return x.i < y.i ? -1 : x.i > y.i ? 1 : 0;
}
static VX_HOT int truthy(Vx *vm, uint32_t a) { uint8_t t = vm->tg[a]; Slot v = vm->st[a];
  return t == T_F ? v.f != 0 : t == T_I8 || t == T_PTR ? v.l != 0 : v.u != 0; }
static VX_HOT void arith(Vx *vm, int op) {        // 0x58 add .. 0x64 shr.un on the top two values
  uint32_t b = TOP, a = TOP - 1; uint8_t ta = vm->tg[a], tb = vm->tg[b]; Slot x = vm->st[a], y = vm->st[b], r; r.l = 0; uint8_t rt = ta;
  if (op >= 0x62) {                                          // shifts: the amount is an int
    int s = y.i;
    if (ta == T_I8) { s &= 63; r.l = op == 0x62 ? (int64_t)((uint64_t)x.l << s) : op == 0x63 ? x.l >> s : (int64_t)((uint64_t)x.l >> s); }
    else { s &= 31; r.i = op == 0x62 ? (int32_t)(x.u << s) : op == 0x63 ? x.i >> s : (int32_t)(x.u >> s); rt = T_I4; }
  } else if (ta == T_F || tb == T_F) {
    double p = ta == T_F ? x.f : ta == T_I8 ? (double)x.l : x.i, q = tb == T_F ? y.f : tb == T_I8 ? (double)y.l : y.i; rt = T_F;
    switch (op) { case 0x58: r.f = p + q; break; case 0x59: r.f = p - q; break; case 0x5A: r.f = p * q; break; case 0x5B: r.f = p / q; break;
      case 0x5D: r.f = fmod(p, q); break; default: fail(vm, "BAD FLOAT OP%s", NULL); }
  } else if (ta == T_I8 || tb == T_I8) {
    int64_t p = ta == T_I8 ? x.l : x.i, q = tb == T_I8 ? y.l : y.i; rt = T_I8;
    switch (op) {
      case 0x58: r.l = (int64_t)((uint64_t)p + (uint64_t)q); break; case 0x59: r.l = (int64_t)((uint64_t)p - (uint64_t)q); break;
      case 0x5A: r.l = (int64_t)((uint64_t)p * (uint64_t)q); break;
      case 0x5B: if (!q) fail(vm, "DIVIDE BY ZERO%s", NULL); r.l = (q == -1) ? (int64_t)(0 - (uint64_t)p) : p / q; break;
      case 0x5C: if (!q) fail(vm, "DIVIDE BY ZERO%s", NULL); r.l = (int64_t)((uint64_t)p / (uint64_t)q); break;
      case 0x5D: if (!q) fail(vm, "DIVIDE BY ZERO%s", NULL); r.l = q == -1 ? 0 : p % q; break;
      case 0x5E: if (!q) fail(vm, "DIVIDE BY ZERO%s", NULL); r.l = (int64_t)((uint64_t)p % (uint64_t)q); break;
      case 0x5F: r.l = p & q; break; case 0x60: r.l = p | q; break; case 0x61: r.l = p ^ q; break;
    }
  } else {                                                   // ints (and pointer arithmetic is refused)
    if (ta == T_PTR || tb == T_PTR || ta == T_REF || tb == T_REF) fail(vm, "POINTER ARITHMETIC%s", NULL);
    uint32_t p = x.u, q = y.u; rt = T_I4;
    switch (op) {
      case 0x58: r.u = p + q; break; case 0x59: r.u = p - q; break; case 0x5A: r.u = p * q; break;
      case 0x5B: if (!q) fail(vm, "DIVIDE BY ZERO%s", NULL); r.i = (y.i == -1) ? (int32_t)(0u - p) : x.i / y.i; break;
      case 0x5C: if (!q) fail(vm, "DIVIDE BY ZERO%s", NULL); r.u = p / q; break;
      case 0x5D: if (!q) fail(vm, "DIVIDE BY ZERO%s", NULL); r.i = y.i == -1 ? 0 : x.i % y.i; break;
      case 0x5E: if (!q) fail(vm, "DIVIDE BY ZERO%s", NULL); r.u = p % q; break;
      case 0x5F: r.u = p & q; break; case 0x60: r.u = p | q; break; case 0x61: r.u = p ^ q; break;
    }
  }
  vm->sp--; vm->st[a] = r; vm->tg[a] = rt;
}
static VX_HOT void conv(Vx *vm, int k, int fromUnsigned) {        // convert the top value to kind k
  uint32_t a = TOP; uint8_t t = vm->tg[a]; Slot v = vm->st[a], r; r.l = 0;
  if (k == VK_R4 || k == VK_R8) {
    double d = t == T_F ? v.f : t == T_I8 ? (fromUnsigned ? (double)v.ul : (double)v.l) : (fromUnsigned ? (double)v.u : (double)v.i);
    r.f = k == VK_R4 ? (double)(float)d : d; vm->st[a] = r; vm->tg[a] = T_F; return;
  }
  int64_t l;
  if (t == T_F) { double d = v.f; if (d != d) d = 0; if (d > 9.2e18) d = 9.2e18; if (d < -9.2e18) d = -9.2e18;
    l = (k == VK_U4 || k == VK_U8) && d >= 0 ? (int64_t)(uint64_t)d : (int64_t)d; }
  else if (t == T_I8 || t == T_PTR) l = v.l;
  else l = fromUnsigned ? (int64_t)v.u : (int64_t)v.i;
  if (k == VK_I8 || k == VK_U8) { r.l = l; vm->st[a] = r; vm->tg[a] = T_I8; return; }
  switch (k) { case VK_I1: r.i = (int8_t)l; break; case VK_U1: r.i = (uint8_t)l; break; case VK_I2: r.i = (int16_t)l; break;
    case VK_U2: r.i = (uint16_t)l; break; default: r.i = (int32_t)(uint32_t)(uint64_t)l; break; }
  vm->st[a] = r; vm->tg[a] = T_I4;
}
static VX_HOT uint8_t *elemAt(Vx *vm, uint32_t arr, Slot idx, uint8_t it, int *k) {
  needObj(vm, arr);
  if (!isArray(vm, arr)) fail(vm, "NOT AN ARRAY%s", NULL);
  int64_t i = it == T_I8 ? idx.l : idx.i;
  if (i < 0 || (uint64_t)i >= arrLen(vm, arr)) fail(vm, "INDEX OUT OF RANGE%s", NULL);
  *k = (int)(hw(vm, arr)[1] >> 24);
  return data(vm, arr) + (uint32_t)i * ksize(*k);
}
static const uint8_t LDELEM_K[] = { VK_I1, VK_U1, VK_I2, VK_U2, VK_I4, VK_U4, VK_I8, VK_I4, VK_R4, VK_R8, VK_REF };   // 0x90..0x9A
static const uint8_t STELEM_K[] = { VK_I4, VK_I1, VK_I2, VK_I4, VK_I8, VK_R4, VK_R8, VK_REF };                        // 0x9B..0xA2
static const uint8_t LDIND_K[] = { VK_I1, VK_U1, VK_I2, VK_U2, VK_I4, VK_U4, VK_I8, VK_I4, VK_R4, VK_R8, VK_REF };     // 0x46..0x50
static const uint8_t STIND_K[] = { VK_REF, VK_I1, VK_I2, VK_I4, VK_I8, VK_R4, VK_R8 };                                // 0x51..0x57

static VX_HOT void run(Vx *vm, int stopAt) {
  while (vm->nfr > stopAt) {
    Frame *f = &vm->fr[vm->nfr - 1];
    const VxMethod *M = &vm->methods[f->m];
    const uint8_t *c = vm->code + M->codeOff, *kinds = vm->kinds + M->kindsOff;
    uint32_t ip = f->ip;
    for (;;) {
      vm->instrs++;
      int op = c[ip++];
      Slot v; uint8_t t;
      switch (op) {
        case 0x00: break;
        case 0x02: case 0x03: case 0x04: case 0x05: { uint32_t i = f->bp + (uint32_t)(op - 2); push(vm, vm->tg[i], vm->st[i]); } break;
        case 0x06: case 0x07: case 0x08: case 0x09: { uint32_t i = f->lp + (uint32_t)(op - 6); push(vm, vm->tg[i], vm->st[i]); } break;
        case 0x0A: case 0x0B: case 0x0C: case 0x0D: { uint32_t n = (uint32_t)(op - 0x0A), i = f->lp + n; POP();
          vm->st[i] = norm(kinds[M->nArgs + n], vm->st[vm->sp]); } break;
        case 0x0E: { uint32_t i = f->bp + c[ip++]; push(vm, vm->tg[i], vm->st[i]); } break;                 // ldarg.s
        case 0x0F: { uint32_t n = c[ip++]; push(vm, T_PTR, mkptr(SP_STACK, f->bp + n, 0, kinds[n])); } break;     // ldarga.s
        case 0x10: { uint32_t n = c[ip++]; POP(); vm->st[f->bp + n] = norm(kinds[n], vm->st[vm->sp]); } break;    // starg.s
        case 0x11: { uint32_t i = f->lp + c[ip++]; push(vm, vm->tg[i], vm->st[i]); } break;                 // ldloc.s
        case 0x12: { uint32_t n = c[ip++]; push(vm, T_PTR, mkptr(SP_STACK, f->lp + n, 0, kinds[M->nArgs + n])); } break;
        case 0x13: { uint32_t n = c[ip++]; POP(); vm->st[f->lp + n] = norm(kinds[M->nArgs + n], vm->st[vm->sp]); } break;
        case 0x14: pushR(vm, 0); break;
        case 0x15: case 0x16: case 0x17: case 0x18: case 0x19: case 0x1A: case 0x1B: case 0x1C: case 0x1D: case 0x1E: pushI(vm, op - 0x16); break;
        case 0x1F: pushI(vm, (int8_t)c[ip++]); break;
        case 0x20: pushI(vm, (int32_t)r32(c + ip)); ip += 4; break;
        case 0x21: { int64_t l = (int64_t)((uint64_t)r32(c + ip) | (uint64_t)r32(c + ip + 4) << 32); pushL(vm, l); ip += 8; } break;
        case 0x22: { uint32_t u = r32(c + ip); float x; memcpy(&x, &u, 4); pushF(vm, x); ip += 4; } break;
        case 0x23: { uint64_t u = (uint64_t)r32(c + ip) | (uint64_t)r32(c + ip + 4) << 32; double d; memcpy(&d, &u, 8); pushF(vm, d); ip += 8; } break;
        case 0x25: push(vm, vm->tg[TOP], vm->st[TOP]); break;
        case 0x26: POP(); break;
        case 0x28: case 0x6F: {                                     // call, callvirt
          uint32_t m = r32(c + ip); ip += 4; f->ip = ip;
          const VxMethod *T = &vm->methods[m];
          if (!(T->flags & MF_STATIC)) {
            uint32_t self = vm->sp - T->nArgs;
            if (vm->tg[self] == T_REF) { needObj(vm, vm->st[self].u); if (op == 0x6F) m = dispatch(vm, m, vm->st[self].u); }
          }
          enter(vm, m, 0);
          goto next_frame;
        }
        case 0x2A: {                                                 // ret
          Slot r; uint8_t rt = T_NONE; r.l = 0;
          if (M->ret != VK_VOID) { POP(); r = vm->st[vm->sp]; rt = vm->tg[vm->sp]; if (M->ret != VK_REF && M->ret < VK_I8) { r = norm(M->ret, r); rt = T_I4; } }
          uint32_t obj = f->pushObj;
          vm->sp = f->bp; vm->nfr--;
          if (rt) push(vm, rt, r);
          if (obj) pushR(vm, obj);
          goto next_frame;
        }
        case 0x2B: { int8_t d = (int8_t)c[ip++]; ip += d; } break;                                          // br.s
        case 0x2C: { int8_t d = (int8_t)c[ip++]; POP(); if (!truthy(vm, vm->sp)) ip += d; } break;            // brfalse.s
        case 0x2D: { int8_t d = (int8_t)c[ip++]; POP(); if (truthy(vm, vm->sp)) ip += d; } break;             // brtrue.s
        case 0x38: { int32_t d = (int32_t)r32(c + ip); ip += 4 + d; } break;
        case 0x39: { int32_t d = (int32_t)r32(c + ip); ip += 4; POP(); if (!truthy(vm, vm->sp)) ip += d; } break;
        case 0x3A: { int32_t d = (int32_t)r32(c + ip); ip += 4; POP(); if (truthy(vm, vm->sp)) ip += d; } break;
        case 0x2E: case 0x2F: case 0x30: case 0x31: case 0x32: case 0x33: case 0x34: case 0x35: case 0x36: case 0x37:
        case 0x3B: case 0x3C: case 0x3D: case 0x3E: case 0x3F: case 0x40: case 0x41: case 0x42: case 0x43: case 0x44: {
          int sh = op < 0x38; int32_t d; if (sh) d = (int8_t)c[ip++]; else { d = (int32_t)r32(c + ip); ip += 4; }
          int k = sh ? op - 0x2E : op - 0x3B;           // beq bge bgt ble blt bne.un bge.un bgt.un ble.un blt.un
          int r = cmpv(vm, TOP - 1, TOP, k >= 5); vm->sp -= 2; int take;
          switch (k) {
            case 0: take = r == 0; break; case 1: take = r == 0 || r == 1; break; case 2: take = r == 1; break;
            case 3: take = r == 0 || r == -1; break; case 4: take = r == -1; break; case 5: take = r != 0; break;
            case 6: take = r != -1; break; case 7: take = r == 1 || r == 2; break; case 8: take = r != 1; break; default: take = r == -1 || r == 2; break;
          }
          if (take) ip += d;
        } break;
        case 0x45: { uint32_t n = r32(c + ip), base = ip + 4 + 4 * n; POP(); uint32_t i = vm->st[vm->sp].u;
          ip = i < n ? base + (uint32_t)(int32_t)r32(c + ip + 4 + 4 * i) : base; } break;
        case 0x46: case 0x47: case 0x48: case 0x49: case 0x4A: case 0x4B: case 0x4C: case 0x4D: case 0x4E: case 0x4F: case 0x50: {
          POP(); Slot p = vm->st[vm->sp]; if (vm->tg[vm->sp] != T_PTR) fail(vm, "NOT A POINTER%s", NULL);
          ptrLoad(vm, p, LDIND_K[op - 0x46], &v, &t); push(vm, t, v); } break;
        case 0x51: case 0x52: case 0x53: case 0x54: case 0x55: case 0x56: case 0x57: {
          vm->sp -= 2; Slot p = vm->st[vm->sp]; if (vm->tg[vm->sp] != T_PTR) fail(vm, "NOT A POINTER%s", NULL);
          int k = STIND_K[op - 0x51];
          if ((p.ul >> 48 & 0xFF) == SP_HEAP && k != VK_REF) { int pk = (int)(p.ul >> 56); if (pk && ksize(pk) == ksize(k)) k = pk; }
          ptrStore(vm, p, k, vm->st[vm->sp + 1]); } break;
        case 0x58: case 0x59: case 0x5A: case 0x5B: case 0x5C: case 0x5D: case 0x5E: case 0x5F: case 0x60: case 0x61:
        case 0x62: case 0x63: case 0x64: arith(vm, op); break;
        case 0x65: { uint32_t a = TOP; if (vm->tg[a] == T_F) vm->st[a].f = -vm->st[a].f; else if (vm->tg[a] == T_I8) vm->st[a].l = (int64_t)(0 - vm->st[a].ul); else vm->st[a].i = (int32_t)(0u - vm->st[a].u); } break;
        case 0x66: { uint32_t a = TOP; if (vm->tg[a] == T_I8) vm->st[a].l = ~vm->st[a].l; else vm->st[a].i = ~vm->st[a].i; } break;
        case 0x67: conv(vm, VK_I1, 0); break; case 0x68: conv(vm, VK_I2, 0); break; case 0x69: conv(vm, VK_I4, 0); break;
        case 0x6A: conv(vm, VK_I8, 0); break; case 0x6B: conv(vm, VK_R4, 0); break; case 0x6C: conv(vm, VK_R8, 0); break;
        case 0x6D: conv(vm, VK_U4, 0); break; case 0x6E: conv(vm, VK_U8, vm->tg[TOP] == T_I4); break;
        case 0x76: conv(vm, VK_R8, 1); break;
        case 0x82: conv(vm, VK_I1, 1); break; case 0x83: conv(vm, VK_I2, 1); break; case 0x84: conv(vm, VK_I4, 1); break; case 0x85: conv(vm, VK_I8, 1); break;
        case 0x86: conv(vm, VK_U1, 1); break; case 0x87: conv(vm, VK_U2, 1); break; case 0x88: conv(vm, VK_U4, 1); break; case 0x89: conv(vm, VK_U8, 1); break;
        case 0x8A: conv(vm, VK_I4, 1); break; case 0x8B: conv(vm, VK_U4, 1); break;
        case 0xB3: conv(vm, VK_I1, 0); break; case 0xB4: conv(vm, VK_U1, 0); break; case 0xB5: conv(vm, VK_I2, 0); break; case 0xB6: conv(vm, VK_U2, 0); break;
        case 0xB7: conv(vm, VK_I4, 0); break; case 0xB8: conv(vm, VK_U4, 0); break; case 0xB9: conv(vm, VK_I8, 0); break; case 0xBA: conv(vm, VK_U8, 0); break;
        case 0xC3: break;                                                                     // ckfinite
        case 0xD1: conv(vm, VK_U2, 0); break; case 0xD2: conv(vm, VK_U1, 0); break; case 0xD3: conv(vm, VK_I4, 0); break;
        case 0xD4: conv(vm, VK_I4, 0); break; case 0xD5: conv(vm, VK_U4, 0); break; case 0xE0: conv(vm, VK_U4, 0); break;
        case 0xD6: arith(vm, 0x58); break; case 0xD7: arith(vm, 0x58); break; case 0xD8: arith(vm, 0x5A); break;   // .ovf: no checks
        case 0xD9: arith(vm, 0x5A); break; case 0xDA: arith(vm, 0x59); break; case 0xDB: arith(vm, 0x59); break;
        case 0x72: pushR(vm, ldstr(vm, r32(c + ip))); ip += 4; break;
        case 0x73: {                                                   // newobj
          uint32_t m = r32(c + ip); ip += 4; f->ip = ip;
          const VxMethod *T = &vm->methods[m];
          if (vm->types[T->owner].flags & 4) fail(vm, "NEW OF A VALUE TYPE: %s", mname(vm, m));
          uint32_t obj = newObj(vm, T->owner), n = T->nArgs - 1u;
          if (vm->sp + 1 >= STACK_SLOTS) fail(vm, "STACK OVERFLOW%s", NULL);
          memmove(&vm->st[vm->sp - n + 1], &vm->st[vm->sp - n], n * sizeof(Slot)); memmove(&vm->tg[vm->sp - n + 1], &vm->tg[vm->sp - n], n);
          vm->st[vm->sp - n].l = 0; vm->st[vm->sp - n].u = obj; vm->tg[vm->sp - n] = T_REF; vm->sp++;
          enter(vm, m, obj);
          goto next_frame;
        }
        case 0x74: case 0x75: {                                        // castclass, isinst
          uint32_t ty = r32(c + ip); ip += 4; uint32_t r = vm->st[TOP].u;
          if (r && !isInst(vm, r, ty)) { if (op == 0x74) fail(vm, "INVALID CAST%s", NULL); vm->st[TOP].u = 0; }
        } break;
        case 0x7A: {                                                   // throw: the game stops with the message
          POP(); uint32_t e = vm->st[vm->sp].u; char msg[64] = "EXCEPTION";
          if (e && !isArray(vm, e) && isInst(vm, e, vm->tException)) {
            uint32_t s = r32(data(vm, e));
            if (s) { uint32_t n = arrLen(vm, s); if (n > 63) n = 63; for (uint32_t i = 0; i < n; i++) msg[i] = (char)strChars(vm, s)[i]; msg[n] = 0; }
            else snprintf(msg, sizeof msg, "%s", vm->names + vm->types[objType(vm, e)].name);
          }
          fail(vm, "%s", msg);
        } break;
        case 0x7B: case 0x7C: case 0x7D: {                             // ldfld, ldflda, stfld
          uint32_t d = r32(c + ip); ip += 4; int k = (int)(d >> 16); uint32_t off = d & 0xFFFF;
          if (op == 0x7D) {
            vm->sp -= 2; uint32_t o = vm->st[vm->sp].u; needObj(vm, o);
            storeK(k, data(vm, o) + off, vm->st[vm->sp + 1]);
          } else {
            POP(); uint32_t o = vm->st[vm->sp].u; needObj(vm, o);
            if (op == 0x7B) { loadK(k, data(vm, o) + off, &v, &t); push(vm, t, v); }
            else push(vm, T_PTR, mkptr(SP_HEAP, o, HDR + off, k));
          }
        } break;
        case 0x7E: { uint32_t i = r32(c + ip); ip += 4; push(vm, vm->staticTags[i], vm->statics[i]); } break;
        case 0x7F: { uint32_t i = r32(c + ip); ip += 4; push(vm, T_PTR, mkptr(SP_STATIC, i, 0, vm->staticKinds[i])); } break;
        case 0x80: { uint32_t i = r32(c + ip); ip += 4; POP(); vm->statics[i] = norm(vm->staticKinds[i], vm->st[vm->sp]); } break;
        case 0x8C: {                                                   // box
          uint32_t ty = r32(c + ip); ip += 4; const VxType *T = &vm->types[ty];
          if (T->prim != 0xFF && T->prim != VK_VOID) { uint32_t o = newObj(vm, ty); storeK(T->prim, data(vm, o), vm->st[TOP]); vm->st[TOP].l = 0; vm->st[TOP].u = o; vm->tg[TOP] = T_REF; }
        } break;
        case 0xA5: {                                                   // unbox.any
          uint32_t ty = r32(c + ip); ip += 4; const VxType *T = &vm->types[ty];
          if (T->prim != 0xFF) { uint32_t o = vm->st[TOP].u; needObj(vm, o); if (isArray(vm, o) || objType(vm, o) != ty) fail(vm, "INVALID CAST%s", NULL);
            POP(); loadK(T->prim, data(vm, o), &v, &t); push(vm, t, v); }
          else if (vm->st[TOP].u && !isInst(vm, vm->st[TOP].u, ty)) fail(vm, "INVALID CAST%s", NULL);
        } break;
        case 0x8D: { uint32_t d = r32(c + ip); ip += 4; POP(); int32_t n = vm->tg[vm->sp] == T_I8 ? (int32_t)vm->st[vm->sp].l : vm->st[vm->sp].i;
          pushR(vm, newArr(vm, d, n)); } break;
        case 0x8E: { uint32_t a = vm->st[TOP].u; needObj(vm, a); POP(); pushI(vm, (int32_t)arrLen(vm, a)); } break;
        case 0x8F: { ip += 4; vm->sp -= 2; int k; uint8_t *p = elemAt(vm, vm->st[vm->sp].u, vm->st[vm->sp + 1], vm->tg[vm->sp + 1], &k);
          uint32_t a = vm->st[vm->sp].u; push(vm, T_PTR, mkptr(SP_HEAP, a, (uint32_t)(p - (vm->heap + a)), k)); } break;
        case 0x90: case 0x91: case 0x92: case 0x93: case 0x94: case 0x95: case 0x96: case 0x97: case 0x98: case 0x99: case 0x9A: case 0xA3: {
          if (op == 0xA3) ip += 4;
          vm->sp -= 2; int k; uint8_t *p = elemAt(vm, vm->st[vm->sp].u, vm->st[vm->sp + 1], vm->tg[vm->sp + 1], &k);
          int lk = op == 0xA3 ? k : LDELEM_K[op - 0x90]; if (op == 0x91 && k == VK_BOOL) lk = VK_U1;
          if (op != 0xA3 && ksize(lk) != ksize(k)) lk = k;             // e.g. ldelem.u2 on a char array
          loadK(lk, p, &v, &t); push(vm, t, v); } break;
        case 0x9B: case 0x9C: case 0x9D: case 0x9E: case 0x9F: case 0xA0: case 0xA1: case 0xA2: case 0xA4: {
          if (op == 0xA4) ip += 4;
          vm->sp -= 3; int k; uint8_t *p = elemAt(vm, vm->st[vm->sp].u, vm->st[vm->sp + 1], vm->tg[vm->sp + 1], &k);
          (void)STELEM_K; storeK(k, p, vm->st[vm->sp + 2]); } break;
        case 0xD0: pushI(vm, (int32_t)r32(c + ip)); ip += 4; break;      // ldtoken: the blob index
        case 0xFE: {
          int op2 = c[ip++];
          switch (op2) {
            case 0x01: case 0x02: case 0x03: case 0x04: case 0x05: {
              int r = cmpv(vm, TOP - 1, TOP, op2 == 0x03 || op2 == 0x05); vm->sp -= 2;
              pushI(vm, op2 == 0x01 ? r == 0 : op2 == 0x02 ? r == 1 : op2 == 0x03 ? (r == 1 || r == 2) : op2 == 0x04 ? r == -1 : (r == -1 || r == 2));
            } break;
            case 0x09: { uint32_t i = f->bp + r16(c + ip); ip += 2; push(vm, vm->tg[i], vm->st[i]); } break;
            case 0x0A: { uint32_t n = r16(c + ip); ip += 2; push(vm, T_PTR, mkptr(SP_STACK, f->bp + n, 0, kinds[n])); } break;
            case 0x0B: { uint32_t n = r16(c + ip); ip += 2; POP(); vm->st[f->bp + n] = norm(kinds[n], vm->st[vm->sp]); } break;
            case 0x0C: { uint32_t i = f->lp + r16(c + ip); ip += 2; push(vm, vm->tg[i], vm->st[i]); } break;
            case 0x0D: { uint32_t n = r16(c + ip); ip += 2; push(vm, T_PTR, mkptr(SP_STACK, f->lp + n, 0, kinds[M->nArgs + n])); } break;
            case 0x0E: { uint32_t n = r16(c + ip); ip += 2; POP(); vm->st[f->lp + n] = norm(kinds[M->nArgs + n], vm->st[vm->sp]); } break;
            case 0x15: { ip += 4; POP(); Slot z; z.l = 0; Slot p = vm->st[vm->sp];               // initobj: primitives / refs only
              int pk = (int)(p.ul >> 56); ptrStore(vm, p, pk ? pk : VK_I8, z); } break;
            case 0x16: {                                                // constrained. T callvirt M
              uint32_t ty = r32(c + ip); ip += 4;
              if (c[ip] != 0x6F) fail(vm, "CONSTRAINED WITHOUT CALLVIRT%s", NULL);
              uint32_t m = r32(c + ip + 1); ip += 5; f->ip = ip;
              const VxMethod *T = &vm->methods[m]; uint32_t self = vm->sp - T->nArgs;
              const VxType *VT = ty < vm->nTypes ? &vm->types[ty] : NULL;
              if (VT && (VT->flags & 4)) {                              // a value type: its own override, or box it
                uint32_t impl = (T->vslot >= 0 && (uint32_t)T->vslot < VT->nVirt) ? vtAt(vm, VT->vtOff + (uint32_t)T->vslot) : m;
                if (vm->methods[impl].owner == ty) { enter(vm, impl, 0); goto next_frame; }
                Slot pv; uint8_t pt; ptrLoad(vm, vm->st[self], VT->prim, &pv, &pt);
                uint32_t o = newObj(vm, ty); storeK(VT->prim, data(vm, o), pv);
                vm->st[self].l = 0; vm->st[self].u = o; vm->tg[self] = T_REF;
              } else { Slot pv; uint8_t pt; ptrLoad(vm, vm->st[self], VK_REF, &pv, &pt); vm->st[self] = pv; vm->tg[self] = T_REF; }
              needObj(vm, vm->st[self].u); enter(vm, dispatch(vm, m, vm->st[self].u), 0); goto next_frame;
            }
            default: { char b[8]; snprintf(b, sizeof b, "FE%02X", op2); fail(vm, "BAD OP %s", b); }
          }
        } break;
        default: { char b[8]; snprintf(b, sizeof b, "%02X", op); fail(vm, "BAD OP %s", b); }
      }
    }
  next_frame:;
  }
}

// call method m (arguments already pushed) and run it to the end
static void invoke(Vx *vm, uint32_t m) {
  int depth = vm->nfr;
  enter(vm, m, 0);
  run(vm, depth);
}
static int guard(Vx *vm) { if (vm->err[0]) return -1; return 0; }
static int startup(Vx *vm) {             // static constructors, then the game object and the screen
  for (uint32_t i = 0; i < vm->nTypes; i++) if (vm->types[i].cctor >= 0) { vm->sp = 0; invoke(vm, (uint32_t)vm->types[i].cctor); }
  vm->sp = 0;
  vm->screen = newObj(vm, vm->tScreen);
  vm->game = newObj(vm, vm->entry);
  pushR(vm, vm->game); invoke(vm, vm->entryCtor); vm->sp = 0;
  return 0;
}
static int call_slot(Vx *vm, uint32_t slot, int nargs, int32_t a1, int32_t a2, int withScreen, int *result) {
  if (guard(vm)) return -1;
  vm->nfr = 0; vm->sp = 0;
  if (setjmp(vm->jb)) { vm->nfr = 0; vm->sp = 0; return -1; }
  if (!vm->game) startup(vm);
  const VxType *T = &vm->types[vm->entry];
  uint32_t m = vtAt(vm, T->vtOff + slot);
  pushR(vm, vm->game);
  if (withScreen) pushR(vm, vm->screen);
  else { if (nargs >= 1) pushI(vm, a1); if (nargs >= 2) pushI(vm, a2); }
  invoke(vm, m);
  if (result) *result = vm->sp ? vm->st[vm->sp - 1].i : 0;
  vm->sp = 0;
  return 0;
}
int vx_start(Vx *vm, int seed, int demo) { return call_slot(vm, vm->slotStart, 2, seed, demo, 0, NULL); }
int vx_tick(Vx *vm, int held, int pressed) { return call_slot(vm, vm->slotTick, 2, held, pressed, 0, NULL); }
int vx_draw(Vx *vm) { return call_slot(vm, vm->slotDraw, 0, 0, 0, 1, NULL); }
int vx_uses_b(Vx *vm) { int r = 1; if (call_slot(vm, vm->slotUsesB, 0, 0, 0, 0, &r)) return 1; return r; }
