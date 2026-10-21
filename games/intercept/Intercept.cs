// INTERCEPT for the VMX: a missile-defence game. A line-for-line port of microgames/ui/games/intercept_core.js to
// C# 1.0, so it plays exactly the same (same random numbers, same doubles); tools/parity checks that frame by frame.
// The D-pad (held) steers the crosshair; A fires from the nearest base with ammo (as POCKET ARCADE vmx/games.js).
using System;
using Microsoft.Vmx;

public class Intercept : Game {
  const int W = 64, H = 64, GROUND = 58;
  static readonly int[] CITY_X = { 9, 16, 23, 40, 47, 54 }, BASE_X = { 3, 32, 61 };
  const int AMMO = 10, BURST_R = 6, BURST_T = 30, SHOT_SPEED = 3;
  const int NONE = -1, AUTO = 3;                 // fire: NONE, AUTO or a base 0..2

  // BURST_R * Math.sin(Math.PI * Math.min(1, t / BURST_T)) as V8 computes it (its sin can differ from C's by an ulp)
  static readonly double[] BURST_RT = { 0, 0.6271707796059207, 1.247470144906556, 1.8541019662496843, 2.440419858454801,
    2.9999999999999996, 3.526711513754839, 4.014783638153149, 4.458868952864366, 4.854101966249685, 5.196152422706632,
    5.481272745855605, 5.706339097770921, 5.868885604402834, 5.967131372209639, 6, 5.96713137220964, 5.868885604402834,
    5.706339097770922, 5.481272745855605, 5.196152422706632, 4.854101966249685, 4.458868952864367, 4.014783638153148,
    3.5267115137548393, 2.9999999999999996, 2.4404198584548, 1.8541019662496852, 1.247470144906556, 0.6271707796059224,
    7.347880794884119e-16 };

  uint rs;                                       // the JS core's rng(): an LCG, divided down to [0, 1)
  double Rnd() { rs = rs * 1664525u + 1013904223u; return rs / 4294967296.0; }

  // V8's Math.hypot (normalised Kahan sum), not sqrt(x*x + y*y): they differ in the last bit
  static double Hypot(double a, double b) {
    a = Math.Abs(a); b = Math.Abs(b);
    double max = 0; if (a > max) max = a; if (b > max) max = b;
    if (max == 0) return 0;
    double sum = 0, comp = 0, n, summand, pre;
    n = a / max; summand = n * n - comp; pre = sum + summand; comp = (pre - sum) - summand; sum = pre;
    n = b / max; summand = n * n - comp; pre = sum + summand; comp = (pre - sum) - summand; sum = pre;
    return Math.Sqrt(sum) * max;
  }

  int t, score, nextBonus, wave, banner, over, toLaunch, launchIn;
  double cx, cy, speed;
  string bannerText;
  bool[] cities = new bool[6], bases = new bool[3];
  int[] ammo = new int[3];
  bool demo; int best;

  // incoming missiles, in the order the JS array holds them (grown when full)
  double[] eX0 = new double[32], eY0 = new double[32], eX = new double[32], eY = new double[32];
  double[] eVx = new double[32], eVy = new double[32], eTx = new double[32], eSplit = new double[32];
  bool[] eDead = new bool[32]; int nE;
  // counter-missiles (at most 30 a wave)
  double[] sX0 = new double[32], sY0 = new double[32], sX = new double[32], sY = new double[32];
  double[] sTx = new double[32], sTy = new double[32], sVx = new double[32], sVy = new double[32];
  int nS;
  // bursts; bHasR false: r still undefined in JS (made this tick, after the radius update)
  double[] bX = new double[32], bY = new double[32], bR = new double[32]; int[] bT = new int[32];
  bool[] bHasR = new bool[32]; int nB;
  int[] tg = new int[9]; int nTg;

  public override bool UsesB { get { return false; } }

  public override void Start(int seed, bool demo) {
    this.demo = demo;
    NewGame(seed);
  }
  void NewGame(int seed) {
    rs = (uint)seed; t = 0; cx = 32; cy = 30;
    for (int i = 0; i < 6; i++) cities[i] = true;
    score = 0; nextBonus = 10000; wave = 0; banner = 0; bannerText = ""; over = 0;
    StartWave();
  }
  void StartWave() {
    wave++; for (int i = 0; i < 3; i++) { ammo[i] = AMMO; bases[i] = true; }
    toLaunch = 10 + wave * 3; launchIn = 40; nE = 0; nS = 0; nB = 0;
    speed = 0.12 + wave * 0.025; banner = 50; bannerText = "WAVE " + wave.ToString();
  }
  void Targets() {                               // standing cities and bases, as x positions
    nTg = 0;
    for (int i = 0; i < 6; i++) if (cities[i]) tg[nTg++] = CITY_X[i];
    for (int i = 0; i < 3; i++) if (bases[i]) tg[nTg++] = BASE_X[i];
  }

