// SIC BO for the VMX: a line-for-line port of microgames/ui/games/sicbo_core.js to C# 1.0, so it plays exactly the
// same (same random numbers, same integer maths, same order of events); tools/parity checks that frame by frame.
// The common Macau table: three dice shaken under a cup, each exactly 1 in 6 (rejection sampling), thrown before the
// cup comes down. Bets 0-20 chips each, several a round, the total never over the stash; payouts stake-included.
// House edges (exact over the 216 rolls, microgames/tools/sicbo_odds.py): SMALL / BIG / ODD / EVEN 1:1, all lose on
// a triple: 2.78%. SUM n: 4/17 60:1 15.28%, 5/16 30:1 13.89%, 6/15 17:1 16.67%, 7/14 12:1 9.72%, 8/13 8:1 12.50%,
// 9/12 6:1 18.98%, 10/11 6:1 12.50%. TRIPS (any triple) 30:1 13.89%. One triple (fff) 150:1 30.09%. DIE n (single
// die number) 1:1 per die showing it: 7.87%.
// Up/Down choose a row, Left/Right a chip off / on (held = repeat; on the TOTAL, TRIPLE and SINGLE rows they pick
// the number / face the row below bets on), A shakes, B clears. VMX CASINO: stakes come from the shared bank
// (Microsoft.Vmx.Chips) when the cup is shaken; a win pays stake + winnings. Board: the biggest payout in one round.
using System;
using Microsoft.Vmx;

public class SicBo : Game {
  int bestPay;                                              // this session's best (Scores)
  const string FONTCH = "ABCDEFGHIKLMNOPRSTUVWXYZ0123456789:!-<>+ ";
  static readonly string[] FONT = {
    "010101111101101", "110101110101110", "011100100100011", "110101101101110", "111100110100111", "111100110100100",
    "011100101101011", "101101111101101", "111010010010111", "101101110101101", "100100100100111", "101111111101101",
    "110101101101101", "010101101101010", "110101110100100", "110101110101101", "011100010001110", "111010010010010",
    "101101101101111", "101101101101010", "101101111111101", "101101010101101", "101101010010010", "111001010100111",
    "111101101101111", "010110010010111", "110001010100111", "110001010001110", "101101111001001", "111100110001110",
    "011100111101111", "111001010010010", "111101111101111", "111101111001110", "000010000010000", "010010010000010",
    "000000111000000", "001010100010001", "100010001010100", "000010111010000", "000000000000000" };
  static readonly int[] KX = { 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2 }, KY = { 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4 };
  static readonly int[] CHIP = { 14, 31, 27, 31, 14 };
  static readonly string[] NAMES = { "SMALL", "BIG", "ODD", "EVEN", "TOTAL", "SUM", "TRIPS", "TRIPLE", "ON", "SINGLE", "DIE" };
  const int SMALL = 0, BIG = 1, ODD = 2, EVEN = 3, TPICK = 4, TOTAL = 5, ANY = 6, FPICK = 7, TRIPLE = 8, DPICK = 9, DIE = 10;
  const int NB = 11, VIS = 3, CAP = 20;
  static readonly int[] TPAY = { 0, 0, 0, 0, 60, 30, 17, 12, 8, 6, 6, 6, 6, 8, 12, 17, 30, 60, 0 };
  static readonly int[] PLO = { 4, 1, 1 }, PN = { 14, 6, 6 };
  static readonly int[] CUPW = { 2, 3, 3, 2, 8, 11, 13, 14, 15, 16, 16, 17, 17, 18, 18, 18, 19, 20, 20 };
  const int CX = 44, CDOWN = 9;
  static readonly int[] DX = { 25, 38, 51 };
  static readonly int[] JIG = { 0, 1, 2, 1, 0, -1, -2, -1 };
  const int DROP = 8, SHK = 44, LF = 24, ROLLT = SHK + LF + 6;
  const int T2 = 34, LAG = 3;
  const int REST = 15, TREST = 31;
  const int TITLE = 0, BET = 1, ROLL = 2, RES = 3, BROKE = 4;

