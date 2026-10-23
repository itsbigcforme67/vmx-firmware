// CRAPS for the VMX: a line-for-line port of microgames/ui/games/craps_core.js to C# 1.0, so it plays exactly the
// same (same random numbers, same integer maths, same order of events); tools/parity checks that frame by frame.
// The core of a real craps table: PASS / DONT (don't pass) line bets 1-20 (come-out 7/11 / 2-3-12, then the point
// before a 7), ODDS behind the line once a point is set (true odds, up to 3x the line, in steps that pay whole chips),
// one-roll FIELD (2 pays 2:1, 12 pays 3:1), ANY 7 (4:1) and CRAPS (any craps, 7:1). House edges: pass 1.41%, don't
// pass 1.36%, odds 0%, field 2.78%, any 7 16.7%, any craps 11.1%. Each die is exactly 1 in 6 (rejection sampling),
// thrown before the dice tumble. Up/Down choose a bet, Left/Right chips off / on (held = repeat), A rolls, B clears
// the bets that may come down. VMX CASINO: stakes come from the shared bank (Microsoft.Vmx.Chips) when the dice are
// thrown; a win pays stake + winnings. Board: the biggest single payout, field 1 = the longest hand (rolls before a
// seven-out).
using System;
using Microsoft.Vmx;

