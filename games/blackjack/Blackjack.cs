// BLACKJACK AND HOOKERS for the VMX: blackjack with a fishing theme (the hookers are fishing hooks). A line-for-line
// port of microgames/ui/games/blackjack_core.js to C# 1.0, plus the web simulator's adapter (POCKET ARCADE
// vmx/games.js: a title screen that waits for A; the demo skips it), so it plays exactly the same (same random
// numbers, same doubles); tools/parity checks that frame by frame.
// Left/right set the bet and A deals; then A hits, B stands, Up doubles.
using System;
using Microsoft.Vmx;

public class Blackjack : Game {
  const int BET = 0, DEAL = 1, PLAY = 2, DEALER = 3, DONE = 4;   // g.phase
  const int MAXC = 40;                                          // cards a hand can hold (4 decks: 16 aces...)

  uint rs;                                       // the JS core's rng(): an LCG, divided down to [0, 1)
  double Rnd() { rs = rs * 1664525u + 1013904223u; return rs / 4294967296.0; }

  int[] shoeR = new int[208], shoeS = new int[208]; int nShoe;
  // the hands: rank, suit, hidden, age per card
  int[] dR = new int[MAXC], dS = new int[MAXC], dAge = new int[MAXC]; bool[] dHid = new bool[MAXC]; int nD;
  int[] pR = new int[MAXC], pS = new int[MAXC], pAge = new int[MAXC]; bool[] pHid = new bool[MAXC]; int nP;
  double[] fishX = new double[2]; int[] fishY = new int[2]; int nFish;
  int t, bait, bet, phase, msgT, wait, hands;
  string msg;
  bool title; int titleT;                        // the adapter's title screen
  bool demo; int best;

  public override bool UsesB { get { return true; } }

  public override void Start(int seed, bool demo) {
    this.demo = demo;
    rs = (uint)seed;
    t = 0; bait = 100; bet = 10; phase = BET; nD = 0; nP = 0; msg = ""; msgT = 0; wait = 0; hands = 0; nFish = 0;
    Shuffle();
    title = true; titleT = 0;
  }
  void Shuffle() {
    nShoe = 0;
    for (int d = 0; d < 4; d++) for (int s = 0; s < 4; s++) for (int r = 0; r < 13; r++) { shoeR[nShoe] = r; shoeS[nShoe] = s; nShoe++; }
    for (int i = nShoe - 1; i > 0; i--) {
      int j = (int)Math.Floor(Rnd() * (i + 1));
      int a = shoeR[i]; shoeR[i] = shoeR[j]; shoeR[j] = a;
      a = shoeS[i]; shoeS[i] = shoeS[j]; shoeS[j] = a;
    }
  }
  // total(hand): hidden cards don't count (all: count them anyway, as the deal check does). soft in totSoft.
  bool totSoft;
  int Total(int[] r, bool[] hid, int n, bool all) {
    int tt = 0, aces = 0;
    for (int i = 0; i < n; i++) {
      if (hid[i] && !all) continue;
      tt += r[i] == 0 ? 11 : Math.Min(10, r[i] + 1); if (r[i] == 0) aces++;
    }
    while (tt > 21 && aces > 0) { tt -= 10; aces--; }
    totSoft = aces > 0;
    return tt;
  }
  int PT() { return Total(pR, pHid, nP, false); }
  int DT() { return Total(dR, dHid, nD, false); }
  void DealP() { if (nShoe < 52) Shuffle(); nShoe--; pR[nP] = shoeR[nShoe]; pS[nP] = shoeS[nShoe]; pHid[nP] = false; pAge[nP] = 0; nP++; }
  void DealD(bool hidden) { if (nShoe < 52) Shuffle(); nShoe--; dR[nD] = shoeR[nShoe]; dS[nD] = shoeS[nShoe]; dHid[nD] = hidden; dAge[nD] = 0; nD++; }
  void Settle(string m, double win) {
    bait += Round(bet * win); msg = m; msgT = 75; phase = DONE; wait = 75; hands++;
  }

