// FROG RACE for the VMX: a line-for-line port of microgames/ui/games/frogs_core.js to C# 1.0, so it plays exactly
// the same (same random numbers, same order of events); tools/parity checks that frame by frame.
// 4-6 named frogs a race, each with posted odds o:1 from the ladder 1..15:1 (set from a hidden strength). A frog's
// exact chance to win is its odds' share of the book: weight 720720 / (o + 1) over S = the sum of the weights, so
// every WIN bet returns 720720 / S on average; house edge = 1 - 720720 / S, kept between 9.50% and 11.11% and shown
// on the race card. The whole finishing order is drawn first (place by place from the weights left, exact 30-bit
// rejection draws); the hops are scripted to land on that result. A winning bet returns stake x (odds + 1).
// Up/Down choose a frog, Left/Right stake 0-20 (repeats when held; the total never over the stash), A races, B clears.
// VMX CASINO: the stakes are taken with Chips.Bet when the race starts, the winning bet paid with Chips.Pay.
// BOG HOP has no C# port: only its frog sprites and pond look are ported here.
using System;
using Microsoft.Vmx;

public class Frogs : Game {
  const string FONTCH = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789:!-<>+/^.% ";
  static readonly int[] FONT = {   // the core's 3x5 glyph strings as 15-bit numbers, bit 14 = top left
    11245, 27566, 14627, 27502, 31143, 31140, 14699, 23533, 29847, 4714, 23469, 18727,
    24557, 27501, 11114, 27556, 11123, 27565, 14478, 29842, 23407, 23402, 23549, 23213,
    23186, 29351, 31599, 11415, 25255, 25230, 23497, 31118, 14831, 29330, 31727, 31694,
    1040, 9346, 448, 5393, 17492, 1488, 4772, 11834, 2, 21157, 0 };
  static readonly int[] CHIP = { 14, 21, 27, 21, 14 };
  static readonly int[] SIT = { 54, 126, 255, 511, 198 }, AIR = { 13, 63, 127, 254, 387 };
  static readonly int[] SITR = { 254, 510, 1023, 2047, 2047, 2047 }, AIRR = { 63, 255, 511, 1023, 2047, 2047 };
  static readonly int[] SITO = { 54, 66, 129, 257, 198 }, AIRO = { 13, 51, 65, 254, 387 };
  static readonly string[] NAMES = { "HOPPS", "LILY", "BOGEY", "CROAK", "RIBBIT", "TOADY", "GULP", "MUDDY", "REEDS",
    "SLICK", "BUBBA", "JUMBO", "PEEPER", "BOING", "WARTS", "DUKE", "ZIPPY", "MOSSY", "PUDDLE", "DASH", "SKIPPY", "FLICK",
    "SWAMPY", "NEWTON", "LEAPY", "GOOEY", "TADDY", "SPLASH" };
  const int NN = 28;
  static readonly int[] LAD = { 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 15 };
  const int K = 720720, SLO = 796395, SHI = 810810;
  const int PADS = 10;
  static readonly string[] PLACE = { "1ST", "2ND", "3RD" };
  const int TITLE = 0, CARD = 1, RACE = 2, RESULT = 3, BROKE = 4;
  static readonly int[] JINGLE = { 784, 988, 1175, 1568 };

  // game state (the JS object g)
  uint rs; int t, phase, pt, n, S, edge, cur, rt, nfin, winT, lead, win, best, rep, dn, di, msgT;
  int[] nm = new int[NN], name = new int[6], odds = new int[6], w = new int[6], stake = new int[6], order = new int[6],
    place = new int[6], fin = new int[6], prof = new int[3], hs = new int[60], hz = new int[60], hm = new int[6],
    hk = new int[6], hj = new int[6], dq = new int[2], da = new int[2];
  int[] fs = new int[6], left = new int[6], wk = new int[10];   // scratch (JS locals)
  bool photo;
  string msg = "";
  bool auto_, demo, pUp, pDown, pLeft, pRight, pA, pB;
  Screen sc;

