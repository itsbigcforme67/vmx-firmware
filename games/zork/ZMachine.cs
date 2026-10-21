// Z-machine, version 3, for the VMX: a line-for-line port of microgames/ui/games/zmachine.js (written from the public
// Z-Machine Standards Document 1.1) to C# 1.0, so the same story and seed give the same game as the web simulator.
//   ZMachine vm = new ZMachine("zork1.z3", seed);   // the story comes from GAME.VXP (Package)
//   vm.Run();               // runs until the game wants a line: vm.state == INPUT (or QUIT); text in vm.outb[0..outn)
//   vm.Input("open mailbox"); vm.Run(); ...
// Differences from the JS, none of them visible:
// - the story is read into one byte[] that is also the game's memory; RESTART reads it again from the package
//   (the JS keeps a second copy);
// - text goes into a byte buffer (outb/outn) instead of a string; the caller empties it (the JS take());
// - output stream 3 writes each character into its table as it is printed; the JS collects them and writes them when
//   the stream closes. Same table at the end;
// - stack 512 words, 40 call frames (the JS grows without limit); more stops the game with a message;
// - a JS `throw new Error` becomes `throw new Exception`, which stops the game with that message on the screen.
// Hot loop: Step() allocates nothing.
using System;
using Microsoft.Vmx;

public class ZMachine {
  public const int RUN = 0, INPUT = 1, QUIT = 2;
  const int STACK_MAX = 512, FRAMES_MAX = 40, LOCALS = 15;   // Zork I needs 13 and 13
  const string A2 = "\0\n0123456789.,!?_#'\"/\\-:()";

  string file; int seed;
  public byte[] m;
  public int state;
  public int dynEnd, globals, objt, abbr, pc;
  ushort[] stack = new ushort[STACK_MAX]; int sp;
  int[] fpc = new int[FRAMES_MAX], fsp = new int[FRAMES_MAX], fstore = new int[FRAMES_MAX], fnl = new int[FRAMES_MAX];
  ushort[] floc = new ushort[FRAMES_MAX * LOCALS]; int nfr;
  public byte[] outb = new byte[1024]; public int outn;
  int[] s3addr = new int[16], s3len = new int[16]; int ns3;
  uint rs;                                        // rng(): an LCG, divided down to [0, 1)
  public bool hasStatus; public string statusRoom; // the JS status (null until the first read); only the room is used
  int textBuf, parseBuf;
  // dictionary
  public int dictStart, dictLen, dictCount; int sepStart, sepCount; bool dictSorted;
  // SAVE snapshot (the JS vm.saved), kept in one reserved buffer; layout in Snapshot()
  public byte[] saved; public bool hasSaved;

  int[] a = new int[4]; int na;                   // the current instruction's operands
  int[] zs = new int[10];                          // encode()
  public char[] tmp = new char[128]; public int ntmp; bool toTmp;   // zstr() into tmp instead of the output