  public override void Tick(int held, int pressed) {
    if (title) { titleT++; if ((pressed & Buttons.A) != 0 || demo) title = false; }
    else if (demo) { int w = Demo(); Step((w & 4) != 0, (w & 8) != 0, (w & 16) != 0, (w & 32) != 0, (w & 1) != 0); }
    else Step((pressed & Buttons.Left) != 0, (pressed & Buttons.Right) != 0, (pressed & Buttons.A) != 0,
              (pressed & Buttons.B) != 0, (pressed & Buttons.Up) != 0);
    if (bait > best) { best = bait; Scores.Report(bait, 0, 0, 0); }
  }

  void Step(bool left, bool right, bool a, bool b, bool up) {
    t++;
    for (int i = 0; i < nD; i++) dAge[i]++;
    for (int i = 0; i < nP; i++) pAge[i]++;
    if (msgT != 0) msgT--;
    if (Rnd() < 0.01 && nFish < 2) {
      fishX[nFish] = Rnd() < 0.5 ? -6 : 70;
      fishY[nFish] = 27 + (int)Math.Floor(Rnd() * 3); nFish++;
    }
    for (int i = 0; i < nFish; i++) fishX[i] += fishX[i] < 32 ? 0.4 : -0.4;
    int k = 0;
    for (int i = 0; i < nFish; i++) {
      double x = fishX[i];
      if (x > -8 && x < 72 && Math.Abs(x - 32) < 40) { fishX[k] = x; fishY[k] = fishY[i]; k++; }
    }
    nFish = k;
    if (wait > 0) { wait--; if (wait != 0) return; }
    if (phase == BET) {
      if (left) bet = Math.Max(5, bet - 5);
      if (right) bet = Math.Min(Math.Min(50, bait), bet + 5);
      if (a) {
        bet = Math.Min(bet, bait); bait -= bet;
        nD = 0; nP = 0; DealP(); DealD(false); DealP(); DealD(true);
        phase = DEAL; wait = 20;
      }
    } else if (phase == DEAL) {
      int p = PT(), d = Total(dR, dHid, nD, true);
      if (p == 21 || d == 21) {
        dHid[1] = false;
        if (p == 21 && d == 21) Settle("SLACK LINE", 1);
        else if (p == 21) Settle("BIG ONE!", 2.5);
        else Settle("SHARK BITE", 0);
      } else phase = PLAY;
    } else if (phase == PLAY) {
      if (a) { DealP(); wait = 8; if (PT() > 21) { dHid[1] = false; Settle("SNAGGED!", 0); } }
      else if (up && nP == 2 && bait >= bet) {
        bait -= bet; bet *= 2; DealP(); wait = 10;
        if (PT() > 21) { dHid[1] = false; Settle("SNAGGED!", 0); } else phase = DEALER;
      } else if (b) { phase = DEALER; }
      if (phase == DEALER) { dHid[1] = false; wait = 18; }
    } else if (phase == DEALER) {
      int d = DT();
      if (d < 17) { DealD(false); wait = 18; return; }
      int p = PT();
      if (d > 21) Settle("IT GOT AWAY", 2);
      else if (p > d) Settle("NICE CATCH!", 2);
      else if (p == d) Settle("SLACK LINE", 1);
      else Settle("THROWN BACK", 0);
    } else if (phase == DONE) {
      if (bet > 10 && bait > 0) bet = Math.Max(10, (int)Math.Floor(bet / 2.0 / 5) * 5);   // doubled bets drop back
      if (bait <= 0) { bait = 100; msg = "FRESH BAIT"; msgT = 60; }
      bet = Math.Min(bet, bait); phase = BET;
    }
  }

  // ---- drawing
  static int Round(double v) { return (int)Math.Floor(v + 0.5); }    // JS Math.round
  static readonly string[] SUITS = {                                   // 5x5: fish, hook, shell, worm
    ".....", ".##.#", "#.###", ".##.#", ".....",
    "..#..", "..#..", "#.#..", "#.#..", ".##..",
    "..#..", ".###.", "#.#.#", "#####", ".#.#.",
    "##...", ".#...", ".##..", "..#.#", "..###",
  };
  static readonly string[] RANKS = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
  // the JS core's glyphs for the ranks (for cards drawn at fractional positions; same as the VMX font)
  static readonly string[] RANKG = { "010101111101101", "110001010100111", "110001010001110", "101101111001001",
    "111100110001110", "011100111101111", "111001010010010", "111101111101111", "111101111001110", "",
    "001001001101010", "010101101110011", "101101110101101" };
  const string G1 = "010110010010111", G0 = "111101101101111", GAMP = "010101010101011";
  static readonly string[] HOOK = { "..#", "..#", "..#", "#.#", "#.#", ".#." };