  // ==== DICE ART (the casino's shared dice look: SIC BO and CHUCK-A-LUCK copy this section) =======================
  // Two sizes: sz 0 = big, a 12x12 die (pips 2x2 on a 3x3 grid at 2/5/8), sz 1 = small, a 7x7 die (pips 1x1 at
  // 1/3/5). A face is a lit body with the corners cut and dark pips. DrawDie(x, y, face, sz) draws face 1-6 with its
  // top-left at x, y; DrawTumble(x, y, k, sz) draws tumbling frame k (0-2: the die turned about 22, 45 and 67
  // degrees), setting only its lit pixels. DieFrame(k, u, f) is the rolling animation: where die k is u ticks into
  // its throw (dfX, dfY = height above the felt) and which frame shows (dfF: 1-6 a face, -1..-3 tumble frame 0..2).
  static readonly int[] DSZ = { 12, 7 }, PIPW = { 2, 1 };
  static readonly int[][] PIPC = { new int[] { 2, 5, 8 }, new int[] { 1, 3, 5 } };
  static readonly int[] PIPS = { 0, 16, 68, 84, 325, 341, 455 };   // face n: pips on the 3x3 grid, bit (row * 3 + col)
  static readonly int[][][] TUMBLE = {                      // rows, bit 0 = left
    new int[][] { new int[] { 24, 248, 1020, 4068, 4070, 2046, 2046, 1663, 639, 1020, 496, 384 },
                  new int[] { 96, 240, 408, 924, 2046, 3219, 3219, 2046, 924, 408, 240, 96 },
                  new int[] { 384, 496, 1020, 831, 1855, 2046, 2046, 4046, 4044, 1020, 248, 24 } },
    new int[][] { new int[] { 4, 62, 126, 54, 63, 62, 16 }, new int[] { 8, 28, 62, 107, 62, 28, 8 },
                  new int[] { 16, 62, 47, 62, 122, 62, 4 } } };
  static readonly int[] FLICK = { 3, 5, 2, 6, 1, 4 };
  void DrawDie(int x, int y, int face, int sz) {
    int n = DSZ[sz], w = PIPW[sz], m = PIPS[face]; int[] pc = PIPC[sz];
    Fill(x + 1, y, n - 2, 1, true); Fill(x, y + 1, n, n - 2, true); Fill(x + 1, y + n - 1, n - 2, 1, true);
    for (int k = 0; k < 9; k++) if (((m >> k) & 1) != 0) Fill(x + pc[k % 3], y + pc[k / 3], w, w, false);
  }
  void DrawTumble(int x, int y, int k, int sz) {
    int[] fr = TUMBLE[sz][k]; int n = DSZ[sz];
    for (int r = 0; r < n; r++) { int m = fr[r]; for (int q = 0; m != 0; q++, m >>= 1) if ((m & 1) != 0) Px(x + q, y + r, true); }
  }
  static int Hop(int u) {
    if (u < 10) return 6 - 6 * u * u / 100;
    if (u < 22) { int t = u - 10; return 16 * t * (12 - t) / 144; }
    if (u < 30) { int t = u - 22; return 8 * t * (8 - t) / 64; }
    return 0;
  }
  int dfX, dfY, dfF;
  void DieFrame(int k, int u, int f) {
    int xf = 37 + 13 * k, s = T2 - u, x = xf - (xf + 13) * s * s / (T2 * T2), q = (x + 13) / 3;
    int fr = f;
    if (s > 2) fr = (q & 1) == 1 ? -1 - ((q / 2 + k) % 3) : FLICK[(q / 2 + 3 * k) % 6];
    dfX = x; dfY = Hop(u); dfF = fr;
  }
  void DrawDieAt(int k, int u, int f, int left, int top) {
    DieFrame(k, u, f);
    if (dfF > 0) DrawDie(dfX + left, top - dfY, dfF, 0); else DrawTumble(dfX + left, top - dfY, -1 - dfF, 0);
  }
  // ==== end of dice art ===========================================================================================

  static int[] GLYPH, CHAR;
  static void BuildFont() {
    GLYPH = new int[FONT.Length]; CHAR = new int[128];
    for (int f = 0; f < FONT.Length; f++) { int m = 0; for (int k = 0; k < 15; k++) if (FONT[f][k] == '1') m |= 1 << k; GLYPH[f] = m; }
    for (int c = 0; c < 128; c++) CHAR[c] = FONTCH.Length - 1;
    for (int f = 0; f < FONTCH.Length; f++) CHAR[FONTCH[f]] = f;
  }

