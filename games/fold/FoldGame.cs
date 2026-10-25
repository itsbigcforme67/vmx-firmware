// FOLD ROOMS' rules: a line-for-line port of microgames/fold/core/fold.c (the surface, moves, signals, journal undo,
// and what a tile says), the parts the VMX game uses. The one difference: the change journal holds JOURNAL entries
// (the C: 65536, 256 KB, more than the 128 KB heap). A plain step logs nothing and a push 2, a room move up to ~35 in
// the shipped levels, so this only shortens how far back undo reaches after hundreds of room moves; the overflow rule
// (forget the oldest move) is the C's.
using System;

public class FoldGame {
  const int N = 8, NN = 64, MAXSQ = 64, MAXSIG = 64, MAXUNDO = 2048, JOURNAL = 4096;
  public const int MOVED = 1, PUSHED = 2, CROSSED = 4, FLIPPED = 8, TURNED = 16, SHIFTED = 32, HINGED = 64, TELEPORTED = 128, GATES = 256;
  const int A_WALL = 0, A_TARGET = 1, A_BOX = 2, A_BTN = 3, A_KIND = 4, A_AUX = 5, A_DRV = 6, A_TXT = 7, A_FLIPS = 8, A_TURNS = 9;
  const int A_MOVED = 10, A_KEYS = 11, A_TOG = 12, A_SIG = 13, A_LST = 14, A_LPREV = 15;
  static readonly int[] DX = { 0, 1, 0, -1 }, DY = { -1, 0, 1, 0 };

  public FoldLevel lv;
  // the surface: where the squares are now, and each side's 4 joins (adjF -1: a rim)
  public sbyte[] sq = new sbyte[MAXSQ * 12]; short[] adjF; byte[] adjE;
  public byte[] wall, target, box, btn, kind, aux, drv, txt;
  public byte[] flips = new byte[MAXSQ], turns = new byte[MAXSQ], moved = new byte[MAXSQ], keys = new byte[1];
  public byte[] toggled = new byte[MAXSIG], sig = new byte[MAXSIG], lstate = new byte[32], lprev = new byte[32];
  byte[][] arrs = new byte[16][];
  public int p, camf, camrot, facing, moves, pushes;
  // kept as they change, the same as counting every time: boxes off their targets (the HUD's number, 0 = solved);
  // whether any tile sends / is worked by a signal (tiles move but are never made or lost, so that holds all level)
  public int left; bool anyBtn, anyDrv;
  // the moves that can be undone: where you were, the start of the move's changes in the journal, and camf | camrot << 8
  // | facing << 10 | pushed << 12 | notmove << 13 (6 bytes a move)
  int nundo; short[] uP = new short[MAXUNDO], uJ = new short[MAXUNDO], uBits = new short[MAXUNDO];
  int nj; int[] journal = new int[JOURNAL];
  public int nd;                                           // fold_step's new heading
  public int sx, sy;                                       // fold_to_screen's answer

  // ---- the surface ----
  // each square's front corners TL TR BR BL (and as one number each, key), 2 x its middle; each edge's two corner
  // keys low / high (the edge either way round), in a hash table: bucket heads, then each edge's next (square order)
  int[] cor = new int[MAXSQ * 12], mid = new int[MAXSQ * 3], key = new int[MAXSQ * 4], elo = new int[MAXSQ * 4], ehi = new int[MAXSQ * 4];
  int[] head = new int[256], next = new int[MAXSQ * 4];
  void Surface() {
    int nsq = lv.nsq;
    for (int j = 0; j < nsq; j++) {
      int b = j * 12;
      for (int i = 0; i < 3; i++) {
        int o = sq[b + i], u = sq[b + 3 + i], v = sq[b + 6 + i];
        cor[b + i] = o; cor[b + 3 + i] = o + u; cor[b + 6 + i] = o + u + v; cor[b + 9 + i] = o + v; mid[j * 3 + i] = 2 * o + u + v;
      }
      for (int k = 0; k < 4; k++) key[j * 4 + k] = (cor[b + k * 3] + 512) << 20 | (cor[b + k * 3 + 1] + 512) << 10 | (cor[b + k * 3 + 2] + 512);
      for (int k = 0; k < 4; k++) {
        int a = key[j * 4 + k], c = key[j * 4 + ((k + 1) & 3)];
        elo[j * 4 + k] = a < c ? a : c; ehi[j * 4 + k] = a < c ? c : a;
      }
    }
    for (int h = 0; h < 256; h++) head[h] = -1;
    for (int x = nsq * 4 - 1; x >= 0; x--) { int h = (elo[x] * 31 + ehi[x]) & 255; next[x] = head[h]; head[h] = x; }   // walked in square order
    for (int f = 0; f < 2 * nsq; f++) {
      int si = f >> 1, back = f & 1, sb = si * 12;
      int fnx = back != 0 ? -sq[sb + 9] : sq[sb + 9], fny = back != 0 ? -sq[sb + 10] : sq[sb + 10], fnz = back != 0 ? -sq[sb + 11] : sq[sb + 11];
      for (int e = 0; e < 4; e++) {
        int pc = sb + ((back != 0 ? e ^ 1 : e) & 3) * 3, qc = sb + ((back != 0 ? (e + 1) ^ 1 : e + 1) & 3) * 3;
        int px = cor[pc], py = cor[pc + 1], pz = cor[pc + 2], qx = cor[qc], qy = cor[qc + 1], qz = cor[qc + 2];
        int ax = mid[si * 3] - px - qx, ay = mid[si * 3 + 1] - py - qy, az = mid[si * 3 + 2] - pz - qz;   // inward, unit
        int pk = key[si * 4 + ((back != 0 ? e ^ 1 : e) & 3)], qk = key[si * 4 + ((back != 0 ? (e + 1) ^ 1 : e + 1) & 3)];
        int lo = pk < qk ? pk : qk, hi = pk < qk ? qk : pk;
        int best = 4, bestSq = -1;
        for (int x = head[(lo * 31 + hi) & 255]; x >= 0; x = next[x]) {   // the squares with this edge, in order
          int j = x >> 2;
          if (j == si || elo[x] != lo || ehi[x] != hi) continue;
          int dx = mid[j * 3] - px - qx, dy = mid[j * 3 + 1] - py - qy, dz = mid[j * 3 + 2] - pz - qz;
          int dn = dx * fnx + dy * fny + dz * fnz, dd = dx * ax + dy * ay + dz * az;
          int turn = dn == 1 ? 1 : dd == -1 ? 2 : dn == -1 ? 3 : 4;
          if (turn < best) { best = turn; bestSq = j; }
        }
        adjF[f * 4 + e] = -1; adjE[f * 4 + e] = 0;
        if (bestSq < 0) continue;
        int tx = best == 1 ? -ax : best == 2 ? -fnx : ax, ty = best == 1 ? -ay : best == 2 ? -fny : ay, tz = best == 1 ? -az : best == 2 ? -fnz : az;
        int bb = bestSq * 12;
        int g = 2 * bestSq + (sq[bb + 9] * tx + sq[bb + 10] * ty + sq[bb + 11] * tz < 0 ? 0 : 1), gb = g & 1;
        for (int k = 0; k < 4; k++)
          if (key[bestSq * 4 + ((gb != 0 ? k ^ 1 : k) & 3)] == qk && key[bestSq * 4 + ((gb != 0 ? (k + 1) ^ 1 : k + 1) & 3)] == pk) { adjF[f * 4 + e] = (short)g; adjE[f * 4 + e] = (byte)k; }
      }
    }
  }
  public int Step(int c, int d) {
    int f = c / NN, x = c % N, y = (c / N) % N, nx = x + DX[d], ny = y + DY[d];
    if (nx >= 0 && ny >= 0 && nx < N && ny < N) { nd = d; return f * NN + ny * N + nx; }
    int af = adjF[f * 4 + d];
    if (af < 0) return -1;
    int pp = d == 0 ? x : d == 1 ? y : d == 2 ? N - 1 - x : N - 1 - y, p2 = N - 1 - pp, x2, y2;
    switch (adjE[f * 4 + d]) {
      case 0: x2 = p2; y2 = 0; break;
      case 1: x2 = N - 1; y2 = p2; break;
      case 2: x2 = N - 1 - p2; y2 = N - 1; break;
      default: x2 = 0; y2 = N - 1 - p2; break;
    }
    nd = (adjE[f * 4 + d] + 2) & 3;
    return af * NN + y2 * N + x2;
  }

