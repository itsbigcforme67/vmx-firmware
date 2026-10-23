// VIDEO POKER for the VMX: a line-for-line port of microgames/ui/games/poker_core.js to C# 1.0, so it plays exactly
// the same (same random numbers, same order of events); tools/parity checks that frame by frame.
// Jacks or Better, one 52-card deck shuffled every hand, the "full pay" 9/6 table (99.54% return with perfect play at
// max bet; house edge 0.46%). Pays per chip, total returned including the stake: royal flush 250 (4000 for a 5-chip
// bet), straight flush 50, four of a kind 25, full house 9, flush 6, straight 4, three of a kind 3, two pair 2, jacks
// or better 1. Up/Down set the bet (1-5), A deals; Left/Right move the cursor, A holds / un-holds, B draws; A after
// a hand goes back to the bet screen (the pay table).
// VMX CASINO: the stake is taken with Chips.Bet when the cards are dealt, the payout paid with Chips.Pay.
using System;
using Microsoft.Vmx;

public class Poker : Game {
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
  static readonly int[] PAY = { 0, 1, 2, 3, 4, 6, 9, 25, 50, 250 };
  static readonly string[] NAMES = { "", "JACKS OR BETTER", "TWO PAIR", "THREE OF A KIND", "STRAIGHT", "FLUSH", "FULL HOUSE",
    "FOUR OF A KIND", "STRAIGHT FLUSH", "ROYAL FLUSH" };
  static readonly string[] ROWS = { "ROYAL FLUSH", "STR FLUSH", "4 OF A KIND", "FULL HOUSE", "FLUSH", "STRAIGHT", "3 OF A KIND",
    "TWO PAIR", "J OR BETTER" };
  const int CY = 22;
  const int TITLE = 0, BET = 1, DEAL = 2, HOLD = 3, DRAW = 4, RESULT = 5, BROKE = 6;
  static readonly int[] JINGLE = { 784, 988, 1175, 1568 };

  // game state (the JS object g)
  uint rs; int t, phase, pt, bet, next, nflip, cur, plan, target, rank, win, last, hands, msgT, best;
  int[] deck = new int[52], hand = new int[5], flip = new int[5], cnt = new int[13];
  bool[] held = new bool[5];
  string msg = "";
  bool auto_, demo, pUp, pDown, pLeft, pRight, pA, pB;
  Screen sc;

