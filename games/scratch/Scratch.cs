// SCRATCH CARDS for the VMX: a line-for-line port of microgames/ui/games/scratch_core.js to C# 1.0, so it plays exactly
// the same (same random numbers, same order of events); tools/parity checks that frame by frame.
// Instant lottery tickets scratched with a coin. The shop: Left/Right picks MATCH 3 or LUCKY 7, Up/Down the price (1, 2
// or 5 chips, never more than the stash), A buys, B shows the prize table. The ticket: D-pad moves the coin, hold A to
// scratch, B = REVEAL ALL; a half-scratched spot flakes off by itself, and when every spot shows the prize is paid.
// The outcome is drawn when the ticket is bought (one Rnd() 0..65535 against weights out of 65536) and the symbols are
// laid out to show exactly that (a MATCH 3 loser never has an amount three times; LUCKY 7 shows 7s only where they win).
//   MATCH 3 (3 alike wins it): 1 2 5 10 20 50 100 500 x price, weights 14000 6000 2100 1000 330 66 26 3:
//     return 92.32% (house edge 7.68%), 35.9% of tickets win.
//   LUCKY 7 (every 7 wins the amount under it): totals 1 2 3 5 10 20 50 100 500 x price, weights 12000 5000 2500 1900
//     900 300 70 30 2: return 93.84% (house edge 6.16%), 34.6% of tickets win.
// Prizes are what you receive: the price leaves the shared bank (Chips.Bet) when the ticket is bought, the prize comes
// back (Chips.Pay) when it is revealed. No chips = OUT OF CHIPS until the bank's daily top-up.
// Drawing: the scratch state is one int per 2-pixel row of cells (bit = scratched), and the latex is drawn as
// Screen.Fill runs of the unscratched cells, so a full ticket costs a few dozen Fill calls.
using System;
using Microsoft.Vmx;

public class Scratch : Game {
  const string FONTCH = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789:!-<>+/%. ";
  static readonly string[] FONT = {
    "010101111101101", "110101110101110", "011100100100011", "110101101101110", "111100110100111", "111100110100100",
    "011100101101011", "101101111101101", "111010010010111", "001001001101010", "101101110101101", "100100100100111",
    "101111111101101", "110101101101101", "010101101101010", "110101110100100", "010101101110011", "110101110101101",
    "011100010001110", "111010010010010", "101101101101111", "101101101101010", "101101111111101", "101101010101101",
    "101101010010010", "111001010100111",
    "111101101101111", "010110010010111", "110001010100111", "110001010001110", "101101111001001", "111100110001110",
    "011100111101111", "111001010010010", "111101111101111", "111101111001110", "000010000010000", "010010010000010",
    "000000111000000", "001010100010001", "100010001010100", "000010111010000", "001001010100100", "101001010100101",
    "000000000000010", "000000000000000" };
  static readonly string[] SEVEN = { "###########", "###########", "#.......##.", ".......##..", "......##...", ".....###...",
    "....###....", "....###....", "...###.....", "...###.....", "...###....." };
  static readonly int[] PRICES = { 1, 2, 5 };
  static readonly int[] AM = { 1, 2, 5, 10, 20, 50, 100, 500 };
  static readonly int[] M3W = { 14000, 6000, 2100, 1000, 330, 66, 26, 3 };
  static readonly int[] L7T = { 1, 2, 3, 5, 10, 20, 50, 100, 500 };
  static readonly int[] L7W = { 12000, 5000, 2500, 1900, 900, 300, 70, 30, 2 };
  static readonly int[][][] L7C = {
    new int[][] { new int[] { 0 } },
    new int[][] { new int[] { 1 }, new int[] { 0, 0 } },
    new int[][] { new int[] { 1, 0 }, new int[] { 0, 0, 0 } },
    new int[][] { new int[] { 2 }, new int[] { 1, 1, 0 } },
    new int[][] { new int[] { 3 }, new int[] { 2, 2 } },
    new int[][] { new int[] { 4 }, new int[] { 3, 3 }, new int[] { 3, 2, 2 } },
    new int[][] { new int[] { 5 }, new int[] { 4, 4, 3 } },
    new int[][] { new int[] { 6 }, new int[] { 5, 5 } },
    new int[][] { new int[] { 7 } } };
  static readonly string[] NAME = { "MATCH 3", "LUCKY 7" }, TAG = { "3 ALIKE WINS", "ANY 7 WINS" };
  const int PM = 0x7FFFFFFE, R0 = 4, R1 = 24;
  const int UB = 1, DB = 2, LB = 4, RB = 8, AB = 16, BB = 32;
  const int TITLE = 0, SHOP = 1, PRIZES = 2, SCRATCH = 3, REVEAL = 4, RESULT = 5, BROKE = 6;