  // stored: a save from Storage (the simulator page's localStorage copy) for the saved slot, or null. It is taken
  // before the story is read so the heap never holds it twice.
  public ZMachine(string file, int seed, byte[] stored) {
    this.file = file; this.seed = seed;
    int n = Package.Length(file);
    if (n < 64) throw new Exception("NO STORY FILE");
    byte[] h = new byte[64]; Package.Read(file, 0, h, 0, 64);
    if (h[0] != 3) throw new Exception("STORY IS VERSION " + h[0].ToString() + "; ONLY 3 WORKS");
    dynEnd = h[0x0e] << 8 | h[0x0f];
    int cap = 16 + dynEnd + 1024;                  // the save slot: room for 1 KB of stack and frames (Zork I: < 600 bytes)
    if (Fits(stored)) {
      if (stored.Length >= cap) saved = stored; else { saved = new byte[cap]; Array.Copy(stored, 0, saved, 0, stored.Length); }
      hasSaved = true;
    } else saved = new byte[cap];
    stored = null; h = null;
    m = new byte[n];
    ReadStory();
    Restart(true);
  }
  void ReadStory() {
    for (int o = 0; o < m.Length; ) { int k = Package.Read(file, o, m, o, m.Length - o); if (k <= 0) throw new Exception("STORY FILE SHORT"); o += k; }
  }
  void Restart(bool first) {
    int keep = first ? 0 : m[0x11] & 3;
    if (!first) ReadStory();
    m[0x11] = (byte)((m[0x11] & ~3) | keep);
    m[1] = (byte)(m[1] & ~0x70);                   // status line available, screen not split, fixed font
    dynEnd = W(0x0e); globals = W(0x0c); objt = W(0x0a); abbr = W(0x18);
    pc = W(6); sp = 0; nfr = 1; fpc[0] = 0; fsp[0] = 0; fstore[0] = -1; fnl[0] = 0;
    outn = 0; ns3 = 0; state = RUN; rs = (uint)seed; hasStatus = false; statusRoom = null;
    ReadDict();
  }
  // ---- memory -----------------------------------------------------------------------------------------------------
  public int W(int ad) { return (m[ad] << 8) | m[ad + 1]; }
  void SW(int ad, int v) { m[ad] = (byte)(v >> 8); m[ad + 1] = (byte)v; }
  int GetVar(int v) {
    if (v == 0) { if (sp == 0) throw new Exception("STACK UNDERFLOW"); return stack[--sp]; }
    if (v < 16) return floc[(nfr - 1) * LOCALS + v - 1];
    int ad = globals + 2 * (v - 16); return (m[ad] << 8) | m[ad + 1];
  }
  void SetVar(int v, int x) {
    x &= 0xffff;
    if (v == 0) { if (sp == STACK_MAX) throw new Exception("STACK OVERFLOW"); stack[sp++] = (ushort)x; }
    else if (v < 16) floc[(nfr - 1) * LOCALS + v - 1] = (ushort)x;
    else { int ad = globals + 2 * (v - 16); m[ad] = (byte)(x >> 8); m[ad + 1] = (byte)x; }
  }
  int PeekVar(int v) { return v == 0 ? stack[sp - 1] : GetVar(v); }          // "indirect" refs:
  void PokeVar(int v, int x) { if (v == 0) stack[sp - 1] = (ushort)x; else SetVar(v, x); }  // the stack in place
  // ---- text -------------------------------------------------------------------------------------------------------
  void Emit(int c) {                               // print(): into tmp, the open stream-3 table, or the output
    if (toTmp) { if (ntmp < tmp.Length) tmp[ntmp++] = (char)c; return; }
    if (ns3 > 0) { int k = ns3 - 1; m[s3addr[k] + 2 + s3len[k]] = (byte)(c == 10 ? 13 : c); s3len[k]++; return; }
    if (outn == outb.Length) { byte[] nb = new byte[outb.Length * 2]; Array.Copy(outb, 0, nb, 0, outn); outb = nb; }
    outb[outn++] = (byte)c;
  }
  void Zscii(int c) { if (c == 13) Emit(10); else if (c >= 32 && c < 127) Emit(c); else if (c != 0) Emit('?'); }
  int Zstr(int ad) {                               // decodes the string at ad; returns the address after it
    int shift = 0, ab = 0, esc = 0, hi = 0;
    for (;;) {
      int x = (m[ad] << 8) | m[ad + 1]; ad += 2;
      for (int sh = 10; sh >= 0; sh -= 5) {
        int z = (x >> sh) & 31;
        if (ab != 0) { Zstr(W(abbr + 2 * (32 * (ab - 1) + z)) * 2); ab = 0; continue; }
        if (esc == 1) { hi = z; esc = 2; continue; }
        if (esc == 2) { Zscii((hi << 5) | z); esc = 0; continue; }
        if (z == 0) { Emit(' '); shift = 0; continue; }
        if (z <= 3) { ab = z; continue; }
        if (z == 4 || z == 5) { shift = z - 3; continue; }
        int al = shift; shift = 0;
        if (al == 2 && z == 6) { esc = 1; continue; }
        if (al == 0) Emit('a' + z - 6); else if (al == 1) Emit('A' + z - 6); else Emit(A2[z - 6]);
      }
      if ((x & 0x8000) != 0) break;
    }
    return ad;
  }
  // the string at ad into tmp[0..ntmp)
  public void DecodeTmp(int ad) { toTmp = true; ntmp = 0; Zstr(ad); toTmp = false; }
  // dictionary lookup of word[0..n) (only the first 6 z-characters count); 0 if it isn't there
  public int Lookup(char[] word, int n) {
    int k = 0;
    for (int i = 0; i < n; i++) {
      char ch = word[i];
      if (ch >= 'a' && ch <= 'z') zs[k++] = ch - 'a' + 6;
      else {
        int j = A2.IndexOf(ch);
        if (j >= 2) { zs[k++] = 5; zs[k++] = j + 6; }
        else { int c = ch & 1023; zs[k++] = 5; zs[k++] = 6; zs[k++] = c >> 5; zs[k++] = c & 31; }
      }
      if (k >= 6) break;
    }
    while (k < 6) zs[k++] = 5;
    uint key = (uint)((zs[0] << 10) | (zs[1] << 5) | zs[2]) << 16 | (uint)(0x8000 | (zs[3] << 10) | (zs[4] << 5) | zs[5]);
    if (dictSorted) {
      int lo = 0, hi = dictCount - 1;
      while (lo <= hi) {
        int mid = (lo + hi) >> 1, ad = dictStart + mid * dictLen;
        uint e = (uint)W(ad) << 16 | (uint)W(ad + 2);
        if (e == key) return ad; if (e < key) lo = mid + 1; else hi = mid - 1;
      }
      return 0;
    }
    for (int i = dictCount - 1; i >= 0; i--) {     // the JS Map: the last entry with the key wins
      int ad = dictStart + i * dictLen;
      if (((uint)W(ad) << 16 | (uint)W(ad + 2)) == key) return ad;
    }
    return 0;
  }
  void ReadDict() {
    int ad = W(8); sepCount = m[ad]; sepStart = ad + 1;
    ad += 1 + sepCount; dictLen = m[ad]; int cnt = (short)W(ad + 1); ad += 3;
    dictStart = ad; dictCount = cnt < 0 ? -cnt : cnt;
    dictSorted = true;                             // binary search when the keys go up (they do in Infocom stories)
    for (int i = 1; i < dictCount; i++) {
      int p = ad + (i - 1) * dictLen, q = p + dictLen;
      if (((uint)W(p) << 16 | (uint)W(p + 2)) >= ((uint)W(q) << 16 | (uint)W(q + 2))) { dictSorted = false; break; }
    }
  }
  bool IsSep(int c) { for (int i = 0; i < sepCount; i++) if (m[sepStart + i] == c) return true; return false; }
  // ---- objects ----------------------------------------------------------------------------------------------------
  int Obj(int o) { return objt + 62 + (o - 1) * 9; }
  int Parent(int o) { return o != 0 ? m[Obj(o) + 4] : 0; }
  int Sibling(int o) { return o != 0 ? m[Obj(o) + 5] : 0; }
  int Child(int o) { return o != 0 ? m[Obj(o) + 6] : 0; }
  void PrintObjName(int o) { if (o == 0) return; int p = W(Obj(o) + 7); if (m[p] != 0) Zstr(p + 1); }
  int FirstProp(int o) { int p = W(Obj(o) + 7); return p + 1 + 2 * m[p]; }
  int PropAddr(int o, int n) {
    if (o == 0) return 0;
    for (int ad = FirstProp(o); ; ) {
      int b = m[ad]; if (b == 0) return 0;
      int num = b & 31; if (num == n) return ad + 1; if (num < n) return 0;
      ad += 2 + (b >> 5);
    }
  }
  void RemoveObj(int o) {
    int p = Parent(o); if (p == 0) return;
    int oa = Obj(o), pa = Obj(p);
    if (m[pa + 6] == o) m[pa + 6] = m[oa + 5];
    else { int x = m[pa + 6]; while (Sibling(x) != o) x = Sibling(x); m[Obj(x) + 5] = m[oa + 5]; }
    m[oa + 4] = 0; m[oa + 5] = 0;
  }
  // ---- execution --------------------------------------------------------------------------------------------------
  void Store(int x) { SetVar(m[pc++], x); }
  void Branch(bool cond) {
    int b = m[pc++]; int off;
    if ((b & 0x40) != 0) off = b & 0x3f;
    else { off = ((b & 0x3f) << 8) | m[pc++]; if ((off & 0x2000) != 0) off -= 0x4000; }
    if (((b & 0x80) != 0) != cond) return;
    if (off == 0 || off == 1) Ret(off); else pc += off - 2;
  }
  void Call(int addr, int store) {                 // arguments: a[1..na)
    if (addr == 0) { SetVar(store, 0); return; }
    int ad = addr * 2; int n = m[ad++];
    if (nfr == FRAMES_MAX) throw new Exception("CALLS TOO DEEP");
    if (n > LOCALS) throw new Exception("TOO MANY LOCALS");
    int lb = nfr * LOCALS;
    for (int i = 0; i < n; i++, ad += 2) floc[lb + i] = (ushort)(i < na - 1 ? a[1 + i] : W(ad));
    fpc[nfr] = pc; fsp[nfr] = sp; fstore[nfr] = store; fnl[nfr] = n; nfr++;
    pc = ad;
  }
  void Ret(int x) {
    nfr--; sp = fsp[nfr]; pc = fpc[nfr];
    if (fstore[nfr] >= 0) SetVar(fstore[nfr], x);
  }
  void ShowStatus() {
    toTmp = true; ntmp = 0; PrintObjName(GetVar(16)); toTmp = false;
    statusRoom = String.FromChars(tmp, 0, ntmp); hasStatus = true;
  }
  public int Run() {
    int limit = 2000000;
    while (state == RUN && limit-- > 0) Step();
    return state;
  }
  public void Input(string line) {
    if (state != INPUT) return;
    int tb = textBuf, pb = parseBuf;
    int n = Math.Min(line.Length, Math.Max(0, m[tb] - 1));
    for (int i = 0; i < n; i++) { int c = line[i]; if (c >= 'A' && c <= 'Z') c += 32; m[tb + 1 + i] = (byte)c; }
    m[tb + 1 + n] = 0;
    int max = m[pb], k = 0;                         // the tokens: separators are words of their own
    for (int i = 0; i < n; ) {
      int c = m[tb + 1 + i];
      if (c == ' ') { i++; continue; }
      int j = i + 1;
      if (!IsSep(c)) while (j < n && m[tb + 1 + j] != ' ' && !IsSep(m[tb + 1 + j])) j++;
      if (k < max) {
        int len = j - i, e = pb + 2 + 4 * k;
        for (int q = 0; q < len && q < 6; q++) tmp[q] = (char)m[tb + 1 + i + q];
        SW(e, Lookup(tmp, Math.Min(len, 6))); m[e + 2] = (byte)len; m[e + 3] = (byte)(i + 1);
      }
      k++; i = j;
    }
    m[pb + 1] = (byte)Math.Min(k, max);
    state = RUN;
  }
  // SAVE snapshot layout (also what Storage.Save writes; the buffer may be longer than the data):
  //   0 'Z', 1 '3', 2..5 pc, 6..7 n = bytes of dynamic memory, 8..9 stack depth S, 10..11 frames F  (all big-endian)
  //   12..12+n: memory 0..n
  //   then S stack words, then F frames: pc (4 bytes), stack depth (2), store variable (2; 0xFFFF = none),
  //   number of locals L (1), L local words
  void Snapshot(int savePc) {
    int need = 12 + dynEnd + 2 * sp;
    for (int f = 0; f < nfr; f++) need += 9 + 2 * fnl[f];
    if (need > saved.Length) throw new Exception("SAVE: THE STACK IS TOO DEEP");   // never in Zork I; a bigger slot
                                                   // could find no room in the heap
    byte[] s = saved;
    s[0] = (byte)'Z'; s[1] = (byte)'3'; Put(s, 2, savePc >> 16); Put(s, 4, savePc); Put(s, 6, dynEnd); Put(s, 8, sp); Put(s, 10, nfr);
    Array.Copy(m, 0, s, 12, dynEnd);
    int o = 12 + dynEnd;
    for (int i = 0; i < sp; i++, o += 2) Put(s, o, stack[i]);
    for (int f = 0; f < nfr; f++) {
      Put(s, o, fpc[f] >> 16); Put(s, o + 2, fpc[f]); Put(s, o + 4, fsp[f]); Put(s, o + 6, fstore[f]); s[o + 8] = (byte)fnl[f]; o += 9;
      for (int i = 0; i < fnl[f]; i++, o += 2) Put(s, o, floc[f * LOCALS + i]);
    }
    hasSaved = true;
  }
  void RestoreSnapshot() {
    byte[] s = saved;
    int n = Get(s, 6); Array.Copy(s, 12, m, 0, n);
    pc = Get(s, 2) << 16 | Get(s, 4); sp = Get(s, 8); nfr = Get(s, 10);
    int o = 12 + n;
    for (int i = 0; i < sp; i++, o += 2) stack[i] = (ushort)Get(s, o);
    for (int f = 0; f < nfr; f++) {
      fpc[f] = Get(s, o) << 16 | Get(s, o + 2); fsp[f] = Get(s, o + 4); fstore[f] = (short)Get(s, o + 6); fnl[f] = s[o + 8]; o += 9;
      for (int i = 0; i < fnl[f]; i++, o += 2) floc[f * LOCALS + i] = (ushort)Get(s, o);
    }
  }
  static void Put(byte[] s, int o, int v) { s[o] = (byte)(v >> 8); s[o + 1] = (byte)v; }
  static int Get(byte[] s, int o) { return s[o] << 8 | s[o + 1]; }
  // is s a snapshot of this story (dynamic memory the same size) that this machine can hold?
  bool Fits(byte[] s) {
    if (s == null || s.Length < 12 || s[0] != 'Z' || s[1] != '3' || Get(s, 6) != dynEnd) return false;
    int o = 12 + dynEnd + 2 * Get(s, 8), nf = Get(s, 10);
    if (Get(s, 8) > STACK_MAX || nf < 1 || nf > FRAMES_MAX) return false;
    for (int f = 0; f < nf; f++) { if (o + 9 > s.Length || s[o + 8] > LOCALS) return false; o += 9 + 2 * s[o + 8]; }
    return o <= s.Length;
  }
  void PrintNum(int v) {
    if (v < 0) { Emit('-'); v = -v; }
    int d = 1; while (d * 10 <= v) d *= 10;
    for (; d > 0; d /= 10) Emit('0' + (v / d) % 10);
  }

