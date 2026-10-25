// A FOLD ROOMS level, read from its text (microgames/fold/docs/FORMAT.md): a port of fold_parse in
// microgames/fold/core/fold.c, down to its sscanf / strtol / atoi rules (Scan below follows glibc's), so a level reads
// the same here as in the C. The cell arrays are sized for the level's squares (the C has room for 64); the game takes
// them over at the start (the C copies them; the level is read again for every start, so nothing else reads them).
using System;

public class FoldLevel {
  public const int N = 8, NN = 64, MAXSQ = 64, MAXSIG = 64, MAXACT = 8, MAXLOGIC = 32, MAXTEXT = 32, TEXTLEN = 96, MAXSOL = 4096;
  public const int MAXTIMER = 8, MAXKP = 32;
  public const int FK_FLOOR = 0, FK_ICE = 1, FK_ONEWAY = 2, FK_PIT = 3, FK_TELE = 4, FK_PLATE = 5, FK_GATE = 6, FK_KEY = 7, FK_LOCK = 8;
  public const int FK_GRATE = 9, FK_SENSOR = 10, FK_LIGHT = 11, FK_SIGN = 12, FK_KEYPAD = 13;
  public const int FL_AND = 0, FL_OR = 1, FL_XOR = 2, FL_NOT = 3, FL_LATCH = 4, FL_COUNT = 5, FL_DELAY = 6, FL_SHIFT = 7;
  public const int FT_TICK = 0, FT_MOVES = 1;
  public const int FV_NUMBER = 0, FV_MOVES = 1, FV_PUSHES = 2, FV_FLIPS = 3, FV_TURNS = 4, FV_KEYS = 5, FV_BOXES = 6, FV_LIGHTS = 7, FV_COUNT = 8, FV_N = 9;
  public static readonly string[] VALUE_NAMES = { "NUMBER", "MOVES", "PUSHES", "FLIPS", "TURNS", "KEYS", "BOXES", "LIGHTS", "COUNT" };
  static readonly string[] OPS = { "and", "or", "xor", "not", "latch", "count", "delay", "shift" };
  const string TILES = "#.xbBpPoO~^>v<_123456789=|:kl+*%;@$&?";
  static bool[] isTile;                                    // TILES as a table

  public int nsq;
  public sbyte[] sq = new sbyte[MAXSQ * 12];              // a square: o u v n (n = v x u)
  public int ncells;                                       // 2 * squares * 64 (at least 2 sides)
  public byte[] wall, target, box, btn, kind, aux, drv, txt;
  public int player, nbtn;
  public int[] nact = new int[MAXSIG];                     // a signal's cell moves: act*[(s - 1) * MAXACT + k]
  public byte[] actKind = new byte[MAXSIG * MAXACT], actSq = new byte[MAXSIG * MAXACT], actS0 = new byte[MAXSIG * MAXACT], actS1 = new byte[MAXSIG * MAXACT];
  public int nlogic;
  public byte[] lOp = new byte[MAXLOGIC], lN = new byte[MAXLOGIC], lOut = new byte[MAXLOGIC], lCount = new byte[MAXLOGIC], lIn = new byte[MAXLOGIC * 4];
  public int allSig, ntimer;
  public byte[] tKind = new byte[MAXTIMER], tSig = new byte[MAXTIMER], tN = new byte[MAXTIMER];
  public int ntext; public byte[][] text = new byte[MAXTEXT][];   // up to 95 characters each
  public bool[] geo = new bool[MAXSQ];                     // the square's one shift / hinge (geoAct*)
  public byte[] geoKind = new byte[MAXSQ], geoSq = new byte[MAXSQ], geoS0 = new byte[MAXSQ], geoS1 = new byte[MAXSQ];
  public int nkp;
  public byte[] kpSrc = new byte[MAXKP], kpOp = new byte[MAXKP], kpCmp = new byte[MAXKP]; public short[] kpArg = new short[MAXKP], kpN = new short[MAXKP];
  public int nsol; public byte[] sol = new byte[0];         // grown as sol lines come (up to MAXSOL)

