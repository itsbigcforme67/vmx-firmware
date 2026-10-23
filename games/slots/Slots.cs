// SLOTS for the VMX: a line-for-line port of microgames/ui/games/slots_core.js to C# 1.0, so it plays exactly the same
// (same random numbers, same order of events); tools/parity checks that frame by frame.
// A 3-reel, 1-line mechanical-style slot machine, VMX CASINO's fast, risky table. Up/Down bet 1-3 chips (never more
// than the stash), A pulls the lever; A while the reels spin stops the next reel early, but the result was drawn when
// the lever was pulled (one Rnd() per reel: a uniformly random stop on its 24-stop strip), so it never changes the
// odds. B shows the pay table (two pages).
// Pays per chip bet, stake included: chip x3 800 (3000 flat at a 3-chip bet), seven x3 150, double bar x3 80,
// BAR x3 40, cherry x3 30, bell x3 20, lemon x3 15, any 3 bars 10, cherry cherry (reels 1-2) 10, cherry (reel 1) 2.
// From all 13824 stop combinations: hit rate 18.45%; return 93.40% at 1-2 chips, 94.85% at 3 chips.
// VMX CASINO: the stake leaves the shared bank (Microsoft.Vmx.Chips) when the lever is pulled; a win comes back stake
// included. No chips = OUT OF CHIPS until the bank's daily top-up.
using System;
using Microsoft.Vmx;

public class Slots : Game {
  int bestWin;                                              // the biggest payout this session (Scores)
  const int N = 24, P = 13, L = N * P;
  static readonly int[][] STRIPS = {
    new int[] { 7, 1, 2, 0, 3, 1, 4, 2, 0, 6, 3, 1, 0, 2, 4, 3, 0, 5, 1, 2, 0, 3, 4, 0 },
    new int[] { 7, 2, 0, 4, 1, 3, 0, 5, 2, 3, 0, 6, 4, 1, 0, 2, 3, 5, 0, 4, 1, 2, 3, 0 },
    new int[] { 7, 3, 0, 2, 4, 0, 2, 1, 3, 0, 5, 2, 4, 0, 3, 2, 6, 0, 4, 5, 1, 3, 2, 0 } };
  static readonly int[] TRIPLE = { 0, 30, 15, 20, 40, 80, 150, 800 };
  static readonly string[][] ART = {
    new string[] { "...........", "...........", "...........", "...........", "...........", "...........", "...........", "...........", "...........", "...........", "..........." },
    new string[] { ".......##..", ".....##.#..", "....#...#..", "...#....#..", "..#.....#..", ".###...###.", "#.###.#.###", "#####.#####", "#####.#####", ".###...###.", "..........." },
    new string[] { "...........", "....####...", "..#######..", ".##.######.", "#.#########", "###########", "#########.#", ".#########.", "..#######..", "...####....", "..........." },
    new string[] { ".....#.....", "....###....", "...#####...", "..##.####..", "..##.####..", "..#######..", ".#########.", ".#########.", "###########", "....###....", "..........." },
    new string[] { "...........", "...........", "###########", "..###.##..#", ".#.#.#.#.#.", "..##...#..#", ".#.#.#.#.#.", "..##.#.#.#.", "###########", "...........", "..........." },
    new string[] { "...........", ".#########.", "###########", "###########", ".#########.", "...........", ".#########.", "###########", "###########", ".#########.", "..........." },
    new string[] { "###########", "###########", "#.......##.", ".......##..", "......##...", ".....###...", "....###....", "....###....", "...###.....", "...###.....", "...###....." },
    new string[] { "...#####...", ".#########.", ".##.###.##.", "###..#..###", "####...####", "#####.#####", "####...####", "###..#..###", ".##.###.##.", ".#########.", "...#####..." } };
  const string FONTCH = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789:!-<>+/ ";
  static readonly string[] FONT = {
    "010101111101101", "110101110101110", "011100100100011", "110101101101110", "111100110100111", "111100110100100",
    "011100101101011", "101101111101101", "111010010010111", "001001001101010", "101101110101101", "100100100100111",
    "101111111101101", "110101101101101", "010101101101010", "110101110100100", "010101101110011", "110101110101101",
    "011100010001110", "111010010010010", "101101101101111", "101101101101010", "101101111111101", "101101010101101",
    "101101010010010", "111001010100111",
    "111101101101111", "010110010010111", "110001010100111", "110001010001110", "101101111001001", "111100110001110",
    "011100111101111", "111001010010010", "111101111101111", "111101111001110", "000010000010000", "010010010000010",
    "000000111000000", "001010100010001", "100010001010100", "000010111010000", "001001010100100", "000000000000000" };
  static readonly int[] RX = { 6, 20, 34 };
  const int WY0 = 10, WY1 = 42, MID = 21;
  const int TITLE = 0, BET = 1, SPIN = 2, BROKE = 3;

