// The VMX's firmware screens, ported from the simulator (POCKET ARCADE vmx/vmx.js, branch vmx-call): boot, main
// menu, GAMES roulette, SAVES, ERASE, SETTINGS, ABOUT, CALL, playing a game, attract mode. Same layout, same timing,
// same sounds. Differences: the device boots at power-up (no tap needed for sound), START/SELECT on the test pad
// are the side button.
#include "vmxfw.h"
#include "surf.h"
#include "sound.h"
#include "records.h"
#include "xmask.h"
#include <math.h>
#include <stdio.h>
#include <string.h>

static uint8_t lcdBuf[64 * 64];
static Surf lcd = { 64, 64, lcdBuf };
static const char *MAIN[4] = { "GAMES", "SAVES", "SETTINGS", "CALL" };
static const char *SETTINGS[4] = { "SOUND", "BRIGHT", "WIFI", "ABOUT" };
enum { SC_OFF, SC_BOOT, SC_MAIN, SC_GAMES, SC_SAVES, SC_ERASE, SC_SETTINGS, SC_ABOUT, SC_WIFI, SC_PLAY,
       SC_CALL, SC_CALLHOLD, SC_CALLDIAL, SC_CALLSEND, SC_CALLCODE };

// ---------------------------------------------------------------- state and saves
static struct { uint8_t sound, bright, pad[2]; uint32_t serial; } SET = { 1, 2, {0, 0}, 0 };
typedef struct { char id[12]; VmxRec r; } SavedRec;
static SavedRec RECS[16];
static struct {
  int screen; float t, boot, idle, flashT; char flash[16];
  int main, game, save, erase, setSel, callSel; float mainPos, gamePos;
  // playing
  int playing, demo, attract, prevHeld, latch; float acc, since; const VmxGame *g; int k;
  // the call
  ScoreRec crec[16]; int cidx[16], cn; uint8_t msg[16 + 16 * REC_LEN + 2]; int mlen; char code[5], digits[24];
  float ct0, dialLen, sendLen;
} S;
static uint32_t nowMs; static int curHeld;
static float clock_s(void) { return nowMs / 1000.0f; }   // the call runs on real time, with its sound

static VmxRec *rec(const char *id) {
  for (int i = 0; i < 16; i++) if (!strcmp(RECS[i].id, id)) return &RECS[i].r;
  for (int i = 0; i < 16; i++) if (!RECS[i].id[0]) { snprintf(RECS[i].id, sizeof RECS[i].id, "%s", id); return &RECS[i].r; }
  return &RECS[15].r;
}
static void saveRecs(void) { plat_save("recs", RECS, sizeof RECS); }
static void saveSet(void) { plat_save("settings", &SET, sizeof SET); }

// ---------------------------------------------------------------- sounds (the simulator's)
static float N(int n) { return 440.0f * powf(2, (n - 69) / 12.0f); }
static void blip(void) { snd_tone(1800, 0, 0.025f, 0.04f, 0); }
static void okSnd(void) { snd_tone(1200, 0, 0.04f, 0.05f, 0); snd_tone(1800, 0.05f, 0.06f, 0.05f, 0); }
static void backSnd(void) { snd_tone(1500, 0, 0.04f, 0.05f, 0); snd_tone(900, 0.05f, 0.06f, 0.05f, 0); }
static void errSnd(void) { snd_tone(220, 0, 0.12f, 0.06f, 0); }
static void jingle(void) {           // owner: A6 E6 A5 E6 A6, single hits, starting right after the X settles
  const float T = 4.12f; const float n[5][3] = { {81, 0, 0.10f}, {76, 0.11f, 0.10f}, {69, 0.22f, 0.10f}, {76, 0.33f, 0.10f}, {81, 0.44f, 0.22f} };
  for (int i = 0; i < 5; i++) snd_chime(N((int)n[i][0]), T + n[i][1], n[i][2], 0.07f);
}

