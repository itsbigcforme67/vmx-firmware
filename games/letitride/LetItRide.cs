// LET IT RIDE for the VMX: a line-for-line port of microgames/ui/games/letitride_core.js to C# 1.0, so it plays
// exactly the same (same random numbers, same order of events); tools/parity checks that frame by frame.
// One 52-card deck shuffled every hand. Three equal bets (1-10 chips each), 3 cards to the player, 2 community cards
// face down. After 3 cards: B pulls bet 1 back, A lets it ride; the first community card turns; B pulls bet 2 back,
// A lets it ride (bet 3 always stays); the second turns and every bet still up is paid: royal flush 1000:1, straight
// flush 200:1, four of a kind 50:1, full house 11:1, flush 8:1, straight 5:1, three of a kind 3:1, two pair 2:1,
// tens or better 1:1. No payout cap. House edge with the best play (which the demo plays): 3.51% of one bet
// (3.5057%; 2.86% of the chips left riding), enumerated exactly by microgames/tools/letitride_odds.py.
// Left/Right set the bet, A deals; A after a hand goes back to the bet screen (the pay table).
// VMX CASINO: all three bets are taken with Chips.Bet when the cards are dealt; a pulled bet comes back at once and
// the winnings (stakes included) are paid with Chips.Pay. Scores.Report(biggest single payout, 0, 0, 0).
using System;
using Microsoft.Vmx;

public class LetItRide : Game {
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
  static readonly int[] MULT = { 0, 1, 2, 3, 5, 8, 11, 50, 200, 1000 };
  static readonly string[] NAMES = { "", "TENS OR BETTER", "TWO PAIR", "THREE OF A KIND", "STRAIGHT", "FLUSH", "FULL HOUSE",
    "FOUR OF A KIND", "STRAIGHT FLUSH", "ROYAL FLUSH" };
  static readonly string[] ROWS = { "ROYAL", "STR FLUSH", "4 OF A KIND", "FULL HOUSE", "FLUSH", "STRAIGHT", "3 OF A KIND",
    "TWO PAIR", "PAIR 10-A" };
  static readonly string[] ODDS = { "1000:1", "200:1", "50:1", "11:1", "8:1", "5:1", "3:1", "2:1", "1:1" };
  const int CY = 7, RY = 29;
  const int TITLE = 0, BET = 1, DEAL = 2, D1 = 3, D2 = 4, TURN1 = 5, TURN2 = 6, RESULT = 7, BROKE = 8;
  static readonly int[] JINGLE = { 784, 988, 1175, 1568 };

  // game state (the JS object g)
  uint rs; int t, phase, pt, bet, rank, win, best, last, hands, target, msgT;
  int[] deck = new int[52], hand = new int[5], flip = new int[5], cnt = new int[13];
  bool[] face = new bool[5], up = new bool[3];
  string msg = "";
  bool auto_, demo, pLeft, pRight, pA, pB;
  Screen sc;