  // game state (the JS object g)
  uint rs; int t, phase, pt, msgT;
  string msg = "";
  int kind, pi, price, tier, prize, best, tickets, nsym;
  int[] val = new int[9], amt = new int[9], rev = new int[9], mask = new int[32], cnt = new int[8];
  int cx, cy, mv, armed, wipe, scr, prev;
  int dKind, dPi, dShow, dSeen, dDir, dTy, dRev, dNext;
  bool demo;
  int bestReported;
  Screen sc;

  public override bool UsesB { get { return true; } }

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    Prepare();
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; msg = ""; msgT = 0;
    kind = 0; pi = 0; price = 1; tier = -1; prize = 0; best = 0; tickets = 0; nsym = 9;
    for (int i = 0; i < 9; i++) { val[i] = 0; amt[i] = 0; rev[i] = 0; }
    for (int i = 0; i < 32; i++) mask[i] = 0;
    cx = 12; cy = 14; mv = 0; armed = 0; wipe = 0; scr = 0; prev = 0;
    dKind = 0; dPi = 0; dShow = 0; dSeen = -1; dDir = 1; dTy = 14; dRev = -1; dNext = 0;
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  void Say(string m) { msg = m; msgT = 90; }
  void SetPhase(int p) { phase = p; pt = 0; }
  int MaxPi() { int c = Chips.Balance(), j = 2; while (j > 0 && PRICES[j] > c) j--; return j; }
  void ToShop() {
    if (Chips.Balance() < 1) { SetPhase(BROKE); return; }
    pi = Math.Min(pi, MaxPi()); SetPhase(SHOP);
  }
  int SpotX(int i) { return 2 + (i % 3) * 20; }
  int SpotY(int i) { return 8 + (i / 3) * (kind == 0 ? 14 : 22); }

