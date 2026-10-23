// CHUCK-A-LUCK for the VMX: a line-for-line port of microgames/ui/games/chuckaluck_core.js to C# 1.0, so it plays
// exactly the same (same random numbers, same integer maths, same order of events); tools/parity checks that frame
// by frame. The carnival "birdcage": three dice in an hourglass wire cage that A flips end over end. Bet 0-20 chips
// on each face 1-6: one die showing it pays 1:1, two 2:1, three 10:1, none loses (house edge 10/216 = 4.63%); or LO
// (4-10) / HI (11-17) at 1:1, losing on triples (2.78%). Each die is exactly 1 in 6 (rejection sampling), thrown
// before the cage turns. Left/Right choose a box, Up/Down chips on / off (held = repeat), A flips, B clears.
// VMX CASINO: the stake comes from the shared bank (Microsoft.Vmx.Chips) when the cage flips; a win pays stake +
// winnings. Board: the biggest single payout.
using System;
using Microsoft.Vmx;

public class ChuckALuck : Game {
  int bestPay;                                              // this session's best (Scores)
  const string FONTCH = "ABCDEFGHIKLMNOPRSTUVWXYZ0123456789:!-<>+ ";
  static readonly string[] FONT = {
    "010101111101101", "110101110101110", "011100100100011", "110101101101110", "111100110100111", "111100110100100",
    "011100101101011", "101101111101101", "111010010010111", "101101110101101", "100100100100111", "101111111101101",
    "110101101101101", "010101101101010", "110101110100100", "110101110101101", "011100010001110", "111010010010010",
    "101101101101111", "101101101101010", "101101111111101", "101101010101101", "101101010010010", "111001010100111",
    "111101101101111", "010110010010111", "110001010100111", "110001010001110", "101101111001001", "111100110001110",
    "011100111101111", "111001010010010", "111101111101111", "111101111001110", "000010000010000", "010010010000010",
    "000000111000000", "001010100010001", "100010001010100", "000010111010000", "000000000000000" };
  static readonly int[] KX = { 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2 }, KY = { 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4 };
  static readonly int[] CHIP = { 14, 31, 27, 31, 14 };
  const int LO = 6, HI = 7, NB = 8, CAP = 20;
  static readonly int[] ORD = { LO, 0, 1, 2, 3, 4, 5, HI };
  static readonly int[] MULT = { 0, 1, 2, 10 };
  const int ROT = 36, T1 = 23, FALL = 14, SETTLE = 62, TCYC = 90;
  static readonly int[] DLY = { 6, 3, 0 };
  const int CX = 32, CY = 25, TCY = 36;
  static readonly int[] RX = { -11, -3, 5 }; const int RY = 8;
  const int TITLE = 0, BET = 1, FLIP = 2, RES = 3, BROKE = 4;