  static double[] GrowD(double[] a) { double[] b = new double[a.Length * 2]; for (int i = 0; i < a.Length; i++) b[i] = a[i]; return b; }
  static bool[] GrowB(bool[] a) { bool[] b = new bool[a.Length * 2]; for (int i = 0; i < a.Length; i++) b[i] = a[i]; return b; }
  static int[] GrowI(int[] a) { int[] b = new int[a.Length * 2]; for (int i = 0; i < a.Length; i++) b[i] = a[i]; return b; }

  void Launch(double x0, double y0, double tx) {
    if (nE == eX.Length) {
      eX0 = GrowD(eX0); eY0 = GrowD(eY0); eX = GrowD(eX); eY = GrowD(eY);
      eVx = GrowD(eVx); eVy = GrowD(eVy); eTx = GrowD(eTx); eSplit = GrowD(eSplit); eDead = GrowB(eDead);
    }
    double dx = tx - x0, dy = GROUND - y0, len = Hypot(dx, dy);
    int i = nE++;
    eX0[i] = x0; eY0[i] = y0; eX[i] = x0; eY[i] = y0; eVx[i] = dx / len * speed; eVy[i] = dy / len * speed; eTx[i] = tx;
    eSplit[i] = wave >= 3 && Rnd() < 0.25 ? 20 + Rnd() * 15 : 0; eDead[i] = false;
  }
  void AddBurst(double x, double y) {
    if (nB == bX.Length) { bX = GrowD(bX); bY = GrowD(bY); bR = GrowD(bR); bT = GrowI(bT); bHasR = GrowB(bHasR); }
    bX[nB] = x; bY[nB] = y; bT[nB] = 0; bR[nB] = 0; bHasR[nB] = false; nB++;
  }
  int NearestBase(double x) {                    // nearest base with ammo; ties: the first (a stable sort)
    int b = NONE;
    for (int i = 0; i < 3; i++) if (bases[i] && ammo[i] > 0 && (b < 0 || Math.Abs(BASE_X[i] - x) < Math.Abs(BASE_X[b] - x))) b = i;
    return b;
  }

  public override void Tick(int held, int pressed) {
    if (demo) { Demo(); Step(wantDx, wantDy, wantFire); }
    else {
      int dx = ((held & Buttons.Right) != 0 ? 1 : 0) - ((held & Buttons.Left) != 0 ? 1 : 0);
      int dy = ((held & Buttons.Down) != 0 ? 1 : 0) - ((held & Buttons.Up) != 0 ? 1 : 0);
      Step(dx, dy, (pressed & Buttons.A) != 0 ? AUTO : NONE);
    }
    if (score > best) { best = score; Scores.Report(score, 0, 0, 0); }
  }