// ---------------------------------------------------------------- the boot
static const uint8_t BAYER[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
#define XW 128
#define XH 103
static uint8_t XM[XW * XH];
static void xmask_init(void) {
  int i = 0; const char *p = XMASK_ROWS;
  while (*p) {
    if (*p == '|' || *p == ',') { p++; continue; }
    int v = *p++ - '0', n = 0; while (*p >= '0' && *p <= '9') n = n * 10 + (*p++ - '0');
    while (n-- > 0 && i < XW * XH) XM[i++] = (uint8_t)v;
  }
  for (int y = 0; y < XH; y++) for (int x = XW / 2; x < XW; x++) XM[y * XW + x] = XM[y * XW + XW - 1 - x];   // symmetrical
}
static float clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
static float smooth(float v) { v = clamp01(v); return v * v * (3 - 2 * v); }
static float ease(float a, float b, float k) { return a + (b - a) * k; }
static int dith(int x, int y, float k) { return BAYER[(y & 3) * 4 + (x & 3)] < k * 16; }
static void goo(int cx, int cy, float R, float t, float glow) {
  for (int y = -30; y <= 30; y++) for (int x = -30; x <= 30; x++) {
    float a = atan2f((float)y, (float)x), d = hypotf((float)x, (float)y);
    float r = R * (1 + 0.22f * sinf(3 * a + t * 2.3f) + 0.16f * sinf(5 * a - t * 3.1f) + 0.10f * sinf(2 * a + t * 4.7f)
                   + 0.12f * sinf(7 * a + t * 1.7f) * sinf(t * 1.3f));
    if (d <= r) s_px(&lcd, cx + x, cy + y, 1);
    else if (glow > 0 && d <= r + glow && dith(cx + x, cy + y, 0.5f * (1 - (d - r) / glow))) s_px(&lcd, cx + x, cy + y, 1);
  }
}
static void drawX(float cx, float cy, float w) {
  float h = w * XH / XW; int x0 = (int)lroundf(cx - w / 2), y0 = (int)lroundf(cy - h / 2);
  float shade = clamp01(1 - (w - 60) / 80);
  for (int j = 0; j < (int)ceilf(h); j++) for (int i = 0; i < (int)ceilf(w); i++) {
    int sx = (int)floorf((i + 0.5f) * XW / w), sy = (int)floorf((j + 0.5f) * XH / h);
    if (sx >= XW || sy >= XH) continue;
    int v = XM[sy * XW + sx], X = x0 + i, Y = y0 + j, mx = X < 32 ? X : 63 - X;
    if (v == 2 || (v == 1 && dith(mx, Y, shade))) s_px(&lcd, X, Y, 1);
  }
}
#define BOOT_END 6.4f
static void drawBoot(float t) {
  s_clear(&lcd, 0);
  if (t < 2.6f) goo(32, 30, ease(3, 8, smooth(t / 1.4f)) + 0.6f * sinf(t * 3), t, 3 + 1.5f * sinf(t * 4));
  else if (t < 3.1f) {
    float k = smooth((t - 2.6f) / 0.5f);
    goo(32, 30, ease(8, 16, k), t, ease(4, 60, k));
    if (k > 0.6f) for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) if (dith(x, y, (k - 0.6f) * 2.5f)) s_px(&lcd, x, y, 1);
  } else {
    float k = smooth((t - 3.1f) / 1.0f);
    if (t < 3.25f) { float f = 1 - (t - 3.1f) / 0.15f; for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) if (dith(x, y, f)) s_px(&lcd, x, y, 1); }
    drawX(32, ease(32, 22, k), ease(150, 46, k));
    if (t > 4.2f) {
      float q = smooth((t - 4.2f) / 0.6f); int sc = (int)lroundf(ease(7, 2, q)); if (sc < 2) sc = 2;
      int w = tw("VMX") * sc;
      s_big(&lcd, 32 - w / 2, (int)lroundf(ease(44, 45, q)), "VMX", sc);
    }
  }
}

