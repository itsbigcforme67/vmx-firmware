// vxlink: the VMX SDK's linker. Reads a compiled VMX game (C# 1.0, built against the VMX mscorlib and
// Microsoft.Vmx.dll), checks it only uses what the VMX runtime runs, resolves every reference to an index and writes
// one compact image (.vxi) for runtime/vxrt.c. The image layout is in runtime/vxrt.h ("image").
//   vxlink out.vxi mscorlib.dll Microsoft.Vmx.dll game.dll
// Not supported (the linker says where): user structs, generics, try/catch/finally, multi-dimensional arrays,
// delegates, unsafe pointers, 64-bit unsigned maths is fine but no decimal.
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

static class K {   // value kinds (runtime/vxrt.h)
  public const byte VOID = 0, BOOL = 1, I1 = 2, U1 = 3, I2 = 4, U2 = 5, I4 = 6, U4 = 7, I8 = 8, U8 = 9, R4 = 10, R8 = 11, REF = 12, PTR = 13;
  public static int Size(int k) => k switch { BOOL or I1 or U1 => 1, I2 or U2 => 2, I4 or U4 or R4 or REF => 4, I8 or U8 or R8 => 8, _ => 4 };
}

class LinkError : Exception { public LinkError(string m) : base(m) { } }

class Asm { public string Path; public PEReader Pe; public MetadataReader Md; public Dictionary<TypeDefinitionHandle, TypeInfo> Types = new(); }

class Sig {
  public int Kind; public TypeInfo Type; public Sig Elem;     // REF: Type (class) or Elem (array); PTR: Elem
  public string Canon => Kind == K.REF ? (Elem != null ? "[" + Elem.Canon + "]" : Type.Full) : Kind == K.PTR ? "&" + Elem.Canon : "k" + Kind;
}

class FieldInfo { public TypeInfo Owner; public string Name; public Sig Type; public bool Static; public int Offset, StaticIndex = -1, Rva; public FieldDefinitionHandle H; }
class MethodInfo {
  public int Index; public TypeInfo Owner; public string Name; public MethodDefinitionHandle H;
  public bool Static, Virtual, Abstract, NewSlot, Native; public Sig Ret; public List<Sig> Params = new(); public List<Sig> Locals = new();
  public byte[] IL; public int MaxStack, VSlot = -1;
  public string Key => Name + "(" + string.Join(",", Params.Select(p => p.Canon)) + ")" + Ret.Canon;
  public string FullName => Owner.Full + "::" + Name;
}
class TypeInfo {
  public int Index; public Asm A; public TypeDefinitionHandle H; public string Full; public TypeInfo Parent;
  public bool IsInterface, IsAbstract, IsValue, IsEnum, Laid, VtDone; public int Prim = -1;   // Prim: kind of a primitive or enum
  public int InitSize = -1;                                                                    // a compiler's array-initializer blob type
  public List<FieldInfo> Fields = new(); public List<MethodInfo> Methods = new(); public List<TypeInfo> Interfaces = new();
  public int InstSize; public List<int> RefOffsets = new(); public List<MethodInfo> VTable = new(); public List<(MethodInfo, MethodInfo)> IfaceMap = new();
  public MethodInfo Cctor;
  public override string ToString() => Full;
}

