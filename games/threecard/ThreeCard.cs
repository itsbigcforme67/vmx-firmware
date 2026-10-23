// 3 CARD POKER for the VMX: a line-for-line port of microgames/ui/games/threecard_core.js to C# 1.0, so it plays
// exactly the same (same random numbers, same order of events); tools/parity checks that frame by frame.
// Standard Three Card Poker, one 52-card deck shuffled every hand. ANTE 1-10 and PAIR PLUS 0-10, taken at the deal;
// you see your 3 cards, then PLAY (a Play bet equal to the ante, if the stash covers it) or FOLD (ante lost, pair
// plus still settles). Dealer qualifies with queen-high or better: not qualified = ante 1:1, play push; qualified =
// better hand wins ante and play 1:1, worse loses both, tie pushes. ANTE BONUS when you play: straight flush 5:1,
// trips 4:1, straight 1:1. PAIR PLUS: straight flush 40:1, trips 30:1, straight 6:1, flush 4:1, pair 1:1.
// House edge (enumerated): ante 3.37% with the Q-6-4 rule; pair plus 2.32%.
// Left/Right set the ante, Up/Down the pair plus, A deals; A plays, B folds; A after a hand goes back to the bets.
// VMX CASINO: ante + pair plus taken with Chips.Bet at the deal, the Play bet when you play, winnings with Chips.Pay.
using System;
using Microsoft.Vmx;

