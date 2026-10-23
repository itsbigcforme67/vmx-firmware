// HI-LO for the VMX: a line-for-line port of microgames/ui/games/hilo_core.js to C# 1.0, so it plays exactly the
// same (same random numbers, same order of events); tools/parity checks that frame by frame.
// One 52-card deck that runs down (reshuffled at a round's start when fewer than 12 cards are left), ranks 2..A,
// suits don't count. Stake 1-10 (Left/Right), A deals the first card; Up = HIGHER, Down = LOWER (the same rank
// loses), B collects the pot (whole chips, stake included) after any right call; 10 right calls or a 9999-chip pot
// collect by themselves. A call pays x floor(97 * left / wins) / 100 from the cards still in the deck, so every
// call returns at most 97% of the pot in expectation (house edge 3% a call; a k-call round returns about 0.97^k),
// less the rounding down to whole chips at collect.
// VMX CASINO: the stake is taken with Chips.Bet at the deal, the collect paid with Chips.Pay.
// Board: Scores.Report(biggest single collect, best streak, 0, 0), not in demo.
using System;
using Microsoft.Vmx;

public class HiLo : Game {
  const string FONTCH = "ABCDEFGHIJKLMNOPQRSTUVWXY0123456789:!-<>+/^.* ";
  static readonly string[] FONT = {
    "010101111101101", "110101110101110", "011100100100011", "110101101101110", "111100110100111", "111100110100100",
    "011100101101011", "101101111101101", "111010010010111", "001001001101010", "101101110101101", "100100100100111",
    "101111111101101", "110101101101101", "010101101101010", "110101110100100", "010101101110011", "110101110101101",
    "011100010001110", "111010010010010", "101101101101111", "101101101101010", "101101111111101", "101101010101101",
    "101101010010010",
    "111101101101111", "010110010010111", "110001010100111", "110001010001110", "101101111001001", "111100110001110",
    "011100111101111", "111001010010010", "111101111101111", "111101111001110", "000010000010000", "010010010000010",
    "000000111000000", "001010100010001", "100010001010100", "000010111010000", "001001010100100", "010111000111010",
    "000000000000010", "000101010101000",
    "000000000000000" };
  static readonly int[] SUIT0 = { 8, 28, 62, 127, 127, 42, 28 }, SUIT1 = { 54, 73, 65, 65, 34, 20, 8 },
    SUIT2 = { 8, 20, 34, 65, 34, 20, 8 }, SUIT3 = { 28, 28, 107, 127, 107, 8, 28 };
  static readonly int[] CHIP = { 14, 21, 27, 21, 14 };
  static readonly int[] UPA = { 8, 28, 62, 127 }, DNA = { 127, 62, 28, 8 };
  static readonly string[] RANKS = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };
  const int CY = 8, LX = 1, RX = 23;
  const int MAXPOT = 999900, MAXCALLS = 10;
  const int TITLE = 0, BET = 1, DEAL = 2, CALL = 3, REVEAL = 4, RIGHT = 5, WON = 6, LOST = 7, BROKE = 8;
  static readonly int[] JINGLE = { 784, 988, 1175, 1568 };

  // game state (the JS object g)
  uint rs; int t, phase, pt, stake, pos, cur, nxt, pick, pot, streak, hiW, loW, left, mHi, mLo, nd, win, target, wantWins, msgT;
  int bestWin, bestStreak;
  int[] deck = new int[52], drawn = new int[11];
  bool tie;
  string msg = "";
  bool auto_, demo, pUp, pDown, pLeft, pRight, pA, pB;
  Screen sc;

  public override bool UsesB { get { return true; } }

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    Prepare();
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; stake = 5; pos = 52;
    for (int i = 0; i < 52; i++) deck[i] = 0;
    for (int i = 0; i < 11; i++) drawn[i] = 0;
    cur = 0; nxt = 0; pick = 0; pot = 0; streak = 0; hiW = 0; loW = 0; left = 0; mHi = 0; mLo = 0; nd = 0;
    win = 0; tie = false; target = 5; wantWins = 3; msg = ""; msgT = 0; auto_ = false;
    pUp = pDown = pLeft = pRight = pA = pB = false; bestWin = 0; bestStreak = 0;
    Shuffle();
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString(), 90);
  }
  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m, int n) { msg = m; msgT = n; }
  static string Fmt(int p) { int c = p % 100; return (p / 100).ToString() + "." + (c < 10 ? "0" : "") + c.ToString(); }
  int Mult(int w) { return w > 0 ? 97 * left / w : 0; }
  void ToBet() {
    int c = Chips.Balance();
    if (c < 1) { SetPhase(BROKE); return; }
    stake = Math.Max(1, Math.Min(stake, Math.Min(10, c)));
    SetPhase(BET);
    if (auto_) target = Math.Min(1 + Rnd() % 10, c);
  }
  void Shuffle() {
    for (int i = 0; i < 52; i++) deck[i] = i;
    for (int i = 51; i > 0; i--) { int j = Rnd() % (i + 1), tt = deck[i]; deck[i] = deck[j]; deck[j] = tt; }
    pos = 0;
  }
  void Deal() {
    if (52 - pos < 12) { Shuffle(); Say("SHUFFLE", 40); }
    Chips.Bet(stake);
    cur = deck[pos++]; drawn[0] = cur; nd = 1;
    pot = stake * 100; streak = 0; win = 0; tie = false; pick = 0;
    if (auto_) wantWins = 3 + Rnd() % 3;
    SetPhase(DEAL);
  }
  void Odds() {
    int cr = cur % 13; hiW = 0; loW = 0; left = 52 - pos;
    for (int i = pos; i < 52; i++) { int r = deck[i] % 13; if (r > cr) hiW++; else if (r < cr) loW++; }
    mHi = Mult(hiW); mLo = Mult(loW);
  }
  void ToCall() {
    Odds();
    if (hiW == 0 && loW == 0) { Collect(); return; }
    SetPhase(CALL);
  }
  void Call(int p) { pick = p; nxt = deck[pos++]; SetPhase(REVEAL); }
  void Report() { if (!demo) Scores.Report(bestWin, bestStreak, 0, 0); }
  void Collect() {
    win = pot / 100; Chips.Pay(win);
    bool rep = false;
    if (win > bestWin) { bestWin = win; rep = true; }
    if (streak > bestStreak) { bestStreak = streak; rep = true; }
    if (rep) Report();
    SetPhase(WON);
  }
  void Judge() {
    int r = nxt % 13, cr = cur % 13;
    bool ok = pick == 1 ? r > cr : r < cr;
    drawn[nd++] = nxt;
    if (!ok) {
      tie = r == cr;
      if (streak > bestStreak) { bestStreak = streak; Report(); }
      SetPhase(LOST); return;
    }
    int m = pick == 1 ? mHi : mLo;
    pot = pot / 100 * m + pot % 100 * m / 100;
    if (pot > MAXPOT) pot = MAXPOT;
    streak++;
    SetPhase(RIGHT);
  }

  public override void Tick(int held_, int pressed) {
    bool auto = demo;
    t++; pt++; if (msgT > 0) msgT--;
    bool up = !auto && (held_ & Buttons.Up) != 0, down = !auto && (held_ & Buttons.Down) != 0,
      left_ = !auto && (held_ & Buttons.Left) != 0, right = !auto && (held_ & Buttons.Right) != 0,
      a = !auto && (held_ & Buttons.A) != 0, b = !auto && (held_ & Buttons.B) != 0;
    bool eUp = up && !pUp, eDown = down && !pDown, eLeft = left_ && !pLeft, eRight = right && !pRight, eA = a && !pA, eB = b && !pB;
    pUp = up; pDown = down; pLeft = left_; pRight = right; pA = a; pB = b;
    auto_ = auto;

    switch (phase) {
      case TITLE:
        if (eA || (auto && pt > 40)) ToBet();
        break;
      case BET: {
        int top = Math.Min(10, Chips.Balance());
        if (auto) {
          if (pt % 5 == 0) { if (stake < target) stake++; else if (stake > target) stake--; }
          if (pt > 45 && stake == target) Deal();
        } else {
          if (eRight) { stake = Math.Min(top, stake + 1); Beeper.Tone(1500, 10, 25); }
          if (eLeft) { stake = Math.Max(1, stake - 1); Beeper.Tone(1200, 10, 25); }
          if (eA) Deal();
        }
        break;
      }
      case DEAL:
        if (pt > 10) ToCall();
        break;
      case CALL:
        if (auto) {
          if (pt == 24) {
            int best = Math.Max(hiW, loW);
            if (streak > 0 && (best * 5 < left * 3 || (streak >= wantWins && pot / 100 > stake))) Collect();
            else Call(hiW >= loW ? 1 : 2);
          }
        } else {
          if (eUp && hiW > 0) Call(1);
          else if (eDown && loW > 0) Call(2);
          else if (eB && streak > 0) Collect();
        }
        break;
      case REVEAL:
        if (pt > 14) Judge();
        break;
      case RIGHT:
        if (!auto && pt == 1) Beeper.Tone(1568, 60, 35);
        if (pt > 20) {
          cur = nxt;
          if (streak >= MAXCALLS || pot >= MAXPOT) Collect(); else ToCall();
        }
        break;
      case WON: case LOST:
        if (phase == WON && win > 0 && !auto && pt <= 13 && (pt - 1) % 4 == 0) Beeper.Tone(JINGLE[(pt - 1) / 4], 90, 40);
        if (phase == LOST && !auto && pt == 1) Beeper.Tone(300, 200, 40);
        if ((eA && pt > 10) || (auto && pt > 75)) ToBet();
        break;
      case BROKE:
        break;
    }
    if ((phase == DEAL || phase == REVEAL) && !auto && pt == 5) Beeper.Tone(2400, 6, 20);   // a click as the card turns
  }

  // ---- drawing ----
  // (the same pixels as the JS core; the font is turned into bit masks once, and the screen's own Set / Fill do the
  // clipping, as px / fill do in JS, so a frame costs fewer interpreter steps)
  static int[] FMASK, FIDX;
  static void Prepare() {
    if (FMASK != null) return;
    FMASK = new int[FONT.Length];
    for (int f = 0; f < FONT.Length; f++) { int m = 0; for (int i = 0; i < 15; i++) if (FONT[f][i] == '1') m |= 1 << i; FMASK[f] = m; }
    FIDX = new int[128];
    for (int c = 0; c < 128; c++) { int f = FONTCH.IndexOf((char)c); FIDX[c] = f < 0 ? FONTCH.Length - 1 : f; }
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
  void Big(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int c = s[n], m = FMASK[c < 128 ? FIDX[c] : FONTCH.Length - 1];
      for (int i = 0; m != 0; i++, m >>= 1) if ((m & 1) != 0) sc.Fill(x + (i % 3) * 2, y + (i / 3) * 2, 2, 2, v);
      x += 8;
    }
  }
  static int Tw(string s) { return s.Length * 4 - 1; }
  void Centre(int y, string s) { Text(32 - (Tw(s) >> 1), y, s, true); }
  void Right(int y, string s) { Text(64 - s.Length * 4, y, s, true); }
  void Sprite(int x, int y, int[] rows, int w, bool v) {
    for (int r = 0; r < rows.Length; r++) { int row = rows[r]; for (int q = 0; q < w; q++) if (((row >> (w - 1 - q)) & 1) != 0) sc.Set(x + q, y + r, v); }
  }
  void Sprite2(int x, int y, int[] rows, int w, bool v) {
    for (int r = 0; r < rows.Length; r++) { int row = rows[r]; for (int q = 0; q < w; q++) if (((row >> (w - 1 - q)) & 1) != 0) sc.Fill(x + q * 2, y + r * 2, 2, 2, v); }
  }
  static int[] Suit(int s) { return s == 0 ? SUIT0 : s == 1 ? SUIT1 : s == 2 ? SUIT2 : SUIT3; }
  void Back(int x0, int y, int w, int h) {
    Fill(x0 + 1, y + 1, w - 2, h - 2, false);
    for (int j = 2; j < h - 2; j++) {
      for (int i = (4 - j % 4) % 4; i < w - 2; i += 4) if (i >= 2) sc.Set(x0 + i, y + j, true);
      for (int i = j % 4; i < w - 2; i += 4) if (i >= 2) sc.Set(x0 + i, y + j, true);
    }
  }
  void BigCard(int x, int y, int c, bool face, int w) {
    int x0 = x + ((20 - w) >> 1);
    Fill(x0, y + 1, w, 28, true); Fill(x0 + 1, y, w - 2, 1, true); Fill(x0 + 1, y + 29, w - 2, 1, true);
    if (!face) { Back(x0, y, w, 30); return; }
    if (w < 20) return;
    int r = c % 13;
    if (r == 8) { Big(x + 2, y + 2, "1", false); Big(x + 10, y + 2, "0", false); } else Big(x + 2, y + 2, RANKS[r], false);
    Sprite2(x + 3, y + 14, Suit(c / 13), 7, false);
  }
  void MiniCard(int x, int y, int c) {
    Fill(x, y + 1, 7, 5, true); Fill(x + 1, y, 5, 1, true); Fill(x + 1, y + 6, 5, 1, true);
    int r = c % 13;
    if (r == 8) { Fill(x + 1, y + 1, 1, 5, false); Text(x + 3, y + 1, "0", false); } else Text(x + 2, y + 1, RANKS[r], false);
  }
  void FlipCard(int x, int c, int f) {
    if (f < 2) BigCard(x, CY, c, false, 14);
    else if (f < 4) BigCard(x, CY, c, false, 6);
    else if (f < 6) BigCard(x, CY, c, true, 6);
    else if (f < 8) BigCard(x, CY, c, true, 14);
    else BigCard(x, CY, c, true, 20);
  }
  void DrawCalls() {
    bool show = phase == CALL || phase == REVEAL || phase == RIGHT || phase == LOST;
    for (int k = 0; k < 2; k++) {
      int y = CY + k * 15; bool lit = (phase == REVEAL || phase == RIGHT || phase == LOST) && pick == k + 1;
      if (lit) Fill(44, y - 1, 20, 14, true);
      Sprite(46, y + 1, k == 0 ? UPA : DNA, 7, !lit);
      Text(55, y, k == 0 ? "HI" : "LO", !lit);
      if (!show) continue;
      int m = k == 0 ? mHi : mLo;
      string s = "--";
      if (m > 0) s = m < 1000 ? "*" + Fmt(m) : Fmt(m);
      Text(65 - s.Length * 4, y + 7, s, !lit);
    }
  }
  void DrawStrip() {
    int n0 = nd > 8 ? nd - 8 : 0;
    for (int i = n0; i < nd; i++) MiniCard((i - n0) * 8, 48, drawn[i]);
  }
  void MsgBox(string l1, string l2) {
    Fill(0, 38, 64, 19, false); Box(1, 39, 62, 18); Box(3, 41, 58, 14);
    Centre(43, l1); Centre(49, l2);
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      Big(12, 2, "HI-LO", true);
      BigCard(9, 15, 44, true, 20);
      Sprite(29, 21, UPA, 7, true); Sprite(29, 33, DNA, 7, true);
      BigCard(37, 15, 0, false, 20);
      if (phase == BROKE) {
        Fill(0, 46, 64, 18, false);
        Centre(48, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Centre(56, "MORE TOMORROW");
        return;
      }
      Centre(48, msgT > 0 ? msg : "HIGHER OR LOWER");
      if (((t >> 4) & 1) != 0) Centre(57, "A: START");
      return;
    }
    Sprite(1, 1, CHIP, 5, true); Text(8, 1, Chips.Balance().ToString(), true);
    if (phase == BET) Right(1, "DECK " + (52 - pos).ToString());
    else Right(1, "STREAK " + streak.ToString());

    if (phase == BET) { BigCard(LX, CY, 0, false, 20); BigCard(RX, CY, 0, false, 20); }
    else if (phase == DEAL) { FlipCard(LX, cur, pt - 1); BigCard(RX, CY, 0, false, 20); }
    else if (phase == REVEAL) { BigCard(LX, CY, cur, true, 20); FlipCard(RX, nxt, pt - 1); }
    else if (phase == RIGHT) {
      BigCard(LX, CY, cur, true, 20);
      if (pt > 12) { BigCard(RX, CY, 0, false, 20); BigCard(RX - 22 * (pt - 12) / 8, CY, nxt, true, 20); }
      else BigCard(RX, CY, nxt, true, 20);
    } else if (phase == LOST) { BigCard(LX, CY, cur, true, 20); BigCard(RX, CY, nxt, true, 20); }
    else { BigCard(LX, CY, cur, true, 20); BigCard(RX, CY, 0, false, 20); }
    DrawCalls();

    if (phase == BET) {
      Text(1, 40, "STAKE", true); string s = stake.ToString();
      Text(24, 40, "<", true); Text(30 + (s.Length == 1 ? 2 : 0), 40, s, true); Text(40, 40, ">", true);
    } else Text(1, 40, "POT " + Fmt(pot), true);
    DrawStrip();
    string hint = "";
    if (msgT > 0 && phase != WON && phase != LOST) hint = msg;
    else if (phase == BET) hint = ((t >> 4) & 1) != 0 ? "A: DEAL" : "< > STAKE";
    else if (phase == CALL) hint = streak > 0 && ((t >> 5) & 1) != 0 ? "B: COLLECT " + (pot / 100).ToString() : "UP OR DOWN";
    else if (phase == RIGHT) hint = pick == 1 ? "HIGHER!" : "LOWER!";
    else if ((phase == WON || phase == LOST) && !auto_ && ((t >> 4) & 1) != 0) hint = "A: NEXT";
    if (hint.Length > 0) Centre(58, hint);
    if (phase == WON) MsgBox(streak >= MAXCALLS || pot >= MAXPOT ? "TOP OF LADDER" : "COLLECTED", "+" + win.ToString() + " CHIPS");
    else if (phase == LOST) MsgBox(tie ? "SAME RANK" : "WRONG", "LOST " + stake.ToString());
  }
}