  void Step(double idx, double idy, int fire) {
    t++;
    if (banner > 0) banner--;
    if (over > 0) { if (--over == 0) NewGame((int)Math.Floor(Rnd() * 1e9)); return; }
    cx = Math.Max(1, Math.Min(62, cx + idx * 1.6));
    cy = Math.Max(4, Math.Min(GROUND - 6, cy + idy * 1.6));
    if (fire != NONE) {
      int b = fire;
      if (b == AUTO) b = NearestBase(cx);
      if (b >= 0 && bases[b] && ammo[b] > 0) {
        ammo[b]--;
        double x0 = BASE_X[b], y0 = GROUND - 3, len = Hypot(cx - x0, cy - y0);
        int i = nS++;
        sX0[i] = x0; sY0[i] = y0; sX[i] = x0; sY[i] = y0; sTx[i] = cx; sTy[i] = cy;
        sVx[i] = (cx - x0) / len * SHOT_SPEED; sVy[i] = (cy - y0) / len * SHOT_SPEED;
      }
    }
    // launches: salvos of 1-3
    if (toLaunch > 0 && --launchIn <= 0) {
      Targets();
      int n = Math.Min(toLaunch, 1 + (int)Math.Floor(Rnd() * 3));
      for (int k = 0; k < n && nTg > 0; k++) { double x0 = 2 + Rnd() * 60; Launch(x0, 0, tg[(int)Math.Floor(Rnd() * nTg)]); }
      toLaunch -= n; launchIn = Math.Max(30, 90 - wave * 8) + (int)Math.Floor(Rnd() * 40);
    }
    // counter-missiles fly to their mark and burst
    int m = 0;
    for (int i = 0; i < nS; i++) {
      sX[i] += sVx[i]; sY[i] += sVy[i];
      if ((sTx[i] - sX[i]) * sVx[i] + (sTy[i] - sY[i]) * sVy[i] <= 0) { AddBurst(sTx[i], sTy[i]); continue; }
      sX0[m] = sX0[i]; sY0[m] = sY0[i]; sX[m] = sX[i]; sY[m] = sY[i]; sTx[m] = sTx[i]; sTy[m] = sTy[i]; sVx[m] = sVx[i]; sVy[m] = sVy[i]; m++;
    }
    nS = m;
    // bursts grow then shrink; they take out missiles (which burst too: chains)
    for (int i = 0; i < nB; i++) { bT[i]++; bR[i] = BURST_RT[Math.Min(BURST_T, bT[i])]; bHasR[i] = true; }
    for (int i = 0; i < nE; i++) {               // nE grows as MIRVs split: the new ones move this tick too
      eX[i] += eVx[i]; eY[i] += eVy[i];
      if (eSplit[i] != 0 && eY[i] >= eSplit[i]) {  // MIRV: splits into 2-3
        eSplit[i] = 0; Targets();
        for (int k = 0; k < 1 + (int)Math.Floor(Rnd() * 2) && nTg > 0; k++) Launch(eX[i], eY[i], tg[(int)Math.Floor(Rnd() * nTg)]);
      }
      for (int j = 0; j < nB; j++)
        if (bHasR[j] && Hypot(eX[i] - bX[j], eY[i] - bY[j]) <= bR[j] + 0.5) {
          eDead[i] = true; score += 25 * Math.Min(6, (wave + 1) >> 1); AddBurst(eX[i], eY[i]); break;
        }
      if (!eDead[i] && eY[i] >= GROUND - 2) {    // impact: the city or base it was aimed at
        eDead[i] = true; AddBurst(eX[i], GROUND - 2);
        for (int c = 0; c < 6; c++) if (Math.Abs(CITY_X[c] - eTx[i]) < 1) cities[c] = false;
        for (int c = 0; c < 3; c++) if (Math.Abs(BASE_X[c] - eTx[i]) < 1) { bases[c] = false; ammo[c] = 0; }
      }
    }
    m = 0;
    for (int i = 0; i < nE; i++) {
      if (eDead[i]) continue;
      eX0[m] = eX0[i]; eY0[m] = eY0[i]; eX[m] = eX[i]; eY[m] = eY[i]; eVx[m] = eVx[i]; eVy[m] = eVy[i];
      eTx[m] = eTx[i]; eSplit[m] = eSplit[i]; eDead[m] = false; m++;
    }
    nE = m;
    m = 0;
    for (int i = 0; i < nB; i++) {
      if (bT[i] >= BURST_T) continue;
      bX[m] = bX[i]; bY[m] = bY[i]; bR[m] = bR[i]; bT[m] = bT[i]; bHasR[m] = bHasR[i]; m++;
    }
    nB = m;
    if (score >= nextBonus) {                    // bonus city
      nextBonus += 10000;
      for (int i = 0; i < 6; i++) if (!cities[i]) { cities[i] = true; banner = 40; bannerText = "BONUS CITY"; break; }
    }
    int standing = 0; for (int i = 0; i < 6; i++) if (cities[i]) standing++;
    if (standing == 0) { over = 120; banner = 120; bannerText = "THE END"; return; }
    if (toLaunch <= 0 && nE == 0 && nS == 0 && nB == 0) {
      int bonus = (ammo[0] + ammo[1] + ammo[2]) * 5 + standing * 100;
      score += bonus; StartWave(); banner = 60; bannerText = "+" + bonus.ToString();
    }
  }

  // ---- drawing
  static void Px(Screen s, double x, double y, bool on) {
    int ix = (int)Math.Floor(x + 0.5), iy = (int)Math.Floor(y + 0.5);      // JS Math.round
    if (ix >= 0 && iy >= 0 && ix < W && iy < H) s.Set(ix, iy, on);
  }
  static void Line(Screen s, double x0, double y0, double x1, double y1) {
    int n = (int)Math.Max(1, Math.Ceiling(Hypot(x1 - x0, y1 - y0)));
    for (int i = 0; i <= n; i++) Px(s, x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, true);
  }
  // the JS core's font has the same glyphs as the VMX font, but fewer: the rest draw as spaces (THE END has no H)
  const string GLYPHS = "ABCDEGIMNORSTUVWXY0123456789!+";
  static string[] glyphStr;                      // each glyph as a one-letter string, made once
  static void Text(Screen s, int x, int y, string str) {
    if (glyphStr == null) { glyphStr = new string[GLYPHS.Length]; for (int k = 0; k < GLYPHS.Length; k++) glyphStr[k] = GLYPHS.Substring(k, 1); }
    for (int i = 0; i < str.Length; i++, x += 4) { int k = GLYPHS.IndexOf(str[i]); if (k >= 0) s.Text(x, y, glyphStr[k], true); }
  }
  static readonly int[] SKY_DX = { -2, -1, 0, 1, 2 }, SKY_H = { 2, 3, 4, 2, 3 };
  static readonly int[] CROSS_DX = { -2, -1, 1, 2, 0, 0, 0, 0 }, CROSS_DY = { 0, 0, 0, 0, -2, -1, 1, 2 };

