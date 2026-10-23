// KENO for the VMX: a line-for-line port of microgames/ui/games/keno_core.js to C# 1.0, so it plays exactly the same
// (same random numbers, same integer maths, same order of events); tools/parity checks that frame by frame.
// Numbers 1-80 on a 10 x 8 ticket; mark 1-10 spots, bet 1-10 chips; 20 numbers are drawn without replacement (a partial
// Fisher-Yates shuffle, every pick rejection-sampled) before the balls come out. Pays PAY[spots][hits] x bet, stake
// included. Returns (exact, hypergeometric): 1 spot 75.0% (3x is the only whole multiple under 100%), 2: 92.1%,
// 3: 94.4%, 4: 94.8%, 5: 95.0%, 6: 94.2%, 7: 93.3%, 8: 92.2%, 9: 93.1%, 10: 94.0% - far kinder than a real keno lounge
// (~75%), because VMX CASINO is a casino of slow, careful builders. The full table is in the JS core's header.
// Ticket: D-pad cursor, A mark/unmark, B menu (PLAY, BET, QUICK n, CLEAR, PAYS). Draw: A = faster. After: A again
// (same ticket and bet), B back to the ticket. VMX CASINO: stakes come from the shared bank (Microsoft.Vmx.Chips).
// Scores: biggest single payout, most hits caught.
using System;
using Microsoft.Vmx;

public class Keno : Game {
  int bestWin, bestHits;                                    // this session's best (Scores)
  const string FONTCH = "ABCDEFGHIKLMNOPQRSTUVWXYZ0123456789:!-<>+ ./%";
  static readonly string[] FONT = {
    "010101111101101", "110101110101110", "011100100100011", "110101101101110", "111100110100111", "111100110100100",
    "011100101101011", "101101111101101", "111010010010111", "101101110101101", "100100100100111", "101111111101101",
    "110101101101101", "010101101101010", "110101110100100", "010101101110011", "110101110101101", "011100010001110",
    "111010010010010", "101101101101111", "101101101101010", "101101111111101", "101101010101101", "101101010010010",
    "111001010100111", "111101101101111", "010110010010111", "110001010100111", "110001010001110", "101101111001001",
    "111100110001110", "011100111101111", "111001010010010", "111101111101111", "111101111001110", "000010000010000",
    "010010010000010", "000000111000000", "001010100010001", "100010001010100", "000010111010000", "000000000000000",
    "000000000000010", "001001010100100", "101001010100101" };
  static readonly int[] KX = { 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2 }, KY = { 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4 };
  static readonly int[] CHIP = { 14, 31, 27, 31, 14 };
  static readonly int[][] PAY = { new int[] { 0 }, new int[] { 0, 3 }, new int[] { 0, 1, 9 }, new int[] { 0, 0, 2, 48 },
    new int[] { 0, 0, 2, 5, 100 }, new int[] { 0, 0, 1, 3, 14, 400 }, new int[] { 0, 0, 0, 3, 5, 70, 1500 },
    new int[] { 0, 0, 0, 2, 3, 20, 180, 5000 }, new int[] { 0, 0, 0, 1, 2, 10, 80, 800, 10000 },
    new int[] { 0, 0, 0, 1, 1, 5, 30, 250, 2500, 10000 }, new int[] { 2, 0, 0, 0, 1, 2, 25, 110, 800, 4000, 10000 } };
  static readonly int[] RET = { 0, 750, 921, 944, 948, 950, 942, 933, 922, 931, 940 };
  static readonly string[] MENU = { "PLAY", "BET", "QUICK", "CLEAR", "PAYS" };
  const int NM = 5, MAXSPOT = 10, MAXBET = 10, BALLS = 20;
  const int BT = 15, LT = 7, FBT = 4, FLT = 2, HOLD = 20;
  const int GX = 2, GY = 8;
  const int TITLE = 0, MARK = 1, MENUP = 2, PAYTAB = 3, DRAW = 4, PAYP = 5, BROKE = 6;

  static int[] GLYPH, CHAR;
  static void BuildFont() {
    GLYPH = new int[FONT.Length]; CHAR = new int[128];
    for (int f = 0; f < FONT.Length; f++) { int m = 0; for (int k = 0; k < 15; k++) if (FONT[f][k] == '1') m |= 1 << k; GLYPH[f] = m; }
    for (int c = 0; c < 128; c++) CHAR[c] = FONTCH.Length - 1;
    for (int f = 0; f < FONTCH.Length; f++) CHAR[FONTCH[f]] = f;
  }