  public override bool UsesB { get { return true; } }

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; bet = 5;
    for (int i = 0; i < 52; i++) deck[i] = 0;
    for (int i = 0; i < 5; i++) { hand[i] = 0; flip[i] = -1; face[i] = false; }
    for (int i = 0; i < 3; i++) up[i] = true;
    rank = 0; win = 0; best = 0; last = 0; hands = 0; target = 5;
    msg = ""; msgT = 0; auto_ = false; pLeft = pRight = pA = pB = false;
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 90; }
  void ToBet() {
    int c = Chips.Balance();
    if (c < 3) { SetPhase(BROKE); return; }
    int top = Math.Min(10, c / 3);
    bet = Math.Max(1, Math.Min(bet, top));
    SetPhase(BET);
    if (auto_) { target = 1 + Rnd() % 10; if (Rnd() % 3 == 0) target = 5; target = Math.Min(target, top); }
  }

  // ---- judging ----
  int Evaluate() {
    for (int r = 0; r < 13; r++) cnt[r] = 0;
    int mask = 0; bool flush = true;
    for (int i = 0; i < 5; i++) { int r = hand[i] % 13; cnt[r]++; mask |= 1 << r; if (hand[i] / 13 != hand[0] / 13) flush = false; }
    int pairs = 0, trips = 0, quads = 0; bool high = false;
    for (int r = 0; r < 13; r++) {
      if (cnt[r] == 2) { pairs++; if (r >= 8) high = true; }
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

  // ---- the demo player: let it ride with the first n cards (3 or 4)? ----
  bool Ride(int n) {
    for (int r = 0; r < 13; r++) cnt[r] = 0;
    int mask = 0, high = 0, lo = 99, hi = -1, loA = 99, hiA = -1; bool suited = true;
    for (int i = 0; i < n; i++) {
      int r = hand[i] % 13; cnt[r]++; mask |= 1 << r;
      if (hand[i] / 13 != hand[0] / 13) suited = false;
      if (r >= 8) high++;
      if (r < lo) lo = r; if (r > hi) hi = r;
      int ra = r == 12 ? -1 : r; if (ra < loA) loA = ra; if (ra > hiA) hiA = ra;
    }
    int pairs = 0;
    for (int r = 0; r < 13; r++) {
      if (cnt[r] >= 3) return true;
      if (cnt[r] == 2) { if (r >= 8) return true; pairs++; }
    }
    if (pairs >= 2) return true;
    if (pairs > 0) return false;
    int span = Math.Min(hi - lo, hiA - loA);
    if (n == 3) {
      if (!suited) return false;
      if (high == 3) return true;
      if (hiA - loA < hi - lo) high--;
      if (span == 2) return mask != 7 && mask != 4099;
      if (span == 3) return high >= 1;
      if (span == 4) return high >= 2;
      return false;
    }
    if (suited) return true;
    return hi - lo == 3 && hi <= 11 && high >= 1;
  }

  // ---- the game ----
  void Deal() {
    Chips.Bet(bet * 3);
    for (int i = 0; i < 52; i++) deck[i] = i;
    for (int i = 51; i > 0; i--) { int j = Rnd() % (i + 1), tt = deck[i]; deck[i] = deck[j]; deck[j] = tt; }
    for (int i = 0; i < 5; i++) { hand[i] = deck[i]; flip[i] = i < 3 ? 2 + i * 4 : -1; face[i] = i < 3; }
    for (int i = 0; i < 3; i++) up[i] = true;
    win = 0; rank = 0;
    SetPhase(DEAL);
  }
  void Decide(int k, bool rideIt) {
    if (!rideIt) { up[k] = false; Chips.Pay(bet); }
    for (int i = 0; i < 5; i++) flip[i] = -1;
    flip[3 + k] = 2; face[3 + k] = true;
    SetPhase(k == 0 ? TURN1 : TURN2);
  }
  void Judge() {
    rank = Evaluate(); win = 0; hands++;
    if (rank > 0) {
      for (int i = 0; i < 3; i++) if (up[i]) win += bet * (MULT[rank] + 1);
      Chips.Pay(win); last = rank;
    }
    if (win > best) { best = win; if (!demo) Scores.Report(best, 0, 0, 0); }
    SetPhase(RESULT);
  }

  public override void Tick(int held_, int pressed) {
    bool auto = demo;
    t++; pt++; if (msgT > 0) msgT--;
    bool left = !auto && (held_ & Buttons.Left) != 0, right = !auto && (held_ & Buttons.Right) != 0,
      a = !auto && (held_ & Buttons.A) != 0, b = !auto && (held_ & Buttons.B) != 0;
    bool eLeft = left && !pLeft, eRight = right && !pRight, eA = a && !pA, eB = b && !pB;
    pLeft = left; pRight = right; pA = a; pB = b;
    auto_ = auto;

    switch (phase) {
      case TITLE:
        if (eA || (auto && pt > 40)) ToBet();
        break;
      case BET: {
        int top = Math.Min(10, Chips.Balance() / 3);
        if (auto) {
          if (pt % 5 == 0) { if (bet < target) bet++; else if (bet > target) bet--; }
          if (pt > 45 && bet == target) Deal();
        } else {
          if (eRight) { bet = Math.Min(top, bet + 1); Beeper.Tone(1500, 10, 25); }
          if (eLeft) { bet = Math.Max(1, bet - 1); Beeper.Tone(1200, 10, 25); }
          if (eA) Deal();
        }
        break;
      }
      case DEAL:
        if (pt > 16) SetPhase(D1);
        break;
      case D1:
      case D2: {
        int k = phase == D1 ? 0 : 1;
        if (auto) { if (pt > 30) Decide(k, Ride(3 + k)); }
        else if (eB) { Beeper.Tone(900, 15, 30); Decide(k, false); }
        else if (eA) { Beeper.Tone(1800, 15, 30); Decide(k, true); }
        break;
      }
      case TURN1:
        if (pt > 8) SetPhase(D2);
        break;
      case TURN2:
        if (pt > 8) Judge();
        break;
      case RESULT:
        if (win > 0 && !auto && pt <= 13 && (pt - 1) % 4 == 0) Beeper.Tone(JINGLE[(pt - 1) / 4], 90, 40);
        if ((eA && pt > 10) || (auto && pt > 75)) ToBet();
        break;
      case BROKE:
        break;
    }
    if ((phase == DEAL || phase == TURN1 || phase == TURN2) && !auto) {   // a click as each card turns over
      for (int i = 0; i < 5; i++) if (flip[i] >= 0 && pt == flip[i] + 2) Beeper.Tone(2400, 6, 20);
    }
  }

  // ---- drawing ----
  void Fill(int x, int y, int w, int h, bool v) { sc.Fill(x, y, w, h, v); }
  void Text(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int f = FONTCH.IndexOf(s[n]); if (f < 0) f = FONTCH.Length - 1;
      int g = FONT[f];
      for (int r = 0; r < 5; r++) {
        int row = (g >> (12 - r * 3)) & 7;
        if (row == 0) continue;
        if (row == 7) { sc.Fill(x, y + r, 3, 1, v); continue; }   // all our text is on screen: no bounds tests
        if ((row & 4) != 0) sc.Set(x, y + r, v); if ((row & 2) != 0) sc.Set(x + 1, y + r, v); if ((row & 1) != 0) sc.Set(x + 2, y + r, v);
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
    for (int r = 0; r < rows.Length; r++) { int bits = rows[r]; for (int q = w - 1; q >= 0 && bits != 0; q--, bits >>= 1) if ((bits & 1) != 0) sc.Set(x + q, y + r, v); }
  }
  static int[] Suit(int s) { return s == 0 ? SUIT0 : s == 1 ? SUIT1 : s == 2 ? SUIT2 : SUIT3; }
  void Card(int x, int y, int c, bool fc, int w) {
    int x0 = x + ((12 - w) >> 1);
    Fill(x0, y + 1, w, 18, true); Fill(x0 + 1, y, w - 2, 1, true); Fill(x0 + 1, y + 19, w - 2, 1, true);
    if (!fc) {
      Fill(x0 + 1, y + 1, w - 2, 18, false);
      for (int j = 2; j < 18; j++) {                     // the lattice: lit where i = -j or i = j (mod 4); stepped, not tested
        int a = (4 - (j & 3)) & 3, b = j & 3;
        for (int i = a < 2 ? a + 4 : a; i < w - 2; i += 4) sc.Set(x0 + i, y + j, true);
        if (b != a) for (int i = b < 2 ? b + 4 : b; i < w - 2; i += 4) sc.Set(x0 + i, y + j, true);
      }
    }
    if (!fc || w < 12) return;
    int r = c % 13;
    if (r == 8) { Text(x + 1, y + 2, "1", false); Text(x + 5, y + 2, "0", false); } else Text(x + 2, y + 2, RANKS[r], false);
    Sprite(x + 3, y + 10, Suit(c / 13), 7, false);
  }
  void DrawCards() {
    bool anim = phase == DEAL || phase == TURN1 || phase == TURN2;
    for (int i = 0; i < 5; i++) {
      int x = i * 13, w = 12; bool fc = face[i];
      if (fc && anim && flip[i] >= 0) {
        int f = pt - flip[i];
        if (f < 0) fc = false;
        else if (f < 2) { fc = false; w = f == 0 ? 8 : 4; }
        else if (f < 4) w = f == 2 ? 4 : 8;
      }
      Card(x, CY, hand[i], fc, w);
    }
  }
  void DrawRows() {
    int k = phase == D1 ? 0 : phase == D2 ? 1 : -1; bool res = phase == RESULT;
    for (int i = 0; i < 3; i++) {
      int y = RY + i * 8;
      if (i == k && ((t >> 3) & 1) != 0) Text(0, y + 1, ">", true);
      Fill(4, y, 7, 7, true); Text(6, y + 1, (i + 1).ToString(), false);
      Sprite(13, y + 1, CHIP, 5, true); Text(20, y + 1, bet.ToString(), true);
      string s = up[i] ? "UP" : "PULLED";
      if (i == k) s = ((t >> 3) & 1) != 0 ? "UP" : "";
      else if (res && up[i]) s = rank > 0 ? "WIN " + (bet * (MULT[rank] + 1)).ToString() : "LOST";
      Right(y + 1, s);
    }
  }
  void DrawTable() {
    int litK = last > 0 ? 9 - last : -1;
    for (int k = 0; k < 9; k++) {
      int y = 1 + k * 6 + (k == litK ? 1 : litK >= 0 && k > litK ? 2 : 0); bool lit = k == litK;
      if (lit) Fill(0, y - 1, 64, 7, true);
      Text(1, y, ROWS[k], !lit); Text(64 - ODDS[k].Length * 4, y, ODDS[k], !lit);
    }
    Fill(0, 57, 64, 1, true);
    Sprite(1, 59, CHIP, 5, true); Text(8, 59, Chips.Balance().ToString(), true);
    Right(59, ((t >> 4) & 1) != 0 ? "A: DEAL" : "<3X" + bet.ToString() + ">");
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      Big(9, 2, "LET IT"); Big(17, 14, "RIDE");
      for (int s = 0; s < 4; s++) Sprite(9 + s * 13, 27, Suit(s), 7, true);
      if (phase == BROKE) {
        Centre(40, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Centre(48, "MORE TOMORROW");
        Sprite(23, 57, CHIP, 5, true); Text(30, 57, Chips.Balance().ToString(), true);
        return;
      }
      Centre(38, "THREE BETS");
      Centre(46, msgT > 0 ? msg : "PULL TWO BACK");
      if (((t >> 4) & 1) != 0) Centre(56, "A: START");
      return;
    }
    if (phase == BET) { DrawTable(); return; }
    Sprite(1, 1, CHIP, 5, true); Text(8, 1, Chips.Balance().ToString(), true);
    Right(1, "3X" + bet.ToString());
    DrawCards();
    DrawRows();
    if (phase == D1 || phase == D2) {
      Centre(53, "B PULL BACK"); Centre(59, "A LET IT RIDE");
    } else if (phase == RESULT) {
      if (rank > 0) {
        if (pt > 30 || ((pt >> 2) & 1) == 0) Centre(53, NAMES[rank]);
        if (!auto_ && pt > 30 && ((t >> 4) & 1) != 0) Centre(59, "A: NEXT"); else Centre(59, "WIN " + win.ToString());
      } else {
        Centre(53, "NO WIN");
        if (!auto_ && ((t >> 4) & 1) != 0) Centre(59, "A: NEXT");
      }
    }
  }
}
