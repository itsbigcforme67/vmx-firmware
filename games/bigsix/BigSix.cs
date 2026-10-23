// BIG SIX for the VMX: a line-for-line port of microgames/ui/games/bigsix_core.js to C# 1.0, so it plays exactly
// the same (same random numbers, same integer maths, same order of events); tools/parity checks that frame by frame.
// The carnival money wheel: 54 segments ($1 x24, $2 x15, $5 x7, $10 x4, $20 x2, JOKER x1, VMX X LOGO x1). Bet 0-20
// chips on each symbol; it pays its face value ($1 1:1 ... $20 20:1, JOKER and LOGO 40:1), stake back on a win.
// The segment is drawn first (each exactly 1 in 54), then the wheel is slowed to stop on it. House edge: $1 11.1%,
// $2 16.7%, $5 22.2%, $10 18.5%, $20 22.2%, JOKER / LOGO 24.1%. Up/Down choose a symbol, Left/Right a chip off / on
// (held = repeat), A spins, B clears. VMX CASINO: stakes come from the shared bank (Microsoft.Vmx.Chips).
using System;
using Microsoft.Vmx;

public class BigSix : Game {
  int bestWin;                                              // the biggest payout this session (Scores)
  static readonly int[] SIN = { 0, 25, 50, 75, 101, 126, 151, 176, 201, 226, 251, 276, 302, 327, 352, 377, 402, 427, 452, 477, 503, 528, 553, 578, 603, 628, 653, 678, 704, 729, 754, 779, 804, 829, 854, 879, 904, 929, 955, 980, 1005, 1030, 1055, 1080, 1105, 1130, 1155, 1180, 1205, 1230, 1255, 1280, 1306, 1331, 1356, 1381, 1406, 1431, 1456, 1481, 1506, 1531, 1556, 1581, 1606, 1631, 1656, 1681, 1706, 1731, 1756, 1781, 1806, 1831, 1856, 1881, 1906, 1931, 1956, 1981, 2006, 2031, 2055, 2080, 2105, 2130, 2155, 2180, 2205, 2230, 2255, 2280, 2305, 2329, 2354, 2379, 2404, 2429, 2454, 2479, 2503, 2528, 2553, 2578, 2603, 2628, 2652, 2677, 2702, 2727, 2752, 2776, 2801, 2826, 2851, 2875, 2900, 2925, 2949, 2974, 2999, 3024, 3048, 3073, 3098, 3122, 3147, 3172, 3196, 3221, 3246, 3270, 3295, 3320, 3344, 3369, 3393, 3418, 3442, 3467, 3492, 3516, 3541, 3565, 3590, 3614, 3639, 3663, 3688, 3712, 3737, 3761, 3786, 3810, 3835, 3859, 3883, 3908, 3932, 3957, 3981, 4005, 4030, 4054, 4078, 4103, 4127, 4151, 4176, 4200, 4224, 4249, 4273, 4297, 4321, 4346, 4370, 4394, 4418, 4442, 4467, 4491, 4515, 4539, 4563, 4587, 4612, 4636, 4660, 4684, 4708, 4732, 4756, 4780, 4804, 4828, 4852, 4876, 4900, 4924, 4948, 4972, 4996, 5020, 5044, 5068, 5092, 5115, 5139, 5163, 5187, 5211, 5235, 5259, 5282, 5306, 5330, 5354, 5377, 5401, 5425, 5449, 5472, 5496, 5520, 5543, 5567, 5591, 5614, 5638, 5661, 5685, 5708, 5732, 5756, 5779, 5803, 5826, 5850, 5873, 5897, 5920, 5943, 5967, 5990, 6014, 6037, 6060, 6084, 6107, 6130, 6154, 6177, 6200, 6223, 6247, 6270, 6293, 6316, 6339, 6363, 6386, 6409, 6432, 6455, 6478, 6501, 6524, 6547, 6570, 6593, 6616, 6639, 6662, 6685, 6708, 6731, 6754, 6777, 6800, 6823, 6846, 6868, 6891, 6914, 6937, 6960, 6982, 7005, 7028, 7050, 7073, 7096, 7118, 7141, 7164, 7186, 7209, 7231, 7254, 7276, 7299, 7321, 7344, 7366, 7389, 7411, 7434, 7456, 7478, 7501, 7523, 7545, 7568, 7590, 7612, 7635, 7657, 7679, 7701, 7723, 7746, 7768, 7790, 7812, 7834, 7856, 7878, 7900, 7922, 7944, 7966, 7988, 8010, 8032, 8054, 8076, 8098, 8119, 8141, 8163, 8185, 8207, 8228, 8250, 8272, 8293, 8315, 8337, 8358, 8380, 8401, 8423, 8445, 8466, 8488, 8509, 8531, 8552, 8573, 8595, 8616, 8638, 8659, 8680, 8702, 8723, 8744, 8765, 8787, 8808, 8829, 8850, 8871, 8892, 8914, 8935, 8956, 8977, 8998, 9019, 9040, 9061, 9082, 9102, 9123, 9144, 9165, 9186, 9207, 9227, 9248, 9269, 9290, 9310, 9331, 9352, 9372, 9393, 9413, 9434, 9455, 9475, 9496, 9516, 9537, 9557, 9577, 9598, 9618, 9638, 9659, 9679, 9699, 9720, 9740, 9760, 9780, 9800, 9820, 9841, 9861, 9881, 9901, 9921, 9941, 9961, 9981, 10001, 10020, 10040, 10060, 10080, 10100, 10120, 10139, 10159, 10179, 10198, 10218, 10238, 10257, 10277, 10296, 10316, 10336, 10355, 10374, 10394, 10413, 10433, 10452, 10471, 10491, 10510, 10529, 10549, 10568, 10587, 10606, 10625, 10644, 10663, 10683, 10702, 10721, 10740, 10759, 10778, 10796, 10815, 10834, 10853, 10872, 10891, 10909, 10928, 10947, 10966, 10984, 11003, 11021, 11040, 11059, 11077, 11096, 11114, 11133, 11151, 11169, 11188, 11206, 11224, 11243, 11261, 11279, 11297, 11316, 11334, 11352, 11370, 11388, 11406, 11424, 11442, 11460, 11478, 11496, 11514, 11532, 11550, 11567, 11585, 11603, 11621, 11638, 11656, 11674, 11691, 11709, 11727, 11744, 11762, 11779, 11797, 11814, 11831, 11849, 11866, 11883, 11901, 11918, 11935, 11952, 11970, 11987, 12004, 12021, 12038, 12055, 12072, 12089, 12106, 12123, 12140, 12157, 12173, 12190, 12207, 12224, 12240, 12257, 12274, 12290, 12307, 12324, 12340, 12357, 12373, 12390, 12406, 12423, 12439, 12455, 12472, 12488, 12504, 12520, 12537, 12553, 12569, 12585, 12601, 12617, 12633, 12649, 12665, 12681, 12697, 12713, 12729, 12744, 12760, 12776, 12792, 12807, 12823, 12839, 12854, 12870, 12885, 12901, 12916, 12932, 12947, 12963, 12978, 12993, 13008, 13024, 13039, 13054, 13069, 13085, 13100, 13115, 13130, 13145, 13160, 13175, 13190, 13205, 13219, 13234, 13249, 13264, 13279, 13293, 13308, 13323, 13337, 13352, 13366, 13381, 13395, 13410, 13424, 13439, 13453, 13467, 13482, 13496, 13510, 13524, 13538, 13553, 13567, 13581, 13595, 13609, 13623, 13637, 13651, 13665, 13678, 13692, 13706, 13720, 13733, 13747, 13761, 13774, 13788, 13802, 13815, 13829, 13842, 13856, 13869, 13882, 13896, 13909, 13922, 13935, 13949, 13962, 13975, 13988, 14001, 14014, 14027, 14040, 14053, 14066, 14079, 14092, 14104, 14117, 14130, 14143, 14155, 14168, 14181, 14193, 14206, 14218, 14231, 14243, 14256, 14268, 14280, 14293, 14305, 14317, 14329, 14341, 14354, 14366, 14378, 14390, 14402, 14414, 14426, 14438, 14449, 14461, 14473, 14485, 14497, 14508, 14520, 14531, 14543, 14555, 14566, 14578, 14589, 14601, 14612, 14623, 14635, 14646, 14657, 14668, 14680, 14691, 14702, 14713, 14724, 14735, 14746, 14757, 14768, 14779, 14789, 14800, 14811, 14822, 14832, 14843, 14854, 14864, 14875, 14885, 14896, 14906, 14917, 14927, 14937, 14948, 14958, 14968, 14978, 14989, 14999, 15009, 15019, 15029, 15039, 15049, 15059, 15069, 15078, 15088, 15098, 15108, 15118, 15127, 15137, 15146, 15156, 15166, 15175, 15184, 15194, 15203, 15213, 15222, 15231, 15240, 15250, 15259, 15268, 15277, 15286, 15295, 15304, 15313, 15322, 15331, 15340, 15349, 15357, 15366, 15375, 15383, 15392, 15401, 15409, 15418, 15426, 15435, 15443, 15451, 15460, 15468, 15476, 15485, 15493, 15501, 15509, 15517, 15525, 15533, 15541, 15549, 15557, 15565, 15573, 15581, 15588, 15596, 15604, 15611, 15619, 15627, 15634, 15642, 15649, 15656, 15664, 15671, 15679, 15686, 15693, 15700, 15707, 15715, 15722, 15729, 15736, 15743, 15750, 15757, 15763, 15770, 15777, 15784, 15791, 15797, 15804, 15810, 15817, 15824, 15830, 15837, 15843, 15849, 15856, 15862, 15868, 15875, 15881, 15887, 15893, 15899, 15905, 15911, 15917, 15923, 15929, 15935, 15941, 15946, 15952, 15958, 15964, 15969, 15975, 15980, 15986, 15991, 15997, 16002, 16008, 16013, 16018, 16024, 16029, 16034, 16039, 16044, 16049, 16054, 16059, 16064, 16069, 16074, 16079, 16084, 16088, 16093, 16098, 16103, 16107, 16112, 16116, 16121, 16125, 16130, 16134, 16138, 16143, 16147, 16151, 16156, 16160, 16164, 16168, 16172, 16176, 16180, 16184, 16188, 16192, 16195, 16199, 16203, 16207, 16210, 16214, 16218, 16221, 16225, 16228, 16232, 16235, 16238, 16242, 16245, 16248, 16251, 16255, 16258, 16261, 16264, 16267, 16270, 16273, 16276, 16279, 16281, 16284, 16287, 16290, 16292, 16295, 16298, 16300, 16303, 16305, 16308, 16310, 16312, 16315, 16317, 16319, 16321, 16324, 16326, 16328, 16330, 16332, 16334, 16336, 16338, 16340, 16341, 16343, 16345, 16347, 16348, 16350, 16352, 16353, 16355, 16356, 16358, 16359, 16360, 16362, 16363, 16364, 16365, 16367, 16368, 16369, 16370, 16371, 16372, 16373, 16374, 16375, 16375, 16376, 16377, 16378, 16378, 16379, 16380, 16380, 16381, 16381, 16382, 16382, 16382, 16383, 16383, 16383, 16384, 16384, 16384, 16384, 16384, 16384 };
  const string FONTCH = "ABCDEFGHIJKLMNOPRSTUVWXYZ0123456789:!-<>+$ ";
  static readonly string[] FONT = {
    "010101111101101", "110101110101110", "011100100100011", "110101101101110", "111100110100111", "111100110100100",
    "011100101101011", "101101111101101", "111010010010111", "001001001101010", "101101110101101", "100100100100111",
    "101111111101101", "110101101101101", "010101101101010", "110101110100100", "110101110101101", "011100010001110",
    "111010010010010", "101101101101111", "101101101101010", "101101111111101", "101101010101101", "101101010010010",
    "111001010100111", "111101101101111", "010110010010111", "110001010100111", "110001010001110", "101101111001001",
    "111100110001110", "011100111101111", "111001010010010", "111101111101111", "111101111001110", "000010000010000",
    "010010010000010", "000000111000000", "001010100010001", "100010001010100", "000010111010000", "011110111011110",
    "000000000000000" };
  static readonly int[] KX = { 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2 }, KY = { 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4 };
  static readonly int[] CHIP = { 14, 31, 27, 31, 14 };
  static readonly int[] LOGO = { 14, 21, 27, 21, 14 };
  static readonly int[] LOGO7 = { 28, 62, 93, 107, 119, 107, 93, 62, 28 };
  static readonly int[] WHEEL = { 6, 0, 1, 2, 0, 1, 0, 3, 0, 1, 0, 2, 1, 0, 4, 0, 1, 0, 0, 2, 3, 1, 0, 1, 0, 0, 1, 5, 2, 0, 1, 0, 0, 1, 3,
    2, 0, 0, 1, 0, 4, 1, 2, 0, 0, 1, 0, 3, 1, 0, 2, 0, 1, 0 };
  static readonly string[] NAMES = { "$1", "$2", "$5", "$10", "$20", "JOKER", "X LOGO" };
  static readonly string[] LAB = { "1", "2", "5", "10", "20", "J", "X" };
  static readonly int[] PAY = { 1, 2, 5, 10, 20, 40, 40 };
  const int NB = 7, NS = 54, CAP = 20, TOP = 3072;
  const int T = 200, LAND = 60;
  const int TITLE = 0, BET = 1, SPIN = 2, LANDP = 3, PAYP = 4, BROKE = 5;

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
  static int Sdist(int a) { return ((a + 2048) & 4095) - 2048; }