  // ---- cell geometry ----
  static int Twin(int c) { int f = c / NN, x = c % N, y = (c / N) % N; return (f ^ 1) * NN + y * N + (N - 1 - x); }
  static int RotCell(int c) {
    int f = c / NN, x = c % N, y = (c / N) % N;
    return f * NN + ((f & 1) != 0 ? (N - 1 - x) * N + y : x * N + (N - 1 - y));
  }
  public void ToScreen(int fx, int fy, int rot) {
    switch (rot & 3) {
      case 0: sx = fx; sy = fy; break;
      case 1: sx = fy; sy = N - 1 - fx; break;
      case 2: sx = N - 1 - fx; sy = N - 1 - fy; break;
      default: sx = N - 1 - fy; sy = fx; break;
    }
  }
  int[] rv = new int[3];
  void Rot90(int wx, int wy, int wz, int ax, int ay, int az, int sgn) {   // about unit a: into rv
    int d = ax * wx + ay * wy + az * wz;
    rv[0] = (ay * wz - az * wy) * sgn + ax * d; rv[1] = (az * wx - ax * wz) * sgn + ay * d; rv[2] = (ax * wy - ay * wx) * sgn + az * d;
  }
  void GeoPose(int i) {                                    // fold_geo_pose: where square i's shift / hinge puts it (into sq)
    int b = i * 12, k = lv.geoKind[i], s0 = lv.geoS0[i], s1 = lv.geoS1[i];
    int ox = lv.sq[b], oy = lv.sq[b + 1], oz = lv.sq[b + 2], ux = lv.sq[b + 3], uy = lv.sq[b + 4], uz = lv.sq[b + 5];
    int vx = lv.sq[b + 6], vy = lv.sq[b + 7], vz = lv.sq[b + 8], nx = lv.sq[b + 9], ny = lv.sq[b + 10], nz = lv.sq[b + 11];
    if (k == 's') {
      if (s0 == 'u') { ox -= vx; oy -= vy; oz -= vz; } else if (s0 == 'd') { ox += vx; oy += vy; oz += vz; }
      else if (s0 == 'l') { ox -= ux; oy -= uy; oz -= uz; } else if (s0 == 'r') { ox += ux; oy += uy; oz += uz; }
      else if (s0 == 'o') { ox += nx; oy += ny; oz += nz; } else { ox -= nx; oy -= ny; oz -= nz; }
    } else {
      int e = (s0 - '0') & 3, sg = s1 == '-' ? -1 : 1;
      int Ax = ox, Ay = oy, Az = oz, Bx = ox, By = oy, Bz = oz;   // corners e and e + 1 of the front
      if (e == 1 || e == 2) { Ax += ux; Ay += uy; Az += uz; } if (e >= 2) { Ax += vx; Ay += vy; Az += vz; }
      int e1 = (e + 1) & 3;
      if (e1 == 1 || e1 == 2) { Bx += ux; By += uy; Bz += uz; } if (e1 >= 2) { Bx += vx; By += vy; Bz += vz; }
      int axx = Bx - Ax, axy = By - Ay, axz = Bz - Az;
      int mx = 2 * ox + ux + vx - 2 * Ax, my = 2 * oy + uy + vy - 2 * Ay, mz = 2 * oz + uz + vz - 2 * Az;
      Rot90(mx, my, mz, axx, axy, axz, 1);
      int dir = (rv[0] * nx + rv[1] * ny + rv[2] * nz) * sg > 0 ? 1 : -1;
      Rot90(ox - Ax, oy - Ay, oz - Az, axx, axy, axz, dir); ox = Ax + rv[0]; oy = Ay + rv[1]; oz = Az + rv[2];
      Rot90(ux, uy, uz, axx, axy, axz, dir); ux = rv[0]; uy = rv[1]; uz = rv[2];
      Rot90(vx, vy, vz, axx, axy, axz, dir); vx = rv[0]; vy = rv[1]; vz = rv[2];
      Rot90(nx, ny, nz, axx, axy, axz, dir); nx = rv[0]; ny = rv[1]; nz = rv[2];
    }
    sq[b] = (sbyte)ox; sq[b + 1] = (sbyte)oy; sq[b + 2] = (sbyte)oz; sq[b + 3] = (sbyte)ux; sq[b + 4] = (sbyte)uy; sq[b + 5] = (sbyte)uz;
    sq[b + 6] = (sbyte)vx; sq[b + 7] = (sbyte)vy; sq[b + 8] = (sbyte)vz; sq[b + 9] = (sbyte)nx; sq[b + 10] = (sbyte)ny; sq[b + 11] = (sbyte)nz;
  }
  void Rebuild() {
    for (int i = 0; i < lv.nsq; i++) {
      if (moved[i] != 0) GeoPose(i); else Array.Copy(lv.sq, i * 12, sq, i * 12, 12);
    }
    Surface();
  }

