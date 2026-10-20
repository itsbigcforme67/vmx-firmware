#include "records.h"
#include <stdio.h>
#include <string.h>
uint16_t crc16(const uint8_t *b, int n) {                       // CRC-16/CCITT-FALSE
  uint16_t c = 0xFFFF;
  for (int i = 0; i < n; i++) { c ^= (uint16_t)(b[i] << 8); for (int k = 0; k < 8; k++) c = c & 0x8000 ? (uint16_t)((c << 1) ^ 0x1021) : (uint16_t)(c << 1); }
  return c;
}
static void put16(uint8_t *p, uint32_t v) { p[0] = (uint8_t)v; p[1] = (uint8_t)(v >> 8); }
static void put32(uint8_t *p, uint32_t v) { put16(p, v); put16(p + 2, v >> 16); }
void rec_pack(const ScoreRec *r, uint8_t *o) {
  put32(o, r->tid); o[4] = r->board; o[5] = r->flags; put16(o + 6, r->date); put32(o + 8, (uint32_t)r->score);
  for (int i = 0; i < 3; i++) put16(o + 12 + i * 2, r->f[i]);
  put16(o + 18, crc16(o, 18));
}
int msg_pack(uint32_t serial, const char *name, const ScoreRec *r, int n, uint8_t *b) {
  b[0] = 0x56; b[1] = 0x58; b[2] = 1; b[3] = (uint8_t)n; put32(b + 4, serial);
  for (int i = 0; i < 8; i++) b[8 + i] = (uint8_t)(i < (int)strlen(name) ? name[i] & 0x7F : 0);
  for (int i = 0; i < n; i++) rec_pack(&r[i], b + 16 + i * REC_LEN);
  int len = 16 + n * REC_LEN; put16(b + len, crc16(b, len));
  return len + 2;
}
void check_code(const uint8_t *msg, int len, char out[5]) { snprintf(out, 5, "%04u", (unsigned)(crc16(msg, len) % 10000)); }
const char *keypad(const char *s, char *out) {
  static const char *L[] = { "ABC", "DEF", "GHI", "JKL", "MNO", "PQRS", "TUV", "WXYZ" };
  char *o = out;
  for (; *s; s++) {
    if ((*s >= '0' && *s <= '9') || *s == '*' || *s == '#') { *o++ = *s; continue; }
    for (int k = 0; k < 8; k++) if (strchr(L[k], *s)) { *o++ = (char)('2' + k); break; }
  }
  *o = 0; return out;
}
