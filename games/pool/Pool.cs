// 9-BALL for the VMX: a line-for-line port of microgames/ui/games/pool_core.js to C# 1.0, so it plays exactly the same
// (same random numbers, same integer physics, same order of events); tools/parity checks that frame by frame.
// 9-ball against the computer: hit the lowest ball first, sink the 9 on a good shot to win the rack. Overhead view:
// Left/Right turn the cue, Up/Down nudge, A goes down to the cue. Down at the cue (a low 3D wireframe view along the
// shot): Left/Right fine aim, Up/Down top/back spin, hold A to draw back, let go to shoot, B back up. Ball in hand:
// the D-pad moves the cue ball, A sets it down. All integer fixed point: 1 px = 256 units, angles 0..4095.
using System;
using Microsoft.Vmx;

public class Pool : Game {
  const int U = 256, R = 576, D2 = 1152, D2SQ = 1152 * 1152;
  const int X0 = 5 * U, X1 = 59 * U, Y0 = 21 * U, Y1 = 48 * U, CXU = 32 * U, CYU = (Y0 + Y1) >> 1;
  const int MC = 896, MS = 640, CR2 = 768 * 768;
  static readonly int[] PX = { X0 - U, CXU, X1 + U, X0 - U, CXU, X1 + U }, PY = { Y0 - U, Y0 - 384, Y0 - U, Y1 + U, Y1 + 384, Y1 + U };
  const int HEAD = X0 + 13 * U + 128, FOOT = X0 + 33 * U + 128;
  const int FR = 11, VMAX = 1264;
  const int CB = 4096, CHT = 2048, FOC = 80, HZ = 10, NEAR = 512, CZ = 800;
  static readonly int[] SIN = { 0, 25, 50, 75, 101, 126, 151, 176, 201, 226, 251, 276, 302, 327, 352, 377, 402, 427, 452, 477, 503, 528, 553, 578, 603, 628, 653, 678, 704, 729, 754, 779, 804, 829, 854, 879, 904, 929, 955, 980, 1005, 1030, 1055, 1080, 1105, 1130, 1155, 1180, 1205, 1230, 1255, 1280, 1306, 1331, 1356, 1381, 1406, 1431, 1456, 1481, 1506, 1531, 1556, 1581, 1606, 1631, 1656, 1681, 1706, 1731, 1756, 1781, 1806, 1831, 1856, 1881, 1906, 1931, 1956, 1981, 2006, 2031, 2055, 2080, 2105, 2130, 2155, 2180, 2205, 2230, 2255, 2280, 2305, 2329, 2354, 2379, 2404, 2429, 2454, 2479, 2503, 2528, 2553, 2578, 2603, 2628, 2652, 2677, 2702, 2727, 2752, 2776, 2801, 2826, 2851, 2875, 2900, 2925, 2949, 2974, 2999, 3024, 3048, 3073, 3098, 3122, 3147, 3172, 3196, 3221, 3246, 3270, 3295, 3320, 3344, 3369, 3393, 3418, 3442, 3467, 3492, 3516, 3541, 3565, 3590, 3614, 3639, 3663, 3688, 3712, 3737, 3761, 3786, 3810, 3835, 3859, 3883, 3908, 3932, 3957, 3981, 4005, 4030, 4054, 4078, 4103, 4127, 4151, 4176, 4200, 4224, 4249, 4273, 4297, 4321, 4346, 4370, 4394, 4418, 4442, 4467, 4491, 4515, 4539, 4563, 4587, 4612, 4636, 4660, 4684, 4708, 4732, 4756, 4780, 4804, 4828, 4852, 4876, 4900, 4924, 4948, 4972, 4996, 5020, 5044, 5068, 5092, 5115, 5139, 5163, 5187, 5211, 5235, 5259, 5282, 5306, 5330, 5354, 5377, 5401, 5425, 5449, 5472, 5496, 5520, 5543, 5567, 5591, 5614, 5638, 5661, 5685, 5708, 5732, 5756, 5779, 5803, 5826, 5850, 5873, 5897, 5920, 5943, 5967, 5990, 6014, 6037, 6060, 6084, 6107, 6130, 6154, 6177, 6200, 6223, 6247, 6270, 6293, 6316, 6339, 6363, 6386, 6409, 6432, 6455, 6478, 6501, 6524, 6547, 6570, 6593, 6616, 6639, 6662, 6685, 6708, 6731, 6754, 6777, 6800, 6823, 6846, 6868, 6891, 6914, 6937, 6960, 6982, 7005, 7028, 7050, 7073, 7096, 7118, 7141, 7164, 7186, 7209, 7231, 7254, 7276, 7299, 7321, 7344, 7366, 7389, 7411, 7434, 7456, 7478, 7501, 7523, 7545, 7568, 7590, 7612, 7635, 7657, 7679, 7701, 7723, 7746, 7768, 7790, 7812, 7834, 7856, 7878, 7900, 7922, 7944, 7966, 7988, 8010, 8032, 8054, 8076, 8098, 8119, 8141, 8163, 8185, 8207, 8228, 8250, 8272, 8293, 8315, 8337, 8358, 8380, 8401, 8423, 8445, 8466, 8488, 8509, 8531, 8552, 8573, 8595, 8616, 8638, 8659, 8680, 8702, 8723, 8744, 8765, 8787, 8808, 8829, 8850, 8871, 8892, 8914, 8935, 8956, 8977, 8998, 9019, 9040, 9061, 9082, 9102, 9123, 9144, 9165, 9186, 9207, 9227, 9248, 9269, 9290, 9310, 9331, 9352, 9372, 9393, 9413, 9434, 9455, 9475, 9496, 9516, 9537, 9557, 9577, 9598, 9618, 9638, 9659, 9679, 9699, 9720, 9740, 9760, 9780, 9800, 9820, 9841, 9861, 9881, 9901, 9921, 9941, 9961, 9981, 10001, 10020, 10040, 10060, 10080, 10100, 10120, 10139, 10159, 10179, 10198, 10218, 10238, 10257, 10277, 10296, 10316, 10336, 10355, 10374, 10394, 10413, 10433, 10452, 10471, 10491, 10510, 10529, 10549, 10568, 10587, 10606, 10625, 10644, 10663, 10683, 10702, 10721, 10740, 10759, 10778, 10796, 10815, 10834, 10853, 10872, 10891, 10909, 10928, 10947, 10966, 10984, 11003, 11021, 11040, 11059, 11077, 11096, 11114, 11133, 11151, 11169, 11188, 11206, 11224, 11243, 11261, 11279, 11297, 11316, 11334, 11352, 11370, 11388, 11406, 11424, 11442, 11460, 11478, 11496, 11514, 11532, 11550, 11567, 11585, 11603, 11621, 11638, 11656, 11674, 11691, 11709, 11727, 11744, 11762, 11779, 11797, 11814, 11831, 11849, 11866, 11883, 11901, 11918, 11935, 11952, 11970, 11987, 12004, 12021, 12038, 12055, 12072, 12089, 12106, 12123, 12140, 12157, 12173, 12190, 12207, 12224, 12240, 12257, 12274, 12290, 12307, 12324, 12340, 12357, 12373, 12390, 12406, 12423, 12439, 12455, 12472, 12488, 12504, 12520, 12537, 12553, 12569, 12585, 12601, 12617, 12633, 12649, 12665, 12681, 12697, 12713, 12729, 12744, 12760, 12776, 12792, 12807, 12823, 12839, 12854, 12870, 12885, 12901, 12916, 12932, 12947, 12963, 12978, 12993, 13008, 13024, 13039, 13054, 13069, 13085, 13100, 13115, 13130, 13145, 13160, 13175, 13190, 13205, 13219, 13234, 13249, 13264, 13279, 13293, 13308, 13323, 13337, 13352, 13366, 13381, 13395, 13410, 13424, 13439, 13453, 13467, 13482, 13496, 13510, 13524, 13538, 13553, 13567, 13581, 13595, 13609, 13623, 13637, 13651, 13665, 13678, 13692, 13706, 13720, 13733, 13747, 13761, 13774, 13788, 13802, 13815, 13829, 13842, 13856, 13869, 13882, 13896, 13909, 13922, 13935, 13949, 13962, 13975, 13988, 14001, 14014, 14027, 14040, 14053, 14066, 14079, 14092, 14104, 14117, 14130, 14143, 14155, 14168, 14181, 14193, 14206, 14218, 14231, 14243, 14256, 14268, 14280, 14293, 14305, 14317, 14329, 14341, 14354, 14366, 14378, 14390, 14402, 14414, 14426, 14438, 14449, 14461, 14473, 14485, 14497, 14508, 14520, 14531, 14543, 14555, 14566, 14578, 14589, 14601, 14612, 14623, 14635, 14646, 14657, 14668, 14680, 14691, 14702, 14713, 14724, 14735, 14746, 14757, 14768, 14779, 14789, 14800, 14811, 14822, 14832, 14843, 14854, 14864, 14875, 14885, 14896, 14906, 14917, 14927, 14937, 14948, 14958, 14968, 14978, 14989, 14999, 15009, 15019, 15029, 15039, 15049, 15059, 15069, 15078, 15088, 15098, 15108, 15118, 15127, 15137, 15146, 15156, 15166, 15175, 15184, 15194, 15203, 15213, 15222, 15231, 15240, 15250, 15259, 15268, 15277, 15286, 15295, 15304, 15313, 15322, 15331, 15340, 15349, 15357, 15366, 15375, 15383, 15392, 15401, 15409, 15418, 15426, 15435, 15443, 15451, 15460, 15468, 15476, 15485, 15493, 15501, 15509, 15517, 15525, 15533, 15541, 15549, 15557, 15565, 15573, 15581, 15588, 15596, 15604, 15611, 15619, 15627, 15634, 15642, 15649, 15656, 15664, 15671, 15679, 15686, 15693, 15700, 15707, 15715, 15722, 15729, 15736, 15743, 15750, 15757, 15763, 15770, 15777, 15784, 15791, 15797, 15804, 15810, 15817, 15824, 15830, 15837, 15843, 15849, 15856, 15862, 15868, 15875, 15881, 15887, 15893, 15899, 15905, 15911, 15917, 15923, 15929, 15935, 15941, 15946, 15952, 15958, 15964, 15969, 15975, 15980, 15986, 15991, 15997, 16002, 16008, 16013, 16018, 16024, 16029, 16034, 16039, 16044, 16049, 16054, 16059, 16064, 16069, 16074, 16079, 16084, 16088, 16093, 16098, 16103, 16107, 16112, 16116, 16121, 16125, 16130, 16134, 16138, 16143, 16147, 16151, 16156, 16160, 16164, 16168, 16172, 16176, 16180, 16184, 16188, 16192, 16195, 16199, 16203, 16207, 16210, 16214, 16218, 16221, 16225, 16228, 16232, 16235, 16238, 16242, 16245, 16248, 16251, 16255, 16258, 16261, 16264, 16267, 16270, 16273, 16276, 16279, 16281, 16284, 16287, 16290, 16292, 16295, 16298, 16300, 16303, 16305, 16308, 16310, 16312, 16315, 16317, 16319, 16321, 16324, 16326, 16328, 16330, 16332, 16334, 16336, 16338, 16340, 16341, 16343, 16345, 16347, 16348, 16350, 16352, 16353, 16355, 16356, 16358, 16359, 16360, 16362, 16363, 16364, 16365, 16367, 16368, 16369, 16370, 16371, 16372, 16373, 16374, 16375, 16375, 16376, 16377, 16378, 16378, 16379, 16380, 16380, 16381, 16381, 16382, 16382, 16382, 16383, 16383, 16383, 16384, 16384, 16384, 16384, 16384, 16384 };
  const string FONTCH = "ABCDEFGHIKLMNOPRSTUVWY0123456789:!- ";
  static readonly string[] FONT = {
    "010101111101101", "110101110101110", "011100100100011", "110101101101110", "111100110100111", "111100110100100",
    "011100101101011", "101101111101101", "111010010010111", "101101110101101", "100100100100111", "101111111101101",
    "110101101101101", "010101101101010", "110101110100100", "110101110101101", "011100010001110", "111010010010010",
    "101101101101111", "101101101101010", "101101111111101", "101101010010010",
    "111101101101111", "010110010010111", "110001010100111", "110001010001110", "101101111001001", "111100110001110",
    "011100111101111", "111001010010010", "111101111101111", "111101111001110", "000010000010000", "010010010000010",
    "000000111000000", "000000000000000" };
  static readonly int[] DISC = { 14, 31, 31, 31, 14 };
  static readonly int[] ROWS = { 1, 2, 3, 2, 1 };
  const int TITLE = 0, PLACE = 1, AIM = 2, VIEW = 3, STRIKE = 4, ROLL = 5, RESULT = 6, OVER = 7;