  // game state (the JS object g)
  uint rs; int t, phase, pt, page, bet;
  int[] pos = new int[3], mode = new int[3], stop = new int[3], at = new int[3];
  int win, winT, big, lt, spins, msgT, dBet, dStop, dPay;
  string msg = "";
  bool pUp, pDown, pA, pB;
  bool demo;
  Screen sc;

  public override bool UsesB { get { return true; } }

  static int PayOf(int a, int b, int c, int bet) {
    if (a == b && b == c && a != 0) return a == 7 && bet == 3 ? 3000 : TRIPLE[a] * bet;
    if ((a == 4 || a == 5) && (b == 4 || b == 5) && (c == 4 || c == 5)) return 10 * bet;
    if (a == 1 && b == 1) return 10 * bet;
    if (a == 1) return 2 * bet;
    return 0;
  }

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    Prepare();
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; page = 0; bet = 1;
    for (int i = 0; i < 3; i++) { pos[i] = 0; mode[i] = 0; stop[i] = 0; at[i] = 0; }
    win = 0; winT = 0; big = 0; lt = -1; spins = 0; msg = ""; msgT = 0; dBet = 1; dStop = 0; dPay = -1;
    pUp = pDown = pA = pB = false;
    for (int i = 0; i < 3; i++) { stop[i] = Rnd() % N; pos[i] = stop[i] * P; }
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  void Say(string m) { msg = m; msgT = 90; }
  void SetPhase(int p) { phase = p; pt = 0; }
  void ToBet() {
    int c = Chips.Balance();
    if (c < 1) { SetPhase(BROKE); return; }
    bet = Math.Max(1, Math.Min(Math.Min(bet, c), 3)); SetPhase(BET);
  }
  void Pull() {
    Chips.Bet(bet); spins++;
    for (int i = 0; i < 3; i++) { stop[i] = Rnd() % N; mode[i] = 1; at[i] = 22 + i * 9; }
    win = 0; winT = 0; big = 0; lt = 0; SetPhase(SPIN);
    Beeper.Tone(220, 40, 30);
  }
  void Land(int i) {
    mode[i] = 2; pos[i] = (stop[i] * P - 5 * P + L) % L;
  }