  // ---- changes, logged for undo ----
  void DropOldest() {
    int cut = nundo > 1 ? uJ[1] : nj;
    Array.Copy(journal, cut, journal, 0, nj - cut); nj -= cut;
    Array.Copy(uP, 1, uP, 0, nundo - 1); Array.Copy(uJ, 1, uJ, 0, nundo - 1); Array.Copy(uBits, 1, uBits, 0, nundo - 1); nundo--;
    for (int k = 0; k < nundo; k++) uJ[k] = (short)(uJ[k] - cut);
  }
  void Set(int a, int i, int v) {
    byte[] arr = arrs[a];
    if (arr[i] == v) return;
    while (nj == JOURNAL && nundo > 1) DropOldest();
    if (nj < JOURNAL) journal[nj++] = a << 24 | i << 8 | arr[i];
    if (a == A_BOX || a == A_TARGET) { if (box[i] != 0 && target[i] == 0) left--; arr[i] = (byte)v; if (box[i] != 0 && target[i] == 0) left++; }
    else arr[i] = (byte)v;
  }
  int PushUndo(int pushed, int notmove) {
    if (nundo == MAXUNDO) DropOldest();
    int k = nundo++;
    uP[k] = (short)p; uJ[k] = (short)nj;
    uBits[k] = (short)((camf & 255) | (camrot & 3) << 8 | (facing & 3) << 10 | pushed << 12 | notmove << 13);
    return k;
  }

  // ---- room moves ----
  byte[] ta = new byte[8], tb = new byte[8], old = new byte[2 * NN * 8];
  void TileGet(int c, byte[] t, int at) { for (int a = 0; a < 8; a++) t[at + a] = arrs[a][c]; }
  void TilePut(int c, byte[] t, int at) { for (int a = 0; a < 8; a++) Set(a, c, t[at + a]); }
  void FlipSquare(int i) {
    for (int c = 2 * i * NN; c < (2 * i + 1) * NN; c++) {
      int t = Twin(c); TileGet(c, ta, 0); TileGet(t, tb, 0);
      if (ta[4] == FoldLevel.FK_ONEWAY && (ta[5] & 1) != 0) ta[5] ^= 2;
      if (tb[4] == FoldLevel.FK_ONEWAY && (tb[5] & 1) != 0) tb[5] ^= 2;
      TilePut(c, tb, 0); TilePut(t, ta, 0);
    }
    if ((p / NN) >> 1 == i) p = Twin(p);
    if (camf >> 1 == i) camf ^= 1;
    Set(A_FLIPS, i, flips[i] ^ 1);
  }
  void TurnSquare(int i) {
    int lo = 2 * i * NN;
    for (int c = lo; c < lo + 2 * NN; c++) TileGet(c, old, (c - lo) * 8);
    for (int c = lo; c < lo + 2 * NN; c++) {
      Array.Copy(old, (c - lo) * 8, ta, 0, 8);
      if (ta[4] == FoldLevel.FK_ONEWAY) ta[5] = (byte)((ta[5] + (((c / NN) & 1) != 0 ? 3 : 1)) & 3);
      TilePut(RotCell(c), ta, 0);
    }
    if ((p / NN) >> 1 == i) p = RotCell(p);
    if (camf >> 1 == i) {                    // your room holds still on screen: the view turns with it
      int c = camf * NN + 2 * N + 1, r = RotCell(c);
      ToScreen(1, 2, camrot); int x0 = sx, y0 = sy;
      for (int q = 0; q < 4; q++) { ToScreen(r % N, (r / N) % N, q); if (sx == x0 && sy == y0) { camrot = q; break; } }
    }
    Set(A_TURNS, i, (turns[i] + 1) & 3);
  }
  int ApplyAct(int a, bool back) {
    int k = lv.actKind[a], s = lv.actSq[a];
    if (k == 'f') { FlipSquare(s); return FLIPPED; }
    if (k == 'r' || k == 'R') { for (int n = 0; n < (back ? 3 : 1); n++) TurnSquare(s); return TURNED; }
    Set(A_MOVED, s, moved[s] ^ 1); Rebuild(); return k == 's' ? SHIFTED : HINGED;
  }