// ---------------------------------------------------------------- menus
static uint8_t glyphBuf[4][144], artBuf[16][576];
static Surf GLYPH[4], ART[16];
static void menuGlyphs(void) {
  for (int k = 0; k < 4; k++) {
    Surf *s = &GLYPH[k]; *s = (Surf){ 12, 12, glyphBuf[k] };
    if (k == 0) { s_pill(s, 0, 3, 12, 7, PILL_LINE, 1); s_fill(s, 2, 6, 3, 1, 1); s_fill(s, 3, 5, 1, 3, 1); s_px(s, 8, 5, 1); s_px(s, 9, 7, 1); }
    if (k == 1) { s_rect(s, 2, 0, 8, 12, 1); s_fill(s, 4, 2, 4, 3, 1); for (int x = 3; x < 9; x += 2) s_fill(s, x, 9, 1, 2, 1); }
    if (k == 2) { s_ring(s, 6, 6, 4, 1); s_disc(s, 6, 6, 1, 1); const int p[8][2] = { {6,0},{6,11},{0,6},{11,6},{2,2},{10,2},{2,10},{10,10} };
      for (int i = 0; i < 8; i++) s_px(s, p[i][0], p[i][1], 1); }
    if (k == 3) { s_line(s, 1, 3, 3, 1, 1); s_fill(s, 1, 3, 2, 6, 1); s_line(s, 1, 8, 3, 10, 1); s_line(s, 3, 1, 4, 2, 1); s_line(s, 3, 10, 4, 9, 1);
      s_px(s, 7, 4, 1); s_px(s, 8, 5, 1); s_px(s, 8, 6, 1); s_px(s, 7, 7, 1); s_px(s, 9, 2, 1); s_px(s, 10, 3, 1); s_line(s, 11, 4, 11, 7, 1);
      s_px(s, 10, 8, 1); s_px(s, 9, 9, 1); }
  }
}
static void battery(int x, int y) { s_rect(&lcd, x, y, 9, 5, 1); s_px(&lcd, x + 9, y + 2, 1); s_fill(&lcd, x + 2, y + 2, 4, 1, 1); }
static void drawMain(void) {
  s_clear(&lcd, 0); battery(53, 1);
  float pos = S.mainPos;
  for (int d = -2; d <= 2; d++) {
    int k = (int)lroundf(pos) + d, idx = ((k % 4) + 4) % 4; float off = k - pos;
    float y = 29 + off * 12; int x = (int)lroundf(7 - 4.5f * off * off), w = tw(MAIN[idx]) + 8;
    s_pill(&lcd, x, (int)lroundf(y) - 1, w, 9, fabsf(off) < 0.5f ? PILL_RING : PILL_FILL, 1);
    s_text(&lcd, x + 4, (int)lroundf(y) + 1, MAIN[idx], 0);
  }
  s_blit(&lcd, &GLYPH[S.main], 48, 25, 12, 12, 0);
}
static void card(int x, int y, int s, int k, int sel) {
  s_fill(&lcd, x, y, s, s, 0); s_rrect(&lcd, x, y, s, s, 1); if (sel) s_rrect(&lcd, x - 1, y - 1, s + 2, s + 2, 1);
  s_blit(&lcd, &ART[k], x + 2, y + 2, s - 4, s - 4, 0);
}
static void drawGames(void) {
  s_clear(&lcd, 0);
  int n = VMX_NGAMES; float pos = S.gamePos;
  struct { float off; int idx, s, x, y; } L[5];
  for (int d = -2; d <= 2; d++) {
    int k = (int)lroundf(pos) + d; float off = k - pos, a = fabsf(off) < 1.5f ? fabsf(off) : 1.5f;
    L[d + 2].off = off; L[d + 2].idx = ((k % n) + n) % n; L[d + 2].s = (int)lroundf(32 - a * 10);
    L[d + 2].x = (int)lroundf(6 + off * 30); L[d + 2].y = (int)lroundf(12 - off * 13 + off * off * 2);
  }
  for (int i = 0; i < 5; i++) for (int j = i + 1; j < 5; j++) if (fabsf(L[j].off) > fabsf(L[i].off)) { __typeof__(L[0]) t = L[i]; L[i] = L[j]; L[j] = t; }
  for (int i = 0; i < 5; i++) card(L[i].x, L[i].y, L[i].s, L[i].idx, fabsf(L[i].off) < 0.5f);
  const VmxGame *g = VMX_GAMES[S.game];
  s_fill(&lcd, 39, 28, 25, 22, 0);
  for (int i = 0; i < 2; i++) if (g->name[i]) s_text(&lcd, 40, 30 + i * 7, g->name[i], 1);
  s_text(&lcd, 40, 44, "A:PLAY", 1);
  s_text(&lcd, 1, 52, "DEMO", 1); s_tri(&lcd, 8, 58, 0, 1);
  char b[24]; snprintf(b, sizeof b, "%d/%d", S.game + 1, n); s_text(&lcd, 64 - tw(b), 58, b, 1);
}
static void scoreLines(const VmxGame *g, const VmxRec *r, char out[3][16], int *n) {
  if (g->wld) { snprintf(out[0], 16, "WON %d", (int)r->w); snprintf(out[1], 16, "LOST %d", (int)r->l); snprintf(out[2], 16, "DRAWN %d", (int)r->d); *n = 3; }
  else { snprintf(out[0], 16, "BEST"); snprintf(out[1], 16, "%d", (int)r->best); *n = 2; }
}
static void drawSaves(void) {
  s_clear(&lcd, 0);
  const VmxGame *g = VMX_GAMES[S.save];
  s_blit(&lcd, &ART[S.save], 4, 4, 24, 24, 1); s_ring(&lcd, 16, 16, 15, 1);
  char L[3][16]; int n; scoreLines(g, rec(g->id), L, &n);
  for (int i = 0; i < n; i++) s_text(&lcd, 36, 4 + i * 8, L[i], 1);
  char b[24]; snprintf(b, sizeof b, "%d/%d", S.save + 1, VMX_NGAMES); s_text(&lcd, 36, 28, b, 1);
  s_tri(&lcd, 32, 37, 1, 1);
  int w = tw(g->title) + 10; if (w < 40) w = 40;
  s_pill(&lcd, 32 - w / 2, 42, w, 9, PILL_FILL, 1); s_centre(&lcd, 44, g->title, 0);
  s_tri(&lcd, 32, 54, 0, 1);
}
static void drawErase(void) {
  s_clear(&lcd, 0);
  s_pill(&lcd, 2, 2, 60, 9, PILL_FILL, 1); s_centre(&lcd, 4, VMX_GAMES[S.save]->title, 0);
  const char *o[2] = { "KEEP", "ERASE" };
  for (int i = 0; i < 2; i++) { int y = 19 + i * 13, on = S.erase == i;
    s_pill(&lcd, 9, y, 46, 9, on ? PILL_FILL : PILL_LINE, 1); s_centre(&lcd, y + 2, o[i], on ? 0 : 1); }
  s_text(&lcd, 2, 58, "B BACK  A OK", 1);
}
static void drawSettings(void) {
  s_clear(&lcd, 0);
  s_pill(&lcd, 2, 1, 60, 9, PILL_FILL, 1); s_centre(&lcd, 3, "SETTINGS", 0);
  const char *BR[3] = { "LOW", "MID", "HIGH" };
  const char *val[4] = { SET.sound ? "ON" : "OFF", BR[SET.bright % 3], ">", ">" };
  for (int i = 0; i < 4; i++) { int y = 14 + i * 10, sel = i == S.setSel;
    if (sel) s_fill(&lcd, 2, y - 2, 60, 9, 1);
    s_text(&lcd, 5, y, SETTINGS[i], !sel); s_text(&lcd, 59 - tw(val[i]), y, val[i], !sel); }
  s_text(&lcd, 2, 59, "A: CHANGE", 1);
}
static void drawAbout(void) {
  s_clear(&lcd, 0);
  s_big(&lcd, 32 - tw("VMX"), 4, "VMX", 2);
  s_centre(&lcd, 20, "FIRMWARE", 1); s_centre(&lcd, 30, plat_name(), 1);
  char b[16]; snprintf(b, sizeof b, "%d GAMES", VMX_NGAMES); s_centre(&lcd, 38, b, 1);
  s_centre(&lcd, 56, "B: BACK", 1);
}
static void drawWifi(void) {             // SETTINGS > WIFI: what the platform says, A = set up
  s_clear(&lcd, 0);
  s_pill(&lcd, 2, 1, 60, 9, PILL_FILL, 1); s_centre(&lcd, 3, "WIFI", 0);
  char L[4][16]; memset(L, 0, sizeof L); int n = plat_wifi(L);
  if (!n) s_centre(&lcd, 24, "NO WIFI HERE", 1);
  for (int i = 0; i < n; i++) s_centre(&lcd, 15 + i * 8, L[i], 1);
  s_text(&lcd, 2, 52, "A: SET UP", 1); s_text(&lcd, 2, 59, "B: BACK", 1);
}
static void drawFlash(void) {
  if (!S.flash[0] || S.t - S.flashT > 1.1f) return;
  int w = tw(S.flash) + 8, x = 32 - w / 2;
  s_fill(&lcd, x - 1, 23, w + 2, 13, 0); s_pill(&lcd, x, 24, w, 11, PILL_FILL, 1); s_text(&lcd, x + 4, 27, S.flash, 0);
}
static void flash(const char *m) { snprintf(S.flash, sizeof S.flash, "%s", m); S.flashT = S.t; }

