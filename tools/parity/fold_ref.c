// FOLD ROOMS' parity reference: a frozen copy of the firmware's C game (core/game_fold.c as of 24 Oct 2026, when the
// game moved to C#: games/fold), so tools/parity/check.sh fold still works once the firmware no longer has it. Only
// two changes: the levels come from games/fold/levels.bin (the C# build's own file, so both play the same levels; the
// firmware had them built in from core/fold_levels.h, made from the same microgames/fold/levels), and the paths.
// The rules and the picture are still the FOLD repo's (microgames/fold core/fold.c, app/vmx.c), so a change there
// shows up here as a parity difference: the C# port wants the same change.
// Added on 24 Oct 2026 with the C# game: the turn into the next room (anim_* below; FoldRooms.cs DoMove / DrawTurn), the
// FOLD builder playtest's slide / corner turn as a 64x64 integer picture, so the check covers it too. Here the new room's
// picture is re-read every frame (the C# only when something changed), which checks the C#'s bookkeeping.
#include "../../core/vmxfw.h"
#include "../../core/surf.h"
#include "../../../microgames/fold/core/fold.h"
#include "../../../microgames/fold/app/vmx.h"
#include <stdlib.h>
#include <stdio.h>
#include <string.h>

static int FOLD_NLEVELS; static char **FOLD_LEVELS;
static void read_levels(void) {
  const char *path = getenv("FOLD_LEVELS_BIN") ? getenv("FOLD_LEVELS_BIN") : "games/fold/levels.bin";
  FILE *f = fopen(path, "rb"); if (!f) { perror(path); exit(1); }
  static uint8_t d[1 << 20]; size_t n = fread(d, 1, sizeof d, f); fclose(f);
  if (n < 2) exit(1);
  FOLD_NLEVELS = d[0] | d[1] << 8; FOLD_LEVELS = calloc((size_t)FOLD_NLEVELS, sizeof *FOLD_LEVELS);
  for (int i = 0; i < FOLD_NLEVELS; i++) {
    const uint8_t *o = d + 2 + 4 * i; uint32_t a = o[0] | o[1] << 8 | o[2] << 16 | (uint32_t)o[3] << 24, b = o[4] | o[5] << 8 | o[6] << 16 | (uint32_t)o[7] << 24;
    FOLD_LEVELS[i] = calloc(b - a + 1, 1); memcpy(FOLD_LEVELS[i], d + a, b - a);
  }
}
static FoldLevel *lv; static FoldGame *g;
static uint8_t buf[64 * 64];
static int level, cleared, hold[4], holdB, popT, wonT, talkOn, talkPage, demoK, demoWait;
static const char *popWord; static char talk[FOLD_TEXTLEN * 2];
enum { ANLEN = 12, D = 146 };
static int anT = -1, anSd, anRel; static int64_t lineOld[56], lineNew[56];
static void anim_lines(const uint8_t *b, int64_t *line, int sd) {   // a 64x64 picture's room as lines across the move
  for (int ta = 0; ta < 56; ta++) {
    int64_t L = 0;
    for (int tp = 0; tp < 56; tp++) {
      int tx = sd == 1 ? ta : sd == 3 ? 55 - ta : tp, ty = sd == 2 ? ta : sd == 0 ? 55 - ta : tp;
      if (b[(8 + ty) * 64 + 4 + tx]) L |= 1LL << tp;
    }
    line[ta] = L;
  }
}
static int anim_rel(int f0, int r0, int sd, int f1) {   // 0 flat, 1 outside corner, 2 inside (FoldRooms.cs Rel)
  static const int RP[4] = { 1, 0, -1, 0 }, RQ[4] = { 0, 1, 0, -1 }, DP[4] = { 0, -1, 0, 1 }, DQ[4] = { 1, 0, -1, 0 };
  const FoldSquare *A = &g->s.sq[f0 >> 1], *B = &g->s.sq[f1 >> 1]; int sa = f0 & 1 ? -1 : 1, sb = f1 & 1 ? -1 : 1;
  int px = sd == 1 ? RP[r0] : sd == 3 ? -RP[r0] : sd == 2 ? DP[r0] : -DP[r0], py = sd == 1 ? RQ[r0] : sd == 3 ? -RQ[r0] : sd == 2 ? DQ[r0] : -DQ[r0];
  int nn = 0, nd = 0;
  for (int i = 0; i < 3; i++) { int nb = sb * B->n[i]; nn += sa * A->n[i] * nb; nd += (px * sa * A->u[i] + py * A->v[i]) * nb; }
  return nn > 0 ? 0 : nd > 0 ? 1 : nd < 0 ? 2 : 0;
}
static const int TC[12] = { 16376, 16273, 15893, 15044, 13606, 11585, 9127, 6489, 3981, 1902, 506, 0 };
static const int TS[12] = { 506, 1902, 3981, 6489, 9127, 11585, 13606, 15044, 15893, 16273, 16376, 16384 };
static const int TE[12] = { 322, 1214, 2560, 4248, 6163, 8192, 10221, 12136, 13824, 15170, 16062, 16384 };
static const int BAYER[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
static void anim_face(const int64_t *line, int c, int sn, int64_t a, int64_t b) {   // FoldRooms.cs Face
  const int64_t DQ14 = D * 16384LL;
  int64_t s2l = -28LL * c + a, z2l = -28LL * sn + b, s2r = 28LL * c + a, z2r = 28LL * sn + b;
  if (s2l * (DQ14 - z2r) >= s2r * (DQ14 - z2l)) return;
  int sh = (4915 + (11469 * (c > 0 ? c : 0) >> 14)) * 40 >> 14, mask = 0;
  for (int k = 0; k < 16; k++) if (2 * BAYER[k] + 1 < sh) mask |= 1 << k;
  int hor = anSd == 1 || anSd == 3, sg = anSd == 1 || anSd == 2 ? 1 : -1, lo = hor ? 0 : 8, cA = hor ? 64 : 72, cP = hor ? 72 : 64, plo = hor ? 8 : 0;
  static int colTa[64], colSc[64];
  for (int i = lo; i < 64; i++) {
    int64_t A = sg * (2 * i + 1 - cA), num = A * (DQ14 - b) - 2LL * D * a, den = 2LL * D * c + A * sn;
    if (den < 0) { num = -num; den = -den; }
    if (den == 0 || num < -28 * den || num >= 28 * den) { colSc[i] = 0; continue; }
    colTa[i] = (int)((num + 28 * den) / den);
    colSc[i] = (int)((DQ14 << 16) / (DQ14 - (num * sn / den + b)));
  }
  int pbase = (cP - 1) * 32768 + 65535;
  #define PX(q) (hor ? (buf[(q) * 64 + i] = 1) : (buf[i * 64 + (q)] = 1))
  for (int i = lo; i < 64; i++) {
    int sc = colSc[i]; if (!sc) continue;
    int bs = pbase - 28 * sc, top = bs >> 16, bot = (56 * sc + bs) >> 16;
    int q0 = top < plo ? plo : top, q1 = bot > 64 ? 64 : bot;
    if ((i > lo && !colSc[i - 1]) || (i < 63 && !colSc[i + 1])) { for (int q = q0; q < q1; q++) PX(q); continue; }
    if (top >= plo) PX(top);
    if (bot - 1 < 64) PX(bot - 1);
    int64_t L = line[colTa[i]];
    int dm = 0; for (int k = 0; k < 4; k++) if (mask >> (hor ? (k << 2 | (i & 3)) : ((i & 3) << 2 | k)) & 1) dm |= 1 << k;
    for (int tp = 0; tp < 56; tp++) if (L >> tp & 1) {
      int q = (tp * sc + bs) >> 16, qe = (tp * sc + sc + bs) >> 16;
      if (q < q0) q = q0; if (qe > q1) qe = q1;
      for (; q < qe; q++) if (dm >> (q & 3) & 1) PX(q);
    }
  }
  #undef PX
}
static void anim_draw(void) {
  int T = anT, C = TC[T], S = TS[T];
  if (anRel == 0) { anim_face(lineOld, 16384, 0, -56LL * TE[T], 0); anim_face(lineNew, 16384, 0, 56LL * (16384 - TE[T]), 0); }
  else if (anRel == 1) { anim_face(lineOld, C, S, -28LL * S, -28LL * (16384 - C)); anim_face(lineNew, S, -C, 28LL * C, -28LL * (16384 - S)); }
  else { anim_face(lineOld, C, -S, -28LL * S, 28LL * (16384 - C)); anim_face(lineNew, S, C, 28LL * C, 28LL * (16384 - S)); }
}
static void popup(const char *line2) {          // vmx_game's pop-up
  for (int y = 23; y <= 43; y++) for (int x = 6; x <= 57; x++) buf[y * 64 + x] = y == 23 || y == 43 || x == 6 || x == 57;
  vmx_text(buf, 32 - 2 * 6 + 1, 27, "A ROOM"); vmx_text(buf, 32 - 2 * (int)strlen(line2) + 1, 35, line2);
}

static void load(int i) {
  level = ((i % FOLD_NLEVELS) + FOLD_NLEVELS) % FOLD_NLEVELS;
  char err[96];
  if (!fold_parse(lv, FOLD_LEVELS[level], err, sizeof err)) { memset(lv, 0, sizeof *lv); }
  fold_start(g, lv); anT = -1; popT = wonT = talkOn = holdB = demoK = demoWait = 0; memset(hold, 0, sizeof hold);
}
static void start(uint32_t seed, int demo) {
  (void)seed;
  if (!lv) { read_levels(); lv = plat_alloc(sizeof *lv); g = plat_alloc(sizeof *g); plat_load("g56581010", &cleared, sizeof cleared); }
  load(demo ? 0 : cleared < FOLD_NLEVELS ? cleared : FOLD_NLEVELS - 1);
}
static int isDemo;
static void moved(int r) {
  popWord = r & FOLD_FLIPPED ? "FLIPPED" : r & FOLD_TURNED ? "TURNED" : r & FOLD_SHIFTED ? "SHIFTED" : r & FOLD_HINGED ? "HINGED" : NULL;
  if (popWord) popT = 30;
  if (fold_solved(g)) {
    wonT = 1;
    if (!isDemo && level + 1 > cleared) { cleared = level + 1; plat_save("g56581010", &cleared, sizeof cleared); }
  }
}
static void moved(int r);
static void do_move(int sd) {                  // FoldRooms.cs DoMove: the turn starts from the room before the move
  static uint8_t old[64 * 64]; memset(old, 0, sizeof old);
  int f0 = g->camf, r0 = g->camrot;
  vmx_room(old, g->wall, g->target, g->box, g->btn, g->kind, g->aux, f0, r0, g->p, &g->s, 0);
  int r = fold_move(g, sd);
  if ((r & FOLD_CROSSED) && !(r & FOLD_TELEPORTED)) { anSd = sd; anim_lines(old, lineOld, sd); anRel = anim_rel(f0, r0, sd, g->camf); anT = 0; }
  moved(r);
}
static void tick(const VmxIn *in, int demo) {
  isDemo = demo;
  if (anT >= 0 && ++anT >= ANLEN) anT = -1;
  if (talkOn) {
    if (in->edges & (B_A | B_B)) { static uint8_t tmp[64 * 64]; if (++talkPage >= vmx_talk(tmp, talk, 0)) talkOn = 0; }
    return;
  }
  if (popT) { popT--; return; }
  if (wonT) {
    wonT++;
    if ((demo && wonT > 60) || (!demo && wonT > 15 && (in->edges & B_A))) load(level + 1);
    return;
  }
  if (demo) {                                   // the level's stored solution, a move every 6 ticks
    if (++demoWait < 6) return;
    demoWait = 0;
    if (demoK >= lv->nsol) { load(level + 1); return; }
    int d = lv->sol[demoK++] - '0';
    do_move((d - g->camrot) & 3);
    return;
  }
  static const int BIT[4] = { B_UP, B_RIGHT, B_DOWN, B_LEFT };
  for (int d = 0; d < 4; d++) {                 // first press, then every 5 ticks after 10 (as the JS core)
    hold[d] = (in->held & BIT[d]) ? hold[d] + 1 : 0;
    if (hold[d] == 1 || (hold[d] > 10 && (hold[d] - 10) % 5 == 0)) { do_move(d); break; }
  }
  if (in->edges & B_A) {
    int c = fold_facing(g);
    if (!fold_describe(g, c, talk, sizeof talk)) fold_describe(g, g->p, talk, sizeof talk);
    if (talk[0]) { talkOn = 1; talkPage = 0; }
  }
  if (in->held & B_B) { if (++holdB == 20) load(level); }
  else { if (holdB > 0 && holdB < 20) fold_undo(g); holdB = 0; }
}
static const uint8_t *draw(void) {
  if (talkOn) { vmx_talk(buf, talk, talkPage); return buf; }
  vmx_game(buf, g, level + 1, popT && anT < 0 ? popWord : NULL);
  if (anT >= 0) {                               // the turn into the next room: the room area redrawn, HUD kept
    anim_lines(buf, lineNew, anSd); memset(buf + 8 * 64, 0, 56 * 64); anim_draw();
    if (popT) popup(popWord);
  }
  if (wonT) {
    Surf s = { 64, 64, buf };
    s_fill(&s, 6, 22, 52, 22, 0); s_rect(&s, 6, 22, 52, 22, 1);
    s_centre(&s, 26, "CLEARED!", 1); if (wonT > 15) s_centre(&s, 35, "A: NEXT", 1);
  }
  return buf;
}
static int record(VmxRec *r) { if (cleared > r->best) { r->best = cleared; return 1; } return 0; }
static void art(void *p) {
  Surf *s = p;
  s_rect(s, 3, 3, 12, 12, 1); s_line(s, 3, 3, 9, 0, 1); s_line(s, 15, 3, 21, 0, 1); s_line(s, 15, 15, 21, 12, 1); s_line(s, 9, 0, 21, 0, 1);
  s_line(s, 21, 0, 21, 12, 1); s_fill(s, 7, 7, 4, 4, 1); s_rect(s, 12, 17, 5, 5, 1); s_px(s, 7, 19, 1); s_px(s, 8, 18, 1); s_px(s, 8, 20, 1);
  s_line(s, 0, 23, 23, 23, 1);
}
VmxGame GAME_FOLD = { "fold", "FOLD ROOMS", { "FOLD", "ROOMS" }, 0x56581010, 1, 0, art, start, tick, draw, record };
