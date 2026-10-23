#include "surf.h"
#include <math.h>
#include <string.h>
#include <stdlib.h>

static const struct { char c; const char *g; } FONT[] = {
  {'A',"010101111101101"},{'B',"110101110101110"},{'C',"011100100100011"},{'D',"110101101101110"},{'E',"111100110100111"},
  {'F',"111100110100100"},{'G',"011100101101011"},{'H',"101101111101101"},{'I',"111010010010111"},{'J',"001001001101010"},
  {'K',"101101110101101"},{'L',"100100100100111"},{'M',"101111111101101"},{'N',"110101101101101"},{'O',"010101101101010"},
  {'P',"110101110100100"},{'Q',"010101101110011"},{'R',"110101110101101"},{'S',"011100010001110"},{'T',"111010010010010"},
  {'U',"101101101101111"},{'V',"101101101101010"},{'W',"101101111111101"},{'X',"101101010101101"},{'Y',"101101010010010"},
  {'Z',"111001010100111"},{'0',"111101101101111"},{'1',"010110010010111"},{'2',"110001010100111"},{'3',"110001010001110"},
  {'4',"101101111001001"},{'5',"111100110001110"},{'6',"011100111101111"},{'7',"111001010010010"},{'8',"111101111101111"},
  {'9',"111101111001110"},{' ',"000000000000000"},{'.',"000000000000010"},{'-',"000000111000000"},{'?',"110001010000010"},
  {'!',"010010010000010"},{':',"000010000010000"},{'/',"001001010100100"},{'%',"101001010100101"},{'(',"010100100100010"},
  {')',"010001001001010"},{'+',"000010111010000"},{'>',"100010001010100"},{'<',"001010100010001"},{'*',"000101010101000"},
};
const char *font_glyph(int ch) {
  if (ch >= 'a' && ch <= 'z') ch -= 32;
  for (unsigned i = 0; i < sizeof FONT / sizeof FONT[0]; i++) if (FONT[i].c == ch) return FONT[i].g;
  return "110001010000010";                                   // '?'
}
// the FOLD renderer (microgames/fold/app/vmx.c) asks for its glyphs by this name; unknown ones are blank there.
// Weak: a build that links FOLD's own gfx.c (VMX World) uses that one.
__attribute__((weak)) const char *gfx_glyph(int ch) {
  if (ch >= 'a' && ch <= 'z') ch -= 32;
  for (unsigned i = 0; i < sizeof FONT / sizeof FONT[0]; i++) if (FONT[i].c == ch) return FONT[i].g;
  return FONT[36].g;
}
int tw(const char *t) { int n = (int)strlen(t); return n ? n * 4 - 1 : 0; }

void s_clear(Surf *s, int c) { memset(s->d, c, (size_t)(s->w * s->h)); }
void s_px(Surf *s, int x, int y, int c) { if (x < 0 || y < 0 || x >= s->w || y >= s->h) return; s->d[y * s->w + x] = (uint8_t)c; }
int s_get(const Surf *s, int x, int y) { return (x < 0 || y < 0 || x >= s->w || y >= s->h) ? 0 : s->d[y * s->w + x]; }
void s_fill(Surf *s, int x, int y, int w, int h, int c) { for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) s_px(s, x + i, y + j, c); }
void s_rect(Surf *s, int x, int y, int w, int h, int c) {
  for (int i = 0; i < w; i++) { s_px(s, x + i, y, c); s_px(s, x + i, y + h - 1, c); }
  for (int j = 0; j < h; j++) { s_px(s, x, y + j, c); s_px(s, x + w - 1, y + j, c); }
}
void s_rrect(Surf *s, int x, int y, int w, int h, int c) {
  for (int i = 1; i < w - 1; i++) { s_px(s, x + i, y, c); s_px(s, x + i, y + h - 1, c); }
  for (int j = 1; j < h - 1; j++) { s_px(s, x, y + j, c); s_px(s, x + w - 1, y + j, c); }
}
void s_line(Surf *s, int x0, int y0, int x1, int y1, int c) {
  int n = abs(x1 - x0) > abs(y1 - y0) ? abs(x1 - x0) : abs(y1 - y0); if (n < 1) n = 1;
  for (int i = 0; i <= n; i++) s_px(s, (int)lroundf(x0 + (x1 - x0) * (float)i / n), (int)lroundf(y0 + (y1 - y0) * (float)i / n), c);
}
void s_disc(Surf *s, int cx, int cy, int r, int c) {
  for (int y = -r - 1; y <= r + 1; y++) for (int x = -r - 1; x <= r + 1; x++) if (x * x + y * y <= r * r + r * 0.6f) s_px(s, cx + x, cy + y, c);
}
void s_ring(Surf *s, int cx, int cy, int r, int c) {
  for (int y = -r - 1; y <= r + 1; y++) for (int x = -r - 1; x <= r + 1; x++) {
    int d = x * x + y * y; if (d <= r * r + r * 0.6f && d > (r - 1) * (r - 1) + (r - 1) * 0.6f) s_px(s, cx + x, cy + y, c); }
}
static int pill_in(int i, int j, int W, int H) {
  float r = H / 2.0f, cx = i + 0.5f; if (cx < r) cx = r; if (cx > W - r) cx = W - r;
  return hypotf(i + 0.5f - cx, j + 0.5f - r) <= r + 0.15f;
}
void s_pill(Surf *s, int x, int y, int w, int h, int mode, int c) {
  if (mode == PILL_RING) { s_pill(s, x - 2, y - 2, w + 4, h + 4, PILL_LINE, c); mode = PILL_FILL; }
  for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) {
    if (!pill_in(i, j, w, h)) continue;
    if (mode == PILL_FILL) s_px(s, x + i, y + j, c);
    else if (!(i > 0 && j > 0 && i < w - 1 && j < h - 1 && pill_in(i - 1, j - 1, w - 2, h - 2))) s_px(s, x + i, y + j, c);
  }
}
void s_text(Surf *s, int x, int y, const char *t, int c) {
  for (; *t; t++, x += 4) { const char *g = font_glyph(*t);
    for (int r = 0; r < 5; r++) for (int q = 0; q < 3; q++) if (g[r * 3 + q] == '1') s_px(s, x + q, y + r, c); }
}
void s_centre(Surf *s, int y, const char *t, int c) { s_text(s, 32 - tw(t) / 2, y, t, c); }
void s_big(Surf *s, int x, int y, const char *t, int sc) {
  for (; *t; t++, x += 4 * sc) { const char *g = font_glyph(*t);
    for (int r = 0; r < 5; r++) for (int q = 0; q < 3; q++) if (g[r * 3 + q] == '1') s_fill(s, x + q * sc, y + r * sc, sc, sc, 1); }
}
void s_tri(Surf *s, int x, int y, int up, int c) {
  for (int r = 0; r < 3; r++) for (int q = -r; q <= r; q++) s_px(s, x + q, up ? y + r : y + 2 - r, c);
}
void s_blit(Surf *s, const Surf *src, int dx, int dy, int dw, int dh, int circle) {
  for (int j = 0; j < dh; j++) for (int i = 0; i < dw; i++) {
    if (circle) { float u = (i + 0.5f) / dw - 0.5f, v = (j + 0.5f) / dh - 0.5f; if (u * u + v * v > 0.25f) continue; }
    int sx = i * src->w / dw, sy = j * src->h / dh;
    s_px(s, dx + i, dy + j, src->d[sy * src->w + sx]);
  }
}