// ---------------------------------------------------------------- CALL (vmx-world/docs/DESIGN.md "Calling in")
static int day_now(void) { int d = plat_day(); return d < 0 ? 0 : d; }
static int callRecords(void) {         // fills S.crec / S.cidx with every score not sent yet; returns how many
  int n = 0;
  for (int i = 0; i < VMX_NGAMES && n < 16; i++) {
    const VmxGame *g = VMX_GAMES[i]; VmxRec *r = rec(g->id); if (!g->tid) continue;
    int32_t score = g->wld ? r->w : r->best;
    if (!score && !(r->l || r->d)) continue;
    int32_t key[4] = { score, g->wld ? r->w : 0, g->wld ? r->l : 0, g->wld ? r->d : 0 };
    if (r->sentOk && !memcmp(r->sent, key, sizeof key)) continue;
    S.crec[n] = (ScoreRec){ g->tid, 0, 0, r->day, score, { (uint16_t)key[1], (uint16_t)key[2], (uint16_t)key[3] } };
    S.cidx[n++] = i;
  }
  // VMX CASINO's chip stash (vmx-world/docs/CASINO.md): score = chips now, field 1 = the most ever held, field 2 =
  // chips cashed out at the Xbox; listed once chips have been played
  const VmxBank *bk = vmx_bank();
  if (n < 16 && (bk->staked || bk->paid) && !(bk->sentOk && bk->sent == bk->chips)) {
    uint16_t pk = bk->peak > 65535 ? 65535 : (uint16_t)bk->peak, ca = bk->cashed > 65535 ? 65535 : (uint16_t)bk->cashed;
    S.crec[n] = (ScoreRec){ 0x56581F00u, 0, 0, (uint16_t)day_now(), bk->chips, { pk, ca, 0 } }; S.cidx[n++] = -1;
  }
  return S.cn = n;
}
static void drawCall(void) {
  s_clear(&lcd, 0); s_centre(&lcd, 1, "CALL", 1); s_line(&lcd, 0, 7, 63, 7, 1);
  char b[24];
  if (S.screen == SC_CALL) {
    int n = callRecords();
    if (!n) { s_centre(&lcd, 22, "NO NEW", 1); s_centre(&lcd, 29, "SCORES", 1); s_centre(&lcd, 52, "B: BACK", 1); return; }
    int top = S.callSel - 1; if (top > n - 4) top = n - 4; if (top < 0) top = 0;
    for (int i = 0; i < 4 && top + i < n; i++) { int y = 10 + i * 8; int gi = S.cidx[top + i];
      snprintf(b, 9, "%s", gi < 0 ? "CHIPS" : VMX_GAMES[gi]->title); s_text(&lcd, 1, y, b, 1);
      snprintf(b, sizeof b, "%d", (int)S.crec[top + i].score); s_text(&lcd, 63 - tw(b), y, b, 1); }
    if (n > 4) { if (top > 0) s_tri(&lcd, 60, 9, 1, 1); if (top + 4 < n) s_tri(&lcd, 60, 41, 0, 1); }
    snprintf(b, sizeof b, "%d NEW", n); s_centre(&lcd, 46, b, 1); s_centre(&lcd, 55, "A: CALL IN", 1);
  } else if (S.screen == SC_CALLHOLD) {
    s_centre(&lcd, 13, "HOLD MY TOP", 1); s_centre(&lcd, 20, "TO THE PHONE", 1); s_centre(&lcd, 27, "MOUTHPIECE", 1);
    s_centre(&lcd, 45, "A: DIAL", 1); s_centre(&lcd, 54, "B: BACK", 1);
  } else if (S.screen == SC_CALLDIAL) {
    s_centre(&lcd, 16, "DIALLING", 1);
    int k = (int)floorf((clock_s() - S.ct0) / 0.16f + 0.2f), len = (int)strlen(S.digits); if (k > len) k = len;
    char n[24] = "", s[32] = ""; memcpy(n, S.digits, (size_t)k); n[k] = 0;
    if (k) { snprintf(s, sizeof s, "1-%.3s", n + 1); if (k > 4) { strcat(s, "-"); strcat(s, n + 4); } }
    s_centre(&lcd, 26, s, 1);
    if (clock_s() - S.ct0 > S.dialLen) { s_centre(&lcd, 40, "WHEN IT SAYS", 1); s_centre(&lcd, 47, "SEND NOW:", 1); s_centre(&lcd, 55, "PRESS A", 1); }
  } else if (S.screen == SC_CALLSEND) {
    s_centre(&lcd, 14, "SENDING", 1); snprintf(b, sizeof b, "%d SCORE%s", S.cn, S.cn > 1 ? "S" : ""); s_centre(&lcd, 22, b, 1);
    float p = clamp01((clock_s() - S.ct0) / S.sendLen);
    s_rect(&lcd, 6, 34, 52, 7, 1); s_fill(&lcd, 7, 35, (int)lroundf(50 * p), 5, 1);
    s_centre(&lcd, 48, "KEEP HOLDING", 1);
  } else if (S.screen == SC_CALLCODE) {
    s_centre(&lcd, 11, "THE VOICE SAYS", 1); s_centre(&lcd, 18, "A CODE. MINE:", 1); s_big(&lcd, 17, 26, S.code, 2);
    s_centre(&lcd, 46, "A: IT MATCHES", 1); s_centre(&lcd, 55, "B: TRY AGAIN", 1);
  }
}
static void startDial(void) {
  if (!callRecords()) return;
  S.mlen = msg_pack(SET.serial, "VMX", S.crec, S.cn, S.msg);
  check_code(S.msg, S.mlen, S.code);
  keypad("1-800-VMX-WRLD", S.digits);
  S.ct0 = clock_s(); S.dialLen = snd_dial(S.digits);
  S.screen = SC_CALLDIAL;
}
static void startSend(void) { S.ct0 = clock_s(); S.sendLen = snd_send(S.msg, S.mlen); S.screen = SC_CALLSEND; }