  // ==== DICE ART (the casino's shared dice look: copied from games/craps/Craps.cs) ================================
  // Two sizes: sz 0 = big, a 12x12 die (pips 2x2 on a 3x3 grid at 2/5/8), sz 1 = small, a 7x7 die (pips 1x1 at
  // 1/3/5). A face is a lit body with the corners cut and dark pips. DrawDie(x, y, face, sz) draws face 1-6 with its
  // top-left at x, y; DrawTumble(x, y, k, sz) draws tumbling frame k (0-2: the die turned about 22, 45 and 67
  // degrees), setting only its lit pixels. (The cage has its own fall below, CageDie.)
  static readonly int[] DSZ = { 12, 7 }, PIPW = { 2, 1 };
  static readonly int[][] PIPC = { new int[] { 2, 5, 8 }, new int[] { 1, 3, 5 } };
  static readonly int[] PIPS = { 0, 16, 68, 84, 325, 341, 455 };   // face n: pips on the 3x3 grid, bit (row * 3 + col)
  static readonly int[][][] TUMBLE = {                      // rows, bit 0 = left
    new int[][] { new int[] { 24, 248, 1020, 4068, 4070, 2046, 2046, 1663, 639, 1020, 496, 384 },
                  new int[] { 96, 240, 408, 924, 2046, 3219, 3219, 2046, 924, 408, 240, 96 },
                  new int[] { 384, 496, 1020, 831, 1855, 2046, 2046, 4046, 4044, 1020, 248, 24 } },
    new int[][] { new int[] { 4, 62, 126, 54, 63, 62, 16 }, new int[] { 8, 28, 62, 107, 62, 28, 8 },
                  new int[] { 16, 62, 47, 62, 122, 62, 4 } } };
  static readonly int[] FLICK = { 3, 5, 2, 6, 1, 4 };
  void DrawDie(int x, int y, int face, int sz) {
    int n = DSZ[sz], w = PIPW[sz], m = PIPS[face]; int[] pc = PIPC[sz];
    Fill(x + 1, y, n - 2, 1, true); Fill(x, y + 1, n, n - 2, true); Fill(x + 1, y + n - 1, n - 2, 1, true);
    for (int k = 0; k < 9; k++) if (((m >> k) & 1) != 0) Fill(x + pc[k % 3], y + pc[k / 3], w, w, false);
  }
  void DrawTumble(int x, int y, int k, int sz) {
    int[] fr = TUMBLE[sz][k]; int n = DSZ[sz];
    for (int r = 0; r < n; r++) { int m = fr[r]; for (int q = 0; m != 0; q++, m >>= 1) if ((m & 1) != 0) Px(x + q, y + r, true); }
  }
  // ==== end of dice art ===========================================================================================

