// BACCARAT for the VMX: a line-for-line port of microgames/ui/games/baccarat_core.js to C# 1.0, so it plays exactly
// the same (same random numbers, same order of events); tools/parity checks that frame by frame.
// Punto Banco from an 8-deck shoe (Fisher-Yates; reshuffled between hands once fewer than 14 cards are left). A = 1,
// 2-9 face, 10/J/Q/K = 0, a hand is its sum mod 10; naturals (8/9) stand, else the standard third-card tableau.
// Bets 0-20 each on PLAYER (1:1), BANKER (1:1 less 5% commission, charged in whole chips rounded up with the overpaid
// part kept as credit toward later commissions) and TIE (8:1; PLAYER/BANKER bets push on a tie); at least 1 in all,
// never more than the stash. House edge (8 decks, exact): PLAYER 1.2351%, BANKER 1.0579%, TIE 14.3596%.
// Up/Down choose the row, Left/Right lower / raise its stake (hold to repeat), A deals, B clears; A after a hand goes
// back to the bets (they stay on).
// VMX CASINO: the stakes are taken with Chips.Bet when the cards are dealt, the returns paid with Chips.Pay.
using System;
using Microsoft.Vmx;

public class Baccarat : Game {
  const string FONTCH = "ABCDEFGHIJKLMNOPQRSTUVWXY0123456789:!-<>+/^. ";
  static readonly int[] FONT = {   // the core's 3x5 glyph strings as 15-bit numbers, bit 14 = top left
    11245, 27566, 14627, 27502, 31143, 31140, 14699, 23533, 29847, 4714, 23469, 18727,
    24557, 27501, 11114, 27556, 11123, 27565, 14478, 29842, 23407, 23402, 23549, 23213,
    23186, 31599, 11415, 25255, 25230, 23497, 31118, 14831, 29330, 31727, 31694, 1040,
    9346, 448, 5393, 17492, 1488, 4772, 11834, 2, 0 };
  static readonly int[] SUIT0 = { 8, 28, 62, 127, 127, 42, 28 }, SUIT1 = { 54, 73, 65, 65, 34, 20, 8 },
    SUIT2 = { 8, 20, 34, 65, 34, 20, 8 }, SUIT3 = { 28, 28, 107, 127, 107, 8, 28 };
  static readonly int[] CHIP = { 14, 21, 27, 21, 14 };
  static readonly string[] RANKS = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };
  static readonly string[] SIDES = { "PLAYER", "BANKER", "TIE" }, ODDS = { "1:1", ".95", "8:1" }, LET = { "P", "B", "T" };
  const int NCARDS = 416, CUT = 14, MAXBET = 20, ROADN = 12;
  const int PY = 7, BY = 29;
  const int TITLE = 0, BET = 1, DEAL = 2, RESULT = 3, BROKE = 4;
  static readonly int[] JINGLE = { 784, 988, 1175, 1568 };

  // game state (the JS object g)
  uint rs; int t, phase, pt, pos, cur, endT, ptot, btot, winner, win, best, credit, nroad, hands, msgT, hL, hR;
  int[] shoe = new int[NCARDS], stake = new int[3], tgt = new int[3], hand = new int[6], at = new int[6], net = new int[3],
    road = new int[ROADN];
  string last = "", msg = "";
  bool auto_, demo, pUp, pDown, pLeft, pRight, pA, pB;
  Screen sc;

  public override bool UsesB { get { return true; } }

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; pos = 0;
    stake[0] = 0; stake[1] = 5; stake[2] = 0; tgt[0] = tgt[1] = tgt[2] = 0; cur = 1;
    for (int i = 0; i < 6; i++) { hand[i] = 0; at[i] = -1; }
    endT = 0; ptot = 0; btot = 0; winner = 0; net[0] = net[1] = net[2] = 0; win = 0; best = 0; credit = 0;
    for (int i = 0; i < ROADN; i++) road[i] = 0;
    nroad = 0; last = ""; hands = 0; msg = ""; msgT = 0; auto_ = false; hL = 0; hR = 0;
    pUp = pDown = pLeft = pRight = pA = pB = false;
    Shuffle();
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 90; }
  static int Value(int c) { int r = c % 13; return r < 8 ? r + 2 : r == 12 ? 1 : 0; }
  void Shuffle() {
    for (int i = 0; i < NCARDS; i++) shoe[i] = i % 52;
    for (int i = NCARDS - 1; i > 0; i--) { int j = Rnd() % (i + 1), tt = shoe[i]; shoe[i] = shoe[j]; shoe[j] = tt; }
    pos = 0;
  }
  int Total() { return stake[0] + stake[1] + stake[2]; }
  void ToBet() {
    int c = Chips.Balance();
    if (c < 1) { SetPhase(BROKE); return; }
    if (NCARDS - pos < CUT) { Shuffle(); Say("NEW SHOE"); }
    for (int k = 2; k >= 0; k--) {
      int over = Total() - c;
      if (over > 0) stake[k] -= Math.Min(over, stake[k]);
    }
    SetPhase(BET);
    if (auto_) {
      int main = Rnd() % 2 == 0 ? 1 : 0;
      tgt[0] = 0; tgt[1] = 0; tgt[2] = 0;
      tgt[main] = Math.Min(1 + Rnd() % 10, c);
      if (Rnd() % 6 == 0) tgt[2] = Math.Min(1 + Rnd() % 2, c - tgt[main]);
    }
  }
  static bool BankerDraws(int b, int p3) {
    if (p3 < 0) return b <= 5;
    if (b <= 2) return true;
    if (b == 3) return p3 != 8;
    if (b == 4) return p3 >= 2 && p3 <= 7;
    if (b == 5) return p3 >= 4 && p3 <= 7;
    if (b == 6) return p3 >= 6 && p3 <= 7;
    return false;
  }

  // ---- the game ----
  void Deal() {
    Chips.Bet(Total());
    for (int i = 0; i < 6; i++) at[i] = -1;
    hand[0] = shoe[pos++]; hand[3] = shoe[pos++]; hand[1] = shoe[pos++]; hand[4] = shoe[pos++];
    at[0] = 4; at[3] = 19; at[1] = 34; at[4] = 49;
    int p = (Value(hand[0]) + Value(hand[1])) % 10, b = (Value(hand[3]) + Value(hand[4])) % 10, tEnd = 49;
    if (p < 8 && b < 8) {
      int p3 = -1;
      if (p <= 5) { hand[2] = shoe[pos++]; p3 = Value(hand[2]); p = (p + p3) % 10; tEnd += 24; at[2] = tEnd; }
      if (BankerDraws(b, p3)) { hand[5] = shoe[pos++]; b = (b + Value(hand[5])) % 10; tEnd += p3 < 0 ? 24 : 15; at[5] = tEnd; }
    }
    ptot = p; btot = b; endT = tEnd + 16;
    winner = p > b ? 0 : b > p ? 1 : 2;
    SetPhase(DEAL);
  }
  void Judge() {
    int w = winner, back = 0;
    for (int k = 0; k < 3; k++) net[k] = -stake[k];
    if (w == 2) { net[0] = 0; net[1] = 0; net[2] = 8 * stake[2]; back = stake[0] + stake[1] + 9 * stake[2]; }
    else if (w == 0) { net[0] = stake[0]; back = 2 * stake[0]; }
    else if (stake[1] > 0) {
      int need = 5 * stake[1], comm = 0;
      if (need <= credit) credit -= need;
      else { comm = (need - credit + 99) / 100; credit += comm * 100 - need; }
      net[1] = stake[1] - comm; back = 2 * stake[1] - comm;
    }
    win = back; Chips.Pay(back); hands++;
    if (nroad == ROADN) { for (int i = 1; i < ROADN; i++) road[i - 1] = road[i]; nroad--; }
    road[nroad++] = w;
    last = ResultText();
    if (back > best) { best = back; if (!demo) Scores.Report(best, 0, 0, 0); }
    SetPhase(RESULT);
  }
  string ResultText() {
    int hi = winner == 1 ? btot : ptot, lo = winner == 1 ? ptot : btot;
    string score = hi.ToString() + "-" + lo.ToString();
    if (winner == 2) return "TIE " + score;
    return SIDES[winner] + " WINS " + score;
  }
  string NetText(bool push) {
    string s = "";
    for (int k = 0; k < 3; k++) {
      if (stake[k] == 0) continue;
      if (s.Length > 0) s += " ";
      if (push && net[k] == 0) s += LET[k] + " PUSH";
      else s += LET[k] + (net[k] >= 0 ? "+" : "") + net[k].ToString();
    }
    if (push && s.Length > 16) return NetText(false);
    return s;
  }
  string BetText() {
    string s = "BET";
    for (int k = 0; k < 3; k++) if (stake[k] > 0) s += " " + LET[k] + stake[k].ToString();
    return s;
  }
  void Adjust(int d) {
    int k = cur;
    if (d > 0) { if (stake[k] < MAXBET && Total() < Chips.Balance()) stake[k]++; }
    else if (stake[k] > 0) stake[k]--;
  }

  public override void Tick(int held_, int pressed) {
    bool auto = demo;
    t++; pt++; if (msgT > 0) msgT--;
    bool up = !auto && (held_ & Buttons.Up) != 0, down = !auto && (held_ & Buttons.Down) != 0,
      left = !auto && (held_ & Buttons.Left) != 0, right = !auto && (held_ & Buttons.Right) != 0,
      a = !auto && (held_ & Buttons.A) != 0, b = !auto && (held_ & Buttons.B) != 0;
    bool eUp = up && !pUp, eDown = down && !pDown, eA = a && !pA, eB = b && !pB;
    hL = left ? hL + 1 : 0; hR = right ? hR + 1 : 0;
    bool rL = hL == 1 || (hL > 12 && hL % 3 == 0), rR = hR == 1 || (hR > 12 && hR % 3 == 0);
    pUp = up; pDown = down; pLeft = left; pRight = right; pA = a; pB = b;
    auto_ = auto;

    switch (phase) {
      case TITLE:
        if (eA || (auto && pt > 40)) ToBet();
        break;
      case BET:
        if (auto) {
          if (pt % 4 == 0) {
            int want = -1;
            for (int k = 0; k < 3 && want < 0; k++) if (stake[k] > tgt[k]) want = k;
            for (int k = 0; k < 3 && want < 0; k++) if (stake[k] < tgt[k]) want = k;
            if (want < 0) { if (pt > 30) Deal(); }
            else if (cur != want) cur += cur < want ? 1 : -1;
            else Adjust(stake[want] < tgt[want] ? 1 : -1);
          }
        } else {
          if (eUp) { cur = (cur + 2) % 3; Beeper.Tone(1200, 10, 25); }
          if (eDown) { cur = (cur + 1) % 3; Beeper.Tone(1200, 10, 25); }
          if (rL) { Adjust(-1); Beeper.Tone(1100, 8, 25); }
          if (rR) { Adjust(1); Beeper.Tone(1600, 8, 25); }
          if (eB) { stake[0] = 0; stake[1] = 0; stake[2] = 0; Beeper.Tone(600, 30, 30); }
          if (eA) { if (Total() > 0) Deal(); else Say("PLACE A BET"); }
        }
        break;
      case DEAL:
        if (!auto) { for (int i = 0; i < 6; i++) if (at[i] >= 0 && pt == at[i] + 8) Beeper.Tone(2400, 6, 20); }
        if (pt >= endT) Judge();
        break;
      case RESULT:
        if (win > 0 && !auto && pt <= 13 && (pt - 1) % 4 == 0) Beeper.Tone(JINGLE[(pt - 1) / 4], 90, 40);
        if ((eA && pt > 10) || (auto && pt > 75)) ToBet();
        break;
      case BROKE:
        break;
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
  void DrawSide(int side, int y) {
    int tot = 0, shown = 0;
    for (int i = 0; i < 3; i++) {
      int k = side * 3 + i;
      if (at[k] < 0) continue;
      int f = pt - at[k];
      if (phase != DEAL) f = 99;
      if (f < 0) continue;
      bool face = f >= 8; int w = 12;
      if (f == 6 || f == 9) w = 8; else if (f == 7 || f == 8) w = 4;
      Card(i * 13, y, hand[k], face, w);
      if (face) { tot += Value(hand[k]); shown++; }
    }
    bool lit = phase == RESULT && (winner == side || winner == 2);
    if (lit) Fill(40, y, 24, 7, true);
    Text(41, y + 1, SIDES[side], !lit);
    if (shown > 0) Big(49, y + 9, (tot % 10).ToString());
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      Big(0, 2, "BACCARAT");
      Card(6, 15, 7, true, 12); Card(19, 15, 24, true, 12);
      Card(33, 15, 44, true, 12); Card(46, 15, 32, true, 12);
      if (phase == BROKE) {
        Centre(40, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Centre(48, "MORE TOMORROW");
        Sprite(23, 57, CHIP, 5, true); Text(30, 57, "0", true);
        return;
      }
      Centre(38, "PUNTO BANCO");
      Centre(46, msgT > 0 ? msg : "8 DECK SHOE");
      if (((t >> 4) & 1) != 0) Centre(56, "A: START");
      return;
    }
    Sprite(1, 1, CHIP, 5, true); Text(8, 1, Chips.Balance().ToString(), true);
    Right(1, phase == RESULT && !auto_ && ((t >> 4) & 1) != 0 ? "A: NEXT" : "BET " + Total().ToString());
    if (phase == BET) {
      for (int k = 0; k < 3; k++) {
        int y = 9 + k * 9; bool lit = k == cur; string s = stake[k].ToString();
        if (lit) Fill(0, y, 64, 9, true);
        Text(2, y + 2, SIDES[k], !lit); Text(29, y + 2, ODDS[k], !lit);
        Text(55 - s.Length * 2, y + 2, s, !lit);
        if (lit) { Text(44, y + 2, "<", false); Text(60, y + 2, ">", false); }
      }
      for (int i = 0; i < nroad; i++) {
        int x = 64 - (nroad - i) * 5, w = road[i];
        if (w == 2) { Fill(x - 1, 38, 5, 7, true); Text(x, 39, "T", false); } else Text(x, 39, LET[w], true);
      }
      Centre(47, msgT > 0 ? msg : last);
      Fill(0, 54, 64, 1, true);
      Centre(58, ((t >> 5) & 1) != 0 ? "A DEAL  B CLEAR" : "^ PICK  <> BET");
      return;
    }
    DrawSide(0, PY);
    DrawSide(1, BY);
    if (phase == DEAL) Centre(52, BetText());
    else {
      if (pt > 24 || ((pt >> 2) & 1) == 0) Centre(51, last);
      Centre(58, NetText(true));
    }
  }
}