class Provider : ISignatureTypeProvider<Sig, object> {
  readonly Linker L; readonly Asm A;
  public Provider(Linker l, Asm a) { L = l; A = a; }
  public Sig GetPrimitiveType(PrimitiveTypeCode c) => c switch {
    PrimitiveTypeCode.Boolean => new Sig { Kind = K.BOOL }, PrimitiveTypeCode.Char => new Sig { Kind = K.U2 },
    PrimitiveTypeCode.SByte => new Sig { Kind = K.I1 }, PrimitiveTypeCode.Byte => new Sig { Kind = K.U1 },
    PrimitiveTypeCode.Int16 => new Sig { Kind = K.I2 }, PrimitiveTypeCode.UInt16 => new Sig { Kind = K.U2 },
    PrimitiveTypeCode.Int32 => new Sig { Kind = K.I4 }, PrimitiveTypeCode.UInt32 => new Sig { Kind = K.U4 },
    PrimitiveTypeCode.Int64 => new Sig { Kind = K.I8 }, PrimitiveTypeCode.UInt64 => new Sig { Kind = K.U8 },
    PrimitiveTypeCode.Single => new Sig { Kind = K.R4 }, PrimitiveTypeCode.Double => new Sig { Kind = K.R8 },
    PrimitiveTypeCode.IntPtr => new Sig { Kind = K.I4 }, PrimitiveTypeCode.UIntPtr => new Sig { Kind = K.U4 },
    PrimitiveTypeCode.Void => new Sig { Kind = K.VOID },
    PrimitiveTypeCode.String => new Sig { Kind = K.REF, Type = L.ByName("System.String") },
    PrimitiveTypeCode.Object => new Sig { Kind = K.REF, Type = L.ByName("System.Object") },
    _ => throw new LinkError("type " + c + " isn't supported")
  };
  public Sig FromType(TypeInfo t) {
    if (t.Prim >= 0) return new Sig { Kind = t.Prim, Type = t };
    if (t.IsValue && t.InitSize < 0) throw new LinkError("struct " + t.Full + ": VMX games use classes, not structs");
    if (t.InitSize >= 0) return new Sig { Kind = K.VOID, Type = t };
    return new Sig { Kind = K.REF, Type = t };
  }
  public Sig GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte raw) => FromType(A.Types[h]);
  public Sig GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte raw) => FromType(L.Resolve(A, h));
  public Sig GetTypeFromSpecification(MetadataReader r, object ctx, TypeSpecificationHandle h, byte raw) => r.GetTypeSpecification(h).DecodeSignature(this, ctx);
  public Sig GetSZArrayType(Sig e) => new Sig { Kind = K.REF, Elem = e };
  public Sig GetArrayType(Sig e, ArrayShape s) => throw new LinkError("multi-dimensional arrays aren't supported: use arrays of arrays");
  public Sig GetByReferenceType(Sig e) => new Sig { Kind = K.PTR, Elem = e };
  public Sig GetPointerType(Sig e) => throw new LinkError("pointers aren't supported");
  public Sig GetPinnedType(Sig e) => e;
  public Sig GetGenericInstantiation(Sig g, ImmutableArray<Sig> a) => throw new LinkError("generics aren't supported (C# 1.0)");
  public Sig GetGenericMethodParameter(object c, int i) => throw new LinkError("generics aren't supported (C# 1.0)");
  public Sig GetGenericTypeParameter(object c, int i) => throw new LinkError("generics aren't supported (C# 1.0)");
  public Sig GetFunctionPointerType(MethodSignature<Sig> s) => throw new LinkError("function pointers aren't supported");
  public Sig GetModifiedType(Sig m, Sig u, bool req) => u;
}

class Linker {
  public List<Asm> Asms = new(); public List<TypeInfo> Types = new(); public List<MethodInfo> Methods = new();
  public List<FieldInfo> Statics = new(); Dictionary<string, TypeInfo> byName = new();
  List<string> strings = new(); Dictionary<string, int> stringIx = new();
  List<byte[]> blobs = new(); Dictionary<(Asm, int), int> blobIx = new();
  string where = "";

  public TypeInfo ByName(string n) => byName.TryGetValue(n, out var t) ? t : throw new LinkError("unknown type " + n);
  static string Full(MetadataReader md, TypeDefinition td) {
    var n = md.GetString(td.Name); var ns = md.GetString(td.Namespace);
    var dt = td.GetDeclaringType();
    if (!dt.IsNil) return Full(md, md.GetTypeDefinition(dt)) + "/" + n;
    return ns.Length > 0 ? ns + "." + n : n;
  }
  static string RefFull(MetadataReader md, TypeReference tr) {
    var n = md.GetString(tr.Name); var ns = md.GetString(tr.Namespace);
    if (tr.ResolutionScope.Kind == HandleKind.TypeReference) return RefFull(md, md.GetTypeReference((TypeReferenceHandle)tr.ResolutionScope)) + "/" + n;
    return ns.Length > 0 ? ns + "." + n : n;
  }
  public TypeInfo Resolve(Asm a, TypeReferenceHandle h) => ByName(RefFull(a.Md, a.Md.GetTypeReference(h)));
  TypeInfo TypeOf(Asm a, EntityHandle h) => h.Kind switch {
    HandleKind.TypeDefinition => a.Types[(TypeDefinitionHandle)h],
    HandleKind.TypeReference => Resolve(a, (TypeReferenceHandle)h),
    _ => throw new LinkError("unexpected type handle " + h.Kind)
  };
  Sig SigOf(Asm a, EntityHandle h) => h.Kind == HandleKind.TypeSpecification
    ? a.Md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(new Provider(this, a), null)
    : new Provider(this, a).FromType(TypeOf(a, h));