  // ---- signals ----
  byte[] v = new byte[MAXSIG];
  bool AllTargets() {
    bool any = false;
    for (int c = 0; c < 2 * lv.nsq * NN; c++) if (target[c] != 0 && wall[c] == 0) { any = true; if (box[c] == 0) return false; }
    return any;
  }
  void Inputs() {
    Array.Clear(v, 0, MAXSIG);
    for (int s = 1; s < MAXSIG; s++) v[s] = toggled[s];
    if (anyBtn) for (int c = 0; c < 2 * lv.nsq * NN; c++) {
      int s = btn[c]; if (s == 0 || wall[c] != 0) continue;
      switch (kind[c]) {
        case FoldLevel.FK_PLATE: v[s] |= box[c]; break;
        case FoldLevel.FK_SENSOR: if (c == p) v[s] |= 1; break;
        case FoldLevel.FK_KEY: if ((aux[c] & 0x80) != 0) v[s] |= 1; break;
        case FoldLevel.FK_LIGHT: if ((aux[c] & 0x80) != 0) v[s] |= 1; break;
        case FoldLevel.FK_SIGN: case FoldLevel.FK_KEYPAD: break;
        default: if (target[c] != 0) v[s] |= box[c]; break;
      }
    }
    if (lv.allSig != 0 && AllTargets()) v[lv.allSig] |= 1;
    for (int k = 0; k < lv.ntimer; k++) {
      int n = lv.tN[k] != 0 ? lv.tN[k] : 1;
      if (lv.tKind[k] == FoldLevel.FT_TICK ? moves > 0 && moves % n == 0 : moves >= n) v[lv.tSig[k]] |= 1;
    }
  }
  void Delays() {
    for (int k = 0; k < lv.nlogic; k++) if (lv.lOp[k] == FoldLevel.FL_DELAY)
      Set(A_LST, k, (byte)(lstate[k] << 1 | (sig[lv.lIn[k * 4]] != 0 ? 1 : 0)));
  }
  void Logic() {
    for (int k = 0; k < lv.nlogic; k++) {
      int op = lv.lOp[k], ln = lv.lN[k], cnt = lv.lCount[k], ib = k * 4, on = 0, n = 0;
      for (int i = 0; i < ln; i++) if (v[lv.lIn[ib + i]] != 0) n++;
      switch (op) {
        case FoldLevel.FL_AND: on = n == ln ? 1 : 0; break;
        case FoldLevel.FL_OR: on = n > 0 ? 1 : 0; break;
        case FoldLevel.FL_XOR: on = n & 1; break;
        case FoldLevel.FL_NOT: on = v[lv.lIn[ib]] == 0 ? 1 : 0; break;
        case FoldLevel.FL_LATCH:
          if (ln > 1 && v[lv.lIn[ib + 1]] != 0) Set(A_LST, k, 0); else if (v[lv.lIn[ib]] != 0) Set(A_LST, k, 1);
          on = lstate[k]; break;
        case FoldLevel.FL_COUNT:
          if (v[lv.lIn[ib]] != 0 && lprev[k] == 0 && lstate[k] < 255) Set(A_LST, k, lstate[k] + 1);
          Set(A_LPREV, k, v[lv.lIn[ib]] != 0 ? 1 : 0);
          on = lstate[k] >= cnt ? 1 : 0; break;
        case FoldLevel.FL_DELAY: { int d = cnt < 1 ? 1 : cnt > 8 ? 8 : cnt; on = lstate[k] >> (d - 1) & 1; break; }
        case FoldLevel.FL_SHIFT: {
          int len = cnt < 1 ? 1 : cnt > 8 ? 8 : cnt; bool clk = ln > 1 && v[lv.lIn[ib + 1]] != 0;
          if (clk && lprev[k] == 0) Set(A_LST, k, ((lstate[k] << 1) | (v[lv.lIn[ib]] != 0 ? 1 : 0)) & ((1 << len) - 1));
          Set(A_LPREV, k, clk ? 1 : 0);
          for (int i = 1; i < len && lv.lOut[k] + i < MAXSIG; i++) v[lv.lOut[k] + i] |= (byte)(lstate[k] >> i & 1);
          on = lstate[k] & 1; break;
        }
      }
      v[lv.lOut[k]] |= (byte)on;
    }
  }
  int Outputs(int s, bool on) {
    int r = 0;
    for (int k = 0; k < lv.nact[s - 1]; k++) {
      int a = (s - 1) * FoldLevel.MAXACT + k;
      r |= ApplyAct(a, lv.actKind[a] == 'R' && !on);
    }
    if (anyDrv) for (int c = 0; c < 2 * lv.nsq * NN; c++) if (drv[c] == s && wall[c] == 0) {
      int k = kind[c];
      if (k == FoldLevel.FK_GATE || k == FoldLevel.FK_TELE || k == FoldLevel.FK_LIGHT) { Set(A_AUX, c, aux[c] ^ 0x80); r |= GATES; }
      else if (k == FoldLevel.FK_ONEWAY) { Set(A_AUX, c, (aux[c] + 2) & 3); r |= GATES; }
    }
    return r;
  }
  int Evaluate() {
    int r = 0;
    for (int pass = 0; pass < 8; pass++) {
      bool changed = false;
      Inputs(); Logic();
      for (int s = 1; s < MAXSIG; s++) if ((v[s] != 0) != (sig[s] != 0)) {
        Set(A_SIG, s, v[s] != 0 ? 1 : 0); r |= Outputs(s, v[s] != 0); changed = true;
      }
      if (!changed) break;
    }
    return r;
  }

