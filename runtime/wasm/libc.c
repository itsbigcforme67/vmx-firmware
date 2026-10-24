// The little C library the browser build of the VMX runtime needs (no libc on wasm32 freestanding): memory and
// string calls, a snprintf for %d %u %x %X %s %c (with width, 0 fill, - and .*), a stack-order allocator for the
// setjmp tables clang makes, and the setjmp helpers of clang 18's WebAssembly-exception lowering (as Emscripten's).
#include <stddef.h>
#include <stdint.h>
#include <stdarg.h>
#include <string.h>
#include <stdlib.h>
#include <math.h>

void *memcpy(void *d, const void *s, size_t n) { uint8_t *a = d; const uint8_t *b = s; while (n--) *a++ = *b++; return d; }
void *memmove(void *d, const void *s, size_t n) {
  uint8_t *a = d; const uint8_t *b = s;
  if (a < b) while (n--) *a++ = *b++; else while (n--) a[n] = b[n];
  return d;
}
void *memset(void *d, int c, size_t n) { uint8_t *a = d; while (n--) *a++ = (uint8_t)c; return d; }
int memcmp(const void *x, const void *y, size_t n) {
  const uint8_t *a = x, *b = y; for (; n; n--, a++, b++) if (*a != *b) return *a - *b; return 0;
}
size_t strlen(const char *s) { size_t n = 0; while (s[n]) n++; return n; }
int strcmp(const char *a, const char *b) { while (*a && *a == *b) a++, b++; return (uint8_t)*a - (uint8_t)*b; }
int strncmp(const char *a, const char *b, size_t n) {
  for (; n; n--, a++, b++) { if (*a != *b) return (uint8_t)*a - (uint8_t)*b; if (!*a) return 0; } return 0;
}
char *strchr(const char *s, int c) { for (;; s++) { if (*s == (char)c) return (char *)s; if (!*s) return NULL; } }
int abs(int x) { return x < 0 ? -x : x; }
// core/surf.c's maths: halves away from zero (x - trunc(x) is exact), and hypot in double as glibc's hypotf
long lroundf(float x) { float t = __builtin_truncf(x), d = x - t; return (long)t + (d >= 0.5f ? 1 : d <= -0.5f ? -1 : 0); }
float hypotf(float x, float y) { return (float)__builtin_sqrt((double)x * x + (double)y * y); }

int vsnprintf(char *b, size_t n, const char *f, va_list ap) {
  size_t o = 0;
#define PUT(ch) do { if (o + 1 < n) b[o] = (ch); o++; } while (0)
  for (; *f; f++) {
    if (*f != '%') { PUT(*f); continue; }
    f++;
    int left = 0, zero = 0, width = 0, prec = -1;
    for (;; f++) { if (*f == '-') left = 1; else if (*f == '0') zero = 1; else break; }
    while (*f >= '0' && *f <= '9') width = width * 10 + *f++ - '0';
    if (*f == '.') { f++; prec = 0; if (*f == '*') { prec = va_arg(ap, int); f++; } else while (*f >= '0' && *f <= '9') prec = prec * 10 + *f++ - '0'; }
    char tmp[16]; const char *s = tmp; int len = 0;
    switch (*f) {
      case 'd': case 'u': case 'x': case 'X': {
        unsigned v; int neg = 0;
        if (*f == 'd') { int i = va_arg(ap, int); neg = i < 0; v = neg ? 0u - (unsigned)i : (unsigned)i; } else v = va_arg(ap, unsigned);
        unsigned base = *f == 'd' || *f == 'u' ? 10 : 16; const char *dig = *f == 'X' ? "0123456789ABCDEF" : "0123456789abcdef";
        char r[12]; int k = 0; do { r[k++] = dig[v % base]; v /= base; } while (v);
        if (neg) tmp[len++] = '-';
        if (zero && !left) while (len + k < width) tmp[len++] = '0';
        while (k) tmp[len++] = r[--k];
        break;
      }
      case 'c': tmp[0] = (char)va_arg(ap, int); len = 1; break;
      case 's': s = va_arg(ap, const char *); if (!s) s = "(null)"; len = (int)strlen(s); if (prec >= 0 && len > prec) len = prec; break;
      case '%': tmp[0] = '%'; len = 1; break;
      default: if (!*f) f--; continue;
    }
    if (!left) for (int i = len; i < width; i++) PUT(' ');
    for (int i = 0; i < len; i++) PUT(s[i]);
    if (left) for (int i = len; i < width; i++) PUT(' ');
  }
  if (n) b[o < n ? o : n - 1] = 0;
#undef PUT
  return (int)o;
}
int snprintf(char *b, size_t n, const char *f, ...) { va_list ap; va_start(ap, f); int r = vsnprintf(b, n, f, ap); va_end(ap); return r; }

// a stack-order allocator: clang's setjmp lowering mallocs a small table when a function that calls setjmp starts
// and frees it when it returns, so the blocks come and go last-in first-out
static uint8_t pool[16384]; static size_t poolTop;
void *malloc(size_t n) {
  n = (n + 15) & ~(size_t)15; if (poolTop + n + 16 > sizeof pool) return NULL;
  uint8_t *p = pool + poolTop + 16; *(size_t *)(p - 16) = n; poolTop += n + 16; return p;
}
void free(void *p) { if (p && (uint8_t *)p + *(size_t *)((uint8_t *)p - 16) == pool + poolTop) poolTop = (size_t)((uint8_t *)p - 16 - pool); }
void *realloc(void *p, size_t n) {
  if (!p) return malloc(n);
  size_t old = *(size_t *)((uint8_t *)p - 16);
  if ((uint8_t *)p + old == pool + poolTop) { free(p); void *q = malloc(n); return q; }   // the top block grows in place
  void *q = malloc(n); if (q) memcpy(q, p, old < n ? old : n); return q;
}

// setjmp helpers (clang 18 -mllvm -wasm-enable-sjlj)
typedef struct { uintptr_t id; uint32_t label; } TableEntry;
static uintptr_t setjmpId; static uint32_t tempRet0;
uint32_t getTempRet0(void) { return tempRet0; }
TableEntry *saveSetjmp(uintptr_t *env, uint32_t label, TableEntry *table, uint32_t size) {
  *env = ++setjmpId;
  for (uint32_t i = 0; i < size; i++) if (table[i].id == 0) {
    table[i].id = setjmpId; table[i].label = label; table[i + 1].id = 0; tempRet0 = size; return table;
  }
  size *= 2; table = realloc(table, sizeof(TableEntry) * (size + 1));
  setjmpId--; table = saveSetjmp(env, label, table, size); tempRet0 = size; return table;
}
uint32_t testSetjmp(uintptr_t id, TableEntry *table, uint32_t size) {
  for (uint32_t i = 0; i < size && table[i].id; i++) if (table[i].id == id) return table[i].label;
  return 0;
}
static struct { void *env; int val; } longjmpArgs;
void __wasm_longjmp(void *env, int val) { longjmpArgs.env = env; longjmpArgs.val = val ? val : 1; __builtin_wasm_throw(1, &longjmpArgs); }