  static readonly Dictionary<string, byte> PRIM = new() {
    ["System.Boolean"] = K.BOOL, ["System.Char"] = K.U2, ["System.SByte"] = K.I1, ["System.Byte"] = K.U1, ["System.Int16"] = K.I2,
    ["System.UInt16"] = K.U2, ["System.Int32"] = K.I4, ["System.UInt32"] = K.U4, ["System.Int64"] = K.I8, ["System.UInt64"] = K.U8,
    ["System.Single"] = K.R4, ["System.Double"] = K.R8, ["System.IntPtr"] = K.I4, ["System.UIntPtr"] = K.U4, ["System.Void"] = K.VOID,
    ["System.RuntimeFieldHandle"] = K.I4, ["System.RuntimeTypeHandle"] = K.I4, ["System.RuntimeMethodHandle"] = K.I4,
  };

  public void Load(string path) {
    var a = new Asm { Path = path, Pe = new PEReader(File.ReadAllBytes(path).ToImmutableArray()) };
    a.Md = a.Pe.GetMetadataReader(); Asms.Add(a);
    foreach (var h in a.Md.TypeDefinitions) {
      var td = a.Md.GetTypeDefinition(h); var full = Full(a.Md, td);
      if (full == "<Module>") continue;
      var t = new TypeInfo { A = a, H = h, Full = full, Index = Types.Count };
      t.IsInterface = (td.Attributes & TypeAttributes.Interface) != 0;
      t.IsAbstract = (td.Attributes & TypeAttributes.Abstract) != 0;
      if (PRIM.TryGetValue(full, out var pk)) t.Prim = pk;
      if (td.GetGenericParameters().Count > 0) throw new LinkError(full + ": generics aren't supported (C# 1.0)");
      Types.Add(t); a.Types[h] = t;
      if (byName.ContainsKey(full)) throw new LinkError("type " + full + " is defined twice");
      byName[full] = t;
    }
  }