  void Buy() {
    price = PRICES[pi]; Chips.Bet(price); tickets++;
    int r = Rnd(), acc = 0; tier = -1;
    if (kind == 0) {
      for (int i = 0; i < 8; i++) { acc += M3W[i]; if (r < acc) { tier = i; break; } }
      prize = tier >= 0 ? AM[tier] * price : 0; nsym = 9;
      for (int i = 0; i < 8; i++) cnt[i] = 0;
      int s = 0;
      if (tier >= 0) { val[0] = val[1] = val[2] = tier; cnt[tier] = 3; s = 3; }
      for (int i = s; i < 9; i++) {
        int v; do { v = Rnd() % 8; } while (v == tier || cnt[v] >= 2);
        val[i] = v; cnt[v]++;
      }
      for (int i = 8; i > 0; i--) { int j = Rnd() % (i + 1), x = val[i]; val[i] = val[j]; val[j] = x; }
    } else {
      for (int i = 0; i < 9; i++) { acc += L7W[i]; if (r < acc) { tier = i; break; } }
      prize = tier >= 0 ? L7T[tier] * price : 0; nsym = 6;
      int[] comp = null; if (tier >= 0) comp = L7C[tier][Rnd() % L7C[tier].Length];
      int n = comp != null ? comp.Length : 0;
      for (int i = 0; i < 6; i++) {
        if (i < n) { val[i] = 7; amt[i] = comp[i]; }
        else { int d = 1 + Rnd() % 8; if (d >= 7) d++; val[i] = d; amt[i] = Rnd() % 8; }
      }
      for (int i = 5; i > 0; i--) {
        int j = Rnd() % (i + 1); int x = val[i]; val[i] = val[j]; val[j] = x;
        x = amt[i]; amt[i] = amt[j]; amt[j] = x;
      }
    }
    for (int i = 0; i < 32; i++) mask[i] = 0;
    for (int i = 0; i < 9; i++) rev[i] = 0;
    cx = 12; cy = 14; mv = 0; armed = 0; scr = 0;
    SetPhase(SCRATCH);
    Beeper.Tone(330, 40, 30);
  }
  int KeyR0(int i) { int y = SpotY(i) >> 1; return kind == 0 ? y + 2 : y + 1; }
  int KeyR1(int i) { int y = SpotY(i) >> 1; return kind == 0 ? y + 4 : y + 9; }
  void CheckSpots() {
    int all = 1;
    for (int i = 0; i < nsym; i++) {
      if (rev[i] == 0) {
        int c0 = (SpotX(i) >> 1) + 1, r0 = KeyR0(i), r1 = KeyR1(i);
        int n = 0; for (int r = r0; r <= r1; r++) n += POP[(mask[r] >> c0) & 255];
        if (n * 2 >= (r1 - r0 + 1) * 8) {
          rev[i] = 1; for (int r = r0; r <= r1; r++) mask[r] |= 255 << c0;
          Beeper.Tone(kind == 1 && val[i] == 7 ? 988 : 523, 30, 30);
        }
      }
      if (rev[i] == 0) all = 0;
    }
    if (all != 0) Finish();
  }
  void Finish() {
    for (int i = 0; i < 9; i++) rev[i] = 1;
    if (prize > 0) {
      Chips.Pay(prize); if (prize > best) best = prize;
      if (!demo && best > bestReported) { bestReported = best; Scores.Report(best, 0, 0, 0); }   // board: biggest prize
      Beeper.Tone(prize >= 20 * price ? 1047 : 784, prize >= 20 * price ? 300 : 120, 50);
    }
    SetPhase(RESULT);
  }
  int Brush() {
    int c = cx >> 1, r = cy >> 1, add = (7 << (c - 1)) & PM, ch = 0;
    for (int y = r - 1; y <= r + 1; y++) {
      if (y < R0 || y > R1) continue;
      if ((mask[y] | add) != mask[y]) { mask[y] |= add; ch = 1; }
    }
    return ch;
  }

  int DemoHeld() {
    int k = 0;
    switch (phase) {
      case SHOP:
        if (pt == 10) { dKind = Rnd() % 2; dPi = Rnd() % 3; dShow = 0; if (tickets % 6 == 2 && dSeen != tickets) { dShow = 1; dSeen = tickets; } }
        if (pt > 10 && pt % 6 == 0) {
          if (kind != dKind) k = RB;
          else if (pi < dPi && pi < MaxPi()) k = UB;
          else if (pi > dPi) k = DB;
          else if (dShow != 0) { dShow = 0; k = BB; }
          else if (pt > 30) k = AB;
        }
        break;
      case PRIZES: if (pt == 75) k = BB; break;
      case SCRATCH:
        if (pt == 1) { dDir = 1; dTy = cy; int q = Rnd() % 4; dRev = q == 0 ? 20 + Rnd() % 120 : -1; }
        if (pt == dRev) { k = BB; break; }
        if (pt > 6) {
          k = AB;
          if (cy < dTy) k |= DB;
          else if (cy > dTy + 1) k |= UB;
          else if (dDir > 0) { if (cx < 59) k |= RB; else { dDir = -1; dTy = cy + 5 > 47 ? 10 : cy + 5; } }
          else { if (cx > 4) k |= LB; else { dDir = 1; dTy = cy + 5 > 47 ? 10 : cy + 5; } }
        }
        break;
      case RESULT:
        if (pt == 1) dNext = Rnd() % 3;
        if (pt == 70) k = dNext == 0 ? BB : AB;
        break;
    }
    return k;
  }

