// The VMX's core library (mscorlib): the only System types a VMX game can use. Compiled with -nostdlib, so nothing
// else from .NET exists to call. Methods marked InternalCall are done by the runtime (runtime/natives.c); the linker
// (sdk/vxlink) matches them by full name. C# 1.0 only: no generics.
using System.Runtime.CompilerServices;

namespace System {
  public class Object {
    public Object() { }
    public virtual bool Equals(object o) { return (object)this == o; }
    public virtual int GetHashCode() { return 0; }
    public virtual string ToString() { return "OBJECT"; }
  }
  public abstract class ValueType { }
  public abstract class Enum : ValueType { }
  public struct Void { }
  public struct Boolean { public override string ToString() { return this ? "TRUE" : "FALSE"; } }
  public struct Char { [MethodImpl(MethodImplOptions.InternalCall)] public extern override string ToString(); }
  public struct SByte { }
  public struct Byte { [MethodImpl(MethodImplOptions.InternalCall)] public extern override string ToString(); }
  public struct Int16 { }
  public struct UInt16 { }
  public struct Int32 {
    public const int MaxValue = 0x7FFFFFFF, MinValue = unchecked((int)0x80000000);
    [MethodImpl(MethodImplOptions.InternalCall)] public extern override string ToString();
  }
  public struct UInt32 { public const uint MaxValue = 0xFFFFFFFF; [MethodImpl(MethodImplOptions.InternalCall)] public extern override string ToString(); }
  public struct Int64 { }
  public struct UInt64 { }
  public struct Single { }
  public struct Double { }
  public struct IntPtr { }
  public struct UIntPtr { }
  public struct RuntimeTypeHandle { }
  public struct RuntimeFieldHandle { }
  public struct RuntimeMethodHandle { }
  public abstract class Type { }
  public abstract class Array {
    public extern int Length { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern void Copy(Array src, int srcIndex, Array dst, int dstIndex, int length);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern void Clear(Array a, int index, int length);
  }
  public sealed class String {
    public extern int Length { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    [IndexerName("Chars")] public extern char this[int i] { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern string Concat(string a, string b);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern string Concat(string a, string b, string c);
    public static string Concat(string a, string b, string c, string d) { return Concat(Concat(a, b), Concat(c, d)); }
    public static string Concat(object a, object b) { return Concat(a == null ? "" : a.ToString(), b == null ? "" : b.ToString()); }
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool Equals(string a, string b);
    public static bool operator ==(string a, string b) { return Equals(a, b); }
    public static bool operator !=(string a, string b) { return !Equals(a, b); }
    public override bool Equals(object o) { return Equals(this, o as string); }
    public override int GetHashCode() { return Length; }
    public override string ToString() { return this; }
    [MethodImpl(MethodImplOptions.InternalCall)] public extern string Substring(int start, int length);
    public string Substring(int start) { return Substring(start, Length - start); }
    [MethodImpl(MethodImplOptions.InternalCall)] public extern string PadLeft(int width, char c);
    [MethodImpl(MethodImplOptions.InternalCall)] public extern string PadRight(int width, char c);
    [MethodImpl(MethodImplOptions.InternalCall)] public extern int IndexOf(char c);
    [MethodImpl(MethodImplOptions.InternalCall)] public extern string ToUpper();
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern string FromChars(char[] c, int start, int length);
  }
  public class Exception {
    string message;
    public Exception() { }
    public Exception(string message) { this.message = message; }
    public virtual string Message { get { return message; } }
  }
  public class IndexOutOfRangeException : Exception { }
  public class NullReferenceException : Exception { }
  public class InvalidOperationException : Exception { public InvalidOperationException(string m) : base(m) { } }
  public class ArgumentException : Exception { public ArgumentException(string m) : base(m) { } }
  public class Attribute { }
  public enum AttributeTargets { All = 0x7FFF }
  [AttributeUsage(AttributeTargets.All)] public sealed class AttributeUsageAttribute : Attribute {
    public AttributeUsageAttribute(AttributeTargets t) { }
    public bool AllowMultiple { get { return false; } set { } }
    public bool Inherited { get { return false; } set { } }
  }
  public sealed class ParamArrayAttribute : Attribute { }
  public sealed class FlagsAttribute : Attribute { }
  public abstract class Delegate { }
  public abstract class MulticastDelegate : Delegate { }
  public interface IDisposable { void Dispose(); }

  public sealed class Math {
    public const double PI = 3.14159265358979323846;
    public static int Abs(int v) { return v < 0 ? -v : v; }
    public static double Abs(double v) { return v < 0 ? -v : v; }
    public static int Min(int a, int b) { return a < b ? a : b; }
    public static int Max(int a, int b) { return a > b ? a : b; }
    public static double Min(double a, double b) { return a < b ? a : b; }
    public static double Max(double a, double b) { return a > b ? a : b; }
    public static int Sign(int v) { return v < 0 ? -1 : v > 0 ? 1 : 0; }
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Floor(double v);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Ceiling(double v);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Round(double v);   // JS Math.round: halves go up
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Sqrt(double v);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Sin(double v);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Cos(double v);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Atan2(double y, double x);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Pow(double x, double y);
  }
}
namespace System.Runtime.CompilerServices {
  public enum MethodImplOptions { InternalCall = 0x1000, NoInlining = 8 }
  public sealed class MethodImplAttribute : Attribute { public MethodImplAttribute(MethodImplOptions o) { } }
  public sealed class IndexerNameAttribute : Attribute { public IndexerNameAttribute(string n) { } }
  public sealed class RuntimeHelpers {
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern void InitializeArray(Array a, RuntimeFieldHandle f);
  }
  public sealed class CompilationRelaxationsAttribute : Attribute { public CompilationRelaxationsAttribute(int r) { } }
  public sealed class RuntimeCompatibilityAttribute : Attribute { public bool WrapNonExceptionThrows { get { return true; } set { } } }
}
namespace System.Reflection {
  public sealed class DefaultMemberAttribute : Attribute { public DefaultMemberAttribute(string n) { } }
}
