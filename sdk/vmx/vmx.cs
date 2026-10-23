// The VMX library (Microsoft.Vmx): everything a VMX game can do besides plain C#. A game is one class that extends
// Game; the runtime makes it, then calls Start, then Tick and Draw 30 times a second.
using System.Runtime.CompilerServices;

namespace Microsoft.Vmx {
  // the buttons, as bits of held / pressed
  public sealed class Buttons {
    public const int Up = 1, Down = 2, Left = 4, Right = 8, A = 16, B = 32;
  }

  public abstract class Game {
    // seed: a different number each play; demo: the VMX plays by itself (attract mode / DEMO on GAMES)
    public abstract void Start(int seed, bool demo);
    // held: buttons down now; pressed: those that went down since the last tick
    public abstract void Tick(int held, int pressed);
    public abstract void Draw(Screen screen);
    // false: B goes back to the GAMES menu (the game doesn't use B)
    public virtual bool UsesB { get { return true; } }
  }

  // the 64 x 64 screen. 1 = lit.
  public sealed class Screen {
    public const int Width = 64, Height = 64;
    Screen() { }
    [MethodImpl(MethodImplOptions.InternalCall)] public extern void Clear();
    [MethodImpl(MethodImplOptions.InternalCall)] public extern void Set(int x, int y, bool on);
    [MethodImpl(MethodImplOptions.InternalCall)] public extern bool Get(int x, int y);
    [MethodImpl(MethodImplOptions.InternalCall)] public extern void Fill(int x, int y, int w, int h, bool on);
    // the VMX's 3x5 font, 4 px a character
    [MethodImpl(MethodImplOptions.InternalCall)] public extern void Text(int x, int y, string s, bool on);
    public static int TextWidth(string s) { return s.Length * 4 - 1; }
  }

  public sealed class Beeper {
    // a square-wave note: hz, length in ms, volume 0..100 (silent when SOUND is off)
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern void Tone(int hz, int ms, int volume);
  }

  public sealed class Scores {
    // this game's best result so far; the firmware keeps the best in SAVES and sends it with CALL.
    // For won / lost / drawn games, score = wins and the fields are won, lost, drawn.
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern void Report(int score, int field1, int field2, int field3);
  }

  public sealed class Storage {
    // this game's own save (up to 16 KB), or null
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern byte[] Load();
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern void Save(byte[] data);
    // another game's save, read only (gameId as in its GAME.VXP), or null
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern byte[] LoadOther(int gameId);
  }

  // VMX CASINO's shared chip bank: every casino game (game.ini casino=1) sees the same balance, so chips won at one
  // table can be staked at another and cashed out at the Xbox (VMX World's CASHIER). Other games get 0 / false.
  // In a demo the VMX plays from a scratch bank of 100 chips, so attract mode never touches the player's chips.
  public sealed class Chips {
    // the chips in the bank now
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern int Balance();
    // takes n chips for a stake; false (and nothing taken) if the bank has fewer
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool Bet(int n);
    // pays n chips into the bank (winnings, stakes back)
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern void Pay(int n);
    // the daily chips the bank gave when this game started (a small top-up once a day while you're low), or 0
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern int Bonus();
  }

  // read-only data files packed into this game's GAME.VXP (tools/vxp.py --file NAME=PATH), read in pieces so a big
  // file needn't fit in the heap
  public sealed class Package {
    // the file's length in bytes, or -1 if the package has no such file
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern int Length(string name);
    // copies up to count bytes from offset into dst at index; returns how many were copied
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern int Read(string name, int offset, byte[] dst, int index, int count);
  }
}