  // the wheel's pixels, worked out once (the JS does it at module load). Faster than the JS but the same pixels: the
  // band pixels are kept sorted into 256 buckets by screen angle, so a lit segment only visits the pixels near it;
  // the static rings are runs for the native Screen.Fill; the font is bitmasks.
  static int[] BND, SEGOF, bandX, bandY, bandA, bucket, runX, runY, runW, GLYPH, CHAR; static int bandN, runN;
  static void BuildWheel() {
    BND = new int[NS + 1]; SEGOF = new int[4096];
    for (int k = 0; k <= NS; k++) BND[k] = k * 4096 / NS;
    for (int k = 0; k < NS; k++) for (int a = BND[k]; a < BND[k + 1]; a++) SEGOF[a] = k;
    GLYPH = new int[FONT.Length]; CHAR = new int[128];
    for (int f = 0; f < FONT.Length; f++) { int m = 0; for (int k = 0; k < 15; k++) if (FONT[f][k] == '1') m |= 1 << k; GLYPH[f] = m; }
    for (int c = 0; c < 128; c++) CHAR[c] = FONTCH.Length - 1;
    for (int f = 0; f < FONTCH.Length; f++) CHAR[FONTCH[f]] = f;
    int[] tp = new int[4096], ta = new int[4096]; int tn = 0;
    runX = new int[300]; runY = new int[300]; runW = new int[300]; runN = 0;
    for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) {
      int dx = 2 * x - 63, dy = 2 * y - 236, d2 = dx * dx + dy * dy;
      if ((d2 >= 48400 && d2 < 49284) || (d2 >= 28900 && d2 < 29584)) {
        if (runN > 0 && runY[runN - 1] == y && runX[runN - 1] + runW[runN - 1] == x) runW[runN - 1]++;
        else { runX[runN] = x; runY[runN] = y; runW[runN] = 1; runN++; }
      } else if (d2 >= 29584 && d2 < 48400) { tp[tn] = y * 64 + x; ta[tn] = Atan2(dy, dx); tn++; }
    }
    bandN = tn; bandX = new int[tn]; bandY = new int[tn]; bandA = new int[tn]; bucket = new int[257];
    for (int i = 0; i < tn; i++) bucket[(ta[i] >> 4) + 1]++;
    for (int k = 0; k < 256; k++) bucket[k + 1] += bucket[k];
    int[] fillp = new int[256];
    for (int k = 0; k < 256; k++) fillp[k] = bucket[k];
    for (int i = 0; i < tn; i++) { int j = fillp[ta[i] >> 4]++; bandX[j] = tp[i] & 63; bandY[j] = tp[i] >> 6; bandA[j] = ta[i]; }
  }

  // game state (the JS object g)
  uint rs; int t, phase, pt, holdT, msgT; string msg = ""; bool auto_, demo;
  int[] st = new int[NB]; int sel; int[] hist = new int[5]; int histN;
  int staked, won, seg, res, wa, waEnd, drel; int[] pr = new int[NB]; int prN;
  int[] dRow = new int[3], dAmt = new int[3]; int dN, dk; bool dClr;
  bool pUp, pDown, pLeft, pRight, pA, pB;
  int lastPeg;                                              // for the clapper's tick sound only
  Screen sc;

  int Rnd() { rs = rs * 1664525u + 1013904223u; return (int)(rs >> 16); }

  public override void Start(int seed, bool demo) {
    if (BND == null) BuildWheel();
    this.demo = demo; rs = (uint)seed; t = 0; phase = TITLE; pt = 0; holdT = 0; msg = ""; msgT = 0; auto_ = false;
    for (int i = 0; i < NB; i++) { st[i] = 0; pr[i] = 0; }
    sel = 0; histN = 0; staked = 0; won = 0; seg = 0; res = 0; wa = 0; waEnd = 0; drel = 0; prN = 0;
    for (int i = 0; i < 3; i++) { dRow[i] = 0; dAmt[i] = 0; }
    dN = 0; dk = 0; dClr = false; lastPeg = 0;
    pUp = pDown = pLeft = pRight = pA = pB = false;
    int bonus = Chips.Bonus(); if (bonus > 0) Say("DAILY CHIPS +" + bonus.ToString());
  }
  public override bool UsesB { get { return true; } }

  void SetPhase(int p) { phase = p; pt = 0; }
  void Say(string m) { msg = m; msgT = 50; }
  int Total() { int s = 0; for (int i = 0; i < NB; i++) s += st[i]; return s; }
  void ToBet() {
    int run = 0, c = Chips.Balance();
    for (int i = 0; i < NB; i++) { st[i] = Math.Min(st[i], c - run); run += st[i]; }
    SetPhase(c < 1 ? BROKE : BET);
    dClr = false; dk = 0; dN = 0;
  }
  void DoSpin() {
    int tot = Total();
    if (tot == 0) { Say("PLACE A BET"); return; }
    Chips.Bet(tot); staked = tot;
    int r = Rnd();
    while (r >= 65502) r = Rnd();
    seg = r % NS; res = WHEEL[seg];
    int w = BND[seg + 1] - BND[seg];
    int pEnd = BND[seg] + 12 + Rnd() % (w - 24);
    waEnd = (TOP - pEnd) & 4095;
    drel = ((waEnd - wa) & 4095) + (2 + Rnd() % 2) * 4096;
    SetPhase(SPIN);
    Beeper.Tone(660, 40, 40);
  }
  void Settle() {
    int w = 0; prN = 0;
    for (int i = 0; i < NB; i++) if (st[i] > 0) {
      pr[prN++] = i;
      if (i == res) w += st[i] * (PAY[i] + 1);
    }
    won = w;
    if (w > 0) { Chips.Pay(w); Beeper.Tone(1047, 120, 40); if (!demo && w > bestWin) { bestWin = w; Scores.Report(bestWin, 0, 0, 0); } }   // board: biggest payout
    SetPhase(PAYP);
  }
  void Act(bool aUp, bool aDown, bool aLeft, bool aRight, bool aA, bool aB) {
    if (aUp) sel = (sel + NB - 1) % NB;
    if (aDown) sel = (sel + 1) % NB;
    if (aLeft && st[sel] > 0) st[sel]--;
    if (aRight && st[sel] < CAP && Total() < Chips.Balance()) st[sel]++;
    if (aB) { for (int i = 0; i < NB; i++) st[i] = 0; }
    if (aA) DoSpin();
  }
  void Plan() {
    int c = Chips.Balance();
    if (c < 6) { dN = 1; dRow[0] = Rnd() % 2; dAmt[0] = c; return; }
    dN = 1; dRow[0] = 0; dAmt[0] = 2 + Rnd() % 4;
    if (Rnd() % 2 == 0) { dRow[dN] = 1; dAmt[dN] = 1 + Rnd() % 3; dN++; }
    if (Rnd() % 4 == 0) { dRow[dN] = 2 + Rnd() % 5; dAmt[dN] = 1; dN++; }
  }
  void DemoMove() {
    bool u = false, d = false, l = false, r = false, a = false, b = false;
    if (!dClr) { dClr = true; if (Total() > 0) b = true; }
    else if (dk < dN) {
      int tr = dRow[dk];
      if (sel < tr) d = true;
      else if (sel > tr) u = true;
      else if (st[tr] < dAmt[dk] && st[tr] < CAP && Total() < Chips.Balance()) r = true;
      else dk++;
    } else a = true;
    Act(u, d, l, r, a, b);
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
    auto_ = auto;

    switch (phase) {
      case TITLE:
        wa = (wa + 3) & 4095;
        if (eA || (auto && pt > 40)) ToBet();
        break;
      case BET:
        if (auto) {
          if (dN == 0) Plan();
          if (pt > 12 && pt % 4 == 0) DemoMove();
        } else Act(eUp || (up && rep), eDown || (down && rep), eLeft || (left && rep), eRight || (right && rep), eA, eB);
        break;
      case SPIN: {
        int s = T - pt;
        wa = (waEnd - drel * s * s / (T * T)) & 4095;
        int peg = SEGOF[(TOP - wa) & 4095];
        if (peg != lastPeg) { lastPeg = peg; Beeper.Tone(1800, 4, 25); }   // the clapper hits a peg
        if (s <= 0) {
          for (int i = 4; i > 0; i--) hist[i] = hist[i - 1];
          hist[0] = res; if (histN < 5) histN++;
          SetPhase(LANDP);
        }
        break;
      }
      case LANDP:
        if (pt >= LAND) Settle();
        break;
      case PAYP:
        if ((eA && pt > 15) || (auto && pt > 90) || pt > 600) ToBet();
        break;
      case BROKE:
        wa = (wa + 2) & 4095;
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
  int Glyph(int c) { return GLYPH[c < 128 ? CHAR[c] : FONTCH.Length - 1]; }
  void Text(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int m = Glyph(s[n]);
      if (x >= 0 && x <= 61 && y >= 0 && y <= 59) { for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) sc.Set(x + KX[k], y + KY[k], v); }
      else for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) Px(x + KX[k], y + KY[k], v);
      x += 4;
    }
  }
  void Big(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int m = Glyph(s[n]);
      for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) Fill(x + KX[k] * 2, y + KY[k] * 2, 2, 2, v);
      x += 8;
    }
  }
  void Tall(int x, int y, string s, bool v) {
    for (int n = 0; n < s.Length; n++) {
      int m = Glyph(s[n]);
      for (int k = 0; m != 0; k++, m >>= 1) if ((m & 1) != 0) Fill(x + KX[k], y + KY[k] * 2, 1, 2, v);
      x += 4;
    }
  }
  void Logo(int x, int y, int sx, int sy, bool v) {
    for (int r = 0; r < 5; r++) for (int q = 0; q < 5; q++) if (((LOGO[r] >> (4 - q)) & 1) != 0) Fill(x + q * sx, y + r * sy, sx, sy, v);
  }
  void Centre(int y, string s) { Text(32 - ((s.Length * 4 - 1) >> 1), y, s, true); }
  void Right(int x, int y, string s, bool v) { Text(x - s.Length * 4, y, s, v); }
  void Boxed(int y, string s) {
    int w = s.Length * 4 + 3, x = 32 - (w >> 1);
    Fill(x, y, w, 9, false); Rect(x, y, w, 9); Text(x + 2, y + 2, s, true);
  }
  void Line(int x0, int y0, int x1, int y1) {
    int dx = Abs(x1 - x0), dy = -Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
    int e = dx + dy;
    for (;;) {
      Px(x0, y0, true);
      if (x0 == x1 && y0 == y1) break;
      int e2 = 2 * e;
      if (e2 >= dy) { e += dy; x0 += sx; }
      if (e2 <= dx) { e += dx; y0 += sy; }
    }
  }
  static int PolX(int r, int a) { return (8064 + ((r * Cos(a)) >> 6) + 128) >> 8; }
  static int PolY(int r, int a) { return (30208 + ((r * Sin(a)) >> 6) + 128) >> 8; }

  void LightSeg(int s) {                                    // light the band pixels of segment s (bucket walk)
    int a0 = (BND[s] + wa) & 4095, a1 = a0 + BND[s + 1] - BND[s] - 1;
    for (int k = a0 >> 4; k <= (a1 >> 4); k++) {
      int kk = k & 255, e = bucket[kk + 1];
      for (int i = bucket[kk]; i < e; i++) if (SEGOF[(bandA[i] - wa) & 4095] == s) sc.Set(bandX[i], bandY[i], true);
    }
  }
  void DrawWheel() {
    int hl = phase == LANDP && ((pt >> 3) & 1) == 0 ? seg : -1;
    for (int i = 0; i < runN; i++) sc.Fill(runX[i], runY[i], runW[i], 1, true);
    if (hl != 0) LightSeg(0);                               // the X LOGO and JOKER segments are lit,
    if (hl != 27) LightSeg(27);                             // the blinking winner the other way round
    if (hl >= 0 && WHEEL[hl] < 5) LightSeg(hl);
    // only the ~10 segments near the top can show; walked in the JS's order (0..53) so overlaps come out the same
    int k0 = SEGOF[(TOP - 260 - wa) & 4095], k1 = k0 + 9;
    if (k1 >= NS) { DrawSegs(0, k1 - NS, hl); DrawSegs(k0, NS - 1, hl); } else DrawSegs(k0, k1, hl);
    int p = (TOP - wa) & 4095, past = BND[SEGOF[p] + 1] - p;
    int lean = past < 8 ? 3 : past < 16 ? 2 : past < 26 ? 1 : 0;
    for (int j = 0; j < 7; j++) {
      int w = j < 2 ? 4 : j < 4 ? 3 : 2, x0 = 32 - (w >> 1) + lean * j * j / 36;
      Fill(x0 - 1, j, w + 2, 1, false); Fill(x0, j, w, 1, true);
    }
  }
  void DrawSegs(int kFrom, int kTo, int hl) {
    for (int k = kFrom; k <= kTo; k++) {
      int a = BND[k] + wa;
      if (Abs(Sdist(a - TOP)) <= 230) {
        Line(PolX(87, a), PolY(87, a), PolX(109, a), PolY(109, a));
        Line(PolX(74, a), PolY(74, a), PolX(81, a), PolY(81, a));
        Fill(PolX(113, a) - 1, PolY(113, a) - 1, 2, 2, true);
      }
      int c = ((BND[k] + BND[k + 1]) >> 1) + wa;
      if (Abs(Sdist(c - TOP)) <= 260) {
        int x = PolX(98, c), y = PolY(98, c) - 5, sy = WHEEL[k]; bool v = !((sy >= 5) != (k == hl));
        if (sy == 6) { for (int r = 0; r < 9; r++) for (int q = 0; q < 7; q++) if (((LOGO7[r] >> (6 - q)) & 1) != 0) Px(x - 3 + q, y + r, v); }
        else { string s = LAB[sy]; Tall(x - ((s.Length * 4 - 1) >> 1), y, s, v); }
      }
    }
  }
  void ChipLabel(int x, int y, int sy, bool v) {
    if (sy == 6) Logo(x + 1, y, 1, 1, v);
    else { string s = LAB[sy]; Text(x + ((7 - (s.Length * 4 - 1)) >> 1), y, s, v); }
  }
  void Strip() {
    if (histN == 0) { Centre(1, "PLACE BETS"); return; }
    for (int k = 0; k < histN; k++) {
      int n = hist[k], x = 2 + k * 12;
      if (n >= 5) { Fill(x, 0, 11, 7, true); ChipLabel(x + 2, 1, n, false); }
      else { Rect(x, 0, 11, 7); ChipLabel(x + 2, 1, n, true); }
    }
  }
  void Stash(int y) {
    for (int r = 0; r < 5; r++) for (int q = 0; q < 5; q++) if (((CHIP[r] >> (4 - q)) & 1) != 0) Px(1 + q, y + r, true);
    Text(8, y, Chips.Balance().ToString(), true);
  }
  void Footer() { Fill(0, 51, 64, 1, true); Stash(53); }
  void DrawBets() {
    Strip();
    for (int r = 0; r < NB; r++) {
      int y = 8 + r * 6; bool on = r == sel, v = !on;
      if (on) Fill(0, y, 64, 6, true);
      Text(2, y + 1, NAMES[r], v);
      Text(28, y + 1, PAY[r].ToString() + ":1", v);
      if (on) Right(64, y + 1, "<" + st[r].ToString() + ">", v);
      else if (st[r] > 0) Right(60, y + 1, st[r].ToString(), v);
    }
    Footer();
    Right(64, 53, "BET " + Total().ToString(), true);
    if (msgT > 0) Centre(59, msg);
    else if (((t >> 4) & 1) != 0) Centre(59, "A:SPIN B:CLEAR");
  }
  void DrawPay() {
    Strip();
    int n = res; bool v = n < 5;
    if (n >= 5) Fill(1, 8, 18, 13, true); else Rect(1, 8, 18, 13);
    if (n == 6) Logo(5, 10, 2, 2, false);
    else { string s = LAB[n]; Big(1 + ((18 - (s.Length * 8 - 2)) >> 1), 10, s, v); }
    Text(22, 9, NAMES[n], true);
    Text(22, 16, "PAYS " + PAY[n].ToString() + ":1", true);
    int pages = (prN + 3) / 4, pg = pages > 1 ? pt / 60 % pages : 0;
    for (int i = 0; i < 4; i++) {
      int k = pg * 4 + i;
      if (k >= prN) break;
      int r = pr[k], y = 22 + i * 7; bool win = r == n, w = !win;
      if (win) Fill(0, y, 64, 7, true);
      Text(2, y + 1, NAMES[r], w);
      Right(41, y + 1, st[r].ToString(), w);
      Right(64, y + 1, win ? "+" + (st[r] * PAY[r]).ToString() : "-" + st[r].ToString(), w);
    }
    Footer();
    int net = won - staked;
    Right(64, 53, net > 0 ? "WON " + net.ToString() : net < 0 ? "LOST " + (0 - net).ToString() : "EVEN", true);
    if (((t >> 4) & 1) != 0) Centre(59, "A: NEXT");
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (phase == BET) { DrawBets(); return; }
    if (phase == PAYP) { DrawPay(); return; }
    DrawWheel();
    if (phase == TITLE || phase == BROKE) {
      Fill(0, 36, 64, 14, false);
      Big(5, 38, "BIG SIX", true);
      if (phase == BROKE) { Boxed(18, "OUT OF CHIPS"); if (((t >> 5) & 1) != 0) Boxed(53, "MORE TOMORROW"); return; }
      if (msgT > 0) Boxed(18, msg);
      if (((t >> 4) & 1) != 0) Boxed(53, "A: START");
      return;
    }
    Footer();
    Right(64, 53, "BET " + staked.ToString(), true);
    if (phase == LANDP && pt > 8) {
      int n = res;
      Boxed(39, NAMES[n] + " " + PAY[n].ToString() + ":1");
      if (((t >> 3) & 1) != 0) Centre(59, st[n] > 0 ? "WINNER!" : "NO WIN");
    } else Centre(59, "NO MORE BETS");
  }
}