  public override void Tick(int held, int pressed) {
    bool auto = demo;
    t++; pt++; if (msgT > 0) msgT--; if (winT > 0) winT--;
    if (lt >= 0) { lt++; if (lt > 14) lt = -1; }
    bool up = !auto && (held & Buttons.Up) != 0, down = !auto && (held & Buttons.Down) != 0,
      a = !auto && (held & Buttons.A) != 0, b = !auto && (held & Buttons.B) != 0;
    bool eUp = up && !pUp, eDown = down && !pDown, eA = a && !pA, eB = b && !pB;
    pUp = up; pDown = down; pA = a; pB = b;

    if (auto) {
      if (page > 0) { if (pt % 45 == 0) eB = true; }
      else if (phase == BET) {
        if (pt == 12) {
          dBet = 1 + Rnd() % Math.Min(3, Chips.Balance());
          if (spins % 12 == 3 && dPay != spins) { dPay = spins; page = 1; pt = 0; return; }
        }
        if (pt > 12 && pt % 6 == 0) { if (bet < dBet) eUp = true; else if (bet > dBet) eDown = true; else if (pt > 30 && winT == 0) eA = true; }
      } else if (phase == SPIN) {
        if (pt == 1) dStop = Rnd() % 3;
        if (dStop > 0 && pt > 8 && pt % (4 + dStop * 3) == 0) eA = true;
      }
    }

    if (page > 0) {
      if (eB) page = page == 2 ? 0 : 2;
      if (eA) page = 0;
      if (phase == BET) {
        if (eUp) bet = Math.Min(bet + 1, Math.Min(3, Chips.Balance()));
        if (eDown) bet = Math.Max(1, bet - 1);
      }
      if (page == 0) pt = 0;
      return;
    }

    switch (phase) {
      case TITLE:
        if (eA || (auto && pt > 40)) ToBet();
        else if (eB) page = 1;
        break;
      case BET:
        if (eUp) bet = Math.Min(bet + 1, Math.Min(3, Chips.Balance()));
        if (eDown) bet = Math.Max(1, bet - 1);
        if (eA) Pull();
        else if (eB) page = 1;
        break;
      case SPIN: {
        if (eA && pt > 4) {
          for (int i = 0; i < 3; i++) if (mode[i] == 1) { if (i == 0 || mode[i - 1] != 1) Land(i); break; }
        }
        bool all = true;
        for (int i = 0; i < 3; i++) {
          if (mode[i] == 1) {
            pos[i] = (pos[i] + 9) % L;
            if (pt >= at[i] && (i == 0 || mode[i - 1] != 1)) Land(i);
          } else if (mode[i] == 2) {
            int d = (stop[i] * P - pos[i] + L) % L, v = Math.Max(1, Math.Min(9, (d + 2) / 3));
            pos[i] = (pos[i] + Math.Min(d, v)) % L;
            if (pos[i] == stop[i] * P) { mode[i] = 0; Beeper.Tone(160, 25, 40); }
          }
          if (mode[i] != 0) all = false;
        }
        if (all) {
          int s0 = STRIPS[0][stop[0]], s1 = STRIPS[1][stop[1]], s2 = STRIPS[2][stop[2]];
          win = PayOf(s0, s1, s2, bet);
          if (win > 0) {
            Chips.Pay(win); if (!demo && win > bestWin) { bestWin = win; Scores.Report(bestWin, 0, 0, 0); }   // board: biggest payout
            big = s0 == s1 && s1 == s2 && s0 >= 6 ? 2 : win >= 20 * bet ? 1 : 0;
            winT = big == 2 ? 120 : big == 1 ? 75 : 40;
            if (s0 == 7 && s1 == 7 && s2 == 7) Say("JACKPOT!"); else if (big == 2) Say("BIG WIN!");
            Beeper.Tone(big > 0 ? 1047 : 784, big > 0 ? 300 : 120, 50);
          }
          ToBet();
        }
        break;
      }
    }
  }

