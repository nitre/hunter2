namespace Hunter.Rules;

/// <summary>(renderer = 3d): a face's corners in order round it. A face lists
/// EDGE numbers; each edge is a pair of corner numbers at edgesAt + 2. From edge 0, take the next unused edge
/// that shares the end corner, until back at the start. A repeated edge is never walked twice. 12 of objfile's
/// 2584 filled faces are open chains (8 of them a single edge): the 3D view closes the gap / draws the line;
/// how the original's fill draws those 12 is not checked yet.</summary>
public static class Outline
{
    /// <summary>Fills loop with the corner numbers in chain order; true when the outline closed.</summary>
    public static bool Of(byte[] buf, int edgesAt, ReadOnlySpan<byte> edges, List<int> loop)
    {
        loop.Clear();
        if (edges.Length == 0) return false;
        var pa = new int[edges.Length];
        var pb = new int[edges.Length];
        for (var k = 0; k < edges.Length; k++)
        {
            pa[k] = buf[edgesAt + 2 + edges[k] * 2];
            pb[k] = buf[edgesAt + 2 + edges[k] * 2 + 1];
        }
        int A(int k) => pa[k];
        int B(int k) => pb[k];
        Span<bool> used = stackalloc bool[edges.Length];
        used[0] = true;
        loop.Add(A(0));
        var end = B(0);
        while (end != loop[0])
        {
            var found = -1;
            for (var k = 0; k < edges.Length && found < 0; k++)
            {
                if (used[k] || (A(k) != end && B(k) != end)) continue;
                var dup = false;                             // the same two corners as an edge already walked
                for (var j = 0; j < edges.Length && !dup; j++)
                    dup = used[j] && Math.Min(A(j), B(j)) == Math.Min(A(k), B(k)) && Math.Max(A(j), B(j)) == Math.Max(A(k), B(k));
                if (dup) { used[k] = true; continue; }
                found = k;
            }
            if (found < 0) break;
            used[found] = true;
            loop.Add(end);
            end = A(found) == end ? B(found) : A(found);
        }
        if (end == loop[0]) return true;
        loop.Add(end);                                      // open: grow the start end too (edge 0 may sit mid-chain)
        for (var grew = true; grew;)
        {
            grew = false;
            for (var k = 0; k < edges.Length && !grew; k++)
                if (!used[k] && (A(k) == loop[0] || B(k) == loop[0]))
                {
                    used[k] = grew = true;
                    loop.Insert(0, A(k) == loop[0] ? B(k) : A(k));
                }
        }
        return false;
    }
}
