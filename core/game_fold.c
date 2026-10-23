// FOLD ROOMS on the VMX: the C core and 64x64 renderer from the FOLD repo (microgames/fold: core/fold.c,
// app/vmx.c), the levels built in (core/fold_levels.h). Controls as the FOLD app's playtest: D-pad moves (repeats
// while held), A talks to the tile you face, B undoes (held: restart). Clearing a level: A goes to the next one.
// The firmware remembers how many levels you have cleared and starts at the next one; SAVES shows that count.
#include "vmxfw.h"
#include "surf.h"
#include "../../microgames/fold/core/fold.h"
#include "../../microgames/fold/app/vmx.h"
#include "fold_levels.h"
#include <stdio.h>
#include <string.h>

static FoldLevel *lv; static FoldGame *g;
static uint8_t buf[64 * 64];
static int level, cleared, hold[4], holdB, popT, wonT, talkOn, talkPage, demoK, demoWait;
static const char *popWord; static char talk[FOLD_TEXTLEN * 2];

static void load(int i) {
  level = ((i % FOLD_NLEVELS) + FOLD_NLEVELS) % FOLD_NLEVELS;
  char err[96];
  if (!fold_parse(lv, FOLD_LEVELS[level], err, sizeof err)) { memset(lv, 0, sizeof *lv); }
  fold_start(g, lv); popT = wonT = talkOn = holdB = demoK = demoWait = 0; memset(hold, 0, sizeof hold);
}
static void start(uint32_t seed, int demo) {
  (void)seed;
  if (!lv) { lv = plat_alloc(sizeof *lv); g = plat_alloc(sizeof *g); plat_load("g56581010", &cleared, sizeof cleared); }
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
static void tick(const VmxIn *in, int demo) {
  isDemo = demo;
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
    moved(fold_move(g, (d - g->camrot) & 3));
    return;
  }
  static const int BIT[4] = { B_UP, B_RIGHT, B_DOWN, B_LEFT };
  for (int d = 0; d < 4; d++) {                 // first press, then every 5 ticks after 10 (as the JS core)
    hold[d] = (in->held & BIT[d]) ? hold[d] + 1 : 0;
    if (hold[d] == 1 || (hold[d] > 10 && (hold[d] - 10) % 5 == 0)) { moved(fold_move(g, d)); break; }
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
  vmx_game(buf, g, level + 1, popT ? popWord : NULL);
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