  // ---- drawing ----
  // (the same pixels as the JS core; here the font and symbols are turned into bit masks and runs once, so a frame
  // costs fewer interpreter steps: the screen's own Set / Fill do the clipping, as px / fill do in JS)
  static int[] FMASK, FIDX; static int[][] RUNS;          // glyph bits, char -> glyph, symbol rows as (x << 8 | len) runs
  static void Prepare() {
    if (FMASK != null) return;
    FMASK = new int[FONT.Length];
    for (int f = 0; f < FONT.Length; f++) { int m = 0; for (int i = 0; i < 15; i++) if (FONT[f][i] == '1') m |= 1 << i; FMASK[f] = m; }
    FIDX = new int[128];
    for (int c = 0; c < 128; c++) { int f = FONTCH.IndexOf((char)c); FIDX[c] = f < 0 ? FONTCH.Length - 1 : f; }
    RUNS = new int[8 * 11][];
    int[] tmp = new int[6];
    for (int k = 0; k < 8; k++) for (int r = 0; r < 11; r++) {
      string row = ART[k][r]; int n = 0, q = 0;
      while (q < 11) {
        if (row[q] != '#') { q++; continue; }
        int q0 = q; while (q < 11 && row[q] == '#') q++;
        tmp[n++] = (q0 << 8) | (q - q0);
      }
      int[] rr = new int[n]; for (int i = 0; i < n; i++) rr[i] = tmp[i]; RUNS[k * 11 + r] = rr;
    }
  }
  void Px(int x, int y, bool v) { sc.Set(x, y, v); }
  void Fill(int x, int y, int w, int h, bool v) { sc.Fill(x, y, w, h, v); }
  void Box(int x, int y, int w, int h) { sc.Fill(x, y, w, 1, true); sc.Fill(x, y + h - 1, w, 1, true); sc.Fill(x, y, 1, h, true); sc.Fill(x + w - 1, y, 1, h, true); }
  void Text(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int c = s[n], m = FMASK[c < 128 ? FIDX[c] : FONTCH.Length - 1];
      for (int i = 0; m != 0; i++, m >>= 1) if ((m & 1) != 0) sc.Set(x + i % 3, y + i / 3, v);
      x += 4;
    }
  }
  void Big(int x, int y, string s) {
    for (int n = 0; n < s.Length; n++) {
      int c = s[n], m = FMASK[c < 128 ? FIDX[c] : FONTCH.Length - 1];
      for (int i = 0; m != 0; i++, m >>= 1) if ((m & 1) != 0) sc.Fill(x + (i % 3) * 2, y + (i / 3) * 2, 2, 2, true);
      x += 8;
    }
  }
  static int Tw(string s) { return s.Length * 4 - 1; }
  void Centre(int y, string s) { Text(32 - (Tw(s) >> 1), y, s, true); }
  void Sym(int k, int x, int y, int y0, int y1, bool inv) {
    if (k == 0) return;
    int ra = y0 - y, rb = y1 - y; if (ra < 0) ra = 0; if (rb > 10) rb = 10;
    for (int r = ra; r <= rb; r++) {
      int[] rr = RUNS[k * 11 + r];
      for (int i = 0; i < rr.Length; i++) sc.Fill(x + (rr[i] >> 8), y + r, rr[i] & 255, 1, !inv);
    }
  }
  void DrawReels() {
    Box(3, 7, 47, 38); Box(5, 9, 43, 35);
    int chase = winT > 0 ? (t >> 1) % 3 : -1;
    for (int n = chase < 0 ? 0 : chase; n < 160; n += chase < 0 ? 4 : 3) {   // n steps round the ring from (4, 8)
      if (n < 44) sc.Set(4 + n, 8, true); else if (n < 80) sc.Set(48, n - 36, true);
      else if (n < 124) sc.Set(128 - n, 44, true); else sc.Set(4, 168 - n, true);
    }
    Fill(19, 10, 1, 33, true); Fill(33, 10, 1, 33, true);
    bool flash = winT > 0 && phase == BET && ((t >> 2) & 1) == 1;
    for (int i = 0; i < 3; i++) {
      int bs = pos[i] / P, frac = pos[i] % P;
      for (int k = -2; k <= 2; k++) {
        int s = ((bs + k) % N + N) % N, y = MID + frac - k * P;
        bool onLine = k == 0 && frac == 0 && flash;
        if (onLine) Fill(RX[i], MID - 1, 13, 13, true);
        Sym(STRIPS[i][s], RX[i] + 1, y, WY0, WY1, onLine);
      }
    }
    for (int d = 0; d < 3; d++) { Fill(2 - d, 26 - d, 1, d * 2 + 1, true); Fill(50 + d, 26 - d, 1, d * 2 + 1, true); }
    Px(19, 26, false); Px(33, 26, false);
  }
  void DrawLever() {
    int off = 0;
    if (lt >= 0) off = lt < 6 ? lt * 4 : Math.Max(0, 24 - (lt - 6) * 3);
    Fill(55, 34, 7, 11, true); Fill(56, 35, 5, 9, false); Fill(57, 38, 3, 3, true);
    int ky = 6 + off;
    Fill(58, ky + 4, 1, 33 - ky, true);
    Fill(57, ky, 3, 5, true); Fill(56, ky + 1, 5, 3, true); Px(57, ky + 1, false);
  }
  static readonly int[][] ROWS1 = { new int[] { 7, 7, 7 }, new int[] { 6, 6, 6 }, new int[] { 5, 5, 5 }, new int[] { 4, 4, 4 }, new int[] { 1, 1, 1 } };
  static readonly int[][] ROWS2 = { new int[] { 3, 3, 3 }, new int[] { 2, 2, 2 }, new int[] { 4, 5, 4 }, new int[] { 1, 1, 0 }, new int[] { 1, 0, 0 } };
  static readonly int[] PAYS1 = { 800, 150, 80, 40, 30 }, PAYS2 = { 20, 15, 10, 10, 2 };
  void DrawPays() {
    bool p2 = page == 2; int bt = phase == BET ? bet : 1;
    int[][] rows = p2 ? ROWS2 : ROWS1;
    int[] pays = p2 ? PAYS2 : PAYS1;
    Text(1, 1, "PAYS " + (p2 ? "2" : "1"), true);
    string hd = "BET " + bt.ToString(); Text(64 - Tw(hd), 1, hd, true);
    Fill(0, 7, 64, 1, true);
    for (int r = 0; r < 5; r++) {
      int y = 9 + r * 11;
      for (int c = 0; c < 3; c++) {
        if (rows[r][c] == 0) { Fill(2 + c * 12 + 3, y + 5, 5, 1, true); continue; }
        Sym(rows[r][c], 1 + c * 12, y, 0, 63, false);
      }
      if (r == 2 && p2) Text(38, y + 3, "ANY", true);
      int v = !p2 && r == 0 && bt == 3 ? 3000 : pays[r] * bt; string s = v.ToString();
      Text(64 - Tw(s), y + 3, s, true);
    }
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (page > 0) { DrawPays(); return; }
    if (phase == TITLE) {
      Big(13, 3, "SLOTS");
      Sym(6, 13, 20, 0, 63, false); Sym(7, 27, 20, 0, 63, false); Sym(6, 41, 20, 0, 63, false);
      Box(11, 18, 43, 15);
      Centre(38, msgT > 0 ? msg : "1 TO 3 CHIPS");
      Centre(46, "B: PAYS");
      if (((t >> 4) & 1) != 0) Centre(56, "A: START");
      return;
    }
    DrawReels(); DrawLever();
    Text(1, 1, "CHIPS " + Chips.Balance().ToString(), true);
    Text(1, 48, "BET " + bet.ToString(), true);
    if (win > 0 && phase == BET) {
      string s = "WIN " + win.ToString(); int x = 64 - Tw(s) - 1;
      if (winT > 0 && ((t >> 2) & 1) == 1) { Fill(x - 2, 46, Tw(s) + 4, 9, true); Text(x, 48, s, false); }
      else Text(x, 48, s, true);
    }
    string hint = "";
    if (msgT > 0) hint = msg;
    else if (phase == SPIN) hint = ((t >> 3) & 1) != 0 ? "A: STOP" : "";
    else if (phase == BET) { int k = (t >> 5) % 3; hint = k == 0 ? "A: SPIN" : k == 1 ? "UP DOWN: BET" : "B: PAYS"; }
    if (hint.Length > 0) Centre(57, hint);
    if (phase == BROKE) {
      Fill(6, 18, 41, 17, false); Box(6, 18, 41, 17);
      Text(26 - (Tw("OUT OF") >> 1), 21, "OUT OF", true); Text(26 - (Tw("CHIPS") >> 1), 28, "CHIPS", true);
      Fill(0, 56, 64, 7, false); if (((t >> 5) & 1) != 0) Centre(57, "MORE TOMORROW"); else Centre(57, "COME BACK");
    }
    if (big == 2 && winT > 90 && ((t >> 2) & 1) == 1)
      for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) sc.Set(x, y, !sc.Get(x, y));
  }
}