  // game state (the JS object g)
  uint rs; int t, phase, pt, holdT, msgT; string msg = "", call = ""; bool auto_, demo;
  int[] st = new int[NB], rst = new int[NB], oc = new int[NB], amt = new int[NB], pr = new int[NB];
  int prN; int[] pk = new int[3]; int sel, d1, d2, d3; int[] hist = new int[5], histT = new int[5]; int histN; int net;
  int[] dRow = new int[2], dAmt = new int[2], dPk = new int[2]; int dN, dk; bool dClr;
  bool pUp, pDown, pLeft, pRight, pA, pB;
  Screen sc;

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }
  int Die() {
    int r = Rnd();
    while (r >= 65532) r = Rnd();
    return r % 6 + 1;
  }

  public override void Start(int seed, bool demo) {
    if (GLYPH == null) BuildFont();
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; holdT = 0; msg = ""; msgT = 0; auto_ = false;
    for (int i = 0; i < NB; i++) { st[i] = 0; rst[i] = 0; oc[i] = 0; amt[i] = 0; pr[i] = 0; }
    prN = 0; pk[0] = 10; pk[1] = 6; pk[2] = 1; sel = 0; d1 = 0; d2 = 0; d3 = 0; histN = 0; net = 0; call = "";
    dN = 0; dk = 0; dClr = false;
    pUp = pDown = pLeft = pRight = pA = pB = false;
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  public override bool UsesB { get { return true; } }

  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 50; }
  int Total() { int s = 0; for (int i = 0; i < NB; i++) s += st[i]; return s; }
  static bool IsPick(int r) { return r == TPICK || r == FPICK || r == DPICK; }
  static int PickOf(int r) { return r == TOTAL ? 0 : r == TRIPLE ? 1 : r == DIE ? 2 : -1; }
  static int PickRow(int r) { return r == TPICK ? 0 : r == FPICK ? 1 : 2; }
  string Label(int r) {
    if (r == TOTAL) return "SUM " + pk[0].ToString();
    if (r == TRIPLE) { string f = pk[1].ToString(); return f + f + f; }
    if (r == DIE) return "DIE " + pk[2].ToString();
    return NAMES[r];
  }
  string PayText(int r) {
    if (r == TOTAL) return TPAY[pk[0]].ToString() + ":1";
    return r == ANY ? "30:1" : r == TRIPLE ? "150:1" : "1:1";
  }
  void ToBet() {
    int run = 0, c = Chips.Balance();
    for (int i = 0; i < NB; i++) { st[i] = Math.Min(st[i], c - run); run += st[i]; }
    SetPhase(c < 1 ? BROKE : BET);
    dClr = false; dk = 0; dN = 0;
  }
  void Shake() {
    int tot = Total();
    if (tot == 0) { Say("PLACE A BET"); return; }
    Chips.Bet(tot);
    for (int i = 0; i < NB; i++) rst[i] = st[i];
    d1 = Die(); d2 = Die(); d3 = Die();
    SetPhase(ROLL);
  }
  void Settle() {
    int s = d1 + d2 + d3; bool trip = d1 == d2 && d2 == d3;
    int paid = 0, nt = 0;
    prN = 0;
    for (int i = 0; i < NB; i++) {
      int stk = st[i];
      if (stk == 0) continue;
      pr[prN++] = i;
      int ret = 0;
      if (i == SMALL) { if (!trip && s <= 10) ret = 2 * stk; }
      else if (i == BIG) { if (!trip && s >= 11) ret = 2 * stk; }
      else if (i == ODD) { if (!trip && (s & 1) == 1) ret = 2 * stk; }
      else if (i == EVEN) { if (!trip && (s & 1) == 0) ret = 2 * stk; }
      else if (i == TOTAL) { if (s == pk[0]) ret = stk * (TPAY[s] + 1); }
      else if (i == ANY) { if (trip) ret = 31 * stk; }
      else if (i == TRIPLE) { if (trip && d1 == pk[1]) ret = 151 * stk; }
      else {
        int f = pk[2], c = (d1 == f ? 1 : 0) + (d2 == f ? 1 : 0) + (d3 == f ? 1 : 0);
        if (c > 0) ret = stk * (c + 1);
      }
      if (ret > 0) { oc[i] = 1; paid += ret; amt[i] = ret - stk; nt += ret - stk; }
      else { oc[i] = 2; amt[i] = stk; nt -= stk; }
    }
    if (paid > 0) Chips.Pay(paid);
    net = nt;
    if (paid > bestPay) { bestPay = paid; if (!demo) Scores.Report(bestPay, 0, 0, 0); }   // board: biggest payout
    call = trip ? "TRIPLE " + d1.ToString() : (s <= 10 ? "SMALL " : "BIG ") + s.ToString();
    for (int k = 4; k > 0; k--) { hist[k] = hist[k - 1]; histT[k] = histT[k - 1]; }
    hist[0] = s; histT[0] = trip ? 1 : 0; if (histN < 5) histN++;
    if (nt > 0) Beeper.Tone(1047, 120, 40); else Beeper.Tone(440, 50, 40);
    SetPhase(RES);
  }
  void Act(bool aUp, bool aDown, bool aLeft, bool aRight, bool aA, bool aB) {
    if (aUp) sel = (sel + NB - 1) % NB;
    if (aDown) sel = (sel + 1) % NB;
    if (IsPick(sel)) {
      int p = PickRow(sel), lo = PLO[p], n = PN[p];
      if (aLeft) pk[p] = lo + (pk[p] - lo + n - 1) % n;
      if (aRight) pk[p] = lo + (pk[p] - lo + 1) % n;
    } else {
      if (aLeft && st[sel] > 0) st[sel]--;
      if (aRight) {
        if (st[sel] >= CAP) Say("TABLE MAX 20");
        else if (Total() >= Chips.Balance()) Say("NOT ENOUGH CHIPS");
        else st[sel]++;
      }
    }
    if (aB) { for (int i = 0; i < NB; i++) st[i] = 0; }
    if (aA) Shake();
  }
  void Plan() {
    int c = Chips.Balance();
    dN = 1; dRow[0] = Rnd() % 2; dPk[0] = 0;
    if (c < 10) { dAmt[0] = c; return; }
    dAmt[0] = 2 + Rnd() % 6;
    int k = Rnd() % 10;
    if (k < 3) { dN = 2; dRow[1] = DIE; dPk[1] = 1 + Rnd() % 6; dAmt[1] = 1 + Rnd() % 3; }
    else if (k < 5) { dN = 2; dRow[1] = TOTAL; dPk[1] = 7 + Rnd() % 8; dAmt[1] = 1 + Rnd() % 2; }
    else if (k == 5) { dN = 2; dRow[1] = ANY; dPk[1] = 0; dAmt[1] = 1; }
    else if (k == 6) { dN = 2; dRow[1] = ODD + Rnd() % 2; dPk[1] = 0; dAmt[1] = 2 + Rnd() % 4; }
  }
  void DemoMove() {
    bool u = false, d = false, l = false, r = false, a = false, b = false;
    if (!dClr) { dClr = true; if (Total() > 0) b = true; }
    else if (dk < dN) {
      int tr = dRow[dk], p = PickOf(tr);
      int want = p >= 0 && pk[p] != dPk[dk] ? tr - 1 : tr;
      if (sel < want) d = true;
      else if (sel > want) u = true;
      else if (want != tr) { if ((dPk[dk] - pk[p] + PN[p]) % PN[p] <= PN[p] / 2) r = true; else l = true; }
      else if (st[tr] < dAmt[dk] && st[tr] < CAP && Total() < Chips.Balance()) r = true;
      else dk++;
    } else a = true;
    Act(u, d, l, r, a, b);
  }

  public override void Tick(int held, int pressed) {
    bool auto = demo;
    t++; pt++; if (msgT > 0) msgT--;
    bool up = !auto && (held & Buttons.Up) != 0, down = !auto && (held & Buttons.Down) != 0,
      left = !auto && (held & Buttons.Left) != 0, right = !auto && (held & Buttons.Right) != 0,
      a = !auto && (held & Buttons.A) != 0, b = !auto && (held & Buttons.B) != 0;
    bool eUp = up && !pUp, eDown = down && !pDown, eLeft = left && !pLeft, eRight = right && !pRight, eA = a && !pA, eB = b && !pB;
    pUp = up; pDown = down; pLeft = left; pRight = right; pA = a; pB = b;
    if (up || down || left || right) holdT++; else holdT = 0;
    bool rep = holdT > 9 && (holdT & 1) == 0;
    auto_ = auto;

    switch (phase) {
      case TITLE:
        if (eA || (auto && pt > 40)) ToBet();
        break;
      case BET:
        if (auto) {
          if (dN == 0) Plan();
          if (pt > 12 && pt % 4 == 0) DemoMove();
        } else Act(eUp || (up && rep), eDown || (down && rep), eLeft || (left && rep), eRight || (right && rep), eA, eB);
        break;
      case ROLL:
        if (pt == DROP) Beeper.Tone(220, 20, 30);
        else if (pt > DROP && pt < SHK && (pt & 3) == 0) Beeper.Tone(330 + (pt & 4) * 40, 15, 20);   // the rattle
        if (pt >= ROLLT) Settle();
        break;
      case RES:
        if ((eA && pt > 10) || (auto && pt > 75) || pt > 600) ToBet();
        break;
      case BROKE:
        break;
    }
  }

  // ---- drawing ----
  void Px(int x, int y, bool v) { if (x >= 0 && x < 64 && y >= 0 && y < 64) sc.Set(x, y, v); }
  void Fill(int x, int y, int w, int h, bool v) {
    if (x < 0) { w += x; x = 0; } if (y < 0) { h += y; y = 0; } if (x + w > 64) w = 64 - x; if (y + h > 64) h = 64 - y;
    if (w > 0 && h > 0) sc.Fill(x, y, w, h, v);
  }
  void Rect(int x, int y, int w, int h, bool dots) {
    if (!dots) { Fill(x, y, w, 1, true); Fill(x, y + h - 1, w, 1, true); Fill(x, y, 1, h, true); Fill(x + w - 1, y, 1, h, true); return; }
    for (int i = 0; i < w; i += 2) { Px(x + i, y, true); Px(x + i, y + h - 1, true); }
    for (int j = 0; j < h; j += 2) { Px(x, y + j, true); Px(x + w - 1, y + j, true); }
  }
  void Text(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int c = s[n], m = GLYPH[c < 128 ? CHAR[c] : FONTCH.Length - 1];
      if (x >= 0 && x <= 61 && y >= 0 && y <= 59) { for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) sc.Set(x + KX[k], y + KY[k], v); }
      else for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) Px(x + KX[k], y + KY[k], v);
      x += 4;
    }
  }
  void Big(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int c = s[n], m = GLYPH[c < 128 ? CHAR[c] : FONTCH.Length - 1];
      for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) Fill(x + k % 3 * 2, y + k / 3 * 2, 2, 2, v);
      x += 8;
    }
  }
  void Centre(int y, string s) { Text(32 - ((s.Length * 4 - 1) >> 1), y, s, true); }
  void Right(int x, int y, string s, bool v) { Text(x - s.Length * 4, y, s, v); }
  void Boxed(int y, string s) {
    int w = s.Length * 4 + 3, x = 32 - (w >> 1);
    Fill(x, y, w, 9, false); Rect(x, y, w, 9, false); Text(x + 2, y + 2, s, true);
  }
  void TopBar() {
    Text(1, 1, "SIC BO", true);
    int x = 64;
    for (int k = 0; k < histN; k++) {
      string s = hist[k].ToString(); int w = s.Length * 4 + 1;
      x -= w;
      if (x < 27) break;
      if (k == 0) { Fill(x, 0, w, 7, true); Text(x + 1, 1, s, false); } else Text(x + 1, 1, s, true);
      if (histT[k] == 1) Fill(x, 7, w, 1, true);
      x -= 2;
    }
  }
  void Cup(int cx, int top) {
    for (int r = 0; r < 19; r++) {
      int hw = CUPW[r], y = top + r;
      if (y < 0 || y > 63) continue;                        // (whole rows off screen draw nothing in JS either)
      Fill(cx - hw - 1, y, 2 * hw + 2, 1, false); Fill(cx - hw, y, 2 * hw, 1, true);
      if (r == 3) Fill(cx - 1, y, 2, 1, false);
      if (r >= 6 && r <= 13) Px(cx - hw + 3, y, false);
      if (r >= 8 && r <= 11) Px(cx - hw + 5, y, false);
      if (r == 15) Fill(cx - hw + 1, y, 2 * hw - 2, 1, false);
    }
  }
  void Felt() {
    Rect(0, 8, 64, 21, true);
    if (phase == ROLL) {
      int p = pt;
      if (p >= SHK) {
        DrawDie(DX[0], REST, d1, 0); DrawDie(DX[1], REST, d2, 0); DrawDie(DX[2], REST, d3, 0);
        int u = Math.Min(LF, p - SHK);
        Cup(CX, CDOWN - 30 * u * u / (LF * LF));
      } else if (p < DROP) {
        int u = DROP - p;
        Cup(CX, CDOWN - 28 * u * u / (DROP * DROP));
      } else {
        int j = JIG[(p >> 1) & 7];
        Cup(CX + j, CDOWN - ((p & 2) >> 1));
        if (j != 0) { int x = j < 0 ? 16 : 19; Fill(x, 15, 2, 1, true); Fill(x + 1, 19, 2, 1, true); Fill(x, 23, 2, 1, true); }
      }
      return;
    }
    if (d1 == 0) { Centre(16, "PLACE BETS"); return; }
    DrawDie(DX[0], REST, d1, 0); DrawDie(DX[1], REST, d2, 0); DrawDie(DX[2], REST, d3, 0);
    int s = d1 + d2 + d3; string ss = s.ToString();
    string w = d1 == d2 && d2 == d3 ? "TRIPLE" : s <= 10 ? "SMALL" : "BIG";
    Big(12 - ((ss.Length * 8 - 2) >> 1), 10, ss, true);
    Text(12 - ((w.Length * 4 - 1) >> 1), 22, w, true);
  }
  void Stash(int y) {
    for (int r = 0; r < 5; r++) for (int q = 0; q < 5; q++) if (((CHIP[r] >> (4 - q)) & 1) != 0) Px(1 + q, y + r, true);
    Text(8, y, Chips.Balance().ToString(), true);
  }
  void DrawBets(bool cursor) {
    int top = Math.Max(0, Math.Min(sel - 1, NB - VIS));
    for (int i = 0; i < VIS; i++) {
      int r = top + i, y = 30 + i * 7; bool on = cursor && r == sel, v = !on;
      if (on) Fill(0, y, 63, 7, true);
      Text(2, y + 1, IsPick(r) ? NAMES[r] : Label(r), v);
      if (r == TPICK) {
        if (on) Right(63, y + 1, "<" + pk[0].ToString() + ">", v); else Right(59, y + 1, pk[0].ToString(), v);
      } else if (IsPick(r)) {
        int f = pk[PickRow(r)], x0 = r == FPICK ? 36 : 52;
        if (on) { Fill(x0 - 1, y, 60 - x0, 7, false); Text(x0 - 5, y + 1, "<", false); Text(60, y + 1, ">", false); }
        for (int x = x0; x < 59; x += 8) DrawDie(x, y, f, 1);
      } else {
        Right(47, y + 1, PayText(r), v);
        if (on) Right(63, y + 1, "<" + st[r].ToString() + ">", v);
        else if (st[r] > 0) Right(59, y + 1, st[r].ToString(), v);
      }
    }
    int th = 21 * VIS / NB, ty = 30 + 21 * top / NB;
    Fill(63, ty, 1, th, true);
    Fill(0, 51, 64, 1, true);
    Stash(53);
    Right(64, 53, "BET " + Total().ToString(), true);
  }
  void DrawRes() {
    int pages = (prN + VIS - 1) / VIS, pg = pages > 1 ? pt / 60 % pages : 0;
    for (int i = 0; i < VIS; i++) {
      int k = pg * VIS + i;
      if (k >= prN) break;
      int r = pr[k], y = 30 + i * 7; bool win = oc[r] == 1, v = !win;
      if (win) Fill(0, y, 64, 7, true);
      Text(2, y + 1, Label(r), v);
      Right(41, y + 1, rst[r].ToString(), v);
      Right(64, y + 1, (win ? "+" : "-") + amt[r].ToString(), v);
    }
    Fill(0, 51, 64, 1, true);
    Stash(53);
    Right(64, 53, net > 0 ? "WON " + net.ToString() : net < 0 ? "LOST " + (0 - net).ToString() : "EVEN", true);
    Centre(59, pt > 40 && ((t >> 4) & 1) == 1 ? "A: NEXT" : call);
  }
  void TitleDice() {
    int c = pt / 75, tt = pt % 75;
    for (int k = 0; k < 3; k++) {
      int u = Math.Max(0, Math.Min(T2, tt - LAG * (2 - k)));
      DrawDieAt(k, u, FLICK[(c + 2 * k) % 6], -24, TREST);
    }
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      TitleDice();
      Big(9, 15, "SIC BO", true);
      Fill(0, 44, 64, 1, true);
      if (phase == BROKE) { Boxed(2, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Boxed(53, "MORE TOMORROW"); return; }
      if (msgT > 0) Boxed(2, msg);
      if (((t >> 4) & 1) != 0) Boxed(53, "A: START");
      return;
    }
    TopBar();
    Felt();
    if (phase == BET) {
      DrawBets(true);
      if (msgT > 0) Centre(59, msg);
      else if (((t >> 4) & 1) != 0) Centre(59, "A:SHAKE B:CLEAR");
      return;
    }
    if (phase == ROLL) { DrawBets(false); return; }
    DrawRes();
  }
}