  // ---- the cage's turn (pool's quarter SIN table) ----
  static readonly int[] SIN = { 0, 25, 50, 75, 101, 126, 151, 176, 201, 226, 251, 276, 302, 327, 352, 377, 402, 427, 452, 477, 503, 528, 553, 578, 603, 628, 653, 678, 704, 729, 754, 779, 804, 829, 854, 879, 904, 929, 955, 980, 1005, 1030, 1055, 1080, 1105, 1130, 1155, 1180, 1205, 1230, 1255, 1280, 1306, 1331, 1356, 1381, 1406, 1431, 1456, 1481, 1506, 1531, 1556, 1581, 1606, 1631, 1656, 1681, 1706, 1731, 1756, 1781, 1806, 1831, 1856, 1881, 1906, 1931, 1956, 1981, 2006, 2031, 2055, 2080, 2105, 2130, 2155, 2180, 2205, 2230, 2255, 2280, 2305, 2329, 2354, 2379, 2404, 2429, 2454, 2479, 2503, 2528, 2553, 2578, 2603, 2628, 2652, 2677, 2702, 2727, 2752, 2776, 2801, 2826, 2851, 2875, 2900, 2925, 2949, 2974, 2999, 3024, 3048, 3073, 3098, 3122, 3147, 3172, 3196, 3221, 3246, 3270, 3295, 3320, 3344, 3369, 3393, 3418, 3442, 3467, 3492, 3516, 3541, 3565, 3590, 3614, 3639, 3663, 3688, 3712, 3737, 3761, 3786, 3810, 3835, 3859, 3883, 3908, 3932, 3957, 3981, 4005, 4030, 4054, 4078, 4103, 4127, 4151, 4176, 4200, 4224, 4249, 4273, 4297, 4321, 4346, 4370, 4394, 4418, 4442, 4467, 4491, 4515, 4539, 4563, 4587, 4612, 4636, 4660, 4684, 4708, 4732, 4756, 4780, 4804, 4828, 4852, 4876, 4900, 4924, 4948, 4972, 4996, 5020, 5044, 5068, 5092, 5115, 5139, 5163, 5187, 5211, 5235, 5259, 5282, 5306, 5330, 5354, 5377, 5401, 5425, 5449, 5472, 5496, 5520, 5543, 5567, 5591, 5614, 5638, 5661, 5685, 5708, 5732, 5756, 5779, 5803, 5826, 5850, 5873, 5897, 5920, 5943, 5967, 5990, 6014, 6037, 6060, 6084, 6107, 6130, 6154, 6177, 6200, 6223, 6247, 6270, 6293, 6316, 6339, 6363, 6386, 6409, 6432, 6455, 6478, 6501, 6524, 6547, 6570, 6593, 6616, 6639, 6662, 6685, 6708, 6731, 6754, 6777, 6800, 6823, 6846, 6868, 6891, 6914, 6937, 6960, 6982, 7005, 7028, 7050, 7073, 7096, 7118, 7141, 7164, 7186, 7209, 7231, 7254, 7276, 7299, 7321, 7344, 7366, 7389, 7411, 7434, 7456, 7478, 7501, 7523, 7545, 7568, 7590, 7612, 7635, 7657, 7679, 7701, 7723, 7746, 7768, 7790, 7812, 7834, 7856, 7878, 7900, 7922, 7944, 7966, 7988, 8010, 8032, 8054, 8076, 8098, 8119, 8141, 8163, 8185, 8207, 8228, 8250, 8272, 8293, 8315, 8337, 8358, 8380, 8401, 8423, 8445, 8466, 8488, 8509, 8531, 8552, 8573, 8595, 8616, 8638, 8659, 8680, 8702, 8723, 8744, 8765, 8787, 8808, 8829, 8850, 8871, 8892, 8914, 8935, 8956, 8977, 8998, 9019, 9040, 9061, 9082, 9102, 9123, 9144, 9165, 9186, 9207, 9227, 9248, 9269, 9290, 9310, 9331, 9352, 9372, 9393, 9413, 9434, 9455, 9475, 9496, 9516, 9537, 9557, 9577, 9598, 9618, 9638, 9659, 9679, 9699, 9720, 9740, 9760, 9780, 9800, 9820, 9841, 9861, 9881, 9901, 9921, 9941, 9961, 9981, 10001, 10020, 10040, 10060, 10080, 10100, 10120, 10139, 10159, 10179, 10198, 10218, 10238, 10257, 10277, 10296, 10316, 10336, 10355, 10374, 10394, 10413, 10433, 10452, 10471, 10491, 10510, 10529, 10549, 10568, 10587, 10606, 10625, 10644, 10663, 10683, 10702, 10721, 10740, 10759, 10778, 10796, 10815, 10834, 10853, 10872, 10891, 10909, 10928, 10947, 10966, 10984, 11003, 11021, 11040, 11059, 11077, 11096, 11114, 11133, 11151, 11169, 11188, 11206, 11224, 11243, 11261, 11279, 11297, 11316, 11334, 11352, 11370, 11388, 11406, 11424, 11442, 11460, 11478, 11496, 11514, 11532, 11550, 11567, 11585, 11603, 11621, 11638, 11656, 11674, 11691, 11709, 11727, 11744, 11762, 11779, 11797, 11814, 11831, 11849, 11866, 11883, 11901, 11918, 11935, 11952, 11970, 11987, 12004, 12021, 12038, 12055, 12072, 12089, 12106, 12123, 12140, 12157, 12173, 12190, 12207, 12224, 12240, 12257, 12274, 12290, 12307, 12324, 12340, 12357, 12373, 12390, 12406, 12423, 12439, 12455, 12472, 12488, 12504, 12520, 12537, 12553, 12569, 12585, 12601, 12617, 12633, 12649, 12665, 12681, 12697, 12713, 12729, 12744, 12760, 12776, 12792, 12807, 12823, 12839, 12854, 12870, 12885, 12901, 12916, 12932, 12947, 12963, 12978, 12993, 13008, 13024, 13039, 13054, 13069, 13085, 13100, 13115, 13130, 13145, 13160, 13175, 13190, 13205, 13219, 13234, 13249, 13264, 13279, 13293, 13308, 13323, 13337, 13352, 13366, 13381, 13395, 13410, 13424, 13439, 13453, 13467, 13482, 13496, 13510, 13524, 13538, 13553, 13567, 13581, 13595, 13609, 13623, 13637, 13651, 13665, 13678, 13692, 13706, 13720, 13733, 13747, 13761, 13774, 13788, 13802, 13815, 13829, 13842, 13856, 13869, 13882, 13896, 13909, 13922, 13935, 13949, 13962, 13975, 13988, 14001, 14014, 14027, 14040, 14053, 14066, 14079, 14092, 14104, 14117, 14130, 14143, 14155, 14168, 14181, 14193, 14206, 14218, 14231, 14243, 14256, 14268, 14280, 14293, 14305, 14317, 14329, 14341, 14354, 14366, 14378, 14390, 14402, 14414, 14426, 14438, 14449, 14461, 14473, 14485, 14497, 14508, 14520, 14531, 14543, 14555, 14566, 14578, 14589, 14601, 14612, 14623, 14635, 14646, 14657, 14668, 14680, 14691, 14702, 14713, 14724, 14735, 14746, 14757, 14768, 14779, 14789, 14800, 14811, 14822, 14832, 14843, 14854, 14864, 14875, 14885, 14896, 14906, 14917, 14927, 14937, 14948, 14958, 14968, 14978, 14989, 14999, 15009, 15019, 15029, 15039, 15049, 15059, 15069, 15078, 15088, 15098, 15108, 15118, 15127, 15137, 15146, 15156, 15166, 15175, 15184, 15194, 15203, 15213, 15222, 15231, 15240, 15250, 15259, 15268, 15277, 15286, 15295, 15304, 15313, 15322, 15331, 15340, 15349, 15357, 15366, 15375, 15383, 15392, 15401, 15409, 15418, 15426, 15435, 15443, 15451, 15460, 15468, 15476, 15485, 15493, 15501, 15509, 15517, 15525, 15533, 15541, 15549, 15557, 15565, 15573, 15581, 15588, 15596, 15604, 15611, 15619, 15627, 15634, 15642, 15649, 15656, 15664, 15671, 15679, 15686, 15693, 15700, 15707, 15715, 15722, 15729, 15736, 15743, 15750, 15757, 15763, 15770, 15777, 15784, 15791, 15797, 15804, 15810, 15817, 15824, 15830, 15837, 15843, 15849, 15856, 15862, 15868, 15875, 15881, 15887, 15893, 15899, 15905, 15911, 15917, 15923, 15929, 15935, 15941, 15946, 15952, 15958, 15964, 15969, 15975, 15980, 15986, 15991, 15997, 16002, 16008, 16013, 16018, 16024, 16029, 16034, 16039, 16044, 16049, 16054, 16059, 16064, 16069, 16074, 16079, 16084, 16088, 16093, 16098, 16103, 16107, 16112, 16116, 16121, 16125, 16130, 16134, 16138, 16143, 16147, 16151, 16156, 16160, 16164, 16168, 16172, 16176, 16180, 16184, 16188, 16192, 16195, 16199, 16203, 16207, 16210, 16214, 16218, 16221, 16225, 16228, 16232, 16235, 16238, 16242, 16245, 16248, 16251, 16255, 16258, 16261, 16264, 16267, 16270, 16273, 16276, 16279, 16281, 16284, 16287, 16290, 16292, 16295, 16298, 16300, 16303, 16305, 16308, 16310, 16312, 16315, 16317, 16319, 16321, 16324, 16326, 16328, 16330, 16332, 16334, 16336, 16338, 16340, 16341, 16343, 16345, 16347, 16348, 16350, 16352, 16353, 16355, 16356, 16358, 16359, 16360, 16362, 16363, 16364, 16365, 16367, 16368, 16369, 16370, 16371, 16372, 16373, 16374, 16375, 16375, 16376, 16377, 16378, 16378, 16379, 16380, 16380, 16381, 16381, 16382, 16382, 16382, 16383, 16383, 16383, 16384, 16384, 16384, 16384, 16384, 16384 };
  static int Sin(int a) {
    a &= 4095;
    return a < 1024 ? SIN[a] : a < 2048 ? SIN[2048 - a] : a < 3072 ? -SIN[a - 2048] : -SIN[4096 - a];
  }
  static int Cos(int a) { return Sin(a + 1024); }
  static int Angle(int u) {
    if (u <= 0) return 0;
    if (u >= ROT) return 2048;
    return 2048 * u * u * (3 * ROT - 2 * u) / (ROT * ROT * ROT);
  }
  static int RotX(int x, int y, int c, int s) { return (x * c - y * s + 8192) >> 14; }
  static int RotY(int x, int y, int c, int s) { return (x * s + y * c + 8192) >> 14; }
  static readonly int[][] CAGE = {
    new int[] { 19, -15, 13, -14, 0, -13, -13, -14, -19, -15, -13, -16, 0, -17, 13, -16, 19, -15 },
    new int[] { 19, 15, 13, 16, 0, 17, -13, 16, -19, 15, -13, 14, 0, 13, 13, 14, 19, 15 },
    new int[] { -19, -15, -4, 0, -19, 15 }, new int[] { 19, -15, 4, 0, 19, 15 },
    new int[] { -5, -1, 5, -1 }, new int[] { -5, 1, 5, 1 } };
  static readonly int[][] WIRES = { new int[] { -10, -14, -2, 0, -10, 14 }, new int[] { 10, -14, 2, 0, 10, 14 }, new int[] { 0, -13, 0, 13 } };
  void Cage(int cx, int cy, int a) {
    int c = Cos(a), s = Sin(a);
    for (int w = 0; w < 2; w++) {
      int[][] set = w == 0 ? WIRES : CAGE;
      for (int r = 0; r < set.Length; r++) {
        int[] p = set[r];
        int ax = cx + RotX(p[0], p[1], c, s), ay = cy + RotY(p[0], p[1], c, s);
        for (int i = 2; i + 1 < p.Length; i += 2) {
          int bx = cx + RotX(p[i], p[i + 1], c, s), by = cy + RotY(p[i], p[i + 1], c, s);
          Line(ax, ay, bx, by, w == 0); ax = bx; ay = by;
        }
      }
    }
  }
  int cdX, cdY, cdF;
  void CageDie(int k, int u, int pf, int nf, int cx, int cy) {
    int st0 = T1 + DLY[k], lx = RX[k] + 3, ly = RY + 3;
    if (u < st0) {
      int a = Angle(u), c = Cos(a), s = Sin(a), q = ((a + 128) >> 8) & 3;
      cdX = cx + RotX(lx, ly, c, s) - 3; cdY = cy + RotY(lx, ly, c, s) - 3; cdF = q == 0 ? pf : -q;
      return;
    }
    int v = u - st0;
    if (v < FALL) {
      int a = Angle(st0), c = Cos(a), s = Sin(a);
      int sx = cx + RotX(lx, ly, c, s), sy = cy + RotY(lx, ly, c, s), ex = cx + lx, ey = cy + ly;
      int f = 256 * v * v / (FALL * FALL), g0 = (256 - f) * (256 - f), g1 = 2 * f * (256 - f), g2 = f * f;
      int x = (g0 * sx + g1 * cx + g2 * ex) / 65536, y = (g0 * sy + g1 * cy + g2 * ey) / 65536, q = v >> 1;
      cdX = x - 3; cdY = y - 3; cdF = (q & 1) == 1 ? -1 - ((q / 2 + k) % 3) : FLICK[(q / 2 + 3 * k) % 6];
      return;
    }
    int w = v - FALL;
    int h = 0;
    if (w < 8) h = 4 * w * (8 - w) / 16;
    else if (w < 14) h = 4 * (w - 8) * (14 - w) / 36;
    int q2 = w >> 1;
    cdX = cx + RX[k]; cdY = cy + RY - h;
    cdF = w >= 8 ? nf : (q2 & 1) == 1 ? -1 - ((q2 / 2 + k) % 3) : FLICK[(q2 / 2 + 3 * k) % 6];
  }
  void DrawCage(int u, int[] pf, int[] nf, int cx, int cy) {
    Cage(cx, cy, Angle(u) & 2047);
    for (int k = 0; k < 3; k++) {
      CageDie(k, u, pf[k], nf[k], cx, cy);
      if (cdF > 0) DrawDie(cdX, cdY, cdF, 1); else DrawTumble(cdX, cdY, -1 - cdF, 1);
    }
  }