  static int Abs(int a) { return a < 0 ? -a : a; }
  static int Isqrt(int n) {
    if (n <= 0) return 0;
    int r = 0, b = 1 << 30;
    while (b > n) b >>= 2;
    while (b != 0) { if (n >= r + b) { n -= r + b; r = (r >> 1) + b; } else r >>= 1; b >>= 2; }
    return r;
  }
  static int Sin(int a) {
    a &= 4095;
    return a < 1024 ? SIN[a] : a < 2048 ? SIN[2048 - a] : a < 3072 ? -SIN[a - 2048] : -SIN[4096 - a];
  }
  static int Cos(int a) { return Sin(a + 1024); }
  static int Asinq(int s) {
    int lo = 0, hi = 1024;
    while (hi - lo > 1) { int m = (lo + hi) >> 1; if (SIN[m] <= s) lo = m; else hi = m; }
    return s - SIN[lo] <= SIN[hi] - s ? lo : hi;
  }
  static int Atan2(int y, int x) {
    int ax = Abs(x), ay = Abs(y), l = Isqrt(x * x + y * y);
    if (l == 0) return 0;
    int a = ay <= ax ? Asinq(ay * 16384 / l) : 1024 - Asinq(ax * 16384 / l);
    return x >= 0 ? (y >= 0 ? a : (4096 - a) & 4095) : (y >= 0 ? 2048 - a : 2048 + a);
  }