  // ---- game ----
  public void Drop() { lv = null; wall = target = box = btn = kind = aux = drv = txt = null; for (int a = 0; a < 8; a++) arrs[a] = null; }
  public void Start(FoldLevel level) {
    lv = level;
    wall = lv.wall; target = lv.target; box = lv.box; btn = lv.btn; kind = lv.kind; aux = lv.aux; drv = lv.drv; txt = lv.txt;
    arrs[0] = wall; arrs[1] = target; arrs[2] = box; arrs[3] = btn; arrs[4] = kind; arrs[5] = aux; arrs[6] = drv; arrs[7] = txt;
    arrs[8] = flips; arrs[9] = turns; arrs[10] = moved; arrs[11] = keys; arrs[12] = toggled; arrs[13] = sig; arrs[14] = lstate; arrs[15] = lprev;
    Array.Clear(flips, 0, MAXSQ); Array.Clear(turns, 0, MAXSQ); Array.Clear(moved, 0, MAXSQ); keys[0] = 0;
    Array.Clear(toggled, 0, MAXSIG); Array.Clear(sig, 0, MAXSIG); Array.Clear(lstate, 0, 32); Array.Clear(lprev, 0, 32);
    int faces = lv.ncells / NN;
    adjF = new short[faces * 4]; adjE = new byte[faces * 4];
    Array.Copy(lv.sq, 0, sq, 0, lv.nsq * 12);
    Surface();
    left = 0; anyBtn = false; anyDrv = false;
    for (int c = 0; c < 2 * lv.nsq * NN; c++) { if (box[c] != 0 && target[c] == 0) left++; if (btn[c] != 0) anyBtn = true; if (drv[c] != 0) anyDrv = true; }
    p = lv.player; camf = p / NN; camrot = 0; facing = 2; moves = 0; pushes = 0; nundo = 0; nj = 0;
    Evaluate();
    nj = 0;
  }
  bool Enterable(int c, int d, bool isBox) {
    if (c < 0 || wall[c] != 0) return false;
    int k = kind[c];
    if (k == FoldLevel.FK_ONEWAY && aux[c] != d) return false;
    if (k == FoldLevel.FK_PIT) return isBox;
    if (k == FoldLevel.FK_GATE && (aux[c] & 0x80) == 0) return false;
    if (k == FoldLevel.FK_LOCK) return !isBox && keys[0] > 0;
    if (k == FoldLevel.FK_GRATE) return !isBox;
    if (k == FoldLevel.FK_LIGHT) return false;
    if (k == FoldLevel.FK_SIGN || k == FoldLevel.FK_KEYPAD) return false;
    return true;
  }
  int BoxSlide(int c, int d) {
    for (int guard = 0; guard < FoldLevel.MAXSQ * 2 * NN; guard++) {
      if (kind[c] == FoldLevel.FK_PIT) { Set(A_BOX, c, 0); Set(A_KIND, c, FoldLevel.FK_FLOOR); return -1; }
      if (kind[c] != FoldLevel.FK_ICE) break;
      int n = Step(c, d);
      if (n < 0 || !Enterable(n, nd, true) || box[n] != 0 || n == p) break;
      Set(A_BOX, c, 0); Set(A_BOX, n, 1); c = n; d = nd;
    }
    return c;
  }
  int Partner(int c) {
    if ((aux[c] & 0x80) != 0) return -1;
    for (int k = 0; k < 2 * lv.nsq * NN; k++) if (k != c && kind[k] == FoldLevel.FK_TELE && aux[k] == aux[c] && wall[k] == 0) return k;
    return -1;
  }
  void StepOn(int c) {
    if (kind[c] == FoldLevel.FK_LOCK) { Set(A_KEYS, 0, keys[0] - 1); Set(A_KIND, c, FoldLevel.FK_FLOOR); }
  }
  public int Move(int sd) {
    int d = (sd + camrot) & 3;
    int c1 = Step(p, d), d1 = nd;
    if (!Enterable(c1, d1, false)) return 0;
    int c2 = -1, d2 = 0;
    if (box[c1] != 0) {
      c2 = Step(c1, d1); d2 = nd;
      if (!Enterable(c2, d2, true) || box[c2] != 0) return 0;
    }
    PushUndo(c2 >= 0 ? 1 : 0, 0);
    int r = MOVED;
    if (c2 >= 0) {                              // the push: the box may slide on ice, then press what it stops on
      Set(A_BOX, c1, 0); Set(A_BOX, c2, 1); pushes++; r |= PUSHED;
      p = c1;
      c2 = BoxSlide(c2, d2);
      if (c2 >= 0 && kind[c2] == FoldLevel.FK_TELE) {
        int t = Partner(c2);
        if (t >= 0 && box[t] == 0 && t != c1) { Set(A_BOX, c2, 0); Set(A_BOX, t, 1); c2 = t; r |= TELEPORTED; }
      }
    }
    int c = c1, hd = d1;
    StepOn(c);
    for (int guard = 0; guard < FoldLevel.MAXSQ * 2 * NN && kind[c] == FoldLevel.FK_ICE; guard++) {
      int n = Step(c, hd);
      if (!Enterable(n, nd, false) || box[n] != 0) break;
      c = n; hd = nd; StepOn(c);
    }
    if (kind[c] == FoldLevel.FK_TELE) {
      int t = Partner(c);
      if (t >= 0 && box[t] == 0) { c = t; r |= TELEPORTED; }
    }
    if (kind[c] == FoldLevel.FK_KEY && (aux[c] & 0x80) == 0) { Set(A_KEYS, 0, keys[0] + 1); Set(A_AUX, c, aux[c] | 0x80); }
    p = c; moves++; facing = hd;
    if (c / NN != camf) { camf = c / NN; camrot = (hd - sd) & 3; r |= CROSSED; }
    if (c2 >= 0 && btn[c2] != 0 && kind[c2] == FoldLevel.FK_FLOOR && target[c2] == 0) Set(A_TOG, btn[c2], toggled[btn[c2]] == 0 ? 1 : 0);
    Delays();
    r |= Evaluate();
    return r;
  }
  public bool Undo() {
    if (nundo == 0) return false;
    int k = --nundo; bool geo = false;
    while (nj > uJ[k]) {
      int e = journal[--nj], a = e >> 24, i = e >> 8 & 0xffff;
      if (a == A_BOX || a == A_TARGET) { if (box[i] != 0 && target[i] == 0) left--; arrs[a][i] = (byte)(e & 0xff); if (box[i] != 0 && target[i] == 0) left++; }
      else arrs[a][i] = (byte)(e & 0xff);
      if (a == A_MOVED) geo = true;
    }
    if (geo) Rebuild();
    int b = uBits[k];
    p = uP[k]; camf = b & 255; camrot = b >> 8 & 3; facing = b >> 10 & 3;
    if ((b >> 13 & 1) == 0) moves--;
    pushes -= b >> 12 & 1;
    return true;
  }
  public bool Solved() { return left == 0; }
  public int Facing() { return Step(p, facing); }