  public override void Tick(int held, int pressed) {
    t++; pt++; if (msgT > 0) msgT--;
    int k = demo ? DemoHeld() : held & 63;
    int e = k & ~prev; prev = k;
    switch (phase) {
      case TITLE:
        if ((e & AB) != 0 || (demo && pt > 40)) ToShop();
        break;
      case SHOP:
        if ((e & (LB | RB)) != 0) kind ^= 1;
        if ((e & UB) != 0) pi = Math.Min(pi + 1, MaxPi());
        if ((e & DB) != 0) pi = Math.Max(0, pi - 1);
        if ((e & AB) != 0) Buy();
        else if ((e & BB) != 0) SetPhase(PRIZES);
        break;
      case PRIZES:
        if ((e & (AB | BB)) != 0) SetPhase(SHOP);
        break;
      case SCRATCH: {
        if ((e & BB) != 0) { wipe = R0; SetPhase(REVEAL); break; }
        int dx = ((k & RB) != 0 ? 1 : 0) - ((k & LB) != 0 ? 1 : 0), dy = ((k & DB) != 0 ? 1 : 0) - ((k & UB) != 0 ? 1 : 0);
        if ((k & (UB | DB | LB | RB)) != 0) mv++; else mv = 0;
        int sp = mv > 8 ? 2 : 1;
        cx = Math.Max(2, Math.Min(61, cx + dx * sp)); cy = Math.Max(8, Math.Min(49, cy + dy * sp));
        scr = 0;
        if ((k & AB) == 0) armed = 1;
        else if (armed != 0 && Brush() != 0) {
          scr = 1; if ((t & 3) == 0) Beeper.Tone(90 + (t & 12) * 4, 15, 15);
          CheckSpots();
        }
        break;
      }
      case REVEAL:
        mask[wipe] = PM; mask[wipe + 1] = PM; wipe += 2;
        if (wipe > R1) Finish();
        break;
      case RESULT:
        if ((e & AB) != 0) { if (Chips.Balance() >= price) Buy(); else ToShop(); }
        else if ((e & BB) != 0) ToShop();
        break;
    }
  }