  // game state (the JS object g)
  uint rs; int t, phase, pt, cur, breaker, aim, spin, pow, holdT, gap, vShot, spinAdd, low0, fc, potm, msgT;
  int[] score = new int[2];
  bool inHand, kitchen, needRel, auto_;
  int[] bx = new int[10], by = new int[10], vx = new int[10], vy = new int[10]; bool[] on = new bool[10];
  string msg = "";
  int pAng, pPow, pX, pY, trS, trHit;
  bool pUp, pDown, pLeft, pRight, pA, pB;
  bool demo;
  Screen sc;

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; cur = 0; score[0] = 0; score[1] = 0; breaker = 0;
    inHand = false; kitchen = false; aim = 0; spin = 0; pow = 0; needRel = false; holdT = 0; gap = 0; vShot = 0; spinAdd = 0;
    for (int k = 0; k < 10; k++) { bx[k] = 0; by[k] = 0; vx[k] = 0; vy[k] = 0; on[k] = false; }
    low0 = 0; fc = 0; potm = 0; msg = ""; msgT = 0; auto_ = false; pAng = 0; pPow = 0; pX = 0; pY = 0; trS = 0; trHit = -1;
    pUp = pDown = pLeft = pRight = pA = pB = false;
  }

  // ---- the table ----
  int Lowest() { for (int k = 1; k <= 9; k++) if (on[k]) return k; return 0; }
  bool FreeAt(int x, int y, int skip) {
    for (int k = 0; k <= 9; k++) if (k != skip && on[k]) { int dx = bx[k] - x, dy = by[k] - y; if (dx * dx + dy * dy < D2SQ) return false; }
    return true;
  }
  void Spot(int k, int x, int y) {
    while (!FreeAt(x, y, k) && x < X1 - R) x += U;
    bx[k] = x; by[k] = y; vx[k] = 0; vy[k] = 0; on[k] = true;
  }
  void NewRack(int brk) {
    int[] order = { 2, 3, 4, 5, 6, 7, 8 };
    for (int i = 6; i > 0; i--) { int j = Rnd() % (i + 1), tt = order[i]; order[i] = order[j]; order[j] = tt; }
    int n = 0, o = 0;
    for (int r = 0; r < 5; r++) for (int i = 0; i < ROWS[r]; i++) {
      int k = n == 0 ? 1 : n == 4 ? 9 : order[o++];
      bx[k] = FOOT + r * 1000 + (Rnd() & 3); by[k] = CYU + (i * 2 - (ROWS[r] - 1)) * 578 + (Rnd() & 3);
      vx[k] = 0; vy[k] = 0; on[k] = true; n++;
    }
    bx[0] = HEAD; by[0] = CYU; vx[0] = 0; vy[0] = 0; on[0] = true;
    breaker = brk; cur = brk; inHand = true; kitchen = true; spin = 0;
    SetPhase(PLACE); Say("BREAK");
  }
  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 50; }

  // ---- physics ----
  void Sink(int k) { on[k] = false; vx[k] = 0; vy[k] = 0; potm |= 1 << k; if (k == 9 || k == 0) Beeper.Tone(330, 60, 40); else Beeper.Tone(523, 40, 40); }
  void Walls(int k) {
    int x = bx[k], y = by[k];
    bool sideY = y < Y0 + MC || y > Y1 - MC, sideX = x < X0 + MC || x > X1 - MC || Abs(x - CXU) < MS;
    int lb = sideY ? X0 - U : X0 + R, rb = sideY ? X1 + U : X1 - R, tb = sideX ? Y0 - U : Y0 + R, bb = sideX ? Y1 + U : Y1 - R;
    if (x < lb) { bx[k] = 2 * lb - x; vx[k] = -vx[k] * 3 / 4; vy[k] = vy[k] * 15 / 16; }
    else if (x > rb) { bx[k] = 2 * rb - x; vx[k] = -vx[k] * 3 / 4; vy[k] = vy[k] * 15 / 16; }
    if (y < tb) { by[k] = 2 * tb - y; vy[k] = -vy[k] * 3 / 4; vx[k] = vx[k] * 15 / 16; }
    else if (y > bb) { by[k] = 2 * bb - y; vy[k] = -vy[k] * 3 / 4; vx[k] = vx[k] * 15 / 16; }
  }
  void Pockets(int k) {
    int x = bx[k], y = by[k];
    if (x < X0 || x > X1 || y < Y0 || y > Y1) { Sink(k); return; }
    for (int p = 0; p < 6; p++) { int dx = x - PX[p], dy = y - PY[p]; if (dx * dx + dy * dy < CR2) { Sink(k); return; } }
  }
  void Collide(int i, int j) {
    int dx = bx[j] - bx[i], dy = by[j] - by[i];
    int dd = dx * dx + dy * dy;
    if (dd >= D2SQ) return;
    int d = Isqrt(dd); if (d == 0) { d = 1; dx = 1; dy = 0; }
    int vn = ((vx[i] - vx[j]) * dx + (vy[i] - vy[j]) * dy) / d;
    if (vn > 0) {
      int p = vn * 31 / 32, ix = p * dx / d, iy = p * dy / d;
      vx[i] -= ix; vy[i] -= iy; vx[j] += ix; vy[j] += iy;
      if (vn > 150) Beeper.Tone(1800, 8, Math.Min(60, vn / 16));
      if (i == 0 && fc == 0) {
        fc = j;
        vx[0] += (Cos(aim) * spinAdd) >> 14; vy[0] += (Sin(aim) * spinAdd) >> 14;
      }
    }
    int ov = D2 - d, sx = ov * dx / (d * 2), sy = ov * dy / (d * 2);
    bx[i] -= sx; by[i] -= sy; bx[j] += sx; by[j] += sy;
  }
  bool Physics() {
    int mv = 0;
    for (int k = 0; k <= 9; k++) if (on[k]) mv = Math.Max(mv, Math.Max(Abs(vx[k]), Abs(vy[k])));
    if (mv == 0) return false;
    int n = 1 + mv / 320;
    for (int s = 0; s < n; s++) {
      for (int k = 0; k <= 9; k++) if (on[k]) { bx[k] += vx[k] / n; by[k] += vy[k] / n; Walls(k); }
      for (int i = 0; i < 9; i++) if (on[i]) for (int j = i + 1; j <= 9; j++) if (on[j]) Collide(i, j);
      for (int k = 0; k <= 9; k++) if (on[k]) Pockets(k);
    }
    for (int k = 0; k <= 9; k++) if (on[k]) {
      int sp = Isqrt(vx[k] * vx[k] + vy[k] * vy[k]);
      if (sp <= FR) { vx[k] = 0; vy[k] = 0; }
      else { vx[k] = vx[k] * (sp - FR) / sp; vy[k] = vy[k] * (sp - FR) / sp; }
    }
    return true;
  }
  void Trace() {
    int c = Cos(aim), s = Sin(aim), cx = bx[0], cy = by[0];
    int best = 1 << 30, hit = -1;
    for (int k = 1; k <= 9; k++) if (on[k]) {
      int rx = bx[k] - cx, ry = by[k] - cy, proj = (rx * c + ry * s) >> 14;
      if (proj <= 0) continue;
      int perp = (ry * c - rx * s) >> 14;
      if (Abs(perp) >= D2) continue;
      int d = proj - Isqrt(D2SQ - perp * perp);
      if (d < best) { best = d; hit = k; }
    }
    int w = best;
    if (c > 0) w = Math.Min(w, (X1 - R - cx) * 16384 / c); else if (c < 0) w = Math.Min(w, (X0 + R - cx) * 16384 / c);
    if (s > 0) w = Math.Min(w, (Y1 - R - cy) * 16384 / s); else if (s < 0) w = Math.Min(w, (Y0 + R - cy) * 16384 / s);
    if (w < best) { best = w; hit = -1; }
    trS = Math.Max(0, best); trHit = hit;
  }

  // ---- the computer player ----
  bool Clear(int ax, int ay, int ex, int ey, int s1, int s2) {
    int ux = ex - ax, uy = ey - ay, l2 = ux * ux + uy * uy, l = Isqrt(l2);
    for (int k = 0; k <= 9; k++) if (on[k] && k != s1 && k != s2) {
      int rx = bx[k] - ax, ry = by[k] - ay, tt = rx * ux + ry * uy;
      if (tt <= 0) { if (rx * rx + ry * ry < D2SQ) return false; }
      else if (tt >= l2) { int qx = bx[k] - ex, qy = by[k] - ey; if (qx * qx + qy * qy < D2SQ) return false; }
      else if (Abs(rx * uy - ry * ux) < D2 * l) return false;
    }
    return true;
  }
  int Plan(int cx, int cy) {
    int tb = Lowest(), best = 0;
    for (int p = 0; p < 6; p++) {
      int tx = PX[p] - bx[tb], ty = PY[p] - by[tb], dt = Isqrt(tx * tx + ty * ty);
      if (dt < 64) continue;
      if (p == 1 || p == 4) { if (Abs(ty) * 3 < Abs(tx) * 2) continue; }
      int gx = bx[tb] - tx * D2 / dt, gy = by[tb] - ty * D2 / dt;
      if (gx < X0 + R - 64 || gx > X1 - R + 64 || gy < Y0 + R - 64 || gy > Y1 - R + 64) continue;
      int ax = gx - cx, ay = gy - cy, dc = Isqrt(ax * ax + ay * ay);
      if (dc < 64) continue;
      int cs = (ax * tx + ay * ty) / (dc * dt / 1024 + 1);
      if (cs < 250) continue;
      if (!Clear(cx, cy, gx, gy, 0, tb) || !Clear(bx[tb], by[tb], PX[p], PY[p], tb, 0)) continue;
      int sc2 = cs * 4096 / (dc + dt * 2 + 2048) + 1;
      if (sc2 > best) {
        best = sc2; pAng = Atan2(ay, ax);
        int st = Isqrt(2 * FR * (dt + 4096)), vi = st * 1056 / cs, v0 = Math.Min(VMAX, Isqrt(vi * vi + 2 * FR * dc));
        pPow = Math.Max(8, Math.Min(100, (v0 - 64 + 11) / 12));
      }
    }
    return best;
  }
  void AimAI() {
    int tb = Lowest(), cx = bx[0], cy = by[0], err = cur == 1 ? 6 : 3;
    if (kitchen) { pAng = Atan2(by[tb] - cy, bx[tb] - cx); pPow = 100; }
    else if (Plan(cx, cy) == 0) {
      int a0 = Atan2(by[tb] - cy, bx[tb] - cx), keep = aim; pAng = a0; pPow = 40;
      for (int d = 0; d <= 320; d += 8) {
        aim = (a0 + d) & 4095; Trace(); if (trHit == tb) { pAng = aim; break; }
        aim = (a0 - d) & 4095; Trace(); if (trHit == tb) { pAng = aim; break; }
      }
      aim = keep;
    }
    pAng = (pAng + Rnd() % (err * 2 + 1) - err) & 4095;
    pPow = Math.Max(4, Math.Min(100, pPow + Rnd() % 9 - 4));
  }
  void PlaceAI() {
    int tb = Lowest(), best = 0;
    pX = HEAD; pY = CYU + (Rnd() % 7 - 3) * 192;
    if (kitchen) return;
    for (int p = 0; p < 6; p++) for (int k = 1; k <= 3; k++) {
      int tx = PX[p] - bx[tb], ty = PY[p] - by[tb], dt = Isqrt(tx * tx + ty * ty);
      if (dt < 64) continue;
      int back = D2 + k * 1280, x = bx[tb] - tx * back / dt, y = by[tb] - ty * back / dt;
      if (x < X0 + R || x > X1 - R || y < Y0 + R || y > Y1 - R || !FreeAt(x, y, 0)) continue;
      int sc2 = Plan(x, y);
      if (sc2 > best) { best = sc2; pX = x; pY = y; }
    }
    if (best == 0) { pX = HEAD; pY = CYU; while (!FreeAt(pX, pY, 0) && pX < X1 - R) pX += U; }
  }

  // ---- the game ----
  void Strike() { vShot = 64 + pow * 12; spinAdd = spin * vShot / 5; gap = pow * 8; SetPhase(STRIKE); }
  void StartTurn() {
    if (inHand) SetPhase(PLACE);
    else { int tb = Lowest(); aim = Atan2(by[tb] - by[0], bx[tb] - bx[0]); SetPhase(AIM); }
    spin = 0; pow = 0;
  }
  void Judge() {
    bool scratch = !on[0], foul = scratch || fc != low0; int nine = (potm >> 9) & 1, was = cur;
    if (nine != 0 && !foul) {
      score[cur]++; Say(cur == 0 ? "YOU WIN" : "CPU WINS"); SetPhase(OVER);
      if (!demo) Scores.Report(score[0], score[0], score[1], 0);
      return;
    }
    if (nine != 0) Spot(9, FOOT, CYU);
    if (foul) {
      Say(scratch ? "SCRATCH" : fc == 0 ? "NO HIT" : "FOUL");
      cur = 1 - cur; inHand = true; kitchen = false;
      if (scratch) Spot(0, HEAD, CYU);
    } else if (potm == 0) { cur = 1 - cur; Say(cur == 0 ? "YOUR SHOT" : "CPU SHOT"); }
    else Say(was == 0 ? "GO AGAIN" : "CPU AGAIN");
    SetPhase(RESULT);
  }

  public override void Tick(int held, int pressed) {
    bool auto = demo;
    t++; pt++; if (msgT > 0) msgT--;
    bool up = !auto && (held & Buttons.Up) != 0, down = !auto && (held & Buttons.Down) != 0,
      left = !auto && (held & Buttons.Left) != 0, right = !auto && (held & Buttons.Right) != 0,
      a = !auto && (held & Buttons.A) != 0, b = !auto && (held & Buttons.B) != 0;
    bool eUp = up && !pUp, eDown = down && !pDown, eLeft = left && !pLeft, eRight = right && !pRight, eA = a && !pA, eB = b && !pB;
    pUp = up; pDown = down; pLeft = left; pRight = right; pA = a; pB = b;
    if (up || down || left || right) holdT++; else holdT = 0;
    auto_ = auto;
    bool ai = auto || cur == 1;

    switch (phase) {
      case TITLE:
        if (eA || (auto && pt > 40)) { score[0] = 0; score[1] = 0; NewRack(0); }
        break;
      case OVER:
        if ((eA && pt > 20) || ((auto || cur == 1) && pt > 120) || pt > 300) NewRack(1 - breaker);
        break;
      case RESULT:
        Physics();
        if (pt > 36) StartTurn();
        break;
      case PLACE: {
        int xmax = kitchen ? HEAD : X1 - R;
        if (ai) {
          if (pt == 1) PlaceAI();
          int dx = pX - bx[0], dy = pY - by[0];
          bx[0] += Math.Max(-256, Math.Min(256, dx)); by[0] += Math.Max(-256, Math.Min(256, dy));
          if (dx == 0 && dy == 0 && pt > 15 && FreeAt(bx[0], by[0], 0)) { inHand = false; StartTurn(); }
          else if (pt > 200) { pX = HEAD; pY = CYU; }
        } else {
          int sp = holdT < 6 ? 64 : 256;
          if (left) bx[0] -= sp; if (right) bx[0] += sp; if (up) by[0] -= sp; if (down) by[0] += sp;
          bx[0] = Math.Max(X0 + R, Math.Min(xmax, bx[0])); by[0] = Math.Max(Y0 + R, Math.Min(Y1 - R, by[0]));
          if (eA && FreeAt(bx[0], by[0], 0)) { inHand = false; StartTurn(); }
        }
        break;
      }
      case AIM:
        if (ai) {
          if (pt == 1) AimAI();
          int d = ((pAng - aim + 2048) & 4095) - 2048;
          aim = (aim + Math.Max(-40, Math.Min(40, d))) & 4095;
          if (d == 0 && pt > 40) { SetPhase(VIEW); needRel = false; pow = 0; }
        } else {
          int turn = holdT < 6 ? 2 : holdT < 20 ? 6 : 20, nudge = eUp || eDown || holdT > 10 ? 1 : 0;
          if (left) aim -= eLeft ? 2 : turn; if (right) aim += eRight ? 2 : turn;
          if (up) aim -= nudge; if (down) aim += nudge;
          aim &= 4095;
          if (eA) { SetPhase(VIEW); needRel = true; pow = 0; }
        }
        break;
      case VIEW:
        if (ai) {
          if (pt > 12) { if (pow < pPow) pow = Math.Min(pPow, pow + 2); else if (pt > 22 + pPow / 2) Strike(); }
        } else {
          int fine = holdT > 8 ? 1 : 0;
          if (left) aim -= eLeft ? 1 : fine; if (right) aim += eRight ? 1 : fine;
          aim &= 4095;
          if (eUp) spin = Math.Min(2, spin + 1); if (eDown) spin = Math.Max(-2, spin - 1);
          if (eB) { SetPhase(AIM); pow = 0; break; }
          if (needRel) { if (!a) needRel = false; }
          else if (a) pow = Math.Min(100, pow + 2);
          else if (pow > 0) { if (pow >= 4) Strike(); else pow = 0; }
        }
        break;
      case STRIKE:
        gap = Math.Max(0, gap - 64 - gap / 3);
        if (gap == 0) {
          low0 = Lowest(); fc = 0; potm = 0;
          vx[0] = (Cos(aim) * vShot) >> 14; vy[0] = (Sin(aim) * vShot) >> 14;
          Beeper.Tone(1200, 15, 30 + pow / 2);
          SetPhase(ROLL);
        }
        break;
      case ROLL:
        if (!Physics()) Judge();
        break;
    }
  }

  // ---- drawing ----
  void Px(int x, int y, bool v) { if (x >= 0 && x < 64 && y >= 0 && y < 64) sc.Set(x, y, v); }
  void Fill(int x, int y, int w, int h, bool v) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Px(i, j, v); }
  void Text(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int f = FONTCH.IndexOf(s[n]); if (f < 0) f = FONTCH.Length - 1;
      string g = FONT[f];
      for (int r = 0; r < 5; r++) for (int q = 0; q < 3; q++) if (g[r * 3 + q] == '1') Px(x + q, y + r, v);
      x += 4;
    }
  }
  void Big(int x, int y, string s) {
    for (int n = 0; n < s.Length; n++) {
      int f = FONTCH.IndexOf(s[n]); if (f < 0) f = FONTCH.Length - 1;
      string g = FONT[f];
      for (int r = 0; r < 5; r++) for (int q = 0; q < 3; q++) if (g[r * 3 + q] == '1') Fill(x + q * 2, y + r * 2, 2, 2, true);
      x += 8;
    }
  }
  void Centre(int y, string s) { Text(32 - ((s.Length * 4 - 1) >> 1), y, s, true); }
  void Line(int x0, int y0, int x1, int y1, int dots) {
    int dx = Abs(x1 - x0), dy = -Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
    int e = dx + dy, n = 0;
    for (;;) {
      if (dots == 0 || n % dots == 0) Px(x0, y0, true);
      n++;
      if (x0 == x1 && y0 == y1) break;
      int e2 = 2 * e;
      if (e2 >= dy) { e += dy; x0 += sx; }
      if (e2 <= dx) { e += dx; y0 += sy; }
    }
  }
  void ClipLine(int x0, int y0, int x1, int y1, int dots) {
    for (int it = 0; it < 6; it++) {
      int c0 = (x0 < 0 ? 1 : x0 > 63 ? 2 : 0) | (y0 < 0 ? 4 : y0 > 63 ? 8 : 0);
      int c1 = (x1 < 0 ? 1 : x1 > 63 ? 2 : 0) | (y1 < 0 ? 4 : y1 > 63 ? 8 : 0);
      if ((c0 | c1) == 0) { Line(x0, y0, x1, y1, dots); return; }
      if ((c0 & c1) != 0) return;
      int c = c0 != 0 ? c0 : c1, x, y;
      if ((c & 8) != 0) { x = x0 + (x1 - x0) * (63 - y0) / (y1 - y0); y = 63; }
      else if ((c & 4) != 0) { x = x0 + (x1 - x0) * (0 - y0) / (y1 - y0); y = 0; }
      else if ((c & 2) != 0) { y = y0 + (y1 - y0) * (63 - x0) / (x1 - x0); x = 63; }
      else { y = y0 + (y1 - y0) * (0 - x0) / (x1 - x0); x = 0; }
      if (c == c0) { x0 = x; y0 = y; } else { x1 = x; y1 = y; }
    }
  }
  void Circle(int cx, int cy, int r, bool v, int dots) {
    int rr = r * r + r;
    for (int d = -r; d <= r; d++) {
      int w = Isqrt(rr - d * d);
      if (dots == 0 || ((d + r) & 1) == 0) { Px(cx - w, cy + d, v); Px(cx + w, cy + d, v); Px(cx + d, cy - w, v); Px(cx + d, cy + w, v); }
    }
  }
  void Disc(int cx, int cy, int r, bool v) {
    int rr = r * r + r;
    for (int d = Math.Max(-r, -cy); d <= Math.Min(r, 63 - cy); d++) {
      int w = Isqrt(rr - d * d); for (int x = Math.Max(0, cx - w); x <= Math.Min(63, cx + w); x++) Px(x, cy + d, v);
    }
  }
  void Ball2(int x, int y, bool solid, bool dot) {
    for (int r = 0; r < 5; r++) for (int q = 0; q < 5; q++) if (((DISC[r] >> (4 - q)) & 1) != 0) Px(x - 2 + q, y - 2 + r, solid || r == 0 || r == 4 || q == 0 || q == 4);
    if (dot) Px(x, y, true);
  }

  void DrawTable() {
    Fill(2, 18, 60, 33, true);
    Fill(X0 >> 8, Y0 >> 8, (X1 - X0) >> 8, (Y1 - Y0) >> 8, false);
    for (int p = 0; p < 6; p++) Disc(PX[p] >> 8, PY[p] >> 8, 2, false);
    for (int k = 1; k < 8; k++) if (k != 4) { int x = 5 + k * 54 / 8; Px(x, 19, false); Px(x, 49, false); }
    for (int k = 1; k < 4; k++) { int y = 21 + k * 27 / 4; Px(3, y, false); Px(60, y, false); }
  }
  void DrawHud() {
    Text(1, 1, "YOU " + score[0].ToString(), true); string s = "CPU " + score[1].ToString(); Text(64 - s.Length * 4, 1, s, true);
    if (phase != OVER) { if (cur == 0) Line(1, 7, 11, 7, 0); else Line(64 - s.Length * 4, 7, 62, 7, 0); }
    int low = Lowest();
    for (int k = 1; k <= 9; k++) if (on[k]) {
      int x = 14 + k * 4 - 4;
      if (k == low && phase != ROLL) { Fill(x - 1, 9, 5, 7, true); Text(x, 10, k.ToString(), false); } else Text(x, 10, k.ToString(), true);
    }
  }
  void DrawOverhead() {
    DrawTable(); DrawHud();
    int low = Lowest(); bool blink = ((t >> 3) & 1) == 1;
    bool aiming = phase == AIM || phase == PLACE;
    for (int k = 1; k <= 9; k++) if (on[k]) Ball2(bx[k] >> 8, by[k] >> 8, k == low && aiming && blink, k == 9);
    if (on[0] && !(phase == PLACE && blink)) Ball2(bx[0] >> 8, by[0] >> 8, true, false);
    if (phase == AIM) {
      Trace();
      int c = Cos(aim), s = Sin(aim), cx = bx[0], cy = by[0];
      for (int d = R + 512; d < trS; d += 512) Px((cx + ((c * d) >> 14)) >> 8, (cy + ((s * d) >> 14)) >> 8, true);
      int gx = cx + ((c * trS) >> 14), gy = cy + ((s * trS) >> 14);
      Circle(gx >> 8, gy >> 8, 2, true, 2);
      if (trHit > 0) {
        int nx = bx[trHit] - gx, ny = by[trHit] - gy, l = Isqrt(nx * nx + ny * ny) + 1;
        for (int d = 3; d <= 8; d++) Px((bx[trHit] + nx * d * 256 / l) >> 8, (by[trHit] + ny * d * 256 / l) >> 8, true);
      }
      for (int d = R + 512; d < R + 18 * U; d += 128) Px((cx - ((c * d) >> 14)) >> 8, (cy - ((s * d) >> 14)) >> 8, true);
    }
    string m = msgT > 0 ? msg : "";
    if (phase == PLACE && msgT == 0) m = cur == 1 && !auto_ ? "CPU BALL IN HAND" : "BALL IN HAND";
    if (m.Length > 0) Centre(56, m);
    if (phase == OVER) {
      Fill(10, 27, 44, 15, false); Line(10, 27, 53, 27, 0); Line(10, 41, 53, 41, 0); Line(10, 27, 10, 41, 0); Line(53, 27, 53, 41, 0);
      Centre(32, msg);
    }
  }

  // ---- the low 3D view along the cue ----
  int camX, camY, camC, camS;
  void Line3c(int f1, int r1, int z1, int f2, int r2, int z2, int dots) {
    if (f1 < NEAR && f2 < NEAR) return;
    if (f1 < NEAR) { r1 += (r2 - r1) * (NEAR - f1) / (f2 - f1); z1 += (z2 - z1) * (NEAR - f1) / (f2 - f1); f1 = NEAR; }
    else if (f2 < NEAR) { r2 += (r1 - r2) * (NEAR - f2) / (f1 - f2); z2 += (z1 - z2) * (NEAR - f2) / (f1 - f2); f2 = NEAR; }
    ClipLine(32 + r1 * FOC / f1, HZ + (CHT - z1) * FOC / f1, 32 + r2 * FOC / f2, HZ + (CHT - z2) * FOC / f2, dots);
  }
  int CamF(int x, int y) { return ((x - camX) * camC + (y - camY) * camS) >> 14; }
  int CamR(int x, int y) { return ((y - camY) * camC - (x - camX) * camS) >> 14; }
  void Line3(int x1, int y1, int z1, int x2, int y2, int z2) { Line3c(CamF(x1, y1), CamR(x1, y1), z1, CamF(x2, y2), CamR(x2, y2), z2, 0); }
  void Cushion(int x1, int y1, int x2, int y2, int ox, int oy) {
    Line3(x1, y1, 0, x2, y2, 0); Line3(x1, y1, CZ, x2, y2, CZ);
    Line3(x1 + ox, y1 + oy, CZ, x2 + ox, y2 + oy, CZ);
    Line3(x1, y1, 0, x1, y1, CZ); Line3(x2, y2, 0, x2, y2, CZ);
    Line3(x1, y1, CZ, x1 + ox, y1 + oy, CZ); Line3(x2, y2, CZ, x2 + ox, y2 + oy, CZ);
  }
  int[] ord = new int[9], fs = new int[9];
  void Draw3d() {
    camC = Cos(aim); camS = Sin(aim);
    camX = bx[0] - ((camC * CB) >> 14); camY = by[0] - ((camS * CB) >> 14);
    const int RO = 768;
    Cushion(X0 + MC, Y0, CXU - MS, Y0, 0, -RO); Cushion(CXU + MS, Y0, X1 - MC, Y0, 0, -RO);
    Cushion(X0 + MC, Y1, CXU - MS, Y1, 0, RO); Cushion(CXU + MS, Y1, X1 - MC, Y1, 0, RO);
    Cushion(X0, Y0 + MC, X0, Y1 - MC, -RO, 0); Cushion(X1, Y0 + MC, X1, Y1 - MC, RO, 0);
    for (int p = 0; p < 6; p++) {
      for (int k = 0; k < 8; k++) {
        int a1 = k * 512, a2 = (k + 1) * 512;
        Line3(PX[p] + ((Cos(a1) * 704) >> 14), PY[p] + ((Sin(a1) * 704) >> 14), 0, PX[p] + ((Cos(a2) * 704) >> 14), PY[p] + ((Sin(a2) * 704) >> 14), 0);
      }
    }
    Trace();
    int low = Lowest(), no = 0;
    for (int k = 1; k <= 9; k++) if (on[k]) {
      int f = CamF(bx[k], by[k]);
      if (f < NEAR + R) continue;
      int i = no; no++;
      while (i > 0 && fs[i - 1] < f) { ord[i] = ord[i - 1]; fs[i] = fs[i - 1]; i--; }
      ord[i] = k; fs[i] = f;
    }
    for (int d = R + 768; d < trS; d += 768) { int f = CB + d; Px(32, HZ + CHT * FOC / f, true); }
    for (int n = 0; n < no; n++) {
      int k = ord[n], f = fs[n], r = CamR(bx[k], by[k]);
      int sx = 32 + r * FOC / f, sy = HZ + (CHT - R) * FOC / f, pr = R * FOC / f;
      if (pr < 1) { Px(sx, sy, true); continue; }
      Disc(sx, sy, pr, false); Circle(sx, sy, pr, true, 0);
      if (k == low) { for (int y = Math.Max(-pr + 1, -sy); y < Math.Min(pr, 64 - sy); y++) for (int x = Math.Max(-pr + 1, -sx); x < Math.Min(pr, 64 - sx); x++) if (((x + y) & 1) == 0 && x * x + y * y < pr * pr - pr) Px(sx + x, sy + y, true); }
      if (k == 9) { if (pr >= 3) Circle(sx, sy, pr >> 1, true, 0); else Px(sx, sy, true); }
    }
    if (trS > 0) { int f = CB + trS; Circle(32, HZ + (CHT - R) * FOC / f, R * FOC / f, true, 2); }
    int cr = R * FOC / CB, cy = HZ + (CHT - R) * FOC / CB;
    Circle(32, cy, cr, true, 0);
    int zt = R + spin * R / 3, ft = CB - R - 128 - (phase == VIEW ? pow * 8 : gap);
    Px(32, cy - spin * cr / 3, true);
    Line3c(ft, -90, zt, ft - 15360, -300, zt + 1600, 0); Line3c(ft, 90, zt, ft - 15360, 300, zt + 1600, 0);
    Line3c(ft, -90, zt, ft, 90, zt, 0);
    Fill(0, 20, 5, 44, false); Line(0, 20, 4, 20, 0); Line(0, 20, 0, 63, 0); Line(4, 20, 4, 63, 0);
    if (pow > 0) Fill(1, 63 - pow * 42 / 100, 3, pow * 42 / 100, true);
    Disc(58, 6, 4, false); Circle(58, 6, 4, true, 0); Px(58, 6 - spin * 2, true);
    Text(1, 1, cur == 0 ? (auto_ ? "DEMO" : "YOU") : "CPU", true);
    Fill(19, 0, 5, 7, true); Text(20, 1, low.ToString(), false);
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE) {
      Big(8, 4, "9-BALL");
      for (int r = 0; r < 5; r++) for (int i = 0; i < ROWS[r]; i++) Ball2(26 + r * 4, 30 + (i * 2 - (ROWS[r] - 1)) * 3, false, r == 2 && i == 1);
      Ball2(10, 30, true, false); Line(0, 30, 6, 30, 0);
      Centre(46, "YOU VS CPU");
      if (((t >> 4) & 1) != 0) Centre(56, "A: START");
      return;
    }
    if (phase == VIEW || phase == STRIKE) Draw3d(); else DrawOverhead();
  }
}