public class ThreeCard : Game {
  const string FONTCH = "ABCDEFGHIJKLMNOPQRSTUVWXY0123456789:!-<>+/^' ";
  static readonly int[] FONT = {   // the core's 3x5 glyph strings as 15-bit numbers, bit 14 = top left
    11245, 27566, 14627, 27502, 31143, 31140, 14699, 23533, 29847, 4714, 23469, 18727,
    24557, 27501, 11114, 27556, 11123, 27565, 14478, 29842, 23407, 23402, 23549, 23213,
    23186, 31599, 11415, 25255, 25230, 23497, 31118, 14831, 29330, 31727, 31694, 1040,
    9346, 448, 5393, 17492, 1488, 4772, 11834, 9216, 0 };
  static readonly int[] SUIT0 = { 8, 28, 62, 127, 127, 42, 28 }, SUIT1 = { 54, 73, 65, 65, 34, 20, 8 },
    SUIT2 = { 8, 20, 34, 65, 34, 20, 8 }, SUIT3 = { 28, 28, 107, 127, 107, 8, 28 };
  static readonly int[] CHIP = { 14, 21, 27, 21, 14 };
  static readonly string[] RANKS = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };
  static readonly string[] HIGHN = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "JACK", "QUEEN", "KING", "ACE" };
  static readonly string[] PAIRN = { "TWOS", "THREES", "FOURS", "FIVES", "SIXES", "SEVENS", "EIGHTS", "NINES", "TENS",
    "JACKS", "QUEENS", "KINGS", "ACES" };
  static readonly string[] CATN = { "", "", "FLUSH", "STRAIGHT", "THREE OF A KIND", "STRAIGHT FLUSH" };
  static readonly int[] PPAY = { 0, 1, 4, 6, 30, 40 };
  static readonly int[] ABON = { 0, 0, 0, 1, 4, 5 };
  static readonly string[] ROWS = { "STRFL", "TRIPS", "STR", "FLUSH", "PAIR" };
  const int QUAL = 10 * 169, Q64 = 10 * 169 + 4 * 13 + 2;
  const int DY = -2, PY = 47, LX = 41;
  const int TITLE = 0, BET = 1, DEAL = 2, DECIDE = 3, REVEAL = 4, RESULT = 5, BROKE = 6;
  static readonly int[] JINGLE = { 784, 988, 1175, 1568 };

  // game state (the JS object g)
  uint rs; int t, phase, pt, ante, pp, play, ps, ds, outcome, rA, rP, rPP, bonus, win, net, last, hands, tA, tP, msgT, best;
  int[] deck = new int[52], cards = new int[6], flip = new int[6];
  bool fold, qual;
  string msg = "";
  bool auto_, demo, pUp, pDown, pLeft, pRight, pA, pB;
  Screen sc;

  public override bool UsesB { get { return true; } }

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; ante = 5; pp = 0; play = 0;
    for (int i = 0; i < 52; i++) deck[i] = 0;
    for (int i = 0; i < 6; i++) { cards[i] = 0; flip[i] = -1; }
    ps = 0; ds = 0; fold = false; qual = false; outcome = 0; rA = 0; rP = 0; rPP = 0; bonus = 0; win = 0; net = 0;
    last = 0; hands = 0; tA = 5; tP = 0; best = 0;
    msg = ""; msgT = 0; auto_ = false; pUp = pDown = pLeft = pRight = pA = pB = false;
    int b = Chips.Bonus(); if (b > 0) Say("DAILY CHIPS +" + b.ToString());
  }
  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 90; }
  void ClampPP() { pp = Math.Max(0, Math.Min(pp, Math.Min(10, Chips.Balance() - ante))); }
  void ToBet() {
    int c = Chips.Balance();
    if (c < 1) { SetPhase(BROKE); return; }
    ante = Math.Max(1, Math.Min(ante, Math.Min(10, c))); ClampPP();
    SetPhase(BET);
    if (auto_) {
      tA = 1 + Rnd() % 10; tP = 0; if (Rnd() % 3 == 0) tP = 1 + Rnd() % 5;
      tA = Math.Min(tA, Math.Min(10, c)); tP = Math.Max(0, Math.Min(tP, Math.Min(10, c - tA)));
    }
  }

  // ---- judging ----
  static int Score(int c0, int c1, int c2) {
    int a = c0 % 13, b = c1 % 13, c = c2 % 13, t;
    if (a < b) { t = a; a = b; b = t; }
    if (b < c) { t = b; b = c; c = t; }
    if (a < b) { t = a; a = b; b = t; }
    bool fl = c0 / 13 == c1 / 13 && c1 / 13 == c2 / 13;
    bool wheel = a == 12 && b == 1 && c == 0;
    bool st = (a == b + 1 && b == c + 1) || wheel; int top = wheel ? 1 : a;
    if (st && fl) return 5 * 2197 + top * 169;
    if (a == c) return 4 * 2197 + a * 169;
    if (st) return 3 * 2197 + top * 169;
    if (fl) return 2 * 2197 + a * 169 + b * 13 + c;
    if (a == b) return 2197 + a * 169 + c * 13;
    if (b == c) return 2197 + b * 169 + a * 13;
    return a * 169 + b * 13 + c;
  }
  static string HandName(int s) {
    int cat = s / 2197, r = (s % 2197) / 169;
    if (cat == 0) return HIGHN[r] + " HIGH";
    if (cat == 1) return "PAIR OF " + PAIRN[r];
    return CATN[cat];
  }

  // ---- the game ----
  void Deal() {
    Chips.Bet(ante + pp);
    for (int i = 0; i < 52; i++) deck[i] = i;
    for (int i = 51; i > 0; i--) { int j = Rnd() % (i + 1), tt = deck[i]; deck[i] = deck[j]; deck[j] = tt; }
    for (int i = 0; i < 6; i++) { cards[i] = deck[i]; flip[i] = i < 3 ? 2 + i * 4 : -1; }
    ps = Score(cards[0], cards[1], cards[2]); ds = Score(cards[3], cards[4], cards[5]);
    play = 0; fold = false; win = 0; net = 0; rA = 0; rP = 0; rPP = 0; bonus = 0;
    SetPhase(DEAL);
  }
  void Decide(bool doPlay) {
    if (doPlay) { play = ante; Chips.Bet(play); } else fold = true;
    for (int i = 0; i < 6; i++) flip[i] = i < 3 ? -1 : 2 + (i - 3) * 4;
    SetPhase(REVEAL);
  }
  void Judge() {
    int cat = ps / 2197;
    qual = ds >= QUAL;
    if (pp > 0 && cat >= 1) rPP = pp * (PPAY[cat] + 1);
    if (fold) outcome = 0;
    else {
      bonus = ante * ABON[cat];
      if (!qual) { outcome = 4; rA = ante * 2; rP = play; }
      else if (ps > ds) { outcome = 1; rA = ante * 2; rP = play * 2; }
      else if (ps == ds) { outcome = 3; rA = ante; rP = play; }
      else outcome = 2;
      rA += bonus;
    }
    win = rA + rP + rPP; net = win - ante - play - pp;
    Chips.Pay(win); hands++; last = cat;
    if (!demo && win > best) { best = win; Scores.Report(best, 0, 0, 0); }
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
        int c = Chips.Balance();
        if (auto) {
          if (pt % 6 == 0) {
            if (ante < tA) ante++; else if (ante > tA) ante--;
            else if (pp < tP) pp++; else if (pp > tP) pp--;
            ClampPP();
          }
          if (pt > 45 && ante == tA && pp == tP) Deal();
        } else {
          if (eRight) { ante = Math.Min(Math.Min(10, c), ante + 1); Beeper.Tone(1500, 10, 25); }
          if (eLeft) { ante = Math.Max(1, ante - 1); Beeper.Tone(1200, 10, 25); }
          ClampPP();
          if (eUp) { pp = Math.Min(Math.Min(10, c - ante), pp + 1); Beeper.Tone(1700, 10, 25); }
          if (eDown) { pp = Math.Max(0, pp - 1); Beeper.Tone(1000, 10, 25); }
          if (eA) Deal();
        }
        break;
      }
      case DEAL:
        if (pt > 16) SetPhase(DECIDE);
        break;
      case DECIDE: {
        bool can = Chips.Balance() >= ante;
        if (auto) { if (pt > 30) Decide(can && ps >= Q64); }
        else if (eA && can) { Beeper.Tone(1600, 30, 30); Decide(true); }
        else if (eB) { Beeper.Tone(500, 60, 30); Decide(false); }
        break;
      }
      case REVEAL:
        if (pt > 18) Judge();
        break;
      case RESULT:
        if (net > 0 && !auto && pt <= 13 && (pt - 1) % 4 == 0) Beeper.Tone(JINGLE[(pt - 1) / 4], 90, 40);
        if ((eA && pt > 10) || (auto && pt > 100)) ToBet();
        break;
      case BROKE:
        break;
    }
    if ((phase == DEAL || phase == REVEAL) && !auto) {           // a click as each card turns over
      for (int i = 0; i < 6; i++) if (flip[i] >= 0 && pt == flip[i] + 2) Beeper.Tone(2400, 6, 20);
    }
  }

  // ---- drawing ----
  void Px(int x, int y, bool v) { if (x >= 0 && x < 64 && y >= 0 && y < 64) sc.Set(x, y, v); }
  void Fill(int x, int y, int w, int h, bool v) {             // clipped to the screen (cards bleed off the edges)
    if (y < 0) { h += y; y = 0; }
    if (y + h > 64) h = 64 - y;
    if (h > 0 && w > 0) sc.Fill(x, y, w, h, v);
  }
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
    bool anim = phase == DEAL || phase == REVEAL;
    for (int i = 0; i < 6; i++) {
      int x = 1 + (i % 3) * 13, y = i < 3 ? PY : DY, w = 12;
      bool face = i < 3 || phase == REVEAL || phase == RESULT;
      if (anim && flip[i] >= 0) {
        int f = pt - flip[i];
        if (f < 0) face = false;
        else if (f < 2) { face = false; w = f == 0 ? 8 : 4; }
        else if (f < 4) w = f == 2 ? 4 : 8;
      }
      Card(x, y, cards[i], face, w);
    }
  }
  void Stash(int x, int y, int c) {
    string s = c.ToString();
    if (s.Length > 4) Text(64 - s.Length * 4, y, s, true);
    else { Sprite(x, y, CHIP, 5, true); Text(x + 7, y, s, true); }
  }
  static string Signed(int v) { return (v > 0 ? "+" : "") + v.ToString(); }
  void BetRow(int y, string label, string val) { Text(LX, y, label, true); Right(y, val); }
  void DrawBets() {
    bool res = phase == RESULT;
    BetRow(47, "AN", res ? Signed(rA - ante) : ante.ToString());
    if (fold) Text(LX, 53, "FOLD", true);
    else if (play > 0) BetRow(53, "PL", res ? Signed(rP - play) : play.ToString());
    if (pp > 0) BetRow(59, "PP", res ? Signed(rPP - pp) : pp.ToString());
  }
  void Box(string s1, string s2) {
    Fill(0, 25, 64, 15, true);
    if (s2 == "") Text(32 - ((s1.Length * 4 - 1) >> 1), 30, s1, false);
    else { Text(32 - ((s1.Length * 4 - 1) >> 1), 27, s1, false); Text(32 - ((s2.Length * 4 - 1) >> 1), 33, s2, false); }
  }
  void DrawTable() {
    Text(22, 1, "PAIR+", true); Text(44, 1, "BONUS", true);
    int litK = last > 0 ? 5 - last : -1;
    for (int k = 0; k < 5; k++) {
      int cat = 5 - k, y = 8 + k * 6; bool lit = k == litK;
      if (lit) Fill(0, y - 1, 64, 7, true);
      Text(1, y, ROWS[k], !lit);
      string p = PPAY[cat].ToString(); Text(42 - p.Length * 4, y, p, !lit);
      string q = ABON[cat] > 0 ? ABON[cat].ToString() : "-"; Text(64 - q.Length * 4, y, q, !lit);
    }
    Fill(0, 38, 64, 1, true);
    Text(1, 41, "<> ANTE", true); Right(41, ante.ToString());
    Text(1, 47, "^ PAIR+", true); Right(47, pp.ToString());
    Fill(0, 54, 64, 1, true);
    Stash(1, 58, Chips.Balance());
    if (((t >> 4) & 1) != 0) Right(58, "A: DEAL");
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      Big(8, 2, "3 CARD"); Big(13, 14, "POKER");
      for (int s = 0; s < 4; s++) Sprite(9 + s * 13, 27, Suit(s), 7, true);
      if (phase == BROKE) {
        Centre(40, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Centre(48, "MORE TOMORROW");
        Sprite(23, 57, CHIP, 5, true); Text(30, 57, Chips.Balance().ToString(), true);
        return;
      }
      Centre(38, "ANTE + PAIR PLUS");
      Centre(46, msgT > 0 ? msg : "PLAY OR FOLD");
      if (((t >> 4) & 1) != 0) Centre(56, "A: START");
      return;
    }
    if (phase == BET) { DrawTable(); return; }
    DrawCards();
    Stash(LX, 1, Chips.Balance());
    Text(LX, 12, "DEALER", true);
    DrawBets();
    if (phase != DEAL) Centre(41, HandName(ps));
    if (phase == DECIDE) {
      if (Chips.Balance() >= ante) Box("A: PLAY " + ante.ToString(), "B: FOLD");
      else Box("NO CHIPS TO PLAY", ((t >> 4) & 1) != 0 ? "B: FOLD" : " ");
    } else if (phase == REVEAL) Box(fold ? "YOU FOLD" : "YOU PLAY " + play.ToString(), "");
    else if (phase == RESULT) {
      Centre(19, HandName(ds));
      if (pt < 50) {
        if (outcome == 0) Box("YOU FOLD", "ANTE LOST");
        else if (outcome == 4) Box("DEALER DOESN'T", "QUALIFY");
        else Box("DEALER QUALIFIES", ((pt >> 2) & 1) == 0 || pt > 20 ? (outcome == 1 ? "YOU WIN" : outcome == 2 ? "DEALER WINS" : "PUSH") : " ");
      } else {
        bool hint = !auto_ && ((t >> 4) & 1) == 1;
        Box(net > 0 ? "WIN " + net.ToString() : net < 0 ? "LOSE " + (-net).ToString() : "EVEN", hint ? "A: NEXT" : bonus > 0 ? "ANTE BONUS " + bonus.ToString() : " ");
      }
    }
  }
}