  public void Link() {
    // parents, value types, enums, array-initializer blob types
    foreach (var t in Types) {
      var td = t.A.Md.GetTypeDefinition(t.H);
      if (!td.BaseType.IsNil) t.Parent = TypeOf(t.A, td.BaseType);
      foreach (var ii in td.GetInterfaceImplementations()) t.Interfaces.Add(TypeOf(t.A, t.A.Md.GetInterfaceImplementation(ii).Interface));
    }
    foreach (var t in Types) {
      for (var p = t.Parent; p != null; p = p.Parent) if (p.Full == "System.ValueType") t.IsValue = true;
      if (t.Parent?.Full == "System.Enum") {
        t.IsEnum = true;
        var td = t.A.Md.GetTypeDefinition(t.H);
        foreach (var fh in td.GetFields()) { var fd = t.A.Md.GetFieldDefinition(fh);
          if ((fd.Attributes & FieldAttributes.Static) == 0) t.Prim = fd.DecodeSignature(new Provider(this, t.A), null).Kind; }
      }
      if (t.Full.Contains("__StaticArrayInitTypeSize=")) t.InitSize = t.A.Md.GetTypeDefinition(t.H).GetLayout().Size;
      if (t.Full.StartsWith("System.") && t.IsValue) t.InitSize = t.Prim < 0 && !t.IsEnum ? 0 : -1;      // corlib's other structs: never laid out
    }
    // fields and methods
    foreach (var t in Types) {
      where = t.Full;
      var md = t.A.Md; var td = md.GetTypeDefinition(t.H); var pv = new Provider(this, t.A);
      foreach (var fh in td.GetFields()) {
        var fd = md.GetFieldDefinition(fh);
        var f = new FieldInfo { Owner = t, Name = md.GetString(fd.Name), H = fh, Static = (fd.Attributes & FieldAttributes.Static) != 0 };
        if ((fd.Attributes & FieldAttributes.Literal) != 0) continue;               // consts are folded by the compiler
        if (t.IsEnum && !f.Static) continue;
        f.Type = fd.DecodeSignature(pv, null);
        if ((fd.Attributes & FieldAttributes.HasFieldRVA) != 0) f.Rva = fd.GetRelativeVirtualAddress();
        else if (f.Static) { f.StaticIndex = Statics.Count; Statics.Add(f); }
        t.Fields.Add(f);
      }
      foreach (var mh in td.GetMethods()) {
        var mdf = md.GetMethodDefinition(mh);
        var m = new MethodInfo { Owner = t, Name = md.GetString(mdf.Name), H = mh, Index = Methods.Count };
        where = t.Full + "::" + m.Name;
        if (mdf.GetGenericParameters().Count > 0) throw new LinkError(where + ": generic methods aren't supported (C# 1.0)");
        m.Static = (mdf.Attributes & MethodAttributes.Static) != 0;
        m.Virtual = (mdf.Attributes & MethodAttributes.Virtual) != 0;
        m.Abstract = (mdf.Attributes & MethodAttributes.Abstract) != 0;
        m.NewSlot = (mdf.Attributes & MethodAttributes.NewSlot) != 0;
        m.Native = (mdf.ImplAttributes & MethodImplAttributes.InternalCall) != 0;
        var sig = mdf.DecodeSignature(pv, null);
        m.Ret = sig.ReturnType; m.Params.AddRange(sig.ParameterTypes);
        if (mdf.RelativeVirtualAddress != 0) {
          var body = t.A.Pe.GetMethodBody(mdf.RelativeVirtualAddress);
          if (body.ExceptionRegions.Length > 0) throw new LinkError(where + ": try / catch / finally (and foreach over a collection, using, lock) aren't supported");
          m.IL = body.GetILBytes(); m.MaxStack = body.MaxStack;
          if (!body.LocalSignature.IsNil) m.Locals.AddRange(md.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(pv, null));
        } else if (!m.Native && !m.Abstract) throw new LinkError(where + ": a method with no body (only the SDK's own libraries may be extern)");
        if (m.Native && t.A != Asms[0] && t.A != Asms[1]) throw new LinkError(where + ": games can't declare extern methods");
        if (m.Name == ".cctor") t.Cctor = m;
        t.Methods.Add(m); Methods.Add(m);
      }
    }
    foreach (var t in Types) Layout(t);
    foreach (var t in Types) VTable(t);
    foreach (var t in Types) Interfaces(t);
    foreach (var m in Methods) if (m.IL != null) { where = m.FullName; Rewrite(m); }
  }