// ---------------------------------------------------------------- playing
static void go(int sc) { S.screen = sc; S.idle = 0; }
static void open_game(int k, int demo, int attract) {
  S.g = VMX_GAMES[k]; S.k = k; S.demo = demo; S.attract = attract; S.acc = 0; S.since = 0; S.latch = 0;
  S.prevHeld = curHeld;                                  // a button still down from the menu isn't a press in the game
  S.g->start(nowMs % 100000, demo); S.playing = 1; go(SC_PLAY);
}
static void close_game(void) { S.playing = 0; go(SC_GAMES); }
static void tickGame(int held) {
  VmxIn in = { held | S.latch, 0 }; S.latch = 0;
  in.edges = in.held & ~S.prevHeld; S.prevHeld = in.held;
  if (S.demo) { if (in.edges & 63) { backSnd(); close_game(); return; } }
  else if ((in.edges & B_B) && !S.g->usesB) { backSnd(); close_game(); return; }
  S.g->tick(&in, S.demo);
  if (!S.demo) { VmxRec *r = rec(S.g->id); if (S.g->record(r)) { r->day = (uint16_t)day_now(); saveRecs(); } }
  if (S.attract && (S.since += 1 / 30.0f) > 25) { int n = (S.k + 1) % VMX_NGAMES; S.game = n; open_game(n, 1, 1); }
}