public class Craps : Game {
  int bestPay, bestRun;                                     // this session's best (Scores)
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
  static readonly int[] PUCKON = { 28, 62, 127, 127, 127, 62, 28 };
  static readonly int[] PUCKOFF = { 28, 34, 65, 65, 65, 34, 28 };
  static readonly string[] NAMES = { "PASS", "DONT", "ODDS", "FIELD", "ANY 7", "CRAPS" };
  static readonly string[] NUMS = { "", "", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE", "TEN", "ELEVEN", "TWELVE" };
  static readonly int[] ON = { 0, 0, 0, 0, 2, 3, 6, 0, 6, 3, 2 }, OD = { 0, 0, 0, 0, 1, 2, 5, 0, 5, 2, 1 };
  const int PASS = 0, DONT = 1, ODDS = 2, FIELD = 3, ANY7 = 4, CRAPS = 5, NB = 6, VIS = 3, CAP = 20;
  const int T2 = 34, LAG = 3, SETTLE = T2 + LAG + 4;
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
  int[] st = new int[NB], cm = new int[NB], rst = new int[NB], oc = new int[NB], amt = new int[NB], pr = new int[NB];
  int prN; bool dec; int sel, point, d1, d2; int[] hist = new int[4]; int histN; int net, hand;
  int[] dT = new int[NB]; int dk; bool dPlan, dClr;
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
    for (int i = 0; i < NB; i++) { st[i] = 0; cm[i] = 0; rst[i] = 0; oc[i] = 0; amt[i] = 0; pr[i] = 0; dT[i] = 0; }
    prN = 0; dec = false; sel = 0; point = 0; d1 = 0; d2 = 0; histN = 0; net = 0; call = ""; hand = 0;
    dk = 0; dPlan = false; dClr = false;
    pUp = pDown = pLeft = pRight = pA = pB = false;
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  public override bool UsesB { get { return true; } }

  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 50; }
  int Total() { int s = 0; for (int i = 0; i < NB; i++) s += st[i]; return s; }
  int Fresh() { int s = 0; for (int i = 0; i < NB; i++) s += st[i] - cm[i]; return s; }
  int Riding() { int s = 0; for (int i = 0; i < NB; i++) s += cm[i]; return s; }
  static bool IsLine(int i) { return i == PASS || i == DONT; }
  int Side() { return st[PASS] > 0 ? PASS : st[DONT] > 0 ? DONT : -1; }
  int StepOf(int i) {
    if (i != ODDS || point == 0) return 1;
    return Side() == DONT ? ON[point] : OD[point];
  }
  int OddsCap() {
    int s = Side();
    if (point == 0 || s < 0) return 0;
    int stp = StepOf(ODDS);
    return 3 * st[s] / stp * stp;
  }
  string BlockAdd(int i) {
    int step = StepOf(i);
    if (IsLine(i) && point != 0) return "LINE BETS LOCKED";
    if ((i == PASS && st[DONT] > 0) || (i == DONT && st[PASS] > 0)) return "ONE LINE BET";
    if (i == ODDS) {
      if (point == 0) return "ODDS AFTER POINT";
      if (Side() < 0) return "NEED A LINE BET";
      int cap = OddsCap();
      if (st[i] + step > cap) return cap == 0 ? "LINE TOO SMALL" : "ODDS MAX 3X";
    } else if (st[i] >= CAP) return "TABLE MAX 20";
    if (Fresh() + step > Chips.Balance()) return "NOT ENOUGH CHIPS";
    return "";
  }
  bool CanSub(int i) { return !(IsLine(i) && point != 0) && st[i] >= StepOf(i); }
  void Refund(int i) {
    if (st[i] < cm[i]) { Chips.Pay(cm[i] - st[i]); cm[i] = st[i]; }
  }
  void AddChip(int i) { string w = BlockAdd(i); if (w.Length != 0) Say(w); else st[i] += StepOf(i); }
  void TakeChip(int i) {
    if (IsLine(i) && point != 0) { if (st[i] > 0) Say("LINE BETS LOCKED"); return; }
    if (st[i] >= StepOf(i)) { st[i] -= StepOf(i); Refund(i); }
  }
  void ClearBets() {
    for (int i = 0; i < NB; i++) if (!(IsLine(i) && point != 0)) { st[i] = 0; Refund(i); }
  }
  void ToBet() {
    int run = 0, c = Chips.Balance();
    for (int i = 0; i < NB; i++) {
      int n = Math.Min(st[i] - cm[i], c - run);
      st[i] = cm[i] + n; run += n;
    }
    SetPhase(c < 1 && Riding() == 0 ? BROKE : BET);
    dPlan = false; dClr = false; dk = 0;
  }
  void DoRoll() {
    if (Total() == 0) { Say("PLACE A BET"); return; }
    int f = Fresh(); if (f > 0) Chips.Bet(f);
    for (int i = 0; i < NB; i++) { cm[i] = st[i]; rst[i] = st[i]; }
    d1 = Die(); d2 = Die();
    hand++;
    SetPhase(ROLL);
    Beeper.Tone(660, 30, 40);
  }
  void Settle() {
    int s = d1 + d2, p = point, sd = Side();
    int paid = 0, nt = 0;
    dec = false;
    prN = 0;
    for (int i = 0; i < NB; i++) {
      int stk = st[i];
      if (stk == 0) continue;
      pr[prN++] = i;
      int r = 4;
      int ret = 0;
      if (i == PASS) {
        if (p == 0) { if (s == 7 || s == 11) r = 1; else if (s == 2 || s == 3 || s == 12) r = 2; }
        else if (s == p) r = 1; else if (s == 7) r = 2;
        ret = 2 * stk;
      } else if (i == DONT) {
        if (p == 0) { if (s == 2 || s == 3) r = 1; else if (s == 12) r = 3; else if (s == 7 || s == 11) r = 2; }
        else if (s == 7) r = 1; else if (s == p) r = 2;
        ret = 2 * stk;
      } else if (i == ODDS) {
        if (s == p) r = sd == PASS ? 1 : 2; else if (s == 7) r = sd == PASS ? 2 : 1;
        ret = stk + (sd == PASS ? stk * ON[p] / OD[p] : stk * OD[p] / ON[p]);
      } else if (i == FIELD) {
        r = s == 2 || s == 3 || s == 4 || s == 9 || s == 10 || s == 11 || s == 12 ? 1 : 2;
        ret = s == 2 ? 3 * stk : s == 12 ? 4 * stk : 2 * stk;
      } else if (i == ANY7) { r = s == 7 ? 1 : 2; ret = 5 * stk; }
      else { r = s == 2 || s == 3 || s == 12 ? 1 : 2; ret = 8 * stk; }
      oc[i] = r;
      if (r == 1) { paid += ret; amt[i] = ret - stk; nt += ret - stk; }
      else if (r == 2) { amt[i] = stk; nt -= stk; }
      else if (r == 3) { paid += stk; amt[i] = 0; }
      if (r != 4) { dec = true; cm[i] = 0; if (i == ODDS) st[i] = 0; }
    }
    if (paid > 0) Chips.Pay(paid);
    net = nt;
    bool better = false;
    if (paid > bestPay) { bestPay = paid; better = true; }
    if (p == 0) {
      if (s == 7 || s == 11) call = "WINNER " + s.ToString();
      else if (s == 2 || s == 3 || s == 12) call = "CRAPS " + s.ToString();
      else { point = s; call = "POINT IS " + s.ToString(); }
    } else if (s == p) { point = 0; call = "WINNER " + s.ToString(); }
    else if (s == 7) {
      point = 0; call = "SEVEN OUT";
      if (hand - 1 > bestRun) { bestRun = hand - 1; better = true; }
      hand = 0;
    } else call = oc[FIELD] == 1 && st[FIELD] > 0 ? "FIELD WINS" : "ROLL AGAIN";
    for (int k = 3; k > 0; k--) hist[k] = hist[k - 1];
    hist[0] = s; if (histN < 4) histN++;
    if (better && !demo) Scores.Report(bestPay, bestRun, 0, 0);   // board: biggest payout, longest hand
    if (nt > 0) Beeper.Tone(1047, 120, 40); else Beeper.Tone(440, 50, 40);
    SetPhase(RES);
  }
  void Act(bool aUp, bool aDown, bool aLeft, bool aRight, bool aA, bool aB) {
    if (aUp) sel = (sel + NB - 1) % NB;
    if (aDown) sel = (sel + 1) % NB;
    if (aLeft) TakeChip(sel);
    if (aRight) AddChip(sel);
    if (aB) ClearBets();
    if (aA) DoRoll();
  }
  void Plan() {
    for (int i = 0; i < NB; i++) dT[i] = st[i];
    if (point == 0) {
      int ln = Rnd() % 4 == 0 ? DONT : PASS;
      dT[PASS] = 0; dT[DONT] = 0; dT[ODDS] = 0;
      dT[ln] = 2 + Rnd() % 5;
    } else if (Side() >= 0 && st[ODDS] == 0) {
      int k = OddsCap() / StepOf(ODDS);
      dT[ODDS] = k > 0 ? StepOf(ODDS) * (1 + Rnd() % k) : 0;
    }
    dT[FIELD] = Rnd() % 3 == 0 ? 1 + Rnd() % 3 : 0;
    dT[ANY7] = Rnd() % 10 == 0 ? 1 : 0;
    dT[CRAPS] = Rnd() % 8 == 0 ? 1 : 0;
    dPlan = true;
  }
  void DemoMove() {
    bool u = false, d = false, l = false, r = false, a = false, b = false;
    if (!dClr) { dClr = true; if (point == 0) b = true; }
    else {
      while (dk < NB) {
        int i = dk;
        if ((st[i] < dT[i] && BlockAdd(i).Length == 0) || (st[i] > dT[i] && CanSub(i))) break;
        dk++;
      }
      if (dk < NB) {
        if (sel < dk) d = true;
        else if (sel > dk) u = true;
        else if (st[dk] < dT[dk]) r = true;
        else l = true;
      } else a = true;
    }
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
          if (!dPlan) Plan();
          if (pt > 12 && pt % 4 == 0) DemoMove();
        } else Act(eUp || (up && rep), eDown || (down && rep), eLeft || (left && rep), eRight || (right && rep), eA, eB);
        break;
      case ROLL:
        if (pt == T2 + LAG) Beeper.Tone(220, 20, 30);
        if (pt >= SETTLE) Settle();
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
    int[] pk = point != 0 ? PUCKON : PUCKOFF;
    for (int r = 0; r < 7; r++) for (int q = 0; q < 7; q++) if (((pk[r] >> (6 - q)) & 1) != 0) sc.Set(q, r, true);
    Text(9, 1, point != 0 ? "POINT " + point.ToString() : "COME OUT", true);
    int x = 64;
    for (int k = 0; k < histN; k++) {
      string s = hist[k].ToString(); int w = s.Length * 4 + 1;
      x -= w;
      if (x < 44) break;
      if (k == 0) { Fill(x, 0, w, 7, true); Text(x + 1, 1, s, false); } else Text(x + 1, 1, s, true);
      x -= 2;
    }
  }
  void Felt(bool still) {
    if (still) {
      if (d1 == 0) { Centre(16, "PLACE BETS"); }
      else {
        DrawDie(37, REST, d1, 0); DrawDie(50, REST, d2, 0);
        int s = d1 + d2; string ss = s.ToString();
        Big(18 - ((ss.Length * 8 - 2) >> 1), 10, ss, true);
        Text(18 - ((NUMS[s].Length * 4 - 1) >> 1), 22, NUMS[s], true);
      }
    } else {
      for (int k = 0; k < 2; k++) {
        int u = Math.Max(0, Math.Min(T2, pt - LAG * (1 - k)));
        DrawDieAt(k, u, k == 0 ? d1 : d2, 0, REST);
      }
    }
    Rect(0, 8, 64, 21, true);
  }
  void Stash(int y) {
    for (int r = 0; r < 5; r++) for (int q = 0; q < 5; q++) if (((CHIP[r] >> (4 - q)) & 1) != 0) Px(1 + q, y + r, true);
    Text(8, y, Chips.Balance().ToString(), true);
  }
  string PayText(int r) {
    if (r == ODDS) {
      if (point == 0) return "--";
      return Side() == DONT ? OD[point].ToString() + ":" + ON[point].ToString() : ON[point].ToString() + ":" + OD[point].ToString();
    }
    return r == ANY7 ? "4:1" : r == CRAPS ? "7:1" : "1:1";
  }
  void DrawBets(bool cursor) {
    int top = Math.Max(0, Math.Min(sel - 1, NB - VIS));
    for (int i = 0; i < VIS; i++) {
      int r = top + i, y = 30 + i * 7; bool on = cursor && r == sel, v = !on;
      if (on) Fill(0, y, 63, 7, true);
      Text(2, y + 1, NAMES[r], v);
      Text(34, y + 1, PayText(r), v);
      bool lck = IsLine(r) && point != 0;
      if (on && !lck) Right(63, y + 1, "<" + st[r].ToString() + ">", v);
      else if (st[r] > 0) Right(59, y + 1, st[r].ToString(), v);
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
      int r = pr[k], y = 30 + i * 7, o = oc[r]; bool win = o == 1, v = !win;
      if (win) Fill(0, y, 64, 7, true);
      Text(2, y + 1, NAMES[r], v);
      Right(41, y + 1, rst[r].ToString(), v);
      Right(64, y + 1, o == 1 ? "+" + amt[r].ToString() : o == 2 ? "-" + amt[r].ToString() : o == 3 ? "PUSH" : "ON", v);
    }
    Fill(0, 51, 64, 1, true);
    Stash(53);
    Right(64, 53, net > 0 ? "WON " + net.ToString() : net < 0 ? "LOST " + (0 - net).ToString() : dec ? "EVEN" : "BET " + Total().ToString(), true);
    Centre(59, pt > 40 && ((t >> 4) & 1) == 1 ? "A: NEXT" : call);
  }
  void TitleDice() {
    int c = pt / 75, tt = pt % 75;
    for (int k = 0; k < 2; k++) {
      int u = Math.Max(0, Math.Min(T2, tt - LAG * (1 - k)));
      DrawDieAt(k, u, FLICK[(c + 4 * k) % 6], -18, TREST);
    }
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      TitleDice();
      Big(13, 15, "CRAPS", true);
      Fill(0, 44, 64, 1, true);
      if (phase == BROKE) { Boxed(2, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Boxed(53, "MORE TOMORROW"); return; }
      if (msgT > 0) Boxed(2, msg);
      if (((t >> 4) & 1) != 0) Boxed(53, "A: START");
      return;
    }
    TopBar();
    if (phase == BET) {
      Felt(true);
      DrawBets(true);
      if (msgT > 0) Centre(59, msg);
      else if (((t >> 4) & 1) != 0) Centre(59, "A:ROLL B:CLEAR");
      return;
    }
    if (phase == ROLL) { Felt(false); DrawBets(false); return; }
    Felt(true);
    DrawRes();
  }
}