  void Layout(TypeInfo t) {
    if (t.Laid) return; t.Laid = true;
    if (t.Parent != null) { Layout(t.Parent); t.InstSize = t.Parent.InstSize; t.RefOffsets.AddRange(t.Parent.RefOffsets); }
    if (t.IsValue) return;
    foreach (var f in t.Fields) {
      if (f.Static || f.Rva != 0) continue;
      int k = f.Type.Kind, s = K.Size(k);
      t.InstSize = (t.InstSize + s - 1) / s * s; f.Offset = t.InstSize; t.InstSize += s;
      if (k == K.REF) t.RefOffsets.Add(f.Offset);
    }
    t.InstSize = (t.InstSize + 3) & ~3;
  }
  void VTable(TypeInfo t) {
    if (t.VtDone) return; t.VtDone = true;
    if (t.Parent != null) { VTable(t.Parent); t.VTable.AddRange(t.Parent.VTable); }
    foreach (var m in t.Methods) {
      if (!m.Virtual || t.IsInterface) continue;
      int slot = -1;
      if (!m.NewSlot) for (int i = 0; i < t.VTable.Count; i++) if (t.VTable[i].Key == m.Key) { slot = i; break; }
      if (slot < 0) { slot = t.VTable.Count; t.VTable.Add(m); } else t.VTable[slot] = m;
      m.VSlot = slot;
    }
  }
  IEnumerable<TypeInfo> AllInterfaces(TypeInfo t) {
    var seen = new HashSet<TypeInfo>(); var todo = new Stack<TypeInfo>();
    for (var p = t; p != null; p = p.Parent) foreach (var i in p.Interfaces) todo.Push(i);
    while (todo.Count > 0) { var i = todo.Pop(); if (seen.Add(i)) { yield return i; foreach (var j in i.Interfaces) todo.Push(j); } }
  }
  void Interfaces(TypeInfo t) {
    if (t.IsInterface || t.IsAbstract && t.IsInterface) return;
    // explicit implementations (MethodImpl table), anywhere up the chain
    var expl = new Dictionary<MethodInfo, MethodInfo>();
    for (var p = t; p != null; p = p.Parent) {
      var td = p.A.Md.GetTypeDefinition(p.H);
      foreach (var ih in td.GetMethodImplementations()) {
        var mi = p.A.Md.GetMethodImplementation(ih);
        var decl = MethodOf(p.A, mi.MethodDeclaration); var body = MethodOf(p.A, mi.MethodBody);
        if (!expl.ContainsKey(decl)) expl[decl] = body;
      }
    }
    foreach (var i in AllInterfaces(t)) foreach (var im in i.Methods) {
      MethodInfo impl = expl.TryGetValue(im, out var e) ? e : null;
      if (impl == null) for (int s = t.VTable.Count - 1; s >= 0 && impl == null; s--) if (t.VTable[s].Key == im.Key) impl = t.VTable[s];
      if (impl == null && !t.IsAbstract) throw new LinkError(t.Full + " doesn't implement " + im.FullName);
      if (impl != null) t.IfaceMap.Add((im, impl));
    }
  }

  // a method token (MethodDef or MemberRef) -> its method
  MethodInfo MethodOf(Asm a, EntityHandle h) {
    if (h.Kind == HandleKind.MethodDefinition) return Methods.First(m => m.Owner.A == a && m.H.Equals((MethodDefinitionHandle)h));
    if (h.Kind != HandleKind.MemberReference) throw new LinkError("unsupported method reference (" + h.Kind + ")");
    var mr = a.Md.GetMemberReference((MemberReferenceHandle)h);
    if (mr.Parent.Kind == HandleKind.TypeSpecification) throw new LinkError("methods on array or generic types aren't supported");
    var t = TypeOf(a, mr.Parent); var name = a.Md.GetString(mr.Name);
    var sig = mr.DecodeMethodSignature(new Provider(this, a), null);
    var key = name + "(" + string.Join(",", sig.ParameterTypes.Select(p => p.Canon)) + ")" + sig.ReturnType.Canon;
    for (var p = t; p != null; p = p.Parent) foreach (var m in p.Methods) if (m.Key == key) return m;
    throw new LinkError("can't find " + t.Full + "::" + key + " (not in the VMX libraries?)");
  }
  FieldInfo FieldOf(Asm a, EntityHandle h) {
    if (h.Kind == HandleKind.FieldDefinition) return Types.SelectMany(t => t.Fields).First(f => f.Owner.A == a && f.H.Equals((FieldDefinitionHandle)h));
    var mr = a.Md.GetMemberReference((MemberReferenceHandle)h);
    var t = TypeOf(a, mr.Parent); var name = a.Md.GetString(mr.Name);
    for (var p = t; p != null; p = p.Parent) foreach (var f in p.Fields) if (f.Name == name) return f;
    throw new LinkError("can't find field " + t.Full + "::" + name);
  }
  int Str(string s) { if (!stringIx.TryGetValue(s, out var i)) { i = strings.Count; strings.Add(s); stringIx[s] = i; } return i; }
  static uint ElemDesc(Sig e) => e.Kind == K.REF ? (uint)(K.REF << 24) | (uint)(e.Type?.Index ?? 0xFFFFFF) : (uint)(e.Kind << 24) | 0xFFFFFF;

