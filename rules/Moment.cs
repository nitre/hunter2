using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Hunter.Rules;

/// <summary>DEV TOOL, NOT THE ORIGINAL (the game and its code are deterministic, so every byte can be tracked): a moment of play kept whole - the game and whatever plays it
/// (Play.Pilot: the bot and its memory) - copied between passes, every object and byte, so a test starts at any moment of a
/// run instead of playing up to it. The game data (GameData: read only) is shared, not copied. Diff lists where two moments
/// differ, byte by byte, by path - the proof that a copy runs on as the original does, and where two runs part.
/// Statics are not copied: the ones that change (Collisions' pair, Ground's scratch, the Navigate / Trip caches) are a
/// pass's scratch or rebuilt from the map (a probe listed them).</summary>
public static class Moment
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>A copy of the play as it stands - call between passes.</summary>
    public static Play Copy(Play play) => (Play)new Copier(play).Of(play)!;

    static bool Shared(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || typeof(Type).IsAssignableFrom(t)
        || typeof(MemberInfo).IsAssignableFrom(t) || t == typeof(GameData);

    static IEnumerable<FieldInfo> AllFields(Type t)
    {
        for (var tt = t; tt is not null && tt != typeof(object) && tt != typeof(ValueType); tt = tt.BaseType)
            foreach (var f in tt.GetFields(Fields)) yield return f;
    }

    static readonly Dictionary<Type, bool> plain = [];
    /// <summary>A value type with no reference anywhere in it: copied as it is.</summary>
    static bool Plain(Type t)
    {
        if (Shared(t)) return true;
        if (!t.IsValueType) return false;
        lock (plain)
        {
            if (plain.TryGetValue(t, out var p)) return p;
            plain[t] = true;                                                 // a struct cannot hold itself
            return plain[t] = AllFields(t).All(f => Plain(f.FieldType));
        }
    }

    sealed class Copier(Play play)
    {
        readonly Dictionary<object, object> done = new(ReferenceEqualityComparer.Instance);
        readonly GameData data = play.Data;

        public object? Of(object? o)
        {
            if (o is null) return null;
            var t = o.GetType();
            if (Plain(t) || ReferenceEquals(o, data)) return o;
            if (!t.IsValueType && done.TryGetValue(o, out var had)) return had;
            if (o is Delegate d) return Remake(d);
            if (o is Array a) return OfArray(a);
            if (!InPlay(o) && (o is IEqualityComparer || o is IComparer || t.GetInterfaces().Any(i => i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(IEqualityComparer<>) || i.GetGenericTypeDefinition() == typeof(IComparer<>)))))
                return o;                                                    // .NET's own comparers: shared (some are known by identity)
            if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(Dictionary<,>) || t.GetGenericTypeDefinition() == typeof(HashSet<>))
                && !Plain(t.GetGenericArguments()[0]))
                return OfSet(o, t);                                          // keyed by objects: their hashes are each object's own - rebuilt
                                                                             // by Add; keyed by values: copied whole, its free slots too (the
                                                                             // same order of reuse, so the same order of a foreach later)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ConditionalWeakTable<,>))
                throw new InvalidOperationException($"Moment: a {t.Name} in the play cannot be copied");
            var c = RuntimeHelpers.GetUninitializedObject(t);
            if (!t.IsValueType) done[o] = c;
            foreach (var f in AllFields(t)) f.SetValue(c, Of(f.GetValue(o)));
            return c;
        }

        Array OfArray(Array a)
        {
            var et = a.GetType().GetElementType()!;
            if (Plain(et)) { var pc = (Array)a.Clone(); done[a] = pc; return pc; }
            var lengths = Enumerable.Range(0, a.Rank).Select(a.GetLength).ToArray();
            var c = Array.CreateInstance(et, lengths);
            done[a] = c;
            if (a.Rank == 1) for (var i = 0; i < a.Length; i++) c.SetValue(Of(a.GetValue(i)), i);
            else
            {
                var idx = new int[a.Rank];
                for (var n = 0; n < a.Length; n++)
                {
                    var rest = n;
                    for (var k = a.Rank - 1; k >= 0; k--) { idx[k] = rest % lengths[k]; rest /= lengths[k]; }
                    c.SetValue(Of(a.GetValue(idx)), idx);
                }
            }
            return c;
        }

        object OfSet(object o, Type t)
        {
            var comparer = Of(t.GetProperty("Comparer")!.GetValue(o));
            var c = Activator.CreateInstance(t, comparer)!;
            done[o] = c;
            if (o is IDictionary dict) foreach (DictionaryEntry e in dict) ((IDictionary)c).Add(Of(e.Key)!, Of(e.Value));
            else
            {
                var add = t.GetMethod("Add")!;
                foreach (var e in (IEnumerable)o) add.Invoke(c, [Of(e)]);
            }
            return c;
        }

        /// <summary>A delegate bound to something in the play: bound to its copy (a hook from outside it stays as it was).</summary>
        Delegate? Remake(Delegate d)
        {
            var parts = d.GetInvocationList().Select(p =>
                p.Target is null ? p : Delegate.CreateDelegate(p.GetType(), done.TryGetValue(p.Target, out var tc) ? tc : InPlay(p.Target) ? Of(p.Target)! : p.Target, p.Method));
            return Delegate.Combine(parts.ToArray());
        }

        /// <summary>The compiler's closure classes and the game's own types are part of the play; anything else (the host's
        /// sound, a window) is not.</summary>
        static bool InPlay(object o) => o.GetType().Assembly == typeof(Play).Assembly
            || o.GetType().GetGenericArguments().Any(g => g.Assembly == typeof(Play).Assembly);
    }

    /// <summary>Where two moments differ: the paths (play.State.Entities.All[3].B[74] ...) whose values are not the same, the
    /// first `most`. Empty: the same to the byte.</summary>
    public static List<string> Diff(object a, object b, int most = 20)
    {
        var found = new List<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        GameData? data = (a as Play)?.Data;
        void Walk(object? x, object? y, string path)
        {
            if (found.Count >= most) return;
            if (x is null || y is null) { if (x is null != y is null) found.Add($"{path}: {(x is null ? "null" : "set")} / {(y is null ? "null" : "set")}"); return; }
            var t = x.GetType();
            if (t != y.GetType()) { found.Add($"{path}: {t.Name} / {y.GetType().Name}"); return; }
            if (ReferenceEquals(x, data) || x is Delegate || typeof(Type).IsAssignableFrom(t) || typeof(MemberInfo).IsAssignableFrom(t)) return;
            if (t.IsPrimitive || t.IsEnum || x is string || x is decimal) { if (!x.Equals(y)) found.Add($"{path}: {x} / {y}"); return; }
            if (!t.IsValueType && !seen.Add(x)) return;
            if (x is Array xa)
            {
                var ya = (Array)y;
                if (xa.Length != ya.Length) { found.Add($"{path}: length {xa.Length} / {ya.Length}"); return; }
                if (x is byte[] xb) { var yb = (byte[])y; for (var i = 0; i < xb.Length && found.Count < most; i++) if (xb[i] != yb[i]) found.Add($"{path}[{i}]: {xb[i]} / {yb[i]}"); return; }
                var n = 0;
                foreach (var (ex, ey) in xa.Cast<object?>().Zip(ya.Cast<object?>())) Walk(ex, ey, $"{path}[{n++}]");
                return;
            }
            if (x is IDictionary dx)
            {
                var dy = (IDictionary)y;
                if (dx.Count != dy.Count) { found.Add($"{path}: count {dx.Count} / {dy.Count}"); return; }
                static List<DictionaryEntry> Entries(IDictionary d) { var l = new List<DictionaryEntry>(); var e = d.GetEnumerator(); while (e.MoveNext()) l.Add(e.Entry); return l; }
                var n = 0;
                foreach (var (ex, ey) in Entries(dx).Zip(Entries(dy)))
                { Walk(ex.Key, ey.Key, $"{path}{{{n}}}.key"); Walk(ex.Value, ey.Value, $"{path}{{{n}}}"); n++; }
                return;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                var n = 0;
                foreach (var (ex, ey) in ((IEnumerable)x).Cast<object?>().Zip(((IEnumerable)y).Cast<object?>())) Walk(ex, ey, $"{path}{{{n++}}}");
                return;
            }
            foreach (var f in AllFields(t)) Walk(f.GetValue(x), f.GetValue(y), $"{path}.{f.Name}");
        }
        Walk(a, b, "play");
        return found;
    }
}