  // an empty level (all zero, as the C's memset when a level doesn't read)
  public FoldLevel(int squares) {
    ncells = 2 * (squares < 1 ? 1 : squares) * NN;
    wall = new byte[ncells]; target = new byte[ncells]; box = new byte[ncells]; btn = new byte[ncells];
    kind = new byte[ncells]; aux = new byte[ncells]; drv = new byte[ncells]; txt = new byte[ncells];
  }

  // ---- sscanf, as glibc does it, for the formats fold_parse uses: ' ' (any white space), %d, %c, %Ns, %n.
  // Results in order in sv (%s: its start in sv, its length in sl). Returns the conversions made (not %n), or -1 if
  // the input ran out before the first.
  int[] sv = new int[12], sl = new int[12];
  static bool Space(int ch) { return ch == ' ' || ch == '\t' || ch == '\n' || ch == '\v' || ch == '\f' || ch == '\r'; }
  int Scan(byte[] s, int pos, string fmt) {
    int at = pos, got = 0, k = 0;
    for (int f = 0; f < fmt.Length; f++) {
      char fc = fmt[f];
      if (fc == ' ') { while (Space(s[at])) at++; continue; }
      f++; int width = 0;
      while (fmt[f] >= '0' && fmt[f] <= '9') { width = width * 10 + (fmt[f] - '0'); f++; }
      char cv = fmt[f];
      if (cv == 'n') { sv[k++] = at - pos; continue; }
      if (cv == 'c') { if (s[at] == 0) return got == 0 ? -1 : got; sv[k++] = s[at++]; got++; continue; }
      while (Space(s[at])) at++;
      if (s[at] == 0) return got == 0 ? -1 : got;
      if (cv == 's') {
        int st = at; while (s[at] != 0 && !Space(s[at]) && (width == 0 || at - st < width)) at++;
        sv[k] = st; sl[k++] = at - st; got++; continue;
      }
      int q = at; bool neg = false;                      // %d: strtol's digits, kept to an int
      if (s[q] == '+' || s[q] == '-') { neg = s[q] == '-'; q++; }
      if (s[q] < '0' || s[q] > '9') return got;
      long v = 0; while (s[q] >= '0' && s[q] <= '9') { if (v < 0x7FFFFFFFFFFFL / 10) v = v * 10 + (s[q] - '0'); q++; }
      sv[k++] = (int)(neg ? -v : v); at = q; got++;
    }
    return got;
  }
  static int Atoi(byte[] s, int at) {
    while (Space(s[at])) at++;
    bool neg = false; if (s[at] == '+' || s[at] == '-') { neg = s[at] == '-'; at++; }
    long v = 0; while (s[at] >= '0' && s[at] <= '9') { if (v < 0x7FFFFFFFFFFFL / 10) v = v * 10 + (s[at] - '0'); at++; }
    return (int)(neg ? -v : v);
  }
  static bool Is(byte[] s, int at, int len, string w) {   // the token s[at..at+len) is w
    if (len != w.Length) return false;
    for (int i = 0; i < len; i++) if (s[at + i] != w[i]) return false;
    return true;
  }
  static bool Has(string set, int ch) { for (int i = 0; i < set.Length; i++) if (set[i] == ch) return true; return false; }