  // operand sizes of the IL ops the runtime runs (others are refused)
  static int Operand(int op) {
    if (op <= 0x0D || op == 0x14 || (op >= 0x15 && op <= 0x1E) || op == 0x25 || op == 0x26 || op == 0x2A) return 0;
    if ((op >= 0x0E && op <= 0x13) || op == 0x1F || (op >= 0x2B && op <= 0x37) || op == 0xDE) return 1;
    if (op == 0x20 || (op >= 0x38 && op <= 0x44) || op == 0xDD || op == 0x22) return 4;
    if (op == 0x21 || op == 0x23) return 8;
    if ((op >= 0x46 && op <= 0x6E) || op == 0x76 || op == 0x7A || op == 0x8E || (op >= 0x90 && op <= 0xA2) || (op >= 0x82 && op <= 0x8B)
        || (op >= 0xB3 && op <= 0xBA) || op == 0xC3 || (op >= 0xD1 && op <= 0xDC) || op == 0xE0) return 0;
    if (op == 0x28 || op == 0x6F || op == 0x73 || op == 0x72 || op == 0x74 || op == 0x75 || (op >= 0x7B && op <= 0x80) || op == 0x8C
        || op == 0x8D || op == 0x8F || op == 0xA3 || op == 0xA4 || op == 0xA5 || op == 0xD0) return 4;
    return -1;
  }
  static int OperandFE(int op) => op switch { >= 0x01 and <= 0x05 => 0, >= 0x09 and <= 0x0E => 2, 0x15 or 0x16 => 4, _ => -1 };

  void Rewrite(MethodInfo m) {
    var il = m.IL; var a = m.Owner.A;
    void Put(int at, uint v) { il[at] = (byte)v; il[at + 1] = (byte)(v >> 8); il[at + 2] = (byte)(v >> 16); il[at + 3] = (byte)(v >> 24); }
    for (int i = 0; i < il.Length;) {
      int op = il[i++];
      if (op == 0xFE) {
        int op2 = il[i++], n = OperandFE(op2);
        if (n < 0) throw new LinkError(where + ": IL op FE " + op2.ToString("X2") + " isn't supported");
        if (n == 4) {
          int tok = BitConverter.ToInt32(il, i); var s = SigOf(a, MetadataTokens.EntityHandle(tok));
          Put(i, s.Type != null ? (uint)s.Type.Index : 0xFFFFFFu);     // initobj / constrained.: the type
        }
        i += n; continue;
      }
      if (op == 0x45) { int n = BitConverter.ToInt32(il, i); i += 4 + 4 * n; continue; }   // switch
      int sz = Operand(op);
      if (sz < 0) throw new LinkError(where + ": IL op " + op.ToString("X2") + " isn't supported");
      if (sz == 4 && (op == 0x28 || op == 0x6F || op == 0x73 || op == 0x72 || op == 0x74 || op == 0x75 || (op >= 0x7B && op <= 0x80)
                      || op == 0x8C || op == 0x8D || op == 0x8F || op == 0xA3 || op == 0xA4 || op == 0xA5 || op == 0xD0)) {
        int tok = BitConverter.ToInt32(il, i); var h = op == 0x72 ? default : MetadataTokens.EntityHandle(tok);
        switch (op) {
          case 0x28: case 0x6F: case 0x73: {          // call, callvirt, newobj
            var t = MethodOf(a, h);
            if (op == 0x73 && t.Owner.Full.Contains("Delegate")) throw new LinkError(where + ": delegates aren't supported");
            Put(i, (uint)t.Index); break; }
          case 0x72: Put(i, (uint)Str(a.Md.GetUserString(MetadataTokens.UserStringHandle(tok & 0xFFFFFF)))); break;
          case 0x7B: case 0x7C: case 0x7D: {           // ldfld, ldflda, stfld
            var f = FieldOf(a, h);
            if (f.Static) throw new LinkError(where + ": instance access to static field " + f.Name);
            Put(i, (uint)(f.Type.Kind << 16 | f.Offset)); break; }
          case 0x7E: case 0x7F: case 0x80: {           // ldsfld, ldsflda, stsfld
            var f = FieldOf(a, h);
            if (f.StaticIndex < 0) throw new LinkError(where + ": static field " + f.Name + " can't be used here");
            Put(i, (uint)f.StaticIndex); break; }
          case 0x8D: case 0x8F: case 0xA3: case 0xA4: Put(i, ElemDesc(SigOf(a, h))); break;    // newarr, ldelema, ldelem, stelem
          case 0x74: case 0x75: case 0xA5: case 0x8C: {     // castclass, isinst, unbox.any, box: the type (arrays: FFFFFE)
            var s = SigOf(a, h); Put(i, s.Elem != null ? 0xFFFFFEu : (uint)s.Type.Index); break; }
          case 0xD0: {                                  // ldtoken: only array-initializer data
            if (h.Kind != HandleKind.FieldDefinition) throw new LinkError(where + ": typeof / ldtoken aren't supported");
            var f = FieldOf(a, h); if (f.Rva == 0) throw new LinkError(where + ": ldtoken of a normal field");
            if (!blobIx.TryGetValue((a, f.Rva), out var bi)) {
              var size = f.Type.Type.InitSize; var data = a.Pe.GetSectionData(f.Rva).GetContent(0, size).ToArray();
              bi = blobs.Count; blobs.Add(data); blobIx[(a, f.Rva)] = bi;
            }
            Put(i, (uint)bi); break; }
        }
      }
      i += sz;
    }
  }

