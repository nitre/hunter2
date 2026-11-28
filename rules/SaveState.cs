using System.Reflection;
using System.Runtime.CompilerServices;

namespace Hunter.Rules;

/// <summary>CHECKS: the whole game state to a file and back - a run saved at a moment can be loaded by other runs to carry
/// on from there (several tries from one moment). Every field of the root (the Play) and the mutable class-wide fields
/// (statics) of the given types are written, and every object they reach, by reflection. Delegates (events, callbacks)
/// are not written: the root's and the statics' own stay as they are; deeper ones load empty.</summary>
public static class SaveState
{
    const int Magic = 0x48535331;   // "HSS1"
    enum Kind : byte { Null, String, Array, Object, Boxed, TypeName }

    public static void Save(string path, object root, IEnumerable<Type> statics)
    {
        var w = new Writer();
        var fields = RootFields(root.GetType());
        var staticFields = StaticFields(statics);
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            w.Out = bw;
            bw.Write(Magic);
            foreach (var f in fields) w.Value(f.FieldType, f.GetValue(root));
            foreach (var f in staticFields) w.Value(f.FieldType, f.GetValue(null));
            w.Drain();
        }
        File.WriteAllBytes(path, ms.ToArray());
    }

    public static void Load(string path, object root, IEnumerable<Type> statics)
    {
        using var br = new BinaryReader(new MemoryStream(File.ReadAllBytes(path)));
        if (br.ReadInt32() != Magic) throw new InvalidDataException("not a save state");
        var r = new Reader { In = br };
        var fields = RootFields(root.GetType());
        var staticFields = StaticFields(statics);
        var rootValues = fields.Select(f => r.Value(f.FieldType)).ToList();
        var staticValues = staticFields.Select(f => r.Value(f.FieldType)).ToList();
        r.ReadObjects();
        for (var i = 0; i < fields.Count; i++) fields[i].SetValue(root, r.Resolve(rootValues[i]));
        for (var i = 0; i < staticFields.Count; i++) staticFields[i].SetValue(null, r.Resolve(staticValues[i]));
    }

    static List<FieldInfo> RootFields(Type t) => Fields(t).Where(f => !Skip(f.FieldType)).ToList();

    static List<FieldInfo> StaticFields(IEnumerable<Type> types) =>
        types.Where(t => !t.IsGenericTypeDefinition && t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
             .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
             .Where(f => !f.IsLiteral && !f.IsInitOnly && !Skip(f.FieldType) && f.GetCustomAttribute<ThreadStaticAttribute>() is null)
             .OrderBy(f => f.DeclaringType!.FullName).ThenBy(f => f.Name).ToList();

    static readonly Dictionary<Type, FieldInfo[]> _fields = [];
    static FieldInfo[] Fields(Type t)
    {
        if (_fields.TryGetValue(t, out var got)) return got;
        var list = new List<FieldInfo>();
        for (var b = t; b is not null && b != typeof(object) && b != typeof(ValueType); b = b.BaseType)
            list.AddRange(b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        return _fields[t] = [.. list];
    }

    /// <summary>Not written: delegates, reflection objects, streams and threads, the engine's own objects.</summary>
    static bool Skip(Type t) => typeof(Delegate).IsAssignableFrom(t) || typeof(MemberInfo).IsAssignableFrom(t) && !typeof(Type).IsAssignableFrom(t)
        || typeof(Stream).IsAssignableFrom(t) || typeof(Thread).IsAssignableFrom(t) || typeof(Task).IsAssignableFrom(t)
        || t.IsPointer || t.Namespace?.StartsWith("Godot") == true;

    sealed class Writer
    {
        public BinaryWriter Out = null!;
        readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);
        readonly Queue<object> _todo = new();
        readonly List<byte[]> _records = [];

        int Id(object o)
        {
            if (_ids.TryGetValue(o, out var id)) return id;
            id = _ids.Count; _ids[o] = id; _todo.Enqueue(o);
            return id;
        }

        /// <summary>A value of a field (or element) declared as t.</summary>
        public void Value(Type t, object? v) => Value(Out, t, v);

        void Value(BinaryWriter o, Type t, object? v)
        {
            if (t.IsPrimitive || t.IsEnum) { Prim(o, t.IsEnum ? Enum.GetUnderlyingType(t) : t, v!); return; }
            if (t.IsValueType && Nullable.GetUnderlyingType(t) is null) { foreach (var f in Fields(t)) Value(o, f.FieldType, Skip(f.FieldType) ? null : f.GetValue(v)); return; }
            o.Write(v is null || Skip(v.GetType()) ? -1 : Id(v));
        }

        /// <summary>The objects reached, each a record: kind, type, contents.</summary>
        public void Drain()
        {
            using var body = new MemoryStream();
            using var b = new BinaryWriter(body);
            while (_todo.Count > 0)
            {
                var obj = _todo.Dequeue();
                var t = obj.GetType();
                switch (obj)
                {
                    case string s: b.Write((byte)Kind.String); b.Write(s); break;
                    case Type ty: b.Write((byte)Kind.TypeName); b.Write(ty.AssemblyQualifiedName!); break;
                    case Array a:
                        b.Write((byte)Kind.Array); b.Write(t.AssemblyQualifiedName!);
                        if (a.Rank != 1) throw new NotSupportedException($"array of rank {a.Rank}: {t}");
                        b.Write(a.Length);
                        var et = t.GetElementType()!;
                        if (et.IsPrimitive) { var bytes = new byte[Buffer.ByteLength(a)]; Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length); b.Write(bytes); }
                        else foreach (var e in a) Value(b, et, e);
                        break;
                    default:
                        if (t.IsValueType) { b.Write((byte)Kind.Boxed); b.Write(t.AssemblyQualifiedName!); Value(b, t, obj); }
                        else
                        {
                            b.Write((byte)Kind.Object); b.Write(t.AssemblyQualifiedName!);
                            foreach (var f in Fields(t)) Value(b, f.FieldType, Skip(f.FieldType) ? null : f.GetValue(obj));
                        }
                        break;
                }
            }
            b.Flush();
            Out.Write(_ids.Count);
            Out.Write(body.ToArray());
        }

        static void Prim(BinaryWriter o, Type t, object v)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean: o.Write((bool)v); break;
                case TypeCode.Byte: o.Write((byte)v); break;
                case TypeCode.SByte: o.Write((sbyte)v); break;
                case TypeCode.Int16: o.Write((short)v); break;
                case TypeCode.UInt16: o.Write((ushort)v); break;
                case TypeCode.Int32: o.Write((int)v); break;
                case TypeCode.UInt32: o.Write((uint)v); break;
                case TypeCode.Int64: o.Write((long)v); break;
                case TypeCode.UInt64: o.Write((ulong)v); break;
                case TypeCode.Single: o.Write((float)v); break;
                case TypeCode.Double: o.Write((double)v); break;
                case TypeCode.Char: o.Write((ushort)(char)v); break;
                default:
                    if (t == typeof(IntPtr)) o.Write((long)(IntPtr)v);
                    else if (t == typeof(UIntPtr)) o.Write((ulong)(UIntPtr)v);
                    else throw new NotSupportedException(t.ToString());
                    break;
            }
        }
    }

    /// <summary>A reference not yet made (an object's id).</summary>
    readonly record struct Ref(int Id);

    sealed class Reader
    {
        public BinaryReader In = null!;
        object?[] _objs = [];

        /// <summary>A value as written: a primitive, a struct (boxed, its references still Refs), or a Ref.</summary>
        public object? Value(Type t) => Value(In, t);

        object? Value(BinaryReader i, Type t)
        {
            if (t.IsPrimitive || t.IsEnum)
            {
                var v = Prim(i, t.IsEnum ? Enum.GetUnderlyingType(t) : t);
                return t.IsEnum ? Enum.ToObject(t, v) : v;
            }
            if (t.IsValueType && Nullable.GetUnderlyingType(t) is null)
            {
                var box = RuntimeHelpers.GetUninitializedObject(t);
                var parts = Fields(t).Select(f => (f, v: Value(i, f.FieldType))).ToList();
                return new Boxed(box, parts);
            }
            var id = i.ReadInt32();
            return id < 0 ? null : new Ref(id);
        }

        sealed record Boxed(object Box, List<(FieldInfo F, object? V)> Parts);

        public object? Resolve(object? v) => v switch
        {
            Ref r => _objs[r.Id],
            Boxed b => Fill(b),
            _ => v,
        };

        object Fill(Boxed b)
        {
            foreach (var (f, v) in b.Parts) if (!Skip(f.FieldType)) f.SetValue(b.Box, Resolve(v));
            return b.Box;
        }

        public void ReadObjects()
        {
            var count = In.ReadInt32();
            _objs = new object?[count];
            var pending = new List<(object Obj, List<(FieldInfo F, object? V)>? Fields, Array? Arr, List<object?>? Elems)>();
            for (var n = 0; n < count; n++)
            {
                var kind = (Kind)In.ReadByte();
                switch (kind)
                {
                    case Kind.String: _objs[n] = In.ReadString(); break;
                    case Kind.TypeName: _objs[n] = Type.GetType(In.ReadString(), true); break;
                    case Kind.Array:
                    {
                        var t = Type.GetType(In.ReadString(), true)!;
                        var len = In.ReadInt32();
                        var et = t.GetElementType()!;
                        var a = Array.CreateInstance(et, len);
                        _objs[n] = a;
                        if (et.IsPrimitive) { var bytes = In.ReadBytes(Buffer.ByteLength(a)); Buffer.BlockCopy(bytes, 0, a, 0, bytes.Length); }
                        else { var elems = new List<object?>(len); for (var k = 0; k < len; k++) elems.Add(Value(In, et)); pending.Add((a, null, a, elems)); }
                        break;
                    }
                    case Kind.Boxed:
                    {
                        var t = Type.GetType(In.ReadString(), true)!;
                        var v = Value(In, t);
                        if (v is Boxed bx) { _objs[n] = bx.Box; pending.Add((bx.Box, bx.Parts, null, null)); }
                        else _objs[n] = v;                                       // a number or an enum value
                        break;
                    }
                    case Kind.Object:
                    {
                        var t = Type.GetType(In.ReadString(), true)!;
                        var o = RuntimeHelpers.GetUninitializedObject(t);
                        _objs[n] = o;
                        pending.Add((o, Fields(t).Select(f => (f, Value(In, f.FieldType))).ToList(), null, null));
                        break;
                    }
                    default: throw new InvalidDataException($"record kind {kind}");
                }
            }
            foreach (var (obj, fields, arr, elems) in pending)
            {
                if (arr is not null) for (var k = 0; k < elems!.Count; k++) arr.SetValue(Resolve(elems[k]), k);
                else foreach (var (f, v) in fields!) if (!Skip(f.FieldType)) f.SetValue(obj, Resolve(v));
            }
            foreach (var o in _objs) if (o is not null) Refile(o);
        }

        /// <summary>A hashed table (Dictionary, HashSet) files its entries by their hash codes, and a loaded object's (or a
        /// string's, this run) differ from the saved run's: emptied and filled again in its own order (the order it is gone
        /// through stays the same).</summary>
        static void Refile(object o)
        {
            var t = o.GetType();
            if (!t.IsGenericType) return;
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(Dictionary<,>) && o is System.Collections.IDictionary d)
            {
                var entries = new List<(object K, object? V)>();
                foreach (System.Collections.DictionaryEntry e in d) entries.Add((e.Key, e.Value));
                d.Clear();
                foreach (var (k, v) in entries) d.Add(k, v);
            }
            else if (def == typeof(HashSet<>) && o is System.Collections.IEnumerable set)
            {
                var items = set.Cast<object?>().ToList();
                t.GetMethod("Clear")!.Invoke(o, null);
                var add = t.GetMethod("Add")!;
                foreach (var i in items) add.Invoke(o, [i]);
            }
        }

        static object Prim(BinaryReader i, Type t) => Type.GetTypeCode(t) switch
        {
            TypeCode.Boolean => i.ReadBoolean(), TypeCode.Byte => i.ReadByte(), TypeCode.SByte => i.ReadSByte(),
            TypeCode.Int16 => i.ReadInt16(), TypeCode.UInt16 => i.ReadUInt16(), TypeCode.Int32 => i.ReadInt32(),
            TypeCode.UInt32 => i.ReadUInt32(), TypeCode.Int64 => i.ReadInt64(), TypeCode.UInt64 => i.ReadUInt64(),
            TypeCode.Single => i.ReadSingle(), TypeCode.Double => i.ReadDouble(), TypeCode.Char => (char)i.ReadUInt16(),
            _ => t == typeof(IntPtr) ? (IntPtr)i.ReadInt64() : t == typeof(UIntPtr) ? (UIntPtr)i.ReadUInt64() : throw new NotSupportedException(t.ToString()),
        };
    }
}