  static void Px(Screen s, double x, double y, bool v) { s.Set(Round(x), Round(y), v); }   // px(): rounds, clips
  static void GlyphD(Screen s, double x, double y, string g, bool v) {
    for (int r = 0; r < 5; r++) for (int q = 0; q < 3; q++) if (g[r * 3 + q] == '1') Px(s, x + q, y + r, v);
  }
  static bool BackOn(int i, int j) {
    return (i == 0 || i == 10 || j == 0 || j == 13) || ((i + (j % 4 < 2 ? 0 : 2)) % 4 == 0 || j % 4 == 3);
  }
  // card(): 11 x 14, lit face with a dark rank + suit; backs: waves. At rest (whole pixels) the fast way.
  static void CardI(Screen s, int x, int y, int r, int su, bool hidden) {
    if (hidden) {
      for (int j = 0; j < 14; j++) for (int i = 0; i < 11; i++) {
        if ((i == 0 || i == 10) && (j == 0 || j == 13)) continue;
        s.Set(x + i, y + j, BackOn(i, j));
      }
      return;
    }
    s.Fill(x + 1, y, 9, 1, true); s.Fill(x, y + 1, 11, 12, true); s.Fill(x + 1, y + 13, 9, 1, true);
    if (r == 9) { s.Text(x + 1, y + 1, "1", false); s.Text(x + 4, y + 1, "0", false); } else s.Text(x + 1, y + 1, RANKS[r], false);
    for (int j = 0; j < 5; j++) { string row = SUITS[su * 5 + j]; for (int i = 0; i < 5; i++) if (row[i] == '#') s.Set(x + 5 + i, y + 7 + j, false); }
  }
  static void CardD(Screen s, double x, double y, int r, int su, bool hidden) {
    for (int j = 0; j < 14; j++) for (int i = 0; i < 11; i++) {
      if ((i == 0 || i == 10) && (j == 0 || j == 13)) continue;
      if (hidden) Px(s, x + i, y + j, BackOn(i, j));
      else Px(s, x + i, y + j, true);
    }
    if (hidden) return;
    if (r == 9) { GlyphD(s, x + 1, y + 1, G1, false); GlyphD(s, x + 4, y + 1, G0, false); } else GlyphD(s, x + 1, y + 1, RANKG[r], false);
    for (int j = 0; j < 5; j++) { string row = SUITS[su * 5 + j]; for (int i = 0; i < 5; i++) if (row[i] == '#') Px(s, x + 5 + i, y + 7 + j, false); }
  }
  static void Hand(Screen s, int[] r, int[] su, bool[] hid, int[] age, int n, int y) {
    int step = n > 4 ? 7 : 12;
    for (int i = 0; i < n; i++) {
      int tx = 2 + i * step;                       // slides in from the shoe (top right)
      if (age[i] >= 6) CardI(s, tx, y, r[i], su[i], hid[i]);
      else { double k = age[i] / 6.0; CardD(s, 60 + (tx - 60) * k, -14 + (y + 14) * k, r[i], su[i], hid[i]); }
    }
  }