  public override bool UsesB { get { return true; } }

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; bet = 5;
    for (int i = 0; i < 52; i++) deck[i] = 0;
    for (int i = 0; i < 5; i++) { hand[i] = 0; held[i] = false; flip[i] = -1; }
    next = 5; nflip = 0; cur = 0; plan = 0; target = 5; rank = 0; win = 0; last = 0; hands = 0; best = 0;
    msg = ""; msgT = 0; auto_ = false; pUp = pDown = pLeft = pRight = pA = pB = false;
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 90; }
  void ToBet() {
    int c = Chips.Balance();
    if (c < 1) { SetPhase(BROKE); return; }
    bet = Math.Max(1, Math.Min(bet, Math.Min(5, c)));
    SetPhase(BET);
    if (auto_) { target = 5; if (Rnd() % 4 == 0) target = 1 + Rnd() % 4; target = Math.Min(target, c); }
  }
  static int PayOf(int rank, int bet) { return rank == 9 && bet == 5 ? 4000 : PAY[rank] * bet; }

  // ---- judging ----
  int Evaluate() {
    for (int r = 0; r < 13; r++) cnt[r] = 0;
    int mask = 0; bool flush = true;
    for (int i = 0; i < 5; i++) { int r = hand[i] % 13; cnt[r]++; mask |= 1 << r; if (hand[i] / 13 != hand[0] / 13) flush = false; }
    int pairs = 0, trips = 0, quads = 0; bool high = false;
    for (int r = 0; r < 13; r++) {
      if (cnt[r] == 2) { pairs++; if (r >= 9) high = true; }
      else if (cnt[r] == 3) trips++;
      else if (cnt[r] == 4) quads++;
    }
    int low = 0; while (((mask >> low) & 1) == 0) low++;
    bool straight = (mask >> low) == 31 || mask == 4111;
    if (straight && flush) return mask == 7936 ? 9 : 8;
    if (quads != 0) return 7;
    if (trips != 0 && pairs != 0) return 6;
    if (flush) return 5;
    if (straight) return 4;
    if (trips != 0) return 3;
    if (pairs == 2) return 2;
    return high ? 1 : 0;
  }

  // ---- the demo player's holds ----
  int Four(int kind) {
    for (int skip = 4; skip >= 0; skip--) {
      int mask = 0, s0 = -1, lo = 99, hi = -1, loA = 99, hiA = -1; bool suitOk = true, royal = true;
      for (int i = 0; i < 5; i++) {
        if (i == skip) continue;
        int r = hand[i] % 13, s = hand[i] / 13;
        if (s0 < 0) s0 = s; else if (s != s0) suitOk = false;
        if (((mask >> r) & 1) != 0) mask |= 8192; mask |= 1 << r;
        if (r < 8) royal = false;
        if (r < lo) lo = r; if (r > hi) hi = r;
        int ra = r == 12 ? -1 : r; if (ra < loA) loA = ra; if (ra > hiA) hiA = ra;
      }
      bool distinct = (mask & 8192) == 0;
      bool ok = false;
      if (kind == 0) ok = suitOk && royal;
      else if (kind == 1) ok = suitOk && distinct && (hi - lo <= 4 || hiA - loA <= 4);
      else if (kind == 2) ok = suitOk;
      else ok = distinct && hi - lo == 3 && hi <= 11;
      if (ok) return 31 & ~(1 << skip);
    }
    return 0;
  }
  int KeepRanks(int n, int minR) {
    int m = 0;
    for (int i = 0; i < 5; i++) { int r = hand[i] % 13; if (cnt[r] == n && r >= minR) m |= 1 << i; }
    return m;
  }
  int Plan() {
    int rk = Evaluate();
    if (rk >= 6) return 31;
    int m = Four(0); if (m != 0) return m;
    if (rk == 4 || rk == 5) return 31;
    for (int r = 0; r < 13; r++) cnt[r] = 0;
    for (int i = 0; i < 5; i++) cnt[hand[i] % 13]++;
    if (rk == 3) return KeepRanks(3, 0);
    if (rk == 2) return KeepRanks(2, 0);
    m = Four(1); if (m != 0) return m;
    if (rk == 1) return KeepRanks(2, 9);
    m = Four(2); if (m != 0) return m;
    m = KeepRanks(2, 0); if (m != 0) return m;
    m = Four(3); if (m != 0) return m;
    int a = -1, b = -1;
    for (int i = 0; i < 5; i++) for (int j = i + 1; j < 5; j++) {
      if (a < 0 && hand[i] % 13 >= 9 && hand[j] % 13 >= 9 && hand[i] / 13 == hand[j] / 13) { a = i; b = j; }
    }
    if (a >= 0) return (1 << a) | (1 << b);
    for (int k = 0; k < 2; k++) {
      int bst = -1;
      for (int i = 0; i < 5; i++) {
        int r = hand[i] % 13;
        if (r >= 9 && i != a && (bst < 0 || r < hand[bst] % 13)) bst = i;
      }
      if (bst < 0) break;
      if (a < 0) a = bst; else b = bst;
    }
    m = 0; if (a >= 0) m |= 1 << a; if (b >= 0) m |= 1 << b;
    return m;
  }

  // ---- the game ----
  void Deal() {
    Chips.Bet(bet);
    for (int i = 0; i < 52; i++) deck[i] = i;
    for (int i = 51; i > 0; i--) { int j = Rnd() % (i + 1), tt = deck[i]; deck[i] = deck[j]; deck[j] = tt; }
    for (int i = 0; i < 5; i++) { hand[i] = deck[i]; held[i] = false; flip[i] = 2 + i * 4; }
    next = 5; cur = 0; win = 0; rank = Evaluate();
    SetPhase(DEAL);
  }
  void Draw5() {
    int k = 0;
    for (int i = 0; i < 5; i++) {
      if (held[i]) flip[i] = -1;
      else { hand[i] = deck[next++]; flip[i] = 2 + k * 4; k++; }
    }
    nflip = k;
    SetPhase(DRAW);
  }
  void Judge() {
    rank = Evaluate(); win = PayOf(rank, bet); Chips.Pay(win); hands++;
    if (rank > 0) last = rank;
    if (!demo && win > best) { best = win; Scores.Report(best, hands, 0, 0); }
    SetPhase(RESULT);
  }

  public override void Tick(int held_, int pressed) {
    bool auto = demo;
    t++; pt++; if (msgT > 0) msgT--;
    bool up = !auto && (held_ & Buttons.Up) != 0, down = !auto && (held_ & Buttons.Down) != 0,
      left = !auto && (held_ & Buttons.Left) != 0, right = !auto && (held_ & Buttons.Right) != 0,
      a = !auto && (held_ & Buttons.A) != 0, b = !auto && (held_ & Buttons.B) != 0;
    bool eUp = up && !pUp, eDown = down && !pDown, eLeft = left && !pLeft, eRight = right && !pRight, eA = a && !pA, eB = b && !pB;
    pUp = up; pDown = down; pLeft = left; pRight = right; pA = a; pB = b;
    auto_ = auto;

    switch (phase) {
      case TITLE:
        if (eA || (auto && pt > 40)) ToBet();
        break;
      case BET: {
        int top = Math.Min(5, Chips.Balance());
        if (auto) {
          if (pt % 6 == 0) { if (bet < target) bet++; else if (bet > target) bet--; }
          if (pt > 45 && bet == target) Deal();
        } else {
          if (eUp) { bet = Math.Min(top, bet + 1); Beeper.Tone(1500, 10, 25); }
          if (eDown) { bet = Math.Max(1, bet - 1); Beeper.Tone(1200, 10, 25); }
          if (eA) Deal();
        }
        break;
      }
      case DEAL:
        if (pt > 22) SetPhase(HOLD);
        break;
      case HOLD:
        if (auto) {
          if (pt == 1) plan = Plan();
          if (pt % 7 == 0) {
            bool want = ((plan >> cur) & 1) == 1;
            if (held[cur] != want) held[cur] = want;
            else if (cur < 4) cur++;
            else Draw5();
          }
        } else {
          if (eLeft) cur = (cur + 4) % 5;
          if (eRight) cur = (cur + 1) % 5;
          if (eA) { held[cur] = !held[cur]; Beeper.Tone(held[cur] ? 1800 : 900, 15, 30); }
          else if (eB) Draw5();
        }
        break;
      case DRAW:
        if (pt > 6 + nflip * 4) Judge();
        break;
      case RESULT:
        if (win > 0 && !auto && pt <= 13 && (pt - 1) % 4 == 0) Beeper.Tone(JINGLE[(pt - 1) / 4], 90, 40);
        if ((eA && pt > 10) || (auto && pt > 75)) ToBet();
        break;
      case BROKE:
        break;
    }
    if ((phase == DEAL || phase == DRAW) && !auto) {           // a click as each card turns over
      for (int i = 0; i < 5; i++) if (flip[i] >= 0 && pt == flip[i] + 2) Beeper.Tone(2400, 6, 20);
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
  void Right(int y, string s) { Text(64 - s.Length * 4, y, s, true); }
  void Sprite(int x, int y, int[] rows, int w, bool v) {
    for (int r = 0; r < rows.Length; r++) for (int q = 0; q < w; q++) if (((rows[r] >> (w - 1 - q)) & 1) != 0) Px(x + q, y + r, v);
  }
  static int[] Suit(int s) { return s == 0 ? SUIT0 : s == 1 ? SUIT1 : s == 2 ? SUIT2 : SUIT3; }
  void Card(int x, int y, int c, bool face, int w) {
    int x0 = x + ((12 - w) >> 1);
    Fill(x0, y + 1, w, 18, true); Fill(x0 + 1, y, w - 2, 1, true); Fill(x0 + 1, y + 19, w - 2, 1, true);
    if (!face) {
      Fill(x0 + 1, y + 1, w - 2, 18, false);
      for (int j = 2; j < 18; j++) for (int i = 2; i < w - 2; i++) if ((i + j) % 4 == 0 || (i - j + 40) % 4 == 0) Px(x0 + i, y + j, true);
    }
    if (!face || w < 12) return;
    int r = c % 13;
    if (r == 8) { Text(x + 1, y + 2, "1", false); Text(x + 5, y + 2, "0", false); } else Text(x + 2, y + 2, RANKS[r], false);
    Sprite(x + 3, y + 10, Suit(c / 13), 7, false);
  }
  void DrawCards() {
    bool anim = phase == DEAL || phase == DRAW;
    for (int i = 0; i < 5; i++) {
      int x = i * 13, w = 12; bool face = true;
      if (anim && flip[i] >= 0) {
        int f = pt - flip[i];
        if (f < 0) face = false;
        else if (f < 2) { face = false; w = f == 0 ? 8 : 4; }
        else if (f < 4) w = f == 2 ? 4 : 8;
      }
      Card(x, CY, hand[i], face, w);
      if (held[i] && phase != DEAL) Text(x + 1, CY - 7, "HLD", true);
    }
  }
  void DrawTop() {
    Sprite(1, 1, CHIP, 5, true); Text(8, 1, Chips.Balance().ToString(), true);
    Right(1, "BET " + bet.ToString());
  }
  void DrawTable() {
    int litK = last > 0 ? 9 - last : -1;
    for (int k = 0; k < 9; k++) {
      int rk = 9 - k, y = 1 + k * 6 + (k == litK ? 1 : litK >= 0 && k > litK ? 2 : 0); string s = PayOf(rk, bet).ToString(); bool lit = k == litK;
      if (lit) Fill(0, y - 1, 64, 7, true);
      Text(1, y, ROWS[k], !lit); Text(64 - s.Length * 4, y, s, !lit);
    }
    Fill(0, 57, 64, 1, true);
    Sprite(1, 59, CHIP, 5, true); Text(8, 59, Chips.Balance().ToString(), true);
    Right(59, ((t >> 4) & 1) != 0 ? "A: DEAL" : "^BET " + bet.ToString());
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      Big(13, 2, "VIDEO"); Big(13, 14, "POKER");
      for (int s = 0; s < 4; s++) Sprite(9 + s * 13, 27, Suit(s), 7, true);
      if (phase == BROKE) {
        Centre(40, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Centre(48, "MORE TOMORROW");
        Sprite(23, 57, CHIP, 5, true); Text(30, 57, "0", true);
        return;
      }
      Centre(38, "JACKS OR BETTER");
      Centre(46, msgT > 0 ? msg : "9/6 FULL PAY");
      if (((t >> 4) & 1) != 0) Centre(56, "A: START");
      return;
    }
    if (phase == BET) { DrawTable(); return; }
    DrawTop();
    DrawCards();
    if (phase == HOLD) {
      int x = cur * 13;
      Fill(x + 5, CY + 22, 2, 1, true); Fill(x + 4, CY + 23, 4, 1, true); Fill(x + 3, CY + 24, 6, 1, true);
      if (rank > 0) Centre(8, NAMES[rank]);
      Centre(58, "A HOLD  B DRAW");
    } else if (phase == RESULT) {
      if (rank > 0) {
        if (pt > 30 || ((pt >> 2) & 1) == 0) Centre(8, NAMES[rank]);
        Centre(49, "WIN " + win.ToString());
      } else Centre(49, "NO WIN");
      if (!auto_ && ((t >> 4) & 1) != 0) Centre(58, "A: NEXT");
    }
  }
}
