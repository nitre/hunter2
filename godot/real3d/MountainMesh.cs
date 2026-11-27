using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace Hunter;

/// <summary>STUNTIN': Stunt Car Racer's mountain horizon (renderMountainHorizon; game_data/scr/mountains.json from the
/// original disk) as one mesh for the card, drawn by mountains.gdshader round the eye: it
/// turns with the view but never comes nearer, as a horizon. The original's ring: 32 mountains, each at its angle (256 to a
/// turn, as the game's headings) and a shape - a quad (or faces) in screen units from its base's left end, x to the right,
/// y up, a mountain's point at angle + x / 32 (the original's screen x = (32 x (angle - view) + x) / 4 + $80 at 8 screen
/// units to an angle step) and y / 4 screen units above the horizon, the same scale. One set for every track.</summary>
public static class MountainMesh
{
    /// <summary>The ring's radius, world units: far beyond any land (the map is 65536 across), inside the far plane.</summary>
    public const double Radius = 80000;

    public static MeshInstance3D? Make(string dataDir, ShaderMaterial material)
    {
        var path = System.IO.Path.Combine(dataDir, "scr", "mountains.json");
        if (!System.IO.File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var shapes = doc.RootElement.GetProperty("shapes");
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        const double step = System.Math.Tau / 256;                          // an angle step
        foreach (var m in doc.RootElement.GetProperty("ring").EnumerateArray())
        {
            int angle = m[0].GetInt32(), shape = m[1].GetInt32();
            var sh = shapes.GetProperty(shape.ToString());
            var pts = new List<Vector3>();
            foreach (var p in sh.GetProperty("points").EnumerateArray())
            {
                double x = p[0].GetDouble(), y = p[1].GetDouble();
                var heading = (angle + x / 32) * step;                       // 0 toward +z (north), a quarter toward +x
                var rise = y / 4 * (step / 8);                                // screen units at 8 to an angle step
                // world (x, height, -z): heights drawn half size (the game's 128 against 256), so doubled to keep the angle
                pts.Add(new Vector3((float)(System.Math.Sin(heading) * Radius), (float)(2 * Radius * System.Math.Tan(rise)), (float)(-System.Math.Cos(heading) * Radius)));
            }
            foreach (var face in sh.GetProperty("faces").EnumerateArray())
            {
                var word = face.GetProperty("colour").GetInt32();
                var idx = new List<int>();
                foreach (var i in face.GetProperty("points").EnumerateArray()) idx.Add(i.GetInt32());
                for (var k = 1; k + 1 < idx.Count; k++)                       // a fan: the faces are convex
                {
                    v.Add(pts[idx[0]]); v.Add(pts[idx[k]]); v.Add(pts[idx[k + 1]]);
                    for (var j = 0; j < 3; j++) uv.Add(new Vector2(word, 1));
                }
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CustomAabb = GpuWorld.Huge };
    }
}