  public override void Draw(Screen s) {
    s.Clear();
    if (title) { DrawTitle(s, titleT); return; }
    // water through the middle, with the odd fish
    for (int x = 0; x < 64; x++) {
      int y = 25 + Round(Math.Sin((x + t * 0.3) / 4)); s.Set(x, y, true);
      if ((x + (t >> 2)) % 7 == 0) s.Set(x, y + 4, true);
    }
    for (int i = 0; i < nFish; i++) {
      double fx = fishX[i]; int fy = fishY[i]; bool l = fx < 32; int e = l ? -1 : 3;
      Px(s, fx + 0, fy, true); Px(s, fx + 1, fy, true); Px(s, fx + 2, fy, true);
      Px(s, fx + 1, fy - 1, true); Px(s, fx + 1, fy + 1, true); Px(s, fx + e, fy - 1, true); Px(s, fx + e, fy + 1, true);
    }
    Hand(s, dR, dS, dHid, dAge, nD, 3);
    Hand(s, pR, pS, pHid, pAge, nP, 33);
    // totals
    if (nD > 0) s.Text(52, 4, DT().ToString().PadLeft(2, ' '), true);
    if (nP > 0) s.Text(52, 34, PT().ToString().PadLeft(2, ' '), true);
    // bottom bar: bait and bet (a hook dangling over the bet)
    s.Fill(0, 51, 64, 1, true);
    s.Text(1, 53, "BAIT " + bait.ToString().PadLeft(3, ' '), true);
    for (int j = 0; j < 6; j++) for (int i = 0; i < 3; i++) if (HOOK[j][i] == '#') s.Set(45 + i, 52 + j, true);
    string bs = bet.ToString().PadLeft(3, ' '); if (bs.Length > 3) bs = bs.Substring(bs.Length - 3);
    s.Text(51, 53, bs, true);
    // the hint; "< BET >": the JS core's font has no < or >, so they draw as spaces
    string hint = phase == BET ? (((t >> 4) & 1) != 0 ? "A: CAST" : "  BET  ") : phase == PLAY ? "A HIT B STAY" : "";
    if (hint.Length > 0) s.Text((64 - Screen.TextWidth(hint)) >> 1, 59, hint, true);
    if (msgT != 0) {                               // the call-out, boxed over the water
      int w = Screen.TextWidth(msg) + 4, x = (64 - w) >> 1;
      s.Fill(x, 20, w, 11, false);
      s.Fill(x, 20, w, 1, true); s.Fill(x, 30, w, 1, true); s.Fill(x, 20, 1, 11, true); s.Fill(x + w - 1, 20, 1, 11, true);
      s.Text(x + 2, 23, msg, true);
    }
  }

  // ---- title card (drawTitle)
  static void DrawTitle(Screen s, int tt) {
    s.Text((64 - Screen.TextWidth("BLACKJACK")) >> 1, 8, "BLACKJACK", true);
    int x0 = (64 - Screen.TextWidth("& HOOKERS")) >> 1;                 // the VMX font has no &: the JS core's
    for (int r = 0; r < 5; r++) for (int q = 0; q < 3; q++) if (GAMP[r * 3 + q] == '1') s.Set(x0 + q, 16 + r, true);
    s.Text(x0 + 4, 16, " HOOKERS", true);
    for (int j = 0; j < 14; j++) s.Set(32, 24 + j, true);                // line + hook
    for (int j = 0; j < 6; j++) for (int i = 0; i < 3; i++) if (HOOK[j][i] == '#') s.Set(30 + i, 38 + j, true);
    for (int x = 0; x < 64; x++) s.Set(x, 46 + Round(Math.Sin((x + tt * 0.3) / 4)), true);
    for (int k = 0; k < 4; k++) for (int j = 0; j < 5; j++) {          // the 4 suits under the water
      string row = SUITS[k * 5 + j];
      for (int i = 0; i < 5; i++) if (row[i] == '#') s.Set(10 + k * 13 + i, 50 + j, true);
    }
    if (((tt >> 4) & 1) != 0) s.Text((64 - Screen.TextWidth("PRESS A")) >> 1, 57, "PRESS A", true);
  }

  // ---- the demo player: basic strategy (simplified: no split). Bits as Buttons (as the JS core's demo()).
  static readonly int[] TARGETS = { 10, 10, 15, 20, 25 };
  int Demo() {
    if (wait != 0 || t % 12 != 0) return 0;
    if (phase == BET) { int target = TARGETS[(int)Math.Floor(Rnd() * 5)]; return bet < target ? Buttons.Right : Buttons.A; }
    if (phase != PLAY) return 0;
    int p = PT(); bool soft = totSoft;
    int up = Math.Min(10, dR[0] == 0 ? 11 : dR[0] + 1);
    if (nP == 2 && (p == 11 || (p == 10 && up <= 9)) && bait >= bet) return Buttons.Up;
    bool hit = soft ? p <= 17 : p <= 11 || (p <= 16 && up >= 7) || (p == 12 && up <= 3);
    return hit ? Buttons.A : Buttons.B;
  }
}