  void Step() {
    byte[] m = this.m;
    int q = pc, op = m[q++];                         // decoding: pc in a local (GetVar doesn't use it)
    int kind, num, x = 0, y = 0, z = 0;              // operands: x y z (0 when missing); VAR form: all in a[0..na)
    if (op >= 0xc0) {
      kind = (op & 0x20) != 0 ? 3 : 2; num = op & 0x1f; int t = m[q++], n = 0;
      for (int sh = 6; sh >= 0; sh -= 2) {
        int ty = (t >> sh) & 3, v;
        if (ty == 3) break;
        if (ty == 0) { v = (m[q] << 8) | m[q + 1]; q += 2; } else if (ty == 1) v = m[q++]; else v = GetVar(m[q++]);
        a[n++] = v;
      }
      na = n;
      if (n > 0) x = a[0]; if (n > 1) y = a[1]; if (n > 2) z = a[2];
    } else if (op >= 0x80) {
      int ty = (op >> 4) & 3; num = op & 0x0f; na = 1;
      if (ty == 3) { kind = 0; na = 0; }
      else if (ty == 0) { kind = 1; x = (m[q] << 8) | m[q + 1]; q += 2; }
      else if (ty == 1) { kind = 1; x = m[q++]; }
      else { kind = 1; x = GetVar(m[q++]); }
    } else {
      kind = 2; num = op & 0x1f; na = 2;
      x = (op & 0x40) != 0 ? GetVar(m[q++]) : m[q++];
      y = (op & 0x20) != 0 ? GetVar(m[q++]) : m[q++];
      a[1] = y;                                      // je reads a[1..na)
    }
    pc = q;
    if (kind == 2) switch (num) {
      case 1: { bool hit = false; for (int i = 1; i < na; i++) if (a[i] == x) hit = true; Branch(hit); return; }
      case 2: Branch((short)x < (short)y); return;
      case 3: Branch((short)x > (short)y); return;
      case 4: { int v = (short)PeekVar(x) - 1; PokeVar(x, v); Branch(v < (short)y); return; }
      case 5: { int v = (short)PeekVar(x) + 1; PokeVar(x, v); Branch(v > (short)y); return; }
      case 6: Branch(Parent(x) == y); return;
      case 7: Branch((x & y) == y); return;
      case 8: Store(x | y); return;
      case 9: Store(x & y); return;
      case 10: Branch(x != 0 && (m[Obj(x) + (y >> 3)] & (0x80 >> (y & 7))) != 0); return;
      case 11: if (x != 0) m[Obj(x) + (y >> 3)] |= (byte)(0x80 >> (y & 7)); return;
      case 12: if (x != 0) m[Obj(x) + (y >> 3)] &= (byte)~(0x80 >> (y & 7)); return;
      case 13: PokeVar(x, y); return;
      case 14: {
        if (x == 0) return;
        RemoveObj(x);
        int oa = Obj(x), da = Obj(y);
        m[oa + 5] = m[da + 6]; m[da + 6] = (byte)x; m[oa + 4] = (byte)y; return;
      }
      case 15: Store(W((x + 2 * y) & 0xffff)); return;
      case 16: Store(m[(x + y) & 0xffff]); return;
      case 17: {
        int p = PropAddr(x, y);
        if (p == 0) { Store(W(objt + 2 * (y - 1))); return; }
        Store((m[p - 1] >> 5) == 0 ? m[p] : W(p)); return;
      }
      case 18: Store(PropAddr(x, y)); return;
      case 19: {
        if (x == 0) { Store(0); return; }
        if (y == 0) { Store(m[FirstProp(x)] & 31); return; }
        int p = PropAddr(x, y); if (p == 0) throw new Exception(String.Concat("GET_NEXT_PROP: OBJECT " + x.ToString(), " HAS NO PROPERTY " + y.ToString()));
        Store(m[p + 1 + (m[p - 1] >> 5)] & 31); return;
      }
      case 20: Store((short)x + (short)y); return;
      case 21: Store((short)x - (short)y); return;
      case 22: Store((short)x * (short)y); return;
      case 23: if ((short)y == 0) throw new Exception("DIVISION BY ZERO"); Store((short)x / (short)y); return;
      case 24: if ((short)y == 0) throw new Exception("DIVISION BY ZERO"); Store((short)x % (short)y); return;
    }
    else if (kind == 1) switch (num) {
      case 0: Branch(x == 0); return;
      case 1: { int v = Sibling(x); Store(v); Branch(v != 0); return; }
      case 2: { int v = Child(x); Store(v); Branch(v != 0); return; }
      case 3: Store(Parent(x)); return;
      case 4: Store(x != 0 ? (m[x - 1] >> 5) + 1 : 0); return;
      case 5: PokeVar(x, (short)PeekVar(x) + 1); return;
      case 6: PokeVar(x, (short)PeekVar(x) - 1); return;
      case 7: Zstr(x); return;
      case 9: RemoveObj(x); return;
      case 10: PrintObjName(x); return;
      case 11: Ret(x); return;
      case 12: pc += (short)x - 2; return;
      case 13: Zstr(x * 2); return;
      case 14: Store(PeekVar(x)); return;
      case 15: Store(~x); return;
    }
    else if (kind == 0) switch (num) {
      case 0: Ret(1); return;
      case 1: Ret(0); return;
      case 2: case 3: {
        pc = Zstr(pc);
        if (num == 3) { Emit(10); Ret(1); } return;
      }
      case 4: return;
      case 5: Snapshot(pc); Storage.Save(saved); Branch(true); return;
      case 6: {
        if (!hasSaved) { Branch(false); return; }
        RestoreSnapshot();
        Branch(true); return;                      // continues as the SAVE did, now succeeding
      }
      case 7: Restart(false); return;
      case 8: { if (sp == 0) throw new Exception("STACK UNDERFLOW"); Ret(stack[--sp]); return; }
      case 9: if (sp > 0) sp--; return;
      case 10: state = QUIT; return;
      case 11: Emit(10); return;
      case 12: ShowStatus(); return;
      case 13: Branch(true); return;
    }
    else switch (num) {
      case 0: { int st = m[pc++]; Call(x, st); return; }
      case 1: SW((x + 2 * y) & 0xffff, z); return;
      case 2: m[(x + y) & 0xffff] = (byte)z; return;
      case 3: {
        int p = PropAddr(x, y); if (p == 0) throw new Exception(String.Concat("PUT_PROP: OBJECT " + x.ToString(), " HAS NO PROPERTY " + y.ToString()));
        if ((m[p - 1] >> 5) == 0) m[p] = (byte)z; else SW(p, z); return;
      }
      case 4: ShowStatus(); textBuf = x; parseBuf = y; state = INPUT; return;
      case 5: Zscii(x); return;
      case 6: PrintNum((short)x); return;
      case 7: {
        int n = (short)x;
        if (n > 0) { rs = rs * 1664525u + 1013904223u; Store(1 + (int)Math.Floor(rs / 4294967296.0 * n)); return; }
        rs = n != 0 ? (uint)(-n) : (uint)seed; Store(0); return;
      }
      case 8: SetVar(0, x); return;
      case 9: { int v = GetVar(0); PokeVar(x, v); return; }
      case 10: case 11: case 21: return;           // split/set window, sound: no upper window here
      case 19: {
        int n = (short)x;
        if (n == 3) { if (ns3 == 16) throw new Exception("OUTPUT STREAM 3 TOO DEEP"); s3addr[ns3] = y; s3len[ns3] = 0; ns3++; }
        else if (n == -3 && ns3 > 0) { ns3--; SW(s3addr[ns3], s3len[ns3]); }
        return;
      }
      case 20: return;
    }
    throw new Exception(String.Concat("UNKNOWN OPCODE " + kind.ToString(), ":" + num.ToString(), " AT " + pc.ToString()));
  }
}