  // parse_act: a link's move ("f3", "r2", "s1:u", "h4:2+", "g5")
  int aKind, aSq, aS0, aS1;
  bool ParseAct(byte[] s, int at, int len) {
    aKind = s[at]; aSq = 0; aS0 = 0; aS1 = 0;
    if (!Has("frRshg", aKind)) return false;
    int q = at + 1, end = at + len; bool neg = false; long i = 0;
    while (q < end && Space(s[q])) q++;
    if (q < end && (s[q] == '+' || s[q] == '-')) { neg = s[q] == '-'; q++; }
    if (q >= end || s[q] < '0' || s[q] > '9') return false;
    while (q < end && s[q] >= '0' && s[q] <= '9') { if (i < 0x7FFFFFFFL) i = i * 10 + (s[q] - '0'); q++; }
    if (neg) i = -i;
    if (i < 0 || i >= MAXSQ || (aKind == 'g' && (i < 1 || i > 127))) return false;
    aSq = (int)i;
    if (aKind == 's' || aKind == 'h') {
      if (q >= end || s[q] != ':' || q + 1 >= end) return false;
      aS0 = s[q + 1]; aS1 = q + 2 < end ? s[q + 2] : 0;
      if (aKind == 's' && !Has("udlroi", aS0)) return false;
      if (aKind == 'h' && (aS0 < '0' || aS0 > '3' || (aS1 != '+' && aS1 != '-'))) return false;
    }
    return true;
  }

  // the number of squares a level text will have (its "square" lines, at most 64): what to size the cells for
  public static int CountSquares(byte[] t, int n) {
    int count = 0, i = 0;
    while (i < n) {
      int j = i; while (j < n && Space(t[j]) && t[j] != '\n') j++;
      if (j + 6 <= n && t[j] == 's' && t[j + 1] == 'q' && t[j + 2] == 'u' && t[j + 3] == 'a' && t[j + 4] == 'r' && t[j + 5] == 'e') count++;
      while (i < n && t[i] != '\n') i++;
      i++;
    }
    return count > MAXSQ ? MAXSQ : count;
  }