  static int[] GLYPH, CHAR;
  static void BuildFont() {
    GLYPH = new int[FONT.Length]; CHAR = new int[128];
    for (int f = 0; f < FONT.Length; f++) { int m = 0; for (int k = 0; k < 15; k++) if (FONT[f][k] == '1') m |= 1 << k; GLYPH[f] = m; }
    for (int c = 0; c < 128; c++) CHAR[c] = FONTCH.Length - 1;
    for (int f = 0; f < FONTCH.Length; f++) CHAR[FONTCH[f]] = f;
  }

  // game state (the JS object g)
  uint rs; int t, phase, pt, holdT, msgT; string msg = "", call = ""; bool demo;
  int[] st = new int[NB], rst = new int[NB], oc = new int[NB], cnt = new int[6];
  int[] d = new int[3], pd = new int[3], tpf = new int[3], tnf = new int[3];
  int sel, net;
  int[] dT = new int[NB]; int dk; bool dPlan, dClr;
  bool pUp, pDown, pLeft, pRight, pA, pB;
  Screen sc;

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }
  int Die() {
    int r = Rnd();
    while (r >= 65532) r = Rnd();
    return r % 6 + 1;
  }

  public override void Start(int seed, bool demo) {
    if (GLYPH == null) BuildFont();
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; holdT = 0; msg = ""; msgT = 0;
    for (int i = 0; i < NB; i++) { st[i] = 0; rst[i] = 0; oc[i] = 0; dT[i] = 0; }
    for (int f = 0; f < 6; f++) cnt[f] = 0;
    d[0] = 5; d[1] = 1; d[2] = 3; pd[0] = 5; pd[1] = 1; pd[2] = 3;
    sel = 1; net = 0; call = "";
    dk = 0; dPlan = false; dClr = false;
    pUp = pDown = pLeft = pRight = pA = pB = false;
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  public override bool UsesB { get { return true; } }

  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 50; }
  int Total() { int s = 0; for (int i = 0; i < NB; i++) s += st[i]; return s; }
  string BlockAdd(int i) {
    if ((i == LO && st[HI] > 0) || (i == HI && st[LO] > 0)) return "LO OR HI";
    if (st[i] >= CAP) return "TABLE MAX 20";
    if (Total() + 1 > Chips.Balance()) return "NOT ENOUGH CHIPS";
    return "";
  }
  void AddChip(int i) { string w = BlockAdd(i); if (w.Length != 0) Say(w); else st[i]++; }
  void TakeChip(int i) { if (st[i] > 0) st[i]--; }
  void ClearBets() { for (int i = 0; i < NB; i++) st[i] = 0; }
  void ToBet() {
    int run = 0, c = Chips.Balance();
    for (int p = 0; p < NB; p++) {
      int i = ORD[p], n = Math.Min(st[i], c - run);
      st[i] = n; run += n;
    }
    SetPhase(c < 1 ? BROKE : BET);
    dPlan = false; dClr = false; dk = 0;
  }
  void DoFlip() {
    int tot = Total();
    if (tot == 0) { Say("PLACE A BET"); return; }
    Chips.Bet(tot);
    for (int k = 0; k < 3; k++) { pd[k] = d[k]; d[k] = Die(); }
    for (int i = 0; i < NB; i++) rst[i] = st[i];
    SetPhase(FLIP);
    Beeper.Tone(660, 30, 40);
  }
  void Settle() {
    int s = d[0] + d[1] + d[2]; bool trip = d[0] == d[1] && d[1] == d[2];
    int paid = 0, nt = 0;
    for (int f = 0; f < 6; f++) cnt[f] = 0;
    for (int k = 0; k < 3; k++) cnt[d[k] - 1]++;
    for (int i = 0; i < NB; i++) {
      int stk = st[i];
      oc[i] = 0;
      if (stk == 0) continue;
      int ret = 0;
      if (i < 6) { if (cnt[i] > 0) ret = stk * (1 + MULT[cnt[i]]); }
      else if (!trip && (i == LO ? s <= 10 : s >= 11)) ret = 2 * stk;
      oc[i] = ret > 0 ? 1 : 2;
      paid += ret; nt += ret - stk;
    }
    if (paid > 0) Chips.Pay(paid);
    net = nt;
    if (paid > bestPay) { bestPay = paid; if (!demo) Scores.Report(bestPay, 0, 0, 0); }   // board: biggest payout
    call = nt > 0 ? "WON " + nt.ToString() : nt < 0 ? "LOST " + (0 - nt).ToString() : "EVEN";
    if (nt > 0) Beeper.Tone(1047, 120, 40); else Beeper.Tone(440, 50, 40);
    SetPhase(RES);
  }
  void Act(bool aUp, bool aDown, bool aLeft, bool aRight, bool aA, bool aB) {
    if (aLeft) sel = (sel + NB - 1) % NB;
    if (aRight) sel = (sel + 1) % NB;
    if (aUp) AddChip(ORD[sel]);
    if (aDown) TakeChip(ORD[sel]);
    if (aB) ClearBets();
    if (aA) DoFlip();
  }
  void Plan() {
    for (int i = 0; i < NB; i++) dT[i] = 0;
    int n = 1 + Rnd() % 3;
    for (int j = 0; j < n; j++) {
      int f = Rnd() % 6;
      while (dT[f] > 0) f = (f + 1) % 6;
      dT[f] = 1 + Rnd() % 3;
    }
    if (Rnd() % 4 == 0) dT[Rnd() % 2 == 0 ? LO : HI] = 1 + Rnd() % 3;
    dPlan = true;
  }
  void DemoMove() {
    bool u = false, dn = false, l = false, r = false, a = false, b = false;
    if (!dClr) { dClr = true; b = true; }
    else {
      while (dk < NB) {
        int i = ORD[dk];
        if ((st[i] < dT[i] && BlockAdd(i).Length == 0) || st[i] > dT[i]) break;
        dk++;
      }
      if (dk < NB) {
        if (sel < dk) r = true;
        else if (sel > dk) l = true;
        else if (st[ORD[dk]] < dT[ORD[dk]]) u = true;
        else dn = true;
      } else a = true;
    }
    Act(u, dn, l, r, a, b);
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
    bool rep = holdT > 9 && (holdT & 1) == 0;

    switch (phase) {
      case TITLE:
        if (eA || (auto && pt > 40)) ToBet();
        break;
      case BET:
        if (auto) {
          if (!dPlan) Plan();
          if (pt > 12 && pt % 4 == 0) DemoMove();
        } else Act(eUp || (up && rep), eDown || (down && rep), eLeft || (left && rep), eRight || (right && rep), eA, eB);
        break;
      case FLIP:
        for (int k = 0; k < 3; k++) if (pt == T1 + DLY[k] + FALL) Beeper.Tone(220 + 40 * k, 20, 30);
        if (pt >= SETTLE) Settle();
        break;
      case RES:
        if ((eA && pt > 10) || (auto && pt > 75) || pt > 600) ToBet();
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
  void Rect(int x, int y, int w, int h) {
    Fill(x, y, w, 1, true); Fill(x, y + h - 1, w, 1, true); Fill(x, y, 1, h, true); Fill(x + w - 1, y, 1, h, true);
  }
  void Line(int x0, int y0, int x1, int y1, bool dots) {
    int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
    int e = dx + dy, n = 0;
    bool inside = x0 >= 0 && x0 < 64 && x1 >= 0 && x1 < 64 && y0 >= 0 && y0 < 64 && y1 >= 0 && y1 < 64;
    for (;;) {
      if (!dots || (n & 1) == 0) { if (inside) sc.Set(x0, y0, true); else Px(x0, y0, true); }
      n++;
      if (x0 == x1 && y0 == y1) break;
      int e2 = 2 * e;
      if (e2 >= dy) { e += dy; x0 += sx; }
      if (e2 <= dx) { e += dx; y0 += sy; }
    }
  }
  void Text(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int c = s[n], m = GLYPH[c < 128 ? CHAR[c] : FONTCH.Length - 1];
      if (x >= 0 && x <= 61 && y >= 0 && y <= 59) { for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) sc.Set(x + KX[k], y + KY[k], v); }
      else for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) Px(x + KX[k], y + KY[k], v);
      x += 4;
    }
  }
  void Big(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int c = s[n], m = GLYPH[c < 128 ? CHAR[c] : FONTCH.Length - 1];
      for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) Fill(x + k % 3 * 2, y + k / 3 * 2, 2, 2, v);
      x += 8;
    }
  }
  void Centre(int y, string s) { Text(32 - ((s.Length * 4 - 1) >> 1), y, s, true); }
  void Right(int x, int y, string s, bool v) { Text(x - s.Length * 4, y, s, v); }
  void Mid(int cx, int y, string s, bool v) { Text(cx - ((s.Length * 4 - 1) >> 1), y, s, v); }
  void Boxed(int y, string s) {
    int w = s.Length * 4 + 3, x = 32 - (w >> 1);
    Fill(x, y, w, 9, false); Rect(x, y, w, 9); Text(x + 2, y + 2, s, true);
  }
  void TopBar() {
    for (int r = 0; r < 5; r++) for (int q = 0; q < 5; q++) if (((CHIP[r] >> (4 - q)) & 1) != 0) sc.Set(1 + q, 1 + r, true);
    Text(8, 1, Chips.Balance().ToString(), true);
    if (phase == RES) {
      int s = d[0] + d[1] + d[2];
      Right(64, 1, d[0] == d[1] && d[1] == d[2] ? "TRIPLE" : "SUM " + s.ToString(), true);
    } else Right(64, 1, "BET " + Total().ToString(), true);
  }
  void SideBox(int i, bool res) {
    int x = i == LO ? 0 : 50, cx = x + 7; bool on = !res && ORD[sel] == i, win = res && oc[i] == 1, v = !(on || win);
    Rect(x, 13, 14, 22);
    if (on || win) Fill(x + 1, 14, 12, 20, true);
    Mid(cx, 15, i == LO ? "LO" : "HI", v);
    Mid(cx, 21, i == LO ? "10-" : "11+", v);
    int stk = res ? rst[i] : st[i];
    if (stk > 0 || on) Mid(cx, 28, stk.ToString(), v);
    if (res && oc[i] == 2) { Line(x + 2, 28, x + 11, 32, false); Line(x + 2, 32, x + 11, 28, false); }
  }
  void FaceBox(int f, bool res) {
    int x = 2 + 10 * f, cx = x + 5; bool on = !res && ORD[sel] == f, win = res && oc[f] == 1;
    Rect(x, 42, 11, 16);
    if (on || win) { Fill(x + 1, 43, 9, 14, true); Fill(x + 1, 43, 9, 9, false); }
    DrawDie(x + 2, 44, f + 1, 1);
    bool v = !(on || win);
    if (res && cnt[f] > 0) {
      int n = cnt[f], x0 = cx - (3 * n - 1) / 2;
      for (int k = 0; k < n; k++) Fill(x0 + 3 * k, 53, 2, 2, v);
    } else {
      int stk = res ? rst[f] : st[f];
      if (stk > 0 || on) Mid(cx, 52, stk.ToString(), v);
      if (res && oc[f] == 2) { Line(x + 1, 52, x + 9, 56, false); Line(x + 1, 56, x + 9, 52, false); }
    }
  }
  void Table(bool res) {
    TopBar();
    SideBox(LO, res); SideBox(HI, res);
    for (int f = 0; f < 6; f++) FaceBox(f, res);
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == TITLE || phase == BROKE) {
      int c = t / TCYC, u = t % TCYC;
      for (int k = 0; k < 3; k++) { tpf[k] = FLICK[(c + 5 + 2 * k) % 6]; tnf[k] = FLICK[(c + 2 * k) % 6]; }
      DrawCage(u, tpf, tnf, CX, TCY);
      Big(13, 1, "CHUCK", true);
      Centre(12, "A-LUCK");
      if (phase == BROKE) { Boxed(2, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Boxed(53, "MORE TOMORROW"); return; }
      if (msgT > 0) Boxed(2, msg);
      if (((t >> 4) & 1) != 0) Boxed(53, "A: START");
      return;
    }
    if (phase == FLIP) { DrawCage(pt, pd, d, CX, CY); return; }
    bool res = phase == RES;
    DrawCage(SETTLE, d, d, CX, CY);
    Table(res);
    if (res) Centre(59, pt > 40 && ((t >> 4) & 1) == 1 ? "A: NEXT" : call);
    else if (msgT > 0) Centre(59, msg);
    else if (((t >> 4) & 1) != 0) Centre(59, "A:FLIP B:CLEAR");
  }
}