  // ---- the image (runtime/vxrt.h)
  public byte[] Write(string gameAsm) {
    var game = ByName("Microsoft.Vmx.Game");
    var entry = Types.Where(t => t.A.Path == gameAsm && !t.IsAbstract && IsA(t, game)).ToList();
    if (entry.Count != 1) throw new LinkError("the game needs exactly one (non-abstract) class that extends Microsoft.Vmx.Game; found " + entry.Count);
    var ctor = entry[0].Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Params.Count == 0) ?? throw new LinkError(entry[0].Full + " needs a constructor with no arguments");
    MethodInfo G(string n) => game.Methods.First(m => m.Name == n);

    var ms = new MemoryStream(); var w = new BinaryWriter(ms);
    var names = new MemoryStream(); var nameIx = new Dictionary<string, int>();
    int Name(string s) { if (!nameIx.TryGetValue(s, out var o)) { o = (int)names.Length; var b = Encoding.ASCII.GetBytes(s); names.Write(b); names.WriteByte(0); nameIx[s] = o; } return o; }
    var vt = new List<int>(); var refs = new List<int>(); var imap = new List<int>(); var kinds = new List<byte>(); var code = new MemoryStream();
    var natives = Methods.Where(m => m.Native).ToList();

    w.Write(Encoding.ASCII.GetBytes("VXIL")); w.Write(1);
    w.Write(Types.Count); w.Write(Methods.Count); w.Write(Statics.Count); w.Write(strings.Count); w.Write(blobs.Count); w.Write(natives.Count);
    w.Write(entry[0].Index); w.Write(ctor.Index); w.Write(G("Start").VSlot); w.Write(G("Tick").VSlot); w.Write(G("Draw").VSlot); w.Write(G("get_UsesB").VSlot);
    w.Write(ByName("Microsoft.Vmx.Screen").Index); w.Write(ByName("System.String").Index); w.Write(ByName("System.Object").Index);
    w.Write(ByName("System.Exception").Index);
    foreach (var t in Types) {          // 40 bytes each
      int flags = (t.IsInterface ? 1 : 0) | (t.IsAbstract ? 2 : 0) | (t.IsValue ? 4 : 0) | (t.IsEnum ? 8 : 0);
      w.Write(t.Parent?.Index ?? -1); w.Write((ushort)flags); w.Write((byte)(t.Prim < 0 ? 0xFF : t.Prim)); w.Write((byte)0);
      w.Write(t.InstSize); w.Write(vt.Count); w.Write(t.VTable.Count); w.Write(refs.Count); w.Write(t.RefOffsets.Count);
      w.Write(imap.Count / 2); w.Write(t.IfaceMap.Count); w.Write(t.Cctor?.Index ?? -1); w.Write(Name(t.Full));
      vt.AddRange(t.VTable.Select(m => m.Index)); refs.AddRange(t.RefOffsets); foreach (var (im, impl) in t.IfaceMap) { imap.Add(im.Index); imap.Add(impl.Index); }
    }
    foreach (var m in Methods) {        // 36 bytes each
      int flags = (m.Static ? 1 : 0) | (m.Virtual ? 2 : 0) | (m.Abstract ? 4 : 0) | (m.Native ? 8 : 0) | (m.Owner.IsInterface ? 16 : 0) | (m.Owner.IsValue ? 32 : 0);
      int nArgs = m.Params.Count + (m.Static ? 0 : 1);
      w.Write((int)code.Length); w.Write(m.IL?.Length ?? 0); w.Write((ushort)nArgs); w.Write((ushort)m.Locals.Count);
      w.Write((ushort)m.MaxStack); w.Write((ushort)flags); w.Write(m.VSlot); w.Write((byte)m.Ret.Kind); w.Write((byte)0); w.Write((ushort)0);
      w.Write(kinds.Count); w.Write(m.Owner.Index); w.Write(Name(m.FullName));
      if (!m.Static) kinds.Add(m.Owner.IsValue ? K.PTR : K.REF);
      foreach (var p in m.Params) kinds.Add((byte)p.Kind);
      foreach (var l in m.Locals) kinds.Add((byte)l.Kind);
      if (m.IL != null) { code.Write(m.IL); while (code.Length % 4 != 0) code.WriteByte(0); }
    }
    foreach (var f in Statics) w.Write((byte)f.Type.Kind);
    Pad(w);
    var pool = new MemoryStream(); var off = new List<int>();
    foreach (var s in strings) { w.Write((int)pool.Length / 2); w.Write(s.Length); foreach (var c in s) { pool.WriteByte((byte)c); pool.WriteByte((byte)(c >> 8)); } }
    var bpool = new MemoryStream();
    foreach (var b in blobs) { w.Write((int)bpool.Length); w.Write(b.Length); bpool.Write(b); while (bpool.Length % 4 != 0) bpool.WriteByte(0); }
    foreach (var m in natives) { w.Write(m.Index); w.Write(Name(m.FullName + "/" + m.Params.Count)); }
    void Ints(List<int> l) { w.Write(l.Count); foreach (var v in l) w.Write(v); }
    Ints(vt); Ints(refs); Ints(imap);
    w.Write(kinds.Count); w.Write(kinds.ToArray()); Pad(w);
    w.Write((int)code.Length); w.Write(code.ToArray());
    w.Write((int)pool.Length); w.Write(pool.ToArray()); Pad(w);
    w.Write((int)bpool.Length); w.Write(bpool.ToArray());
    w.Write((int)names.Length); w.Write(names.ToArray()); Pad(w);
    return ms.ToArray();
  }
  static void Pad(BinaryWriter w) { while (w.BaseStream.Length % 4 != 0) w.Write((byte)0); }
  static bool IsA(TypeInfo t, TypeInfo b) { for (var p = t; p != null; p = p.Parent) if (p == b) return true; return false; }
  public string Where => where;
}

static class Program {
  static int Main(string[] args) {
    if (args.Length < 4) { Console.Error.WriteLine("usage: vxlink out.vxi mscorlib.dll Microsoft.Vmx.dll game.dll"); return 2; }
    var L = new Linker();
    try {
      for (int i = 1; i < args.Length; i++) L.Load(args[i]);
      L.Link();
      var img = L.Write(args[3]);
      File.WriteAllBytes(args[0], img);
      Console.WriteLine($"{args[0]}: {img.Length} bytes, {L.Types.Count} types, {L.Methods.Count} methods");
      return 0;
    } catch (Exception e) when (e is LinkError || e is InvalidOperationException || e is ArgumentException || e is KeyNotFoundException) {
      Console.Error.WriteLine("vxlink: " + e.Message + (L.Where.Length > 0 && !e.Message.Contains(L.Where) ? "  (in " + L.Where + ")" : ""));
      return 1;
    }
  }
}