  // fold_parse on the n bytes of t; false = not a valid level (the game then plays an empty one)
  public bool Parse(byte[] t, int n) {
    if (isTile == null) { isTile = new bool[256]; for (int i = 0; i < TILES.Length; i++) isTile[TILES[i]] = true; }
    for (int c = 0; c < ncells; c++) wall[c] = 1;
    player = -1;
    byte[] line = new byte[n + 1 < MAXSOL + 16 ? n + 1 : MAXSOL + 16];   // a line is never longer than the text
    int roomFace = -1, roomRow = 0, s = 0;
    while (s < n && t[s] != 0) {
      int len = 0;
      while (s < n && t[s] != 0 && t[s] != '\n') { if (len < MAXSOL + 16 - 1 && t[s] != '\r') line[len++] = t[s]; s++; }
      if (s < n && t[s] != 0) s++;
      line[len] = 0;
      if (roomFace >= 0) {                      // inside a room block: 8 rows of 8 cells
        if (len < N) return false;
        for (int x = 0; x < N; x++) {
          int c = roomFace * NN + roomRow * N + x, ch = line[x];
          if (!isTile[ch]) return false;
          if (c >= ncells) return false;
          if (ch == '#') { wall[c] = 1; continue; }   // the C's tests in turn, by tile (the arrays start at 0)
          wall[c] = 0;
          switch (ch) {
            case 'x': target[c] = 1; break;
            case 'b': box[c] = 1; break;
            case 'B': target[c] = 1; box[c] = 1; break;
            case 'p': player = c; break;
            case 'P': target[c] = 1; player = c; break;
            case 'o': if (nbtn == MAXSIG - 1) return false; btn[c] = (byte)++nbtn; break;
            case 'O': box[c] = 1; if (nbtn == MAXSIG - 1) return false; btn[c] = (byte)++nbtn; break;
            case '@': kind[c] = FK_LIGHT; break;
            case '$': kind[c] = FK_LIGHT; aux[c] = 0x80; break;
            case '*': kind[c] = FK_SENSOR; break;
            case '%': kind[c] = FK_GATE; aux[c] = 0x40; break;
            case ';': kind[c] = FK_GATE; aux[c] = 0x40 | 0x80; break;
            case '+': kind[c] = FK_GRATE; break;
            case 'k': kind[c] = FK_KEY; break;
            case 'l': kind[c] = FK_LOCK; break;
            case '&': kind[c] = FK_SIGN; break;
            case '?': kind[c] = FK_KEYPAD; break;
            case '|': kind[c] = FK_GATE; break;
            case ':': kind[c] = FK_GATE; aux[c] = 0x80; break;
            case '=': if (nbtn == MAXSIG - 1) return false; kind[c] = FK_PLATE; btn[c] = (byte)++nbtn; break;
            case '_': kind[c] = FK_PIT; break;
            case '~': kind[c] = FK_ICE; break;
            case '^': kind[c] = FK_ONEWAY; break;
            case '>': kind[c] = FK_ONEWAY; aux[c] = 1; break;
            case 'v': kind[c] = FK_ONEWAY; aux[c] = 2; break;
            case '<': kind[c] = FK_ONEWAY; aux[c] = 3; break;
            default: if (ch >= '1' && ch <= '9') { kind[c] = FK_TELE; aux[c] = (byte)(ch - '0'); } break;
          }
        }
        if (++roomRow == N) roomFace = -1;
        continue;
      }
      for (int i = 0; i < len; i++) if (line[i] == '#') { line[i] = 0; break; }
      if (Scan(line, 0, " %15s %n") < 1) continue;
      int ws = sv[0], wl = sl[0], rest = sv[1];
      if (Is(line, ws, wl, "fold")) { if (Atoi(line, rest) != 1) return false; }
      else if (Is(line, ws, wl, "name")) { }
      else if (Is(line, ws, wl, "cellname")) {
        if (Scan(line, rest, "%d %n") < 1 || sv[1] == 0 || sv[0] < 0 || sv[0] >= MAXSQ) return false;
      }
      else if (Is(line, ws, wl, "square")) {
        if (Scan(line, rest, "%d %d %d %d %d %d %d %d %d") != 9) return false;
        if (nsq == MAXSQ) return false;
        int q = nsq++ * 12;
        for (int i = 0; i < 9; i++) sq[q + i] = (sbyte)sv[i];
        int ux = sq[q + 3], uy = sq[q + 4], uz = sq[q + 5], vx = sq[q + 6], vy = sq[q + 7], vz = sq[q + 8];
        int nx = vy * uz - vz * uy, ny = vz * ux - vx * uz, nz = vx * uy - vy * ux;
        if (nx * nx + ny * ny + nz * nz != 1 || ux * vx + uy * vy + uz * vz != 0) return false;
        sq[q + 9] = (sbyte)nx; sq[q + 10] = (sbyte)ny; sq[q + 11] = (sbyte)nz;
      }
      else if (Is(line, ws, wl, "room")) {
        if (Scan(line, rest, "%d %c") != 2 || sv[0] < 0 || sv[0] >= nsq || (sv[1] != 'f' && sv[1] != 'b')) return false;
        roomFace = 2 * sv[0] + (sv[1] == 'b' ? 1 : 0); roomRow = 0;
      }
      else if (Is(line, ws, wl, "link")) {
        if (Scan(line, rest, "%d %n") < 1 || sv[0] < 1 || sv[0] >= MAXSIG) return false;
        int b = sv[0], w = rest + sv[1];
        while (Scan(line, w, " %15s%n") == 1) {
          int ts = sv[0], tl = sl[0], m = sv[1];
          if (nact[b - 1] == MAXACT) return false;
          int a = (b - 1) * MAXACT + nact[b - 1]++;
          if (!ParseAct(line, ts, tl) || (aKind != 'g' && aSq >= nsq)) return false;
          actKind[a] = (byte)aKind; actSq[a] = (byte)aSq; actS0[a] = (byte)aS0; actS1[a] = (byte)aS1;
          if (aKind == 'g') { nact[b - 1]--; w += m; continue; }
          if ((aKind == 's' || aKind == 'h') && !geo[aSq]) { geo[aSq] = true; geoKind[aSq] = (byte)aKind; geoSq[aSq] = (byte)aSq; geoS0[aSq] = (byte)aS0; geoS1[aSq] = (byte)aS1; }
          w += m;
        }
      }
      else if (Is(line, ws, wl, "gate")) {
        if (Scan(line, rest, "%d %c %d %d %d") != 5) return false;
        int i = sv[0], x = sv[2], y = sv[3], k = sv[4];
        if (i < 0 || i >= nsq || x < 0 || y < 0 || x >= N || y >= N || k < 0 || k > 127) return false;
        int c = (2 * i + (sv[1] == 'b' ? 1 : 0)) * NN + y * N + x;
        if (kind[c] != FK_GATE) return false;
        drv[c] = (byte)k;
      }
      else if (Is(line, ws, wl, "signal") || Is(line, ws, wl, "drive") || Is(line, ws, wl, "text") || Is(line, ws, wl, "text2")) {
        int got = Scan(line, rest, "%d %c %d %d %n");
        int i = sv[0], x = sv[2], y = sv[3], nn = got >= 4 ? sv[4] : 0;
        if (got < 4 || i < 0 || i >= nsq || x < 0 || y < 0 || x >= N || y >= N) return false;
        int c = (2 * i + (sv[1] == 'b' ? 1 : 0)) * NN + y * N + x;
        if (line[ws] == 't') {
          if (ntext == MAXTEXT) return false;
          int st = rest + nn, l = 0; while (l < TEXTLEN - 1 && line[st + l] != 0) l++;
          byte[] tx = new byte[l]; Array.Copy(line, st, tx, 0, l); text[ntext] = tx; ++ntext;
          if (line[ws + 4] == '2') aux[c] = (byte)ntext; else txt[c] = (byte)ntext;
        } else {
          if (Scan(line, rest + nn, "%d") != 1 || sv[0] < 0 || sv[0] >= MAXSIG) return false;
          if (line[ws] == 's') btn[c] = (byte)sv[0]; else drv[c] = (byte)sv[0];
        }
      }
      else if (Is(line, ws, wl, "keypad")) {
        int got = Scan(line, rest, "%d %c %d %d %n");
        int i = sv[0], x = sv[2], y = sv[3], nn = got >= 4 ? sv[4] : 0, side = sv[1];
        if (got < 4 || i < 0 || i >= nsq || x < 0 || y < 0 || x >= N || y >= N) return false;
        if (nkp == MAXKP) return false;
        int src = 255, op = 0, cmp = 0, arg = 0, kn = 0, w = rest + nn;
        while (Scan(line, w, " %15s%n") == 1) {
          int ts = sv[0], tl = sl[0]; w += sv[1];
          if (src == 255) {
            for (int v = 0; v < FV_N; v++) {
              string a = VALUE_NAMES[v]; bool same = true;
              for (int q = 0; q < a.Length || q < tl; q++) {
                int tq = q < tl ? line[ts + q] : 0, aq = q < a.Length ? a[q] : 0;
                if ((tq & ~32) != aq) { same = false; break; }
              }
              if (same) src = v;
            }
            if (src == 255) return false;
            if (src == FV_NUMBER || src == FV_COUNT) { if (Scan(line, w, " %d%n") != 1) return false; arg = (short)sv[0]; w += sv[1]; }
          } else if (Is(line, ts, tl, "+") || Is(line, ts, tl, "-") || Is(line, ts, tl, "*")) {
            int t0 = line[ts];
            if (Scan(line, w, " %d%n") != 1) return false;
            op = t0 == '+' ? 1 : t0 == '-' ? 2 : 3; kn = (short)sv[0]; w += sv[1];
          } else if (Is(line, ts, tl, "=")) cmp = 0; else if (Is(line, ts, tl, ">=")) cmp = 1; else if (Is(line, ts, tl, "<=")) cmp = 2;
          else return false;
        }
        if (src == 255) return false;
        int c = (2 * i + (side == 'b' ? 1 : 0)) * NN + y * N + x;
        kpSrc[nkp] = (byte)src; kpOp[nkp] = (byte)op; kpCmp[nkp] = (byte)cmp; kpArg[nkp] = (short)arg; kpN[nkp] = (short)kn;
        nkp++; aux[c] = (byte)nkp;
      }
      else if (Is(line, ws, wl, "input")) {
        sv[2] = 0;
        int got = Scan(line, rest, "%d %15s %d");
        int k = sv[0], whatS = sv[1], whatL = sl[1], nn = got >= 3 ? sv[2] : 0;
        if (got < 2 || k < 1 || k >= MAXSIG) return false;
        if (Is(line, whatS, whatL, "alltargets")) allSig = k;
        else if (Is(line, whatS, whatL, "tick") || Is(line, whatS, whatL, "moves")) {
          if (nn < 1 || nn > 255 || ntimer == MAXTIMER) return false;
          tKind[ntimer] = (byte)(line[whatS] == 't' ? FT_TICK : FT_MOVES); tSig[ntimer] = (byte)k; tN[ntimer] = (byte)nn; ntimer++;
        } else return false;
      }
      else if (Is(line, ws, wl, "logic")) {
        if (nlogic == MAXLOGIC) return false;
        int L = nlogic; lOp[L] = 0; lN[L] = 0; lOut[L] = 0; lCount[L] = 0; for (int i = 0; i < 4; i++) lIn[L * 4 + i] = 0;
        int got = Scan(line, rest, "%d %7s %n");
        int outS = sv[0], opS = sv[1], opL = sl[1];
        if (got < 2 || outS < 1 || outS >= MAXSIG) return false;
        int w = rest + sv[2], op = 255;
        for (int k = 0; k < 8; k++) if (Is(line, opS, opL, OPS[k])) op = k;
        if (op == 255) return false;
        lOp[L] = (byte)op;
        if (op == FL_COUNT || op == FL_DELAY || op == FL_SHIFT) {
          if (Scan(line, w, "%d %n") < 1 || sv[0] < 1 || sv[0] > (op == FL_COUNT ? 255 : 8)) return false;
          if (op == FL_SHIFT && outS + sv[0] - 1 >= MAXSIG) return false;
          lCount[L] = (byte)sv[0]; w += sv[1];
        }
        while (lN[L] < 4 && Scan(line, w, "%d%n") == 1) {
          if (sv[0] < 1 || sv[0] >= MAXSIG) return false;
          lIn[L * 4 + lN[L]] = (byte)sv[0]; lN[L]++; w += sv[1];
        }
        if (lN[L] == 0) return false;
        if (op == FL_SHIFT && lN[L] < 2) return false;
        lOut[L] = (byte)outS; nlogic++;
      }
      else if (Is(line, ws, wl, "box") || Is(line, ws, wl, "player")) {
        if (Scan(line, rest, "%d %c %d %d") != 4) return false;
        int i = sv[0], x = sv[2], y = sv[3];
        if (i < 0 || i >= nsq || x < 0 || y < 0 || x >= N || y >= N) return false;
        int c = (2 * i + (sv[1] == 'b' ? 1 : 0)) * NN + y * N + x;
        if (line[ws] == 'b') box[c] = 1; else player = c;
      }
      else if (Is(line, ws, wl, "sol")) {
        int more = 0; for (int c = rest; line[c] != 0; c++) if (line[c] >= '0' && line[c] <= '3') more++;
        if (nsol + more > sol.Length) { byte[] b = new byte[nsol + more > MAXSOL ? MAXSOL : nsol + more]; Array.Copy(sol, 0, b, 0, nsol); sol = b; }
        for (int c = rest; line[c] != 0; c++) if (line[c] >= '0' && line[c] <= '3' && nsol < MAXSOL) sol[nsol++] = line[c];
      }
      else return false;
    }
    if (roomFace >= 0) return false;
    if (nsq == 0) return false;
    return true;
  }
}