// ---------------------------------------------------------------- input
static void power(void) { snd_mute(!SET.sound); S.playing = 0; snd_hangup(); go(SC_BOOT); S.boot = 0; jingle(); }
static void press(int b) {
  S.idle = 0;
  int sc = S.screen;
  if (sc == SC_OFF) { power(); return; }
  if (sc == SC_BOOT) return;
  if (sc == SC_PLAY) { if (b == B_SIDE) { backSnd(); close_game(); } return; }
  if (b == B_SIDE) b = B_B;
  int n = VMX_NGAMES;
  switch (sc) {
    case SC_MAIN:
      if (b == B_UP) { S.main = (S.main + 3) % 4; blip(); }
      if (b == B_DOWN) { S.main = (S.main + 1) % 4; blip(); }
      if (b == B_A) { okSnd(); if (S.main == 3) S.callSel = 0; const int to[4] = { SC_GAMES, SC_SAVES, SC_SETTINGS, SC_CALL }; go(to[S.main]); }
      break;
    case SC_GAMES:
      if (b == B_LEFT || b == B_UP) { S.game = (S.game + n - 1) % n; blip(); }
      if (b == B_RIGHT) { S.game = (S.game + 1) % n; blip(); }
      if (b == B_A) { okSnd(); open_game(S.game, 0, 0); }
      if (b == B_DOWN) { okSnd(); open_game(S.game, 1, 0); }
      if (b == B_B) { backSnd(); go(SC_MAIN); }
      break;
    case SC_SAVES:
      if (b == B_UP) { S.save = (S.save + n - 1) % n; blip(); }
      if (b == B_DOWN) { S.save = (S.save + 1) % n; blip(); }
      if (b == B_A) { okSnd(); S.erase = 0; go(SC_ERASE); }
      if (b == B_B) { backSnd(); go(SC_MAIN); }
      break;
    case SC_ERASE:
      if (b == B_UP || b == B_DOWN) { S.erase = 1 - S.erase; blip(); }
      if (b == B_A) { if (S.erase) { memset(rec(VMX_GAMES[S.save]->id), 0, sizeof(VmxRec)); saveRecs(); backSnd(); flash("ERASED"); } else okSnd(); go(SC_SAVES); }
      if (b == B_B) { backSnd(); go(SC_SAVES); }
      break;
    case SC_SETTINGS:
      if (b == B_UP) { S.setSel = (S.setSel + 3) % 4; blip(); }
      if (b == B_DOWN) { S.setSel = (S.setSel + 1) % 4; blip(); }
      if (b == B_A || b == B_LEFT || b == B_RIGHT) {
        if (S.setSel == 0) { SET.sound = !SET.sound; snd_mute(!SET.sound); okSnd(); }
        if (S.setSel == 1) { SET.bright = (uint8_t)((SET.bright + (b == B_LEFT ? 2 : 1)) % 3); blip(); }
        if (S.setSel == 2 && b == B_A) { okSnd(); go(SC_WIFI); }
        if (S.setSel == 3 && b == B_A) { okSnd(); go(SC_ABOUT); }
        saveSet();
      }
      if (b == B_B) { backSnd(); go(SC_MAIN); }
      break;
    case SC_ABOUT: if (b == B_B) { backSnd(); go(SC_SETTINGS); } break;
    case SC_WIFI: if (b == B_A) { okSnd(); plat_wifi_setup(); } if (b == B_B) { backSnd(); go(SC_SETTINGS); } break;
    case SC_CALL: {
      int c = callRecords();
      if (b == B_UP && S.callSel > 0) { S.callSel--; blip(); }
      if (b == B_DOWN && S.callSel < c - 1) { S.callSel++; blip(); }
      if (b == B_A) { if (c) { okSnd(); go(SC_CALLHOLD); } else errSnd(); }
      if (b == B_B) { backSnd(); go(SC_MAIN); }
    } break;
    case SC_CALLHOLD: if (b == B_A) startDial(); if (b == B_B) { backSnd(); go(SC_CALL); } break;
    case SC_CALLDIAL:
      if (b == B_A && clock_s() - S.ct0 > S.dialLen) startSend();
      if (b == B_B) { snd_hangup(); backSnd(); go(SC_CALL); }
      break;
    case SC_CALLSEND: if (b == B_B) { snd_hangup(); backSnd(); go(SC_CALL); } break;
    case SC_CALLCODE:
      if (b == B_A) {
        for (int i = 0; i < S.cn; i++) { if (S.cidx[i] < 0) { vmx_bank_sent(S.crec[i].score); continue; }
          VmxRec *r = rec(VMX_GAMES[S.cidx[i]]->id); const ScoreRec *q = &S.crec[i];
          r->sent[0] = q->score; for (int k = 0; k < 3; k++) r->sent[k + 1] = q->f[k]; r->sentOk = 1; }
        saveRecs(); snd_hangup(); okSnd(); go(SC_MAIN); flash("SENT");
      }
      if (b == B_B) { snd_hangup(); backSnd(); go(SC_CALL); }
      break;
  }
}