  // game state (the JS object g)
  uint rs; int t, phase, pt, holdT, msgT; string msg = ""; bool auto_, demo;
  int[] mk = new int[80]; int nmk, cur, bet, qn, msel, ptn; int[] dr = new int[BALLS]; int[] pool = new int[80];
  int lit, bn, bt; bool fast; int hits, won;
  int dN, dk, dw, dQ, dBet; bool dPays;
  bool pUp, pDown, pLeft, pRight, pA, pB;
  int[] litA = new int[80];
  Screen sc;

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }
  int Below(int m) {
    int lim = 65536 - 65536 % m;
    int r = Rnd();
    while (r >= lim) r = Rnd();
    return r % m;
  }

  public override void Start(int seed, bool demo) {
    if (GLYPH == null) BuildFont();
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; holdT = 0; msg = ""; msgT = 0; auto_ = false;
    for (int i = 0; i < 80; i++) { mk[i] = 0; pool[i] = 0; }
    for (int i = 0; i < BALLS; i++) dr[i] = 0;
    nmk = 0; cur = 0; bet = 1; qn = 5; msel = 0; ptn = 5; lit = 0; bn = 0; bt = 0; fast = false; hits = 0; won = 0;
    dN = 0; dk = 0; dw = 0; dQ = 0; dBet = 0; dPays = false;
    pUp = pDown = pLeft = pRight = pA = pB = false;
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  public override bool UsesB { get { return true; } }

  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 50; }
  void Shuffle(int n) {
    for (int i = 0; i < 80; i++) pool[i] = i;
    for (int i = 0; i < n; i++) {
      int j = i + Below(80 - i), s = pool[i];
      pool[i] = pool[j]; pool[j] = s;
    }
  }
  void ToMark() {
    int c = Chips.Balance();
    if (bet > c) bet = c;
    if (bet < 1) bet = 1;
    SetPhase(c < 1 ? BROKE : MARK);
    dN = 0;
  }
  void Play() {
    if (nmk == 0) { Say("MARK SPOTS"); SetPhase(MARK); return; }
    int c = Chips.Balance();
    if (c < 1) { SetPhase(BROKE); return; }
    if (bet > c) bet = c;
    Chips.Bet(bet);
    Shuffle(BALLS);
    for (int i = 0; i < BALLS; i++) dr[i] = pool[i];
    lit = 0; bn = 0; bt = 0; hits = 0; won = 0; fast = false;
    SetPhase(DRAW);
    Beeper.Tone(880, 30, 40);
  }
  void Quick() {
    Shuffle(qn);
    for (int i = 0; i < 80; i++) mk[i] = 0;
    for (int i = 0; i < qn; i++) mk[pool[i]] = 1;
    nmk = qn;
  }
  void Settle() {
    won = PAY[nmk][hits] * bet;
    if (won > 0) { Chips.Pay(won); Beeper.Tone(1047, 120, 40); }
    if (!demo && (won > bestWin || hits > bestHits)) {        // board: biggest payout, most hits caught
      if (won > bestWin) bestWin = won;
      if (hits > bestHits) bestHits = hits;
      Scores.Report(bestWin, bestHits, 0, 0);
    }
    SetPhase(PAYP);
  }
  int MaxBet() { int c = Chips.Balance(); return c < MAXBET ? c : MAXBET; }
  void Act(bool aUp, bool aDown, bool aLeft, bool aRight, bool aA, bool aB) {
    if (phase == MARK) {
      if (aUp) cur = (cur + 70) % 80;
      if (aDown) cur = (cur + 10) % 80;
      if (aLeft) cur = (cur + 79) % 80;
      if (aRight) cur = (cur + 1) % 80;
      if (aA) {
        if (mk[cur] == 1) { mk[cur] = 0; nmk--; }
        else if (nmk < MAXSPOT) { mk[cur] = 1; nmk++; }
        else Say("10 SPOTS MAX");
      }
      if (aB) { SetPhase(MENUP); msel = 0; }
    } else if (phase == MENUP) {
      if (aUp) msel = (msel + NM - 1) % NM;
      if (aDown) msel = (msel + 1) % NM;
      if (msel == 1) {
        if (aLeft && bet > 1) bet--;
        if (aRight && bet < MaxBet()) bet++;
      } else if (msel == 2) {
        if (aLeft && qn > 1) qn--;
        if (aRight && qn < MAXSPOT) qn++;
      }
      if (aA) {
        if (msel == 0) Play();
        else if (msel == 2) { Quick(); SetPhase(MARK); }
        else if (msel == 3) { for (int i = 0; i < 80; i++) mk[i] = 0; nmk = 0; SetPhase(MARK); }
        else if (msel == 4) { ptn = nmk > 0 ? nmk : qn; SetPhase(PAYTAB); }
      } else if (aB) SetPhase(MARK);
    } else if (phase == PAYTAB) {
      if (aLeft && ptn > 1) ptn--;
      if (aRight && ptn < MAXSPOT) ptn++;
      if (aA || aB) SetPhase(MENUP);
    }
  }
  void Plan() {
    dQ = 4 + Below(5);
    dBet = 1 + Below(5);
    int c = Chips.Balance();
    if (dBet > c) dBet = c;
    dPays = Below(3) == 0;
    dN = 1; dk = 0; dw = 0;
  }
  void DemoMove() {
    bool u = false, d = false, l = false, r = false, a = false, b = false;
    int ph = phase;
    if (dk == 0 || dk == 1) {
      int row = dk + 1, have = dk == 0 ? bet : qn, want = dk == 0 ? dBet : dQ;
      if (ph == MARK) b = true;
      else if (msel < row) d = true;
      else if (msel > row) u = true;
      else if (have < want) r = true;
      else if (have > want) l = true;
      else if (dk == 0) dk = 1;
      else { a = true; dk = dPays ? 2 : 4; dw = 0; }
    } else if (dk == 2) {
      if (ph == MARK) { dw++; if (dw > 4) b = true; }
      else if (ph == MENUP) { if (msel < 4) d = true; else a = true; }
      else { dw++; if (dw > 16) { b = true; dk = 3; } }
    } else if (dk == 4) {
      dw++; if (dw > 4) { b = true; dk = 3; }
    } else {
      if (ph == MARK) b = true;
      else if (msel > 0) u = true;
      else a = true;
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
        if (eA || (auto && pt > 40)) ToMark();
        break;
      case MARK: case MENUP: case PAYTAB:
        if (auto) {
          if (dN == 0) Plan();
          if (t % 5 == 0) DemoMove();
        } else Act(eUp || (up && rep), eDown || (down && rep), eLeft || (left && rep), eRight || (right && rep), eA, eB);
        break;
      case DRAW: {
        if (eA) fast = true;
        int bt_ = fast ? FBT : BT, lt = fast ? FLT : LT;
        bt++;
        if (lit == bn && bt >= lt) {
          bool hit = mk[dr[bn]] == 1;
          if (hit) hits++;
          lit++;
          Beeper.Tone(hit ? 1320 : 660, 25, 30);
        }
        if (lit >= BALLS) { if (bt >= bt_ + HOLD) Settle(); }
        else if (lit > bn && bt >= bt_) { bn++; bt = 0; }
        break;
      }
      case PAYP:
        if (auto) { if (pt > 75) { if (Below(3) == 0) ToMark(); else Play(); } }
        else if (eA && pt > 10) { ToMark(); if (phase == MARK) Play(); }
        else if ((eB && pt > 10) || pt > 900) ToMark();
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
  void Rect(int x, int y, int w, int h) { Fill(x, y, w, 1, true); Fill(x, y + h - 1, w, 1, true); Fill(x, y, 1, h, true); Fill(x + w - 1, y, 1, h, true); }
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
      for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) Fill(x + KX[k] * 2, y + KY[k] * 2, 2, 2, v);
      x += 8;
    }
  }
  void Centre(int y, string s) { Text(32 - ((s.Length * 4 - 1) >> 1), y, s, true); }
  void Right(int x, int y, string s, bool v) { Text(x - s.Length * 4, y, s, v); }
  void Boxed(int y, string s) {
    int w = s.Length * 4 + 3, x = 32 - (w >> 1);
    Fill(x, y, w, 9, false); Rect(x, y, w, 9); Text(x + 2, y + 2, s, true);
  }
  void Stash(int x, int y) {
    for (int r = 0; r < 5; r++) for (int q = 0; q < 5; q++) if (((CHIP[r] >> (4 - q)) & 1) != 0) Px(x + q, y + r, true);
    Text(x + 7, y, Chips.Balance().ToString(), true);
  }
  void Ball(int n) {
    Rect(0, 50, 20, 14);
    if (n < 0) return;
    string s = n.ToString();
    Big(10 - ((s.Length * 8 - 2) >> 1), 52, s, true);
  }
  void Cell(int i, int k) {
    int x = GX + (i % 10) * 6, y = GY + i / 10 * 5;
    if (k == 3) { sc.Fill(x - 1, y - 1, 7, 6, true); sc.Set(x + 2, y + 1, false); sc.Set(x + 2, y + 2, false); }
    else if (k == 1) sc.Fill(x, y, 5, 4, true);
    else if (k == 2) { sc.Fill(x, y, 5, 1, true); sc.Fill(x, y + 3, 5, 1, true); sc.Fill(x, y + 1, 1, 2, true); sc.Fill(x + 4, y + 1, 1, 2, true); }
    else sc.Set(x + 2, y + 2, true);
  }
  void Grid(int shown) {
    Rect(0, 6, 64, 43);
    for (int i = 0; i < 80; i++) litA[i] = 0;
    for (int i = 0; i < shown; i++) litA[dr[i]] = 2;
    int fresh = phase == DRAW && lit > bn && bt < LT + 8 && ((t >> 1) & 1) == 1 ? dr[bn] : -1;
    for (int i = 0; i < 80; i++) if (i != fresh) Cell(i, mk[i] + litA[i]);
    if (fresh >= 0) Cell(fresh, mk[fresh]);
  }
  void TopBar() {
    Text(0, 0, nmk.ToString() + (nmk == 1 ? " SPOT" : " SPOTS"), true);
    Right(64, 0, "BET " + bet.ToString(), true);
  }
  void DrawTicket() {
    TopBar();
    Grid(0);
    if (phase == MARK) {
      int x = GX + (cur % 10) * 6, y = GY + cur / 10 * 5;
      Rect(x - 1, y - 1, 7, 6);
      if (((t >> 3) & 1) != 0) sc.Fill(x, y, 5, 4, mk[cur] == 0);
    }
    Ball(cur + 1);
    Stash(23, 51);
    Text(23, 58, ((t >> 5) & 1) != 0 ? "B:MENU" : "A:MARK", true);
    if (phase == MENUP) {
      sc.Fill(9, 9, 46, 38, false); Rect(9, 9, 46, 38);
      for (int i = 0; i < NM; i++) {
        int y = 11 + i * 7; bool on = i == msel, v = !on;
        if (on) sc.Fill(10, y, 44, 7, true);
        Text(12, y + 1, MENU[i], v);
        if (i == 1 || i == 2) {
          string n = (i == 1 ? bet : qn).ToString();
          Right(53, y + 1, on ? "<" + n + ">" : n, v);
        }
      }
    }
    if (msgT > 0) Boxed(23, msg);
  }
  void DrawPaytab() {
    int n = ptn;
    Centre(0, n.ToString() + (n == 1 ? " SPOT" : " SPOTS") + " BET " + bet.ToString());
    if (n > 1) Text(0, 0, "<", true);
    if (n < MAXSPOT) Text(61, 0, ">", true);
    sc.Fill(0, 6, 64, 1, true);
    int y = 8;
    int[] p = PAY[n];
    for (int k = n; k >= 0; k--) if (p[k] > 0) {
      Text(2, y, "HIT " + k.ToString(), true);
      Right(63, y, (p[k] * bet).ToString(), true);
      y += 6;
    }
    sc.Fill(0, 56, 64, 1, true);
    string pc = (RET[n] / 10).ToString() + "."; pc = pc + (RET[n] % 10).ToString() + "%";
    Centre(58, "RETURN " + pc);
  }
  void DrawDraw() {
    TopBar();
    Grid(lit);
    int n = -1;
    if (bt >= 1 || lit > bn) n = dr[bn] + 1;
    Ball(n);
    Text(23, 51, "HITS " + hits.ToString(), true);
    Text(23, 58, "BALL " + (bn + 1).ToString(), true);
  }
  void DrawPay() {
    Stash(0, 0);
    Right(64, 0, won > 0 ? "WIN " + won.ToString() : "NO WIN", true);
    Grid(BALLS);
    Centre(50, "CAUGHT " + hits.ToString() + " OF " + nmk.ToString());
    if (((t >> 4) & 1) != 0) Centre(57, "A:AGAIN B:NEW");
    if (won > 0 && pt < 45) Boxed(23, "WIN " + won.ToString());
  }
  void TitleGrid() {
    Rect(0, 6, 64, 43);
    for (int i = 0; i < 80; i++) {
      int m = (i * 13) % 17 == 3 ? 1 : 0, d = (i * 7 + (t >> 2)) % 9 == 0 ? 2 : 0;
      Cell(i, m + d);
    }
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      TitleGrid();
      sc.Fill(13, 19, 38, 16, false); Rect(13, 19, 38, 16); Rect(15, 21, 34, 12);
      Big(17, 22, "KENO", true);
      if (phase == BROKE) { Boxed(0, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Centre(53, "MORE TOMORROW"); return; }
      if (msgT > 0) Boxed(0, msg);
      else Centre(0, "20 BALLS OF 80");
      if (((t >> 4) & 1) != 0) Centre(53, "A: START");
      return;
    }
    if (phase == PAYTAB) { DrawPaytab(); return; }
    if (phase == DRAW) { DrawDraw(); return; }
    if (phase == PAYP) { DrawPay(); return; }
    DrawTicket();
  }
}
