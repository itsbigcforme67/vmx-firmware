// 1-bit drawing for the VMX: the same shapes and 3x5 font as the simulator (POCKET ARCADE vmx/vmx.js, itself from
// the VMX mockup in XMU GADGET/ui/mockup).
#ifndef SURF_H
#define SURF_H
#include <stdint.h>

typedef struct { int w, h; uint8_t *d; } Surf;
enum { PILL_FILL, PILL_LINE, PILL_RING };

void s_clear(Surf *s, int c);
void s_px(Surf *s, int x, int y, int c);
int  s_get(const Surf *s, int x, int y);
void s_fill(Surf *s, int x, int y, int w, int h, int c);
void s_rect(Surf *s, int x, int y, int w, int h, int c);
void s_rrect(Surf *s, int x, int y, int w, int h, int c);   // corners left out
void s_line(Surf *s, int x0, int y0, int x1, int y1, int c);
void s_disc(Surf *s, int cx, int cy, int r, int c);
void s_ring(Surf *s, int cx, int cy, int r, int c);
void s_pill(Surf *s, int x, int y, int w, int h, int mode, int c);
void s_text(Surf *s, int x, int y, const char *t, int c);
void s_centre(Surf *s, int y, const char *t, int c);        // centred on a 64-wide screen
void s_big(Surf *s, int x, int y, const char *t, int sc);   // the font, each dot sc x sc
void s_tri(Surf *s, int x, int y, int up, int c);
void s_blit(Surf *s, const Surf *src, int dx, int dy, int dw, int dh, int circle);
int  tw(const char *t);                                      // text width in pixels
const char *font_glyph(int ch);                              // 15 chars of '0'/'1', rows top to bottom
#endif