// ---------------------------------------------------------------- the frame
static int prevHeld; static uint32_t sideAt, lastMs; static int sideDown;
#define LONG_MS 700
static float chase(float cur, float target, int n, float dt) {
  float m = fmodf(cur, (float)n); if (m < 0) m += n;
  float d = target - m; if (d > n / 2.0f) d -= n; if (d < -n / 2.0f) d += n;
  return cur + d * (dt * 14 < 1 ? dt * 14 : 1);
}
void vmx_init(void) {
  games_init(); xmask_init(); menuGlyphs();
  for (int i = 0; i < VMX_NGAMES && i < 16; i++) { ART[i] = (Surf){ 24, 24, artBuf[i] }; VMX_GAMES[i]->art(&ART[i]); }
  plat_load("settings", &SET, sizeof SET); plat_load("recs", RECS, sizeof RECS);
  memset(&S, 0, sizeof S); S.screen = SC_OFF;
}
const uint8_t *vmx_screen(void) { return S.screen == SC_PLAY && S.playing ? S.g->draw() : lcdBuf; }
int vmx_bright(void) { return SET.bright; }
int vmx_on(void) { return S.screen != SC_OFF; }
void vmx_frame(uint32_t ms, int held) {
  if (!lastMs) {                                         // power-up: pick this VMX's serial once, then boot
    lastMs = ms ? ms : 1;
    if (!SET.serial) { SET.serial = ((ms + 1) * 2654435761u ^ (uint32_t)plat_day() * 40503u) & 0xFFFFFF; if (!SET.serial) SET.serial = 1; saveSet(); }
    power();
  }
  float raw = (ms - lastMs) / 1000.0f, dt = raw < 0.05f ? raw : 0.05f; lastMs = ms; nowMs = ms; S.t += dt;
  // buttons: edges into press(); the side button decides on release (short = back, long = power)
  int down = held & ~prevHeld, up = prevHeld & ~held; prevHeld = held; curHeld = held;
  if (down & B_SIDE) { sideAt = ms; sideDown = 1; }
  if ((up & B_SIDE) && sideDown) { sideDown = 0; if (ms - sideAt > LONG_MS) power(); else press(B_SIDE); }
  if (sideDown && ms - sideAt > LONG_MS) { sideDown = 0; power(); }
  for (int b = B_UP; b <= B_B; b <<= 1) if (down & b) {
    if (S.screen == SC_PLAY) { S.latch |= b; if (S.demo) S.idle = 0; }
    else press(b);
  }
  S.mainPos = chase(S.mainPos, (float)S.main, 4, dt);
  S.gamePos = chase(S.gamePos, (float)S.game, VMX_NGAMES, dt);
  if (S.screen == SC_PLAY && S.playing) {
    S.acc += raw < 0.25f ? raw : 0.25f;
    while (S.playing && S.screen == SC_PLAY && S.acc >= 1 / 30.0f) { S.acc -= 1 / 30.0f; tickGame(held & 63); }
    return;
  }
  if (S.screen == SC_MAIN || S.screen == SC_GAMES) { S.idle += dt; if (S.idle > 30) { S.idle = 0; open_game(S.game, 1, 1); return; } }
  if (S.screen == SC_CALLSEND && clock_s() - S.ct0 > S.sendLen) go(SC_CALLCODE);
  switch (S.screen) {
    case SC_OFF: s_clear(&lcd, 0); break;
    case SC_BOOT: S.boot += dt; drawBoot(S.boot); if (S.boot > BOOT_END) go(SC_MAIN); break;
    case SC_MAIN: drawMain(); break;
    case SC_GAMES: drawGames(); break;
    case SC_SAVES: drawSaves(); break;
    case SC_ERASE: drawErase(); break;
    case SC_SETTINGS: drawSettings(); break;
    case SC_ABOUT: drawAbout(); break;
    case SC_WIFI: drawWifi(); break;
    default: drawCall(); break;
  }
  drawFlash();
}
