// RED DOG (acey-deucey) for the VMX: a line-for-line port of microgames/ui/games/reddog_core.js to C# 1.0, so it
// plays exactly the same (same random numbers, same order of events); tools/parity checks that frame by frame.
// A 6-deck shoe (reshuffled between hands once fewer than 78 cards are left), ranks 2 low .. A high. Bet 1-20; two
// cards face up. Consecutive ranks push; a pair deals a third card (three of a kind pays 11:1, else push); otherwise
// the spread pays 5:1 (1), 4:1 (2), 2:1 (3) or 1:1 (4-11) if the third card lands strictly between, and the player
// may RAISE (double the bet) first. House edge 2.80% raising on spread 7+ (7.97% never raising), 6 decks, exact.
// Left/Right bet -1/+1, Up/Down +5/-5, A deals; on a spread Up = RAISE, A = CALL; A after a hand = the pay table.
// VMX CASINO: the stake is taken with Chips.Bet when the cards are dealt (again on a raise), paid with Chips.Pay.
using System;
using Microsoft.Vmx;

public class RedDog : Game {
  const string FONTCH = "ABCDEFGHIJKLMNOPQRSTUVWXY0123456789:!-<>+/^ ";
  static readonly int[] FONT = {   // the core's 3x5 glyph strings as 15-bit numbers, bit 14 = top left
    11245, 27566, 14627, 27502, 31143, 31140, 14699, 23533, 29847, 4714, 23469, 18727,
    24557, 27501, 11114, 27556, 11123, 27565, 14478, 29842, 23407, 23402, 23549, 23213,
    23186, 31599, 11415, 25255, 25230, 23497, 31118, 14831, 29330, 31727, 31694, 1040,
    9346, 448, 5393, 17492, 1488, 4772, 11834, 0 };
  static readonly int[] SUIT0 = { 8, 28, 62, 127, 127, 42, 28 }, SUIT1 = { 54, 73, 65, 65, 34, 20, 8 },
    SUIT2 = { 8, 20, 34, 65, 34, 20, 8 }, SUIT3 = { 28, 28, 107, 127, 107, 8, 28 };
  static readonly int[] CHIP = { 14, 21, 27, 21, 14 };
  static readonly string[] RANKS = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };
  static readonly string[] ODDS = { "", "5:1", "4:1", "2:1", "1:1" };
  static readonly string[] ROWS = { "SPREAD 1", "SPREAD 2", "SPREAD 3", "SPREAD 4-11", "PAIR TRIPS", "CONSECUTIVE" };
  static readonly string[] RODDS = { "5:1", "4:1", "2:1", "1:1", "11:1", "PUSH" };
  static readonly string[] LABELS = { "", "CONSECUTIVE", "NO TRIPS", "THREE OF A KIND", "IN BETWEEN", "OUTSIDE" };
  const int SHOE = 312, CUT = 234;
  const int CY = 10;
  static readonly int[] CX = { 10, 42, 26 };
  const int TITLE = 0, BET = 1, DEAL = 2, SPREAD = 3, PAIR = 4, THIRD = 5, RESULT = 6, BROKE = 7;
  static readonly int[] JINGLE = { 784, 988, 1175, 1568 };

  // game state (the JS object g)
  uint rs; int t, phase, pt, bet, pos, spread, res, win, best, last, target, msgT;
  int[] shoe = new int[SHOE], c = new int[3], flip = new int[3];
  bool has3, raised, fresh;
  string msg = "";
  bool auto_, demo, pUp, pDown, pLeft, pRight, pA, pB;
  Screen sc;

  public override bool UsesB { get { return false; } }

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; bet = 5;
    for (int i = 0; i < SHOE; i++) shoe[i] = 0;
    pos = SHOE;
    for (int i = 0; i < 3; i++) { c[i] = 0; flip[i] = -1; }
    has3 = false; spread = 0; raised = false; res = 0; win = 0; best = 0; last = -1; fresh = false; target = 5;
    msg = ""; msgT = 0; auto_ = false; pUp = pDown = pLeft = pRight = pA = pB = false;
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 90; }
  void ToBet() {
    int ch = Chips.Balance();
    if (ch < 1) { SetPhase(BROKE); return; }
    int top = Math.Min(20, ch);
    bet = Math.Max(1, Math.Min(bet, top));
    SetPhase(BET);
    if (auto_) target = 1 + Rnd() % top;
  }
  static int OddsOf(int s) { return s >= 4 ? 1 : s == 3 ? 2 : s == 2 ? 4 : 5; }

  // ---- the game ----
  void Deal() {
    Chips.Bet(bet);
    fresh = false;
    if (pos > CUT) {
      for (int i = 0; i < SHOE; i++) shoe[i] = i % 52;
      for (int i = SHOE - 1; i > 0; i--) { int j = Rnd() % (i + 1), tt = shoe[i]; shoe[i] = shoe[j]; shoe[j] = tt; }
      pos = 0; fresh = true;
    }
    c[0] = shoe[pos++]; c[1] = shoe[pos++]; c[2] = 0;
    flip[0] = 2; flip[1] = 8; flip[2] = -1; has3 = false;
    raised = false; res = 0; win = 0;
    int a = c[0] % 13, b = c[1] % 13;
    spread = a > b ? a - b - 1 : b - a - 1;
    SetPhase(DEAL);
  }
  void Third(int p) {
    c[2] = shoe[pos++]; has3 = true; flip[2] = 6;
    SetPhase(p);
  }
  void Raise() { Chips.Bet(bet); bet *= 2; raised = true; }
  void Judge() {
    int a = c[0] % 13, b = c[1] % 13, r = c[2] % 13;
    if (spread == 0) { res = 1; win = bet; }
    else if (spread < 0) {
      if (r == a) { res = 3; win = bet * 12; } else { res = 2; win = bet; }
    } else {
      int lo = Math.Min(a, b), hi = Math.Max(a, b);
      if (r > lo && r < hi) { res = 4; win = bet * (OddsOf(spread) + 1); } else { res = 5; win = 0; }
    }
    Chips.Pay(win);
    if ((res == 3 || res == 4) && win > best) { best = win; if (!demo) Scores.Report(best, 0, 0, 0); }
    last = res == 1 ? 5 : res == 2 ? -1 : res == 3 ? 4 : res == 4 ? Math.Min(spread, 4) - 1 : -1;
    SetPhase(RESULT);
  }

  public override void Tick(int held_, int pressed) {
    bool auto = demo;
    t++; pt++; if (msgT > 0) msgT--;
    bool up = !auto && (held_ & Buttons.Up) != 0, down = !auto && (held_ & Buttons.Down) != 0,
      left = !auto && (held_ & Buttons.Left) != 0, right = !auto && (held_ & Buttons.Right) != 0,
      a = !auto && (held_ & Buttons.A) != 0, b = !auto && (held_ & Buttons.B) != 0;
    bool eUp = up && !pUp, eDown = down && !pDown, eLeft = left && !pLeft, eRight = right && !pRight, eA = a && !pA;
    pUp = up; pDown = down; pLeft = left; pRight = right; pA = a; pB = b;
    auto_ = auto;

    switch (phase) {
      case TITLE:
        if (eA || (auto && pt > 40)) ToBet();
        break;
      case BET: {
        int top = Math.Min(20, Chips.Balance());
        if (auto) {
          if (pt % 3 == 0) { if (bet < target) bet++; else if (bet > target) bet--; }
          if (pt > 45 && bet == target) Deal();
        } else {
          int old = bet;
          if (eRight) bet = Math.Min(top, bet + 1);
          if (eLeft) bet = Math.Max(1, bet - 1);
          if (eUp) bet = Math.Min(top, bet + 5);
          if (eDown) bet = Math.Max(1, bet - 5);
          if (bet != old) Beeper.Tone(bet > old ? 1500 : 1200, 10, 25);
          if (eA) Deal();
        }
        break;
      }
      case DEAL:
        if (pt > 16) {
          if (spread == 0) Judge();
          else if (spread < 0) Third(PAIR);
          else SetPhase(SPREAD);
        }
        break;
      case SPREAD: {
        bool can = Chips.Balance() >= bet;
        if (auto) {
          if (pt > 40) {
            if (spread >= 7 && can) Raise();
            Third(THIRD);
          }
        } else if (eUp && can) { Raise(); Beeper.Tone(1800, 30, 35); Third(THIRD); }
        else if (eA) Third(THIRD);
        break;
      }
      case PAIR:
        if (pt > 30) Judge();
        break;
      case THIRD:
        if (pt > 22) Judge();
        break;
      case RESULT:
        if ((res == 3 || res == 4) && !auto && pt <= 13 && (pt - 1) % 4 == 0) Beeper.Tone(JINGLE[(pt - 1) / 4], 90, 40);
        if (res == 5 && !auto && pt == 1) Beeper.Tone(300, 120, 30);
        if ((eA && pt > 10) || (auto && pt > 75)) {
          if (raised) bet = bet >> 1;
          ToBet();
        }
        break;
      case BROKE:
        break;
    }
    if (!auto && (phase == DEAL || phase == PAIR || phase == THIRD)) {   // a click as each card turns over
      for (int i = 0; i < 3; i++) if (flip[i] >= 0 && pt == flip[i] + 2 && (i < 2) == (phase == DEAL)) Beeper.Tone(2400, 6, 20);
    }
  }

  // ---- drawing ----
  void Px(int x, int y, bool v) { if (x >= 0 && x < 64 && y >= 0 && y < 64) sc.Set(x, y, v); }
  void Fill(int x, int y, int w, int h, bool v) { sc.Fill(x, y, w, h, v); }
  void Text(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int f = FONTCH.IndexOf(s[n]); if (f < 0) f = FONTCH.Length - 1;
      int g = FONT[f];
      for (int r = 0; r < 5; r++) {
        int row = (g >> (12 - r * 3)) & 7;
        if (row == 0) continue;
        if ((row & 4) != 0) Px(x, y + r, v); if ((row & 2) != 0) Px(x + 1, y + r, v); if ((row & 1) != 0) Px(x + 2, y + r, v);
      }
      x += 4;
    }
  }
  void Big(int x, int y, string s) {
    for (int n = 0; n < s.Length; n++) {
      int f = FONTCH.IndexOf(s[n]); if (f < 0) f = FONTCH.Length - 1;
      int g = FONT[f];
      for (int r = 0; r < 5; r++) for (int q = 0; q < 3; q++) if (((g >> (14 - r * 3 - q)) & 1) != 0) sc.Fill(x + q * 2, y + r * 2, 2, 2, true);
      x += 8;
    }
  }
  void Centre(int y, string s) { Text(32 - ((s.Length * 4 - 1) >> 1), y, s, true); }
  void BigCentre(int y, string s) { Big(32 - ((s.Length * 8 - 2) >> 1), y, s); }
  void Right(int y, string s) { Text(64 - s.Length * 4, y, s, true); }
  void Sprite(int x, int y, int[] rows, int w, bool v) {
    for (int r = 0; r < rows.Length; r++) for (int q = 0; q < w; q++) if (((rows[r] >> (w - 1 - q)) & 1) != 0) Px(x + q, y + r, v);
  }
  static int[] Suit(int s) { return s == 0 ? SUIT0 : s == 1 ? SUIT1 : s == 2 ? SUIT2 : SUIT3; }
  void Card(int x, int y, int cd, bool face, int w) {
    int x0 = x + ((12 - w) >> 1);
    Fill(x0, y + 1, w, 18, true); Fill(x0 + 1, y, w - 2, 1, true); Fill(x0 + 1, y + 19, w - 2, 1, true);
    if (!face) {
      Fill(x0 + 1, y + 1, w - 2, 18, false);
      for (int j = 2; j < 18; j++) for (int i = 2; i < w - 2; i++) if ((i + j) % 4 == 0 || (i - j + 40) % 4 == 0) Px(x0 + i, y + j, true);
    }
    if (!face || w < 12) return;
    int r = cd % 13;
    if (r == 8) { Text(x + 1, y + 2, "1", false); Text(x + 5, y + 2, "0", false); } else Text(x + 2, y + 2, RANKS[r], false);
    Sprite(x + 3, y + 10, Suit(cd / 13), 7, false);
  }
  void DrawCards() {
    for (int i = 0; i < 3; i++) {
      bool face = true; int w = 12;
      bool anim = i < 2 ? phase == DEAL : phase == PAIR || phase == THIRD;
      if (i == 2 && !has3) face = false;
      else if (anim) {
        int f = pt - flip[i];
        if (f < 0) face = false;
        else if (f < 2) { face = false; w = f == 0 ? 8 : 4; }
        else if (f < 4) w = f == 2 ? 4 : 8;
      }
      Card(CX[i], CY, c[i], face, w);
    }
  }
  void DrawTop() {
    Sprite(1, 1, CHIP, 5, true); Text(8, 1, Chips.Balance().ToString(), true);
    Right(1, (raised ? "RAISED " : "BET ") + bet.ToString());
  }
  void DrawTable() {
    for (int k = 0; k < 6; k++) {
      bool lit = k == last; int y = 1 + k * 6 + (lit ? 1 : last >= 0 && k > last ? 2 : 0);
      if (lit) Fill(0, y - 1, 64, 7, true);
      Text(1, y, ROWS[k], !lit); Text(64 - RODDS[k].Length * 4, y, RODDS[k], !lit);
    }
    Fill(0, 39, 64, 1, true);
    Text(1, 45, "BET", true);
    string s = bet.ToString();
    Big(40 - ((s.Length * 8 - 2) >> 1), 42, s);
    Text(24, 45, "<", true); Text(53, 45, ">", true);
    Sprite(1, 58, CHIP, 5, true); Text(8, 58, Chips.Balance().ToString(), true);
    if (((t >> 4) & 1) != 0) Right(58, "A: DEAL");
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      BigCentre(2, "RED DOG");
      Card(10, 16, 3, true, 12); Card(26, 16, 0, false, 12); Card(42, 16, 37, true, 12);
      if (phase == BROKE) {
        Centre(41, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Centre(49, "MORE TOMORROW");
        Sprite(26, 57, CHIP, 5, true); Text(33, 57, "0", true);
        return;
      }
      Centre(40, "ACEY-DEUCEY");
      Centre(48, msgT > 0 ? msg : "6 DECK SHOE");
      if (((t >> 4) & 1) != 0) Centre(57, "A: START");
      return;
    }
    if (phase == BET) { DrawTable(); return; }
    DrawTop();
    DrawCards();
    int p = phase;
    if (p == DEAL) {
      if (fresh) Centre(36, "NEW SHOE");
    } else if (p == SPREAD || p == THIRD) {
      Centre(34, "SPREAD " + spread.ToString() + " PAYS");
      BigCentre(41, ODDS[Math.Min(spread, 4)]);
      if (p == SPREAD && !auto_) Centre(57, Chips.Balance() >= bet ? "^ RAISE  A CALL" : "A: CALL");
    } else if (p == PAIR) {
      Centre(34, "PAIR: TRIPS PAY");
      BigCentre(41, "11:1");
    } else if (p == RESULT) {
      Centre(34, LABELS[res]);
      string big3 = res == 1 || res == 2 ? "PUSH" : res == 5 ? "LOSE" : "WIN " + win.ToString();
      if (res < 3 || res == 5 || pt > 30 || ((pt >> 2) & 1) == 0) BigCentre(41, big3);
      if (!auto_ && ((t >> 4) & 1) != 0) Centre(57, "A: NEXT");
    }
  }
}