  public override void Draw(Screen s) {
    s.Clear();
    for (int x = 0; x < W; x++) { s.Set(x, GROUND + 1, true); if (x % 2 == 0) s.Set(x, GROUND + 3, true); s.Set(x, H - 1, true); }
    for (int i = 0; i < 6; i++) {                // cities: a little skyline each
      int x = CITY_X[i];
      if (cities[i]) { for (int k = 0; k < 5; k++) for (int y = 0; y < SKY_H[k]; y++) s.Set(x + SKY_DX[k], GROUND - y, true); }
      else { s.Set(x - 2, GROUND, true); s.Set(x, GROUND, true); s.Set(x + 2, GROUND, true); }
    }
    for (int i = 0; i < 3; i++) {                // bases: a mound with the ammo left as dots
      if (!bases[i]) continue;
      int x = BASE_X[i];
      for (int dx = -3; dx <= 3; dx++) for (int y = 0; y < 3 - Math.Abs(dx) / 2; y++) Px(s, x + dx, GROUND - y, true);
      for (int k = 0; k < ammo[i]; k++) { Px(s, x - 4 + (k % 5) * 2, GROUND + 2 + (k / 5) * 2, false); Px(s, x - 4 + (k % 5) * 2, GROUND + 2, true); }
    }
    for (int i = 0; i < nE; i++) { Line(s, eX0[i], eY0[i], eX[i], eY[i]); if (t % 4 < 2) Px(s, eX[i] + 1, eY[i], true); }
    for (int i = 0; i < nS; i++) {
      Line(s, sX0[i], sY0[i], sX[i], sY[i]);
      Px(s, sTx[i] - 1, sTy[i] - 1, true); Px(s, sTx[i] + 1, sTy[i] + 1, true); Px(s, sTx[i] + 1, sTy[i] - 1, true); Px(s, sTx[i] - 1, sTy[i] + 1, true);
    }
    for (int i = 0; i < nB; i++) {
      double r = bR[i]; int q = bT[i] >> 2;      // r is 0 while JS's is undefined (bu.r || 0)
      for (double y = -r; y <= r; y++) for (double x = -r; x <= r; x++) {
        if (x * x + y * y > r * r) continue;
        if ((((int)(x + y + q)) & 1) != 0 || x * x + y * y > (r - 1) * (r - 1)) Px(s, bX[i] + x, bY[i] + y, true);
      }
    }
    if (over == 0) for (int k = 0; k < 8; k++) Px(s, cx + CROSS_DX[k], cy + CROSS_DY[k], true);
    if (banner > 0) Text(s, (W - Screen.TextWidth(bannerText)) >> 1, 20, bannerText);
    Text(s, 1, 0, score.ToString().PadLeft(6, '0'));
  }

  // ---- the demo player (as the JS core's demo()): the first uncovered missile below y 20, led so the burst meets it.
  // (The JS core's sort by time-to-land sits inside a comment there, so it takes the first in array order; kept.)
  double wantDx, wantDy; int wantFire;
  bool Covered(int i) {
    for (int j = 0; j < nB; j++) if (Hypot(eX[i] - bX[j], eY[i] - bY[j]) < BURST_R + 2) return true;
    for (int j = 0; j < nS; j++) if (Hypot(eX[i] + eVx[i] * 15 - sTx[j], eY[i] + eVy[i] * 15 - sTy[j]) < BURST_R) return true;
    return false;
  }
  void Demo() {
    wantDx = 0; wantDy = 0; wantFire = NONE;
    if (over > 0) return;
    int e = -1;
    for (int i = 0; i < nE && e < 0; i++) if (!Covered(i) && eY[i] > 20) e = i;
    if (e < 0) { wantDx = (32 - cx) / 20; wantDy = (28 - cy) / 20; return; }
    int b = NearestBase(eX[e]);
    if (b < 0) return;
    double lx = eX[e], ly = eY[e];
    for (int i = 0; i < 3; i++) {
      double tt = Hypot(lx - BASE_X[b], ly - GROUND) / SHOT_SPEED + 8;
      lx = eX[e] + eVx[e] * tt; ly = Math.Min(GROUND - 6, eY[e] + eVy[e] * tt);
    }
    double dx = lx - cx, dy = ly - cy, d = Hypot(dx, dy);
    wantDx = Math.Max(-1, Math.Min(1, dx / 4)); wantDy = Math.Max(-1, Math.Min(1, dy / 4));
    if (d < 2.5 && t % 6 == 0) wantFire = b;
  }
}