  // ---- drawing ----
  // (the same pixels as the JS core; glyphs become bit masks and the seven its runs once, and the screen's own Set /
  // Fill do the clipping, as px / fill do in JS)
  static int[] FMASK, FIDX, POP; static int[][] SRUNS;
  static void Prepare() {
    if (FMASK != null) return;
    FMASK = new int[FONT.Length];
    for (int f = 0; f < FONT.Length; f++) { int m = 0; for (int i = 0; i < 15; i++) if (FONT[f][i] == '1') m |= 1 << i; FMASK[f] = m; }
    FIDX = new int[128];
    for (int c = 0; c < 128; c++) { int f = FONTCH.IndexOf((char)c); FIDX[c] = f < 0 ? FONTCH.Length - 1 : f; }
    POP = new int[256];
    for (int i = 0; i < 256; i++) { int n = 0; for (int v = i; v != 0; v >>= 1) n += v & 1; POP[i] = n; }
    SRUNS = new int[11][];
    int[] tmp = new int[6];
    for (int r = 0; r < 11; r++) {
      string row = SEVEN[r]; int n = 0, q = 0;
      while (q < 11) {
        if (row[q] != '#') { q++; continue; }
        int q0 = q; while (q < 11 && row[q] == '#') q++;
        tmp[n++] = (q0 << 8) | (q - q0);
      }
      int[] rr = new int[n]; for (int i = 0; i < n; i++) rr[i] = tmp[i]; SRUNS[r] = rr;
    }
  }
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
  void Seven(int x, int y, bool v) {
    for (int r = 0; r < 11; r++) { int[] rr = SRUNS[r]; for (int i = 0; i < rr.Length; i++) sc.Fill(x + (rr[i] >> 8), y + r, rr[i] & 255, 1, v); }
  }
  void Latex(int x, int y, int w, int h) {
    for (int j = y; j < y + h; j++) sc.Fill(x, j, w, 1, (j & 1) == 0);
  }
  void Coin(int x, int y) {
    Fill(x - 1, y - 3, 3, 1, false); Fill(x - 2, y - 2, 5, 1, false); Fill(x - 3, y - 1, 7, 3, false);
    Fill(x - 2, y + 2, 5, 1, false); Fill(x - 1, y + 3, 3, 1, false);
    Fill(x - 1, y - 2, 3, 1, true); Fill(x - 2, y - 1, 5, 3, true); Fill(x - 1, y + 2, 3, 1, true);
    sc.Set(x, y, false);
  }
  void DrawSpots() {
    bool blink = phase == RESULT && tier >= 0 && ((t >> 2) & 1) == 1;
    for (int i = 0; i < nsym; i++) {
      int x0 = SpotX(i), y0 = SpotY(i);
      if (kind == 0) {
        string s = (AM[val[i]] * price).ToString(); bool hi = blink && val[i] == tier;
        if (hi) Fill(x0 + 2, y0 + 4, 16, 7, true);
        Text(x0 + 10 - (Tw(s) >> 1), y0 + 5, s, !hi);
      } else {
        string s = (AM[amt[i]] * price).ToString(); bool hi = blink && val[i] == 7;
        if (hi) Fill(x0 + 2, y0 + 1, 16, 19, true);
        if (val[i] == 7) Seven(x0 + 5, y0 + 2, !hi); else Big(x0 + 7, y0 + 2, val[i].ToString(), true);
        Text(x0 + 10 - (Tw(s) >> 1), y0 + 14, s, !hi);
      }
    }
  }
  void DrawCoating() {
    for (int r = R0; r <= R1; r++) {
      int m = mask[r];
      if (m == PM) continue;
      if (m == 0) { sc.Fill(2, r * 2, 60, 1, true); sc.Fill(2, r * 2 + 1, 60, 1, false); continue; }
      int c = 1;
      while (c <= 30) {
        if (((m >> c) & 1) != 0) { c++; continue; }
        int c0 = c; while (c <= 30 && ((m >> c) & 1) == 0) c++;
        sc.Fill(c0 * 2, r * 2, (c - c0) * 2, 1, true); sc.Fill(c0 * 2, r * 2 + 1, (c - c0) * 2, 1, false);
      }
    }
  }
  void DrawTicket() {
    Fill(0, 0, 64, 7, true); Text(2, 1, NAME[kind], false);
    string ps = price.ToString() + (price == 1 ? " CHIP" : " CHIPS"); Text(62 - Tw(ps), 1, ps, false);
    Box(1, 7, 62, 44);
    DrawSpots(); DrawCoating();
    if (phase == SCRATCH) Coin(cx, cy + (scr != 0 && (t & 1) != 0 ? 1 : 0));
    if (phase == RESULT) {
      if (prize > 0) {
        string s = "WIN " + prize.ToString();
        if (pt < 90 && ((t >> 2) & 1) == 1) { Fill(30 - (Tw(s) >> 1), 51, Tw(s) + 4, 7, true); Text(32 - (Tw(s) >> 1), 52, s, false); }
        else Centre(52, s);
      } else Centre(52, "NO WIN");
      int k = (t >> 5) % 3;
      Centre(58, msgT > 0 ? msg : k == 0 ? "A: AGAIN" : k == 1 ? "B: SHOP" : "CHIPS " + Chips.Balance().ToString());
      if (prize >= 50 * price && pt < 24 && ((t >> 2) & 1) == 1)
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) sc.Set(x, y, !sc.Get(x, y));
    } else {
      Centre(52, "CHIPS " + Chips.Balance().ToString());
      Centre(58, msgT > 0 ? msg : ((t >> 5) & 1) == 0 ? "A: SCRATCH" : "B: REVEAL ALL");
    }
  }
  void DrawShop() {
    int chips = Chips.Balance();
    Text(1, 1, "CHIPS " + chips.ToString(), true); Text(63 - Tw("SHOP"), 1, "SHOP", true);
    Box(2, 8, 60, 36);
    Text(5, 11, "<", true); Text(56, 11, ">", true); Centre(11, NAME[kind]);
    if (kind == 0) {
      for (int i = 0; i < 3; i++) Box(10 + i * 16, 18, 13, 9);
      Text(13, 20, "20", true); Latex(27, 19, 11, 7); Text(45, 20, "20", true);
    } else {
      Box(11, 17, 13, 13); Latex(12, 18, 11, 11); Seven(27, 18, true); Box(40, 17, 13, 13); Latex(41, 18, 11, 11);
    }
    Centre(31, TAG[kind]);
    Centre(37, "TOP " + (500 * PRICES[pi]).ToString());
    Text(2, 47, "PRICE", true);
    for (int j = 0; j < 3; j++) {
      int x = 30 + j * 12; string s = PRICES[j].ToString();
      if (j == pi) { Fill(x - 2, 45, 7, 9, true); Text(x, 47, s, false); }
      else { Text(x, 47, s, true); if (PRICES[j] > chips) Fill(x - 1, 49, 5, 1, true); }
    }
    int k = (t >> 5) % 4;
    Centre(57, msgT > 0 ? msg : k == 0 ? "A: BUY" : k == 1 ? "< >: TICKET" : k == 2 ? "UP DOWN: PRICE" : "B: PRIZES");
    if (phase == BROKE) {
      Fill(8, 17, 48, 19, false); Box(8, 17, 48, 19);
      Centre(21, "OUT OF"); Centre(28, "CHIPS");
      Fill(0, 55, 64, 9, false); Centre(57, ((t >> 5) & 1) == 1 ? "MORE TOMORROW" : "COME BACK");
    }
  }
  void DrawPrizes() {
    Fill(0, 0, 64, 7, true); Text(1, 1, NAME[kind], false);
    int n = kind == 0 ? 8 : 9, sum = 0;
    for (int i = 0; i < n; i++) sum += kind == 0 ? AM[i] * M3W[i] : L7T[i] * L7W[i];
    int rt = (sum * 1000 + 32768) / 65536; string rs = (rt / 10).ToString() + "." + (rt % 10).ToString() + "%";
    Text(63 - Tw(rs), 1, rs, false);
    int pr = PRICES[pi];
    for (int i = 0; i < n; i++) {
      int j = n - 1 - i, w = kind == 0 ? M3W[j] : L7W[j], m = kind == 0 ? AM[j] : L7T[j], y = 9 + i * 6;
      Text(2, y, (m * pr).ToString(), true);
      string os = "1 IN " + ((65536 + (w >> 1)) / w).ToString(); Text(63 - Tw(os), y, os, true);
    }
  }
  void DrawTitle() {
    Big(4, 3, "SCRATCH", true);
    Box(10, 17, 44, 22);
    Seven(14, 22, true);
    Latex(28, 19, 24, 18);
    Fill(30, 24, 13, 9, false); Text(33, 26, "50", true);
    Coin(46, 30);
    Centre(43, msgT > 0 ? msg : "1 2 OR 5 CHIPS");
    if (((t >> 4) & 1) != 0) Centre(55, "A: START");
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    switch (phase) {
      case TITLE: DrawTitle(); break;
      case SHOP: case BROKE: DrawShop(); break;
      case PRIZES: DrawPrizes(); break;
      default: DrawTicket(); break;
    }
  }
}
