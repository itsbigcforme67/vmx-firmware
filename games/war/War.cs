// CASINO WAR for the VMX: a line-for-line port of microgames/ui/games/war_core.js to C# 1.0, so it plays exactly the
// same (same random numbers, same order of events); tools/parity checks that frame by frame.
// High card wins from a 6-deck shoe (312 cards, reshuffled between rounds after the cut card at 75%), aces high.
// Bet 1-20 (Left/Right) plus an optional TIE side bet 0-10 (Up/Down) that pays 10:1 if the first two cards tie; A
// deals. Higher card pays 1:1. On a tie A goes to war (raise = bet; 3 cards burned, one card each; equal or higher
// wins the raise 1:1 and pushes the bet), B surrenders half the bet (rounded down: the house keeps the odd chip).
// House edge 2.877% of the bet going to war every time (142853/4965115, exact for the shoe); tie bet 18.65% (58/311).
// VMX CASINO: bet + tie taken with Chips.Bet at the deal, the raise at war; winnings paid stake-included with Chips.Pay.
using System;
using Microsoft.Vmx;

public class War : Game {
  const string FONTCH = "ABCDEFGHIJKLMNOPQRSTUVWXY0123456789:!-<>+/^? ";
  static readonly int[] FONT = {   // the core's 3x5 glyph strings as 15-bit numbers, bit 14 = top left
    11245, 27566, 14627, 27502, 31143, 31140, 14699, 23533, 29847, 4714, 23469, 18727,
    24557, 27501, 11114, 27556, 11123, 27565, 14478, 29842, 23407, 23402, 23549, 23213,
    23186, 31599, 11415, 25255, 25230, 23497, 31118, 14831, 29330, 31727, 31694, 1040,
    9346, 448, 5393, 17492, 1488, 4772, 11834, 29314, 0 };
  static readonly int[] SUIT0 = { 8, 28, 62, 127, 127, 42, 28 }, SUIT1 = { 54, 73, 65, 65, 34, 20, 8 },
    SUIT2 = { 8, 20, 34, 65, 34, 20, 8 }, SUIT3 = { 28, 28, 107, 127, 107, 8, 28 };
  static readonly int[] CHIP = { 14, 21, 27, 21, 14 };
  static readonly string[] RANKS = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };
  static readonly string[] RES = { "YOU WIN", "YOU LOSE", "SURRENDER", "WAR WON", "TIE WINS WAR", "WAR LOST" };
  const int SHOE = 312, CUT = 234;
  const int DY = 8, PY = 37, CX = 14, WX = 51, FP = 6, FD = 14;
  const int TITLE = 0, BET = 1, DEAL = 2, TIEBET = 3, CHOOSE = 4, WARP = 5, RESULT = 6, BROKE = 7;
  static readonly int[] JINGLE = { 784, 988, 1175, 1568 };
  static int[] BACK;               // the card back's lattice, one bit per column (bit i = column i), rows 0..17

  // game state (the JS object g)
  uint rs; int t, phase, pt, bet, tie, raise, pos, pc, dc, wp, wd, res, paid, staked, best, wars, rounds, tb, tt, msgT;
  bool war, tieHit;
  int[] shoe = new int[SHOE];
  string msg = "";
  bool auto_, demo, pUp, pDown, pLeft, pRight, pA, pB;
  Screen sc;

  public override bool UsesB { get { return true; } }

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    if (BACK == null) {
      BACK = new int[18];
      for (int j = 2; j < 18; j++) for (int i = 2; i < 10; i++) if ((i + j) % 4 == 0 || (i - j + 40) % 4 == 0) BACK[j] |= 1 << i;
    }
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; bet = 5; tie = 0; raise = 0; pos = 0;
    pc = dc = wp = wd = 0; war = false; tieHit = false; res = 0; paid = 0; staked = 0; best = 0; wars = 0; rounds = 0;
    tb = 5; tt = 0; msg = ""; msgT = 0; auto_ = false; pUp = pDown = pLeft = pRight = pA = pB = false;
    Shuffle();
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 90; }
  void Shuffle() {
    for (int i = 0; i < SHOE; i++) shoe[i] = i;
    for (int i = SHOE - 1; i > 0; i--) { int j = Rnd() % (i + 1), x = shoe[i]; shoe[i] = shoe[j]; shoe[j] = x; }
    pos = 0;
  }
  int Next() { return shoe[pos++]; }
  void ToBet() {
    int c = Chips.Balance();
    if (c < 1) { SetPhase(BROKE); return; }
    if (pos >= CUT) { Shuffle(); Say("SHUFFLING"); }
    bet = Math.Max(1, Math.Min(bet, Math.Min(20, c)));
    tie = Math.Min(tie, c - bet);
    SetPhase(BET);
    if (auto_) {
      tb = 1 + Rnd() % Math.Min(20, c); tt = 0;
      if (Rnd() % 3 == 0 && c - tb > 0) tt = 1 + Rnd() % Math.Min(3, c - tb);
    }
  }

  // ---- the round ----
  void Deal() {
    Chips.Bet(bet + tie);
    staked = bet + tie; paid = 0; raise = 0; war = false;
    pc = Next(); dc = Next();
    tieHit = pc % 13 == dc % 13;
    SetPhase(DEAL);
  }
  void Pay(int n) { Chips.Pay(n); paid += n; }
  void AfterDeal() {
    if (tie > 0) { if (tieHit) Pay(tie * 11); SetPhase(TIEBET); }
    else Judge();
  }
  void Finish(int r) {
    res = r; rounds++;
    if (paid > best) { best = paid; if (!demo) Scores.Report(best, wars, 0, 0); }
    SetPhase(RESULT);
  }
  void Judge() {
    int p = pc % 13, d = dc % 13;
    if (p > d) { Pay(bet * 2); Finish(0); }
    else if (p < d) Finish(1);
    else SetPhase(CHOOSE);
  }
  void GoWar() {
    Chips.Bet(bet); raise = bet; staked += bet; war = true;
    pos += 3;
    wp = Next(); wd = Next();
    SetPhase(WARP);
  }
  void Surrender() { Pay(bet >> 1); Finish(2); }
  void WarEnd() {
    int p = wp % 13, d = wd % 13;
    if (p >= d) { Pay(bet * 3); wars++; if (!demo) Scores.Report(Math.Max(best, paid), wars, 0, 0); Finish(p > d ? 3 : 4); }
    else Finish(5);
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
          if (pt % 4 == 0) {
            if (bet < tb) bet++; else if (bet > tb) bet--;
            else if (tie < tt) tie++; else if (tie > tt) tie--;
          }
          if (pt > 45 && bet == tb && tie == tt) Deal();
        } else {
          if (eRight) { bet = Math.Min(Math.Min(20, c), bet + 1); tie = Math.Min(tie, c - bet); Beeper.Tone(1500, 10, 25); }
          if (eLeft) { bet = Math.Max(1, bet - 1); Beeper.Tone(1200, 10, 25); }
          if (eUp) { tie = Math.Min(Math.Min(10, c - bet), tie + 1); Beeper.Tone(1700, 10, 25); }
          if (eDown) { tie = Math.Max(0, tie - 1); Beeper.Tone(1000, 10, 25); }
          if (eA) Deal();
        }
        break;
      }
      case DEAL:
        if (!auto && (pt == FP + 2 || pt == FD + 2)) Beeper.Tone(2400, 6, 20);
        if (pt > FD + 10) AfterDeal();
        break;
      case TIEBET:
        if (!auto && tieHit && pt <= 13 && (pt - 1) % 4 == 0) Beeper.Tone(JINGLE[(pt - 1) / 4], 90, 40);
        if (pt > 36) Judge();
        break;
      case CHOOSE: {
        bool can = Chips.Balance() >= bet;
        if (auto) { if (pt > 30) { if (can) GoWar(); else Surrender(); } }
        else if (eA && can) GoWar();
        else if (eB || eA) Surrender();
        break;
      }
      case WARP:
        if (!auto) {
          if (pt == 1) Beeper.Tone(330, 200, 50);
          if (pt == 4 || pt == 9 || pt == 14) Beeper.Tone(1800, 6, 20);
          if (pt == 26 || pt == 34) Beeper.Tone(2400, 6, 20);
        }
        if (pt > 44) WarEnd();
        break;
      case RESULT:
        if (!auto && pt <= 13 && (pt - 1) % 4 == 0 && (res == 0 || res == 3 || res == 4)) Beeper.Tone(JINGLE[(pt - 1) / 4], 90, 40);
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
  void Centre(int y, string s, bool v) { Text(32 - ((s.Length * 4 - 1) >> 1), y, s, v); }
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
      for (int j = 2; j < 18; j++) {
        int m = BACK[j];
        for (int i = 2; i < w - 2; i++) if (((m >> i) & 1) != 0) Px(x0 + i, y + j, true);
      }
    }
    if (!face || w < 12) return;
    int r = c % 13;
    if (r == 8) { Text(x + 1, y + 2, "1", false); Text(x + 5, y + 2, "0", false); } else Text(x + 2, y + 2, RANKS[r], false);
    Sprite(x + 3, y + 10, Suit((c / 13) % 4), 7, false);
  }
  void FlipCard(int x, int y, int c, int at) {
    int f = pt - at;
    if (f < 0) Card(x, y, c, false, 12);
    else if (f < 2) Card(x, y, c, false, f == 0 ? 8 : 4);
    else if (f < 4) Card(x, y, c, true, f == 2 ? 4 : 8);
    else Card(x, y, c, true, 12);
  }
  void DrawTop() {
    Sprite(1, 1, CHIP, 5, true); Text(8, 1, Chips.Balance().ToString(), true);
    string s = "BET " + bet.ToString();
    if (raise > 0) s += "+" + raise.ToString();
    else if (tie > 0 && (phase == DEAL || phase == TIEBET)) s += " T" + tie.ToString();
    Right(1, s);
  }
  void DrawBet() {
    Text(2, 4, "BET", true); Big(20, 2, bet.ToString()); Right(4, "1:1");
    Text(2, 18, "TIE", true); Big(20, 16, tie.ToString()); Right(18, "10:1");
    Centre(30, msgT > 0 ? msg : "HIGH CARD WINS", true);
    Centre(38, "<> BET   ^ TIE", true);
    Centre(46, "SHOE " + (SHOE - pos).ToString(), true);
    Fill(0, 55, 64, 1, true);
    Sprite(1, 58, CHIP, 5, true); Text(8, 58, Chips.Balance().ToString(), true);
    if (!auto_ && ((t >> 4) & 1) != 0) Right(58, "A: DEAL");
  }
  void Banner(int y, string s, bool inv) {
    if (inv) { int w = s.Length * 4 + 3; Fill(32 - (w >> 1), y - 1, w, 7, true); }
    Centre(y, s, !inv);
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      Big(8, 2, "CASINO"); Big(21, 14, "WAR");
      if (phase == BROKE) {
        Centre(32, "OUT OF CHIPS", true); if (((t >> 5) & 1) != 0) Centre(40, "MORE TOMORROW", true);
        Sprite(23, 52, CHIP, 5, true); Text(30, 52, "0", true);
        return;
      }
      Card(18, 27, 12, true, 12); Card(34, 27, 24, true, 12);
      Centre(49, msgT > 0 ? msg : "HIGH CARD WINS", true);
      if (((t >> 4) & 1) != 0) Centre(57, "A: START", true);
      return;
    }
    if (phase == BET) { DrawBet(); return; }
    DrawTop();
    Text(1, DY + 1, "DLR", true); Text(1, PY + 1, "YOU", true);
    if (phase == DEAL) { FlipCard(CX, PY, pc, FP); FlipCard(CX, DY, dc, FD); }
    else { Card(CX, PY, pc, true, 12); Card(CX, DY, dc, true, 12); }
    if (phase == WARP) {
      for (int i = 0; i < 3; i++) if (pt >= 4 + i * 5) Card(28 + i * 4, 22, 0, false, 12);
      if (pt >= 20) { FlipCard(WX, PY, wp, 24); FlipCard(WX, DY, wd, 32); }
      Banner(58, "WAR!", ((pt >> 2) & 1) == 0);
    } else if (war) { Card(WX, PY, wp, true, 12); Card(WX, DY, wd, true, 12); }

    if (phase == TIEBET) {
      if (pt > 20 || ((pt >> 2) & 1) == 0) Banner(30, tieHit ? "TIE BET PAYS " + (tie * 11).ToString() : "TIE BET LOST", tieHit);
    } else if (phase == CHOOSE) {
      bool can = Chips.Balance() >= bet;
      Banner(30, can ? "TIE! GO TO WAR?" : "NEED " + bet.ToString() + " FOR WAR", false);
      Centre(58, can ? "A WAR  B SURR" : "A: SURRENDER", true);
    } else if (phase == RESULT) {
      bool won = res == 0 || res == 3 || res == 4;
      if (pt > 30 || ((pt >> 2) & 1) == 0) Banner(30, RES[res], won);
      string d = res == 2 ? "BACK " + paid.ToString() : paid > 0 ? "PAID " + paid.ToString() : "LOST " + staked.ToString();
      if (auto_ || pt <= 40 || ((t >> 4) & 1) == 0) Centre(58, d, true);
      else Centre(58, "A: NEXT", true);
    }
  }
}