  // ---- talking: what a tile says (fold_describe), into o (a C string of up to olen - 1 characters) ----
  byte[] o; int olen, on_;
  void Cat(string s) { for (int i = 0; i < s.Length && on_ < olen - 1; i++) o[on_++] = (byte)s[i]; o[on_] = 0; }
  string SrcName(int s) {
    for (int k = 0; k < lv.ntimer; k++) if (lv.tSig[k] == s) {
      if (lv.tKind[k] == FoldLevel.FT_TICK) return lv.tN[k] <= 1 ? "YOU MOVE" : "EVERY " + ((int)lv.tN[k]).ToString() + " MOVES";
      return "YOU HAVE MADE " + ((int)lv.tN[k]).ToString() + " MOVES";
    }
    for (int c = 0; c < 2 * lv.nsq * NN; c++) if (btn[c] == s && wall[c] == 0) {
      switch (kind[c]) {
        case FoldLevel.FK_PLATE: return "A PLATE IS HELD";
        case FoldLevel.FK_SENSOR: return "YOU STAND ON A SENSOR";
        case FoldLevel.FK_KEY: return "A KEY IS TAKEN";
        case FoldLevel.FK_LIGHT: return "A LIGHT IS ON";
      }
      return target[c] != 0 ? "A BOX IS ON A TARGET" : "A BUTTON IS PRESSED";
    }
    if (lv.allSig == s) return "EVERY TARGET IS FILLED";
    return null;
  }
  static readonly string[] ONE = { "A PLATE IS HELD", "A BUTTON IS PRESSED", "A BOX IS ON A TARGET", "A KEY IS TAKEN", "A LIGHT IS ON" };
  static readonly string[] MANY = { "PLATES ARE HELD", "BUTTONS ARE PRESSED", "BOXES ARE ON THEIR TARGETS", "KEYS ARE TAKEN", "LIGHTS ARE ON" };
  static readonly string[] NOUN = { "PLATE IS HELD", "BUTTON IS PRESSED", "BOX IS ON ITS TARGET", "KEY IS TAKEN", "LIGHT IS ON" };
  void Cond(int s, int depth) {
    string n = SrcName(s);
    if (n != null) { Cat(n); return; }
    for (int k = 0; k < lv.nlogic; k++) {
      if (lv.lOp[k] != FoldLevel.FL_SHIFT || s < lv.lOut[k] || s >= lv.lOut[k] + lv.lCount[k]) continue;
      if (depth > 1) { Cat("SOMETHING ELSE HAPPENS"); return; }
      int i = s - lv.lOut[k];
      Cond(lv.lIn[k * 4], depth + 1);
      Cat(i == 0 ? " (AT THE LAST CLOCK: " : i == 1 ? " (A CLOCK BEFORE THE LAST: " : " (" + i.ToString() + " CLOCKS BEFORE THE LAST: ");
      Cond(lv.lIn[k * 4 + 1], depth + 1); Cat(")"); return;
    }
    for (int k = 0; k < lv.nlogic; k++) if (lv.lOut[k] == s) {
      int op = lv.lOp[k], ln = lv.lN[k], cnt = lv.lCount[k];
      if (depth > 1) { Cat("SOMETHING ELSE HAPPENS"); return; }
      if (op == FoldLevel.FL_NOT) { Cat("NOT ("); Cond(lv.lIn[k * 4], depth + 1); Cat(")"); return; }
      if (op == FoldLevel.FL_LATCH) { Cond(lv.lIn[k * 4], depth + 1); Cat(" (ONCE IS ENOUGH)"); return; }
      if (op == FoldLevel.FL_COUNT) { Cond(lv.lIn[k * 4], depth + 1); Cat(" " + cnt.ToString() + " TIMES"); return; }
      if (op == FoldLevel.FL_DELAY) { Cond(lv.lIn[k * 4], depth + 1); Cat(cnt > 1 ? " (" + cnt.ToString() + " MOVES LATER)" : " (A MOVE LATER)"); return; }
      string n0 = SrcName(lv.lIn[k * 4]); bool same = n0 != null; int kd = -1;
      for (int i = 1; i < ln; i++) { string ni = SrcName(lv.lIn[k * 4 + i]); if (ni == null || n0 == null || ni != n0) same = false; }
      for (int j = 0; j < 5 && same; j++) if (n0 == ONE[j]) kd = j;
      if (same && kd >= 0 && ln > 1) {
        if (op == FoldLevel.FL_AND) Cat(ln == 2 ? "BOTH " + MANY[kd] : "ALL " + ln.ToString() + " " + MANY[kd]);
        else if (op == FoldLevel.FL_OR) Cat(ln == 2 ? "EITHER " + NOUN[kd] : "ANY OF " + ln.ToString() + ": A " + NOUN[kd]);
        else Cat("JUST ONE " + NOUN[kd]);
        return;
      }
      for (int i = 0; i < ln; i++) {
        if (i != 0) Cat(op == FoldLevel.FL_AND ? " AND " : " OR ");
        Cond(lv.lIn[k * 4 + i], depth + 1);
      }
      if (op == FoldLevel.FL_XOR) Cat(" (BUT NOT BOTH)");
      return;
    }
    Cat("SOMETHING HAPPENS");
  }
  void Effect(int s) {
    int n = 0, mv = 0;
    for (int k = 0; k < lv.nact[s - 1]; k++) {
      int kd = lv.actKind[(s - 1) * FoldLevel.MAXACT + k];
      Cat(mv++ != 0 ? " AND " : n++ != 0 ? ". IT " : " IT ");
      Cat(kd == 'f' ? "FLIPS A CELL" : kd == 's' ? "SHIFTS A CELL" : kd == 'h' ? "HINGES A CELL" : "TURNS A CELL");
    }
    bool gate = false, tele = false, arrow = false, light = false;
    for (int c = 0; c < 2 * lv.nsq * NN; c++) if (drv[c] == s) {
      int k = kind[c];
      gate |= k == FoldLevel.FK_GATE; tele |= k == FoldLevel.FK_TELE; arrow |= k == FoldLevel.FK_ONEWAY; light |= k == FoldLevel.FK_LIGHT;
    }
    for (int k = 0; k < 4; k++) {
      string w = k == 0 ? (gate ? "WORKS A GATE" : null) : k == 1 ? (tele ? "SWITCHES A TELEPORTER" : null) :
                 k == 2 ? (arrow ? "TURNS AN ARROW ROUND" : null) : (light ? "WORKS A LIGHT" : null);
      if (w != null) { Cat(mv++ != 0 ? " AND " : n++ != 0 ? ". IT " : " IT "); Cat(w); }
    }
    if (mv != 0) n = 1;
    for (int k = 0; k < lv.nlogic; k++) for (int i = 0; i < lv.lN[k]; i++) if (lv.lIn[k * 4 + i] == s) {
      int op = lv.lOp[k], ln = lv.lN[k], cnt = lv.lCount[k]; string b;
      switch (op) {
        case FoldLevel.FL_AND: b = "IT IS 1 OF " + ln.ToString() + " NEEDED TOGETHER"; break;
        case FoldLevel.FL_OR: b = "IT IS 1 OF " + ln.ToString() + ": ANY WILL DO"; break;
        case FoldLevel.FL_XOR: b = "IT IS 1 OF " + ln.ToString() + ": ONLY 1 AT A TIME"; break;
        case FoldLevel.FL_NOT: b = "SOMETHING HAPPENS WHILE IT IS OFF"; break;
        case FoldLevel.FL_LATCH: b = i != 0 ? "IT RESETS SOMETHING" : "ONCE IS ENOUGH"; break;
        case FoldLevel.FL_DELAY: b = cnt > 1 ? "IT ACTS " + cnt.ToString() + " MOVES LATE" : "IT ACTS A MOVE LATE"; break;
        default: b = "IT NEEDS " + cnt.ToString() + " GOES"; break;
      }
      Cat(n++ != 0 ? ". " : " "); Cat(b);
    }
    if (n == 0) Cat(" IT DOES NOTHING");
    Cat(".");
  }
  int Value(int which, int arg) {
    int n = 0, C = 2 * lv.nsq * NN;
    switch (which) {
      case FoldLevel.FV_NUMBER: return arg;
      case FoldLevel.FV_MOVES: return moves;
      case FoldLevel.FV_PUSHES: return pushes;
      case FoldLevel.FV_FLIPS: for (int i = 0; i < lv.nsq; i++) n += flips[i]; return n;
      case FoldLevel.FV_TURNS: for (int i = 0; i < lv.nsq; i++) n += turns[i]; return n;
      case FoldLevel.FV_KEYS: return keys[0];
      case FoldLevel.FV_BOXES: for (int c = 0; c < C; c++) if (box[c] != 0 && target[c] != 0) n++; return n;
      case FoldLevel.FV_LIGHTS: for (int c = 0; c < C; c++) if (wall[c] == 0 && kind[c] == FoldLevel.FK_LIGHT && (aux[c] & 0x80) != 0) n++; return n;
      case FoldLevel.FV_COUNT: for (int k = 0; k < lv.nlogic; k++) if (lv.lOut[k] == arg && lv.lOp[k] == FoldLevel.FL_COUNT) return lstate[k]; return 0;
    }
    return 0;
  }
  void Expand(byte[] t) {                          // a text with its {MOVES}-style words filled in, from the start of o
    on_ = 0; int i = 0, tn = t.Length;
    while (i < tn && on_ < olen - 1) {
      if (t[i] == '{') {
        int e = -1; for (int j = i; j < tn; j++) if (t[j] == '}') { e = j; break; }
        int w = -1, arg = 0;
        if (e >= 0) for (int k = 1; k < FoldLevel.FV_N; k++) {
          string nm = FoldLevel.VALUE_NAMES[k]; int l = nm.Length; bool same = true;
          for (int q = 0; q < l; q++) if (i + 1 + q >= tn || t[i + 1 + q] != nm[q]) { same = false; break; }
          int after = i + 1 + l < tn ? t[i + 1 + l] : 0;
          if (same && (after == '}' || after == ' ')) { w = k; arg = AtoiAt(t, i + 1 + l); }
        }
        if (w >= 0) {
          string b = Value(w, arg).ToString();
          for (int q = 0; q < b.Length && on_ < olen - 1; q++) o[on_++] = (byte)b[q];
          i = e + 1; continue;
        }
      }
      o[on_++] = t[i++];
    }
    o[on_] = 0;
  }
  static int AtoiAt(byte[] t, int at) {
    int n = t.Length;
    while (at < n && (t[at] == ' ' || t[at] == '\t' || t[at] == '\v' || t[at] == '\f')) at++;
    bool neg = false; if (at < n && (t[at] == '+' || t[at] == '-')) { neg = t[at] == '-'; at++; }
    long v = 0; while (at < n && t[at] >= '0' && t[at] <= '9') { if (v < 0x7FFFFFFFFFFFL / 10) v = v * 10 + (t[at] - '0'); at++; }
    return (int)(neg ? -v : v);
  }
  public bool Describe(int c, byte[] outb, int len) {
    o = outb; olen = len; on_ = 0; o[0] = 0;
    if (c < 0) return false;
    if (kind[c] == FoldLevel.FK_SIGN && wall[c] == 0) {
      bool on = drv[c] != 0 && sig[drv[c]] != 0 && aux[c] != 0; int t = on ? aux[c] : txt[c];
      if (t != 0) Expand(lv.text[t - 1]); else Cat("A SIGN. IT IS BLANK.");
      return true;
    }
    if (txt[c] != 0) { Expand(lv.text[txt[c] - 1]); return true; }
    if (kind[c] == FoldLevel.FK_KEYPAD && wall[c] == 0) { Cat(btn[c] != 0 && toggled[btn[c]] != 0 ? "A KEYPAD. THE CODE WAS RIGHT." : "A KEYPAD. ENTER THE CODE."); return true; }
    if (box[c] != 0 && btn[c] == 0) { Cat("A BOX. PUSH IT ONTO A TARGET."); return true; }
    int s = btn[c], k = kind[c];
    if (k == FoldLevel.FK_LIGHT && wall[c] == 0) {
      Cat((aux[c] & 0x80) != 0 ? "A LIGHT. IT IS ON." : "A LIGHT. IT IS OFF.");
      if (drv[c] != 0) {
        bool drawnOn = ((aux[c] & 0x80) != 0) != (sig[drv[c]] != 0);
        Cat(drawnOn ? " IT GOES OUT WHEN " : " IT LIGHTS WHEN "); Cond(drv[c], 0); Cat(".");
      } else Cat(" NOTHING WORKS IT.");
      if (s != 0) { Cat(" WHILE IT IS ON"); Effect(s); }
      return true;
    }
    if (s != 0 && wall[c] == 0) {
      Cat(k == FoldLevel.FK_PLATE ? "A PLATE. ON WHILE A BOX IS ON IT." : k == FoldLevel.FK_SENSOR ? "A SENSOR. ON WHILE YOU STAND ON IT." :
          k == FoldLevel.FK_KEY ? "A KEY. TAKING IT DOES SOMETHING." : target[c] != 0 ? "A TARGET. A BOX ON IT DOES SOMETHING." :
          "A BUTTON. A BOX ON IT SWITCHES IT ON OR OFF.");
      Effect(s);
      return true;
    }
    if (wall[c] != 0) return false;
    switch (k) {
      case FoldLevel.FK_ICE: Cat("ICE. YOU AND BOXES SLIDE ACROSS IT."); break;
      case FoldLevel.FK_ONEWAY: Cat("ONE WAY ONLY."); break;
      case FoldLevel.FK_PIT: Cat("A PIT. A BOX CAN FILL IT."); break;
      case FoldLevel.FK_TELE: Cat((aux[c] & 0x80) != 0 ? "A TELEPORTER. IT IS OFF." : "A TELEPORTER. IT TAKES YOU TO ITS PARTNER."); break;
      case FoldLevel.FK_GATE: Cat((aux[c] & 0x40) != 0 ? ((aux[c] & 0x80) != 0 ? "A GAP IN THE WALL." : "A WALL. IT LOOKS ODD.") : (aux[c] & 0x80) != 0 ? "A GATE. IT IS OPEN." : "A GATE. IT IS SHUT."); break;
      case FoldLevel.FK_KEY: Cat((aux[c] & 0x80) != 0 ? "" : "A KEY. TAKE IT."); break;
      case FoldLevel.FK_LOCK: Cat("A LOCK. IT NEEDS A KEY."); break;
      case FoldLevel.FK_GRATE: Cat("A GRATE. YOU CAN CROSS IT. BOXES CAN'T."); break;
      case FoldLevel.FK_SENSOR: Cat("A SENSOR. IT ISN'T WIRED TO ANYTHING."); break;
      default: if (target[c] != 0) Cat("A TARGET. PUT A BOX ON IT."); break;
    }
    if (drv[c] != 0 && (k == FoldLevel.FK_GATE || k == FoldLevel.FK_TELE || k == FoldLevel.FK_ONEWAY)) {
      bool drawnOpen = ((aux[c] & 0x80) != 0) != (sig[drv[c]] != 0);
      Cat(k == FoldLevel.FK_GATE ? (drawnOpen ? " IT SHUTS WHEN " : " IT OPENS WHEN ") : k == FoldLevel.FK_TELE ? (drawnOpen ? " IT SWITCHES OFF WHEN " : " IT SWITCHES ON WHEN ") : " IT TURNS ROUND WHEN ");
      Cond(drv[c], 0);
      Cat(".");
    }
    return o[0] != 0;
  }
}