  public override bool UsesB { get { return true; } }

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }
  int R30(int total) {
    int lim = 1073741824 - 1073741824 % total;
    int r = (Rnd() << 14) | (Rnd() >> 2);
    while (r >= lim) r = (Rnd() << 14) | (Rnd() >> 2);
    return r % total;
  }

  public override void Start(int seed, bool demo) {
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; n = 4;
    for (int i = 0; i < NN; i++) nm[i] = i;
    for (int i = 0; i < 6; i++) { name[i] = 0; odds[i] = 0; w[i] = 0; stake[i] = 0; order[i] = 0; place[i] = 0; fin[i] = 0; hm[i] = 0; hk[i] = 0; hj[i] = 0; }
    for (int i = 0; i < 60; i++) { hs[i] = 0; hz[i] = 0; }
    S = 0; edge = 0; cur = 0; rt = 0; nfin = 0; winT = 0; photo = false; lead = 0; win = 0; best = 0; rep = 0;
    dn = 0; di = 0; msg = ""; msgT = 0; auto_ = false; pUp = pDown = pLeft = pRight = pA = pB = false;
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 90; }
  int Total() { int s = 0; for (int i = 0; i < n; i++) s += stake[i]; return s; }
  static int HopT(int z) { return z == 1 ? 7 : 11; }
  static int PadX(int j) { return 3 + 5 * j; }

  // ---- a race ----
  void Field() {
    for (int tries = 0; tries < 200; tries++) {
      n = 4 + Rnd() % 3;
      int s = 0;
      for (int i = 0; i < n; i++) fs[i] = 2 + Rnd() % 9;
      int f = Rnd() % n;
      fs[f] += 6 + Rnd() % 10;
      for (int i = 0; i < n; i++) s += fs[i];
      int B = 0;
      for (int i = 0; i < n; i++) {
        int bst = 0, bd = 1 << 30;
        for (int l = 0; l < 11; l++) {
          int d = Math.Abs((LAD[l] + 1) * fs[i] * 10 - 9 * s);
          if (d < bd) { bd = d; bst = l; } else break;
        }
        odds[i] = LAD[bst]; B += K / (LAD[bst] + 1);
      }
      if (B >= SLO && B <= SHI) return;
    }
    n = 4; odds[0] = 1; odds[1] = 3; odds[2] = 4; odds[3] = 5;
  }
  void NewRace() {
    Field();
    for (int i = 0; i < n; i++) {
      int j = i + Rnd() % (NN - i), tt = nm[i]; nm[i] = nm[j]; nm[j] = tt;
      name[i] = nm[i];
    }
    S = 0;
    for (int i = 0; i < n; i++) { w[i] = K / (odds[i] + 1); S += w[i]; stake[i] = 0; }
    edge = 1000 - (1441440000 / S + 1) / 2;
    for (int i = 0; i < n; i++) left[i] = w[i];
    int rest = S;
    for (int p = 0; p < n; p++) {
      int r = R30(rest), i = 0;
      while (r >= left[i]) { r -= left[i]; i++; }
      order[p] = i; place[i] = p; rest -= left[i]; left[i] = 0;
    }
    fin[order[0]] = 240 + Rnd() % 60;
    photo = Rnd() % 4 == 0;
    fin[order[1]] = fin[order[0]] + (photo ? 1 : 3 + Rnd() % 14);
    for (int p = 2; p < n; p++) fin[order[p]] = fin[order[p - 1]] + 2 + Rnd() % 16;
    bool closer = Rnd() % 2 == 0; int fast = 1 + Rnd() % (n - 1);
    for (int p = 0; p < n; p++) {
      int i = order[p];
      for (int s = 0; s < 3; s++) prof[s] = 1 + Rnd() % 8;
      if (p == 0 && closer) { prof[0] = 8; prof[1] = 5; prof[2] = 1; }
      if (p == fast) { prof[0] = 1; prof[1] = 3; prof[2] = 9; }
      int rem = PADS, m = 0, sumD = 0;
      while (rem > 0) {
        int z = rem >= 2 && Rnd() % 4 == 0 ? 2 : 1;
        hz[i * 10 + m] = z; rem -= z; sumD += HopT(z); m++;
      }
      hm[i] = m;
      int extra = fin[i] - sumD - 2 * m;
      int sw = 0, pads = 0;
      for (int k = 0; k < m; k++) {
        wk[k] = prof[pads * 3 / PADS] * 4 + Rnd() % 3; sw += wk[k]; pads += hz[i * 10 + k];
      }
      int used = 0;
      for (int k = 0; k < m; k++) { wk[k] = 2 + extra * wk[k] / sw; used += wk[k] - 2; }
      wk[0] += extra - used;
      int tt = 0;
      for (int k = 0; k < m; k++) { tt += wk[k]; hs[i * 10 + k] = tt; tt += HopT(hz[i * 10 + k]); }
      hk[i] = 0; hj[i] = 0;
    }
    rt = 0; nfin = 0; winT = 0; lead = -1; win = 0;
  }
  void ToCard() {
    int ch = Chips.Balance();
    if (ch < 1) { SetPhase(BROKE); return; }
    NewRace();
    cur = 0; rep = 0;
    SetPhase(CARD);
    if (auto_) {
      int fav = 0, lng = 0;
      for (int i = 1; i < n; i++) { if (odds[i] < odds[fav]) fav = i; if (odds[i] > odds[lng]) lng = i; }
      dq[0] = fav; da[0] = Math.Min(2 + Rnd() % 7, ch); dn = 1; di = 0;
      if (Rnd() % 3 == 0 && ch > da[0]) { dq[1] = lng; da[1] = Math.Min(1 + Rnd() % 3, ch - da[0]); dn = 2; }
    }
  }
  bool AddStake(int i, int d) {
    if (d > 0) { if (stake[i] < 20 && Total() < Chips.Balance()) { stake[i]++; return true; } }
    else if (stake[i] > 0) { stake[i]--; return true; }
    return false;
  }
  void StartRace() {
    int tot = Total();
    if (tot < 1) { Say("PLACE A BET"); return; }
    Chips.Bet(tot);
    SetPhase(RACE);
  }
  int X0f(int i) {
    int k = hk[i], j = hj[i];
    if (k < hm[i] && rt >= hs[i * 10 + k]) {
      int z = hz[i * 10 + k], e = rt - hs[i * 10 + k];
      return PadX(j) + 5 * z * e / HopT(z);
    }
    return PadX(j);
  }
  void RunRace() {
    rt++;
    for (int i = 0; i < n; i++) {
      int k = hk[i];
      if (k < hm[i] && rt == hs[i * 10 + k] + HopT(hz[i * 10 + k])) {
        hj[i] += hz[i * 10 + k]; hk[i]++;
        if (hj[i] == PADS) {
          if (nfin == 0) winT = rt;
          nfin++;
          if (!auto_) Beeper.Tone(stake[i] > 0 ? 1568 : 880, 40, 30);
        }
      }
    }
    int bx = -1;
    if (lead >= 0) bx = X0f(lead);
    for (int i = 0; i < n; i++) { int x = X0f(i); if (x > bx) { bx = x; lead = i; } }
  }
  void Settle() {
    int wn = order[0];
    win = stake[wn] * (odds[wn] + 1);
    Chips.Pay(win);
    if (win > best) { best = win; if (!demo) Scores.Report(best, 0, 0, 0); }
    SetPhase(RESULT);
  }

  public override void Tick(int held_, int pressed) {
    bool auto = demo;
    t++; pt++; if (msgT > 0) msgT--;
    bool up = !auto && (held_ & Buttons.Up) != 0, down = !auto && (held_ & Buttons.Down) != 0,
      lf = !auto && (held_ & Buttons.Left) != 0, rg = !auto && (held_ & Buttons.Right) != 0,
      a = !auto && (held_ & Buttons.A) != 0, b = !auto && (held_ & Buttons.B) != 0;
    bool eUp = up && !pUp, eDown = down && !pDown, eLeft = lf && !pLeft, eRight = rg && !pRight, eA = a && !pA, eB = b && !pB;
    pUp = up; pDown = down; pLeft = lf; pRight = rg; pA = a; pB = b;
    auto_ = auto;

    switch (phase) {
      case TITLE:
        if (eA || (auto && pt > 40)) ToCard();
        break;
      case CARD:
        if (auto) {
          if (pt > 20 && pt % 4 == 0) {
            if (di < dn) {
              int q = dq[di];
              if (cur != q) cur = cur < q ? cur + 1 : cur - 1;
              else if (stake[q] < da[di]) AddStake(q, 1);
              else di++;
            } else if (pt > 60) StartRace();
          }
        } else {
          if (eUp) { cur = (cur + n - 1) % n; Beeper.Tone(1200, 8, 20); }
          if (eDown) { cur = (cur + 1) % n; Beeper.Tone(1200, 8, 20); }
          if (lf || rg) rep++; else rep = 0;
          bool rp = rep > 12 && rep % 3 == 0;
          if (eRight || (rg && rp)) { if (AddStake(cur, 1)) Beeper.Tone(1500, 10, 25); }
          if (eLeft || (lf && rp)) { if (AddStake(cur, -1)) Beeper.Tone(1100, 10, 25); }
          if (eB) for (int i = 0; i < n; i++) stake[i] = 0;
          if (eA) StartRace();
        }
        break;
      case RACE:
        if (!auto && (pt == 1 || pt == 16)) Beeper.Tone(880, 60, 30);
        if (!auto && pt == 31) Beeper.Tone(1760, 120, 35);
        if (pt > 45) RunRace();
        if (nfin == n && rt > fin[order[n - 1]] + 30) Settle();
        break;
      case RESULT:
        if (win > 0 && !auto && pt <= 13 && (pt - 1) % 4 == 0) Beeper.Tone(JINGLE[(pt - 1) / 4], 90, 40);
        if (win == 0 && !auto && pt == 1) Beeper.Tone(300, 120, 30);
        if ((eA && pt > 10) || (auto && pt > 120)) ToCard();
        break;
      case BROKE:
        break;
    }
  }

  // ---- drawing ----
  void Px(int x, int y, bool v) { if (x >= 0 && x < 64 && y >= 0 && y < 64) sc.Set(x, y, v); }
  void Fill(int x, int y, int w, int h, bool v) {
    if (x < 0) { w += x; x = 0; } if (y < 0) { h += y; y = 0; } if (x + w > 64) w = 64 - x; if (y + h > 64) h = 64 - y;
    if (w > 0 && h > 0) sc.Fill(x, y, w, h, v);
  }
  void Text(int x, int y, string s, bool v) {
    for (int k = 0; k < s.Length; k++) {
      int f = FONTCH.IndexOf(s[k]); if (f < 0) f = FONTCH.Length - 1;
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
    for (int k = 0; k < s.Length; k++) {
      int f = FONTCH.IndexOf(s[k]); if (f < 0) f = FONTCH.Length - 1;
      int g = FONT[f];
      for (int r = 0; r < 5; r++) for (int q = 0; q < 3; q++) if (((g >> (14 - r * 3 - q)) & 1) != 0) Fill(x + q * 2, y + r * 2, 2, 2, true);
      x += 8;
    }
  }
  void Centre(int y, string s) { Text(32 - ((s.Length * 4 - 1) >> 1), y, s, true); }
  void BigCentre(int y, string s) { Big(32 - ((s.Length * 8 - 2) >> 1), y, s); }
  void Right(int y, string s) { Text(64 - s.Length * 4, y, s, true); }
  void Sprite(int x, int y, int[] rows, int w, bool v) {   // runs of lit bits drawn as fills (same pixels)
    for (int r = 0; r < rows.Length; r++) {
      int row = rows[r], q = 0;
      while (q < w) {
        if (((row >> (w - 1 - q)) & 1) == 0) { q++; continue; }
        int q0 = q;
        while (q < w && ((row >> (w - 1 - q)) & 1) != 0) q++;
        Fill(x + q0, y + r, q - q0, 1, v);
      }
    }
  }
  void Frog(int x, int y, bool air, bool dim) {
    Sprite(x - 5, y - 5, air ? AIRR : SITR, 11, false);
    Sprite(x - 4, y - 4, dim ? (air ? AIRO : SITO) : (air ? AIR : SIT), 9, true);
  }
  void Cattail(int x, int y0) { Fill(x, y0 + 4, 1, 64 - y0 - 4, true); Fill(x - 1, y0, 3, 4, true); Px(x, y0 - 1, true); }
  void Pond(int y) {
    for (int k = 0; k < 6; k++) Fill(((k * 13 + (t >> 3) * ((k & 1) != 0 ? 1 : -1)) % 64 + 64) % 64, y + 2 + (k % 2) * 2, 3, 1, true);
    Fill(8, y, 15, 1, true); Fill(9, y + 1, 13, 1, true); Px(17, y + 1, false);
    Fill(40, y, 15, 1, true); Fill(41, y + 1, 13, 1, true); Px(49, y + 1, false);
    Cattail(2, y - 18); Cattail(4, y - 12); Cattail(60, y - 16); Cattail(62, y - 10);
  }
  void DrawArt() {
    BigCentre(2, "FROG"); BigCentre(14, "RACE");
    Pond(44);
    Fill(0, 49, 64, 15, false);
    int c = t % 60;
    if (c < 16) {
      int e = c, x = 15 + 32 * e / 16, yy = 43 - 4 * 10 * e * (16 - e) / 256;
      Frog(x, yy, true, false);
    } else if (c < 30) Frog(47, 43, false, false);
    else if (c < 46) {
      int e = c - 30, x = 47 - 32 * e / 16, yy = 43 - 4 * 10 * e * (16 - e) / 256;
      Frog(x, yy, true, false);
    } else Frog(15, 43, false, false);
  }
  void DrawCard() {
    Sprite(1, 1, CHIP, 5, true); Text(8, 1, Chips.Balance().ToString(), true);
    Right(1, "BET " + Total().ToString());
    for (int i = 0; i < n; i++) {
      int y = 9 + i * 7; bool sel = i == cur;
      if (sel) Fill(0, y - 1, 64, 7, true);
      Text(1, y, NAMES[name[i]], !sel);
      string o = odds[i].ToString() + ":1";
      Text(46 - o.Length * 4, y, o, !sel);
      string s = stake[i] > 0 ? stake[i].ToString() : "-";
      Text(64 - s.Length * 4, y, s, !sel);
    }
    Fill(0, 53, 64, 1, true);
    if (msgT > 0) Centre(57, msg);
    else if (auto_ || ((t >> 6) & 1) == 0) { string e = "HOUSE EDGE " + (edge / 10).ToString(); Centre(57, e + "." + (edge % 10).ToString() + "%"); }
    else Centre(57, "A:RACE B:CLEAR");
  }
  void DrawRace() {
    int sp = 56 / n;
    if (photo && nfin > 0 && rt - winT < 2) { Fill(0, 0, 64, 64, true); return; }
    for (int y = 9; y < 64; y += 2) Px(49, y, true);
    Cattail(62, 10); Cattail(60, 24);
    for (int i = 0; i < n; i++) {
      int py = 8 + (i + 1) * sp - 2;
      Fill(0, py, 6, 2, true); Fill(50, py, 9, 2, true);
      for (int j = 1; j < PADS; j++) { int x = PadX(j); Fill(x - 2, py, 4, 1, true); Fill(x - 1, py + 1, 2, 1, true); }
      for (int k = 0; k < 2; k++) Fill(((i * 17 + k * 29 + (t >> 3) * ((i & 1) != 0 ? 1 : -1)) % 48 + 48) % 48 + 4, py + 3, 2, 1, true);
    }
    for (int i = 0; i < n; i++) {
      int py = 8 + (i + 1) * sp - 2, k = hk[i];
      int x = PadX(hj[i]), y = py - 1; bool air = false;
      if (k < hm[i] && rt >= hs[i * 10 + k]) {
        int z = hz[i * 10 + k], D = HopT(z), e = rt - hs[i * 10 + k], hh = Math.Min(sp - 4, z == 1 ? 4 : 6);
        x += 5 * z * e / D; y -= 4 * hh * e * (D - e) / (D * D); air = true;
      }
      Frog(x, y, air, stake[i] == 0);
      if (hj[i] == PADS) { Fill(57, py - 7, 5, 7, false); Text(58, py - 6, (place[i] + 1).ToString(), true); }
    }
    Fill(0, 0, 64, 6, false);
    if (pt <= 45) Centre(1, pt < 30 ? "READY" : "GO!");
    else if (nfin == 0) Centre(1, "LEAD " + NAMES[name[lead]]);
    else if (photo && rt - winT < 45) { if (((t >> 2) & 1) != 0) Centre(1, "PHOTO FINISH"); }
    else Centre(1, "WINNER " + NAMES[name[order[0]]]);
  }
  void DrawResult() {
    for (int p = 0; p < 3; p++) {
      int i = order[p]; string o = odds[i].ToString() + ":1";
      Text(1, 1 + p * 6, PLACE[p] + " " + NAMES[name[i]], true); Right(1 + p * 6, o);
    }
    Fill(0, 19, 64, 1, true);
    int y = 22;
    for (int i = 0; i < n; i++) {
      if (stake[i] == 0) continue;
      Text(1, y, NAMES[name[i]] + " " + stake[i].ToString(), true);
      Right(y, i == order[0] ? "+" + win.ToString() : "LOSE");
      y += 6;
    }
    if (y <= 40) {
      int up = (t >> 3) & 1;
      Fill(26, 50, 13, 1, true); Fill(27, 51, 11, 1, true); Px(34, 51, false);
      Frog(32, 49 - up * 3, up == 1, false);
    }
    Text(1, 58, win > 0 ? "WON " + win.ToString() : "NO WIN", true);
    if (!auto_ && ((t >> 4) & 1) != 0) Right(58, "A:NEXT");
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      DrawArt();
      if (phase == BROKE) {
        Centre(50, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Centre(57, "MORE TOMORROW");
        return;
      }
      if (msgT > 0) Centre(50, msg);
      if (((t >> 4) & 1) != 0) Centre(57, "A: START");
      return;
    }
    if (phase == CARD) DrawCard();
    else if (phase == RACE) DrawRace();
    else DrawResult();
  }
}
