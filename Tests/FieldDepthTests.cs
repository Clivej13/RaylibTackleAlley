using System.Numerics;
using Raylib_cs;
using Xunit;
using Xunit.Abstractions;

public sealed class FieldDepthTests(ITestOutputHelper output)
{
    private static List<Vector2> Clip(List<Vector2> polygon, int axis, float boundary, bool greater)
    {
        var result = new List<Vector2>();
        if (polygon.Count == 0) return result;
        float Distance(Vector2 p) => (axis == 0 ? p.X : p.Y) - boundary;
        Vector2 previous = polygon[^1];
        float previousDistance = Distance(previous);
        foreach (var current in polygon)
        {
            float distance = Distance(current);
            bool inside = greater ? distance >= 0 : distance <= 0;
            bool previousInside = greater ? previousDistance >= 0 : previousDistance <= 0;
            if (inside != previousInside)
                result.Add(Vector2.Lerp(previous, current, previousDistance / (previousDistance - distance)));
            if (inside) result.Add(current);
            previous = current; previousDistance = distance;
        }
        return result;
    }

    [Fact]
    public unsafe void StadiumFloorLeavesFieldOpeningAndDepthProbeHasNoLargeOrderDependentPatches()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(960, 540, "Field depth validation");
        try
        {

            foreach (string name in new[] { "football_field", "stadium" })
            {
                Model model = Raylib.LoadModel(Path.Combine(AppContext.BaseDirectory, "Assets/Models", name + ".glb"));
                try
                {
                    var b = Raylib.GetModelBoundingBox(model);
                    output.WriteLine($"{name}: {model.MeshCount} meshes bounds {b.Min} .. {b.Max}, transform {model.Transform}");
                    for (int m = 0; m < model.MeshCount; m++)
                    {
                        Mesh mesh = model.Meshes[m];
                        var bounds = Raylib.GetMeshBoundingBox(mesh);
                        if (name == "stadium")
                        for (int t = 0; t < mesh.TriangleCount; t++)
                        {
                            Vector3 Vertex(int corner)
                            {
                                int index = mesh.Indices == null ? t * 3 + corner : mesh.Indices[t * 3 + corner];
                                return new(mesh.Vertices[index * 3], mesh.Vertices[index * 3 + 1], mesh.Vertices[index * 3 + 2]);
                            }
                            var vertices = new[] { Vertex(0), Vertex(1), Vertex(2) };
                            if (vertices.All(v => v.Y <= .05f))
                            {
                                var polygon = vertices.Select(v => new Vector2(v.X, v.Z)).ToList();
                                polygon = Clip(polygon, 0, -26.6664f, true);
                                polygon = Clip(polygon, 0, 26.6664f, false);
                                polygon = Clip(polygon, 1, -59.9999f, true);
                                polygon = Clip(polygon, 1, 59.9999f, false);
                                float area = 0;
                                for (int i = 0; i < polygon.Count; i++)
                                {
                                    var p = polygon[i]; var q = polygon[(i + 1) % polygon.Count];
                                    area += p.X * q.Y - q.X * p.Y;
                                }
                                Assert.True(Math.Abs(area) < .0001f, $"Stadium mesh {m}, triangle {t} overlaps the field: area {Math.Abs(area) / 2}");
                            }
                        }
                    }
                }
                finally { Raylib.UnloadModel(model); }
            }
            output.WriteLine($"Clip planes: {Rlgl.GetCullDistanceNear()} .. {Rlgl.GetCullDistanceFar()}");
            Model field = Raylib.LoadModel(Path.Combine(AppContext.BaseDirectory, "Assets/Models/football_field.glb"));
            Model stadium = Raylib.LoadModel(Path.Combine(AppContext.BaseDirectory, "Assets/Models/stadium.glb"));
            double originalNear = Rlgl.GetCullDistanceNear();
            double originalFar = Rlgl.GetCullDistanceFar();
            try
            {
                byte[] Capture(float height, bool reverse, float cameraZ, bool overlays, bool floor, float cameraX, int direction)
                {
                    Raylib.BeginDrawing();
                    Raylib.ClearBackground(Color.Magenta);
                    Raylib.BeginMode3D(new Camera3D { Position = new(cameraX, 5.5f, cameraZ), Target = new(cameraX, 1.1f, cameraZ + direction * 12.25f), Up = Vector3.UnitY, FovY = 55, Projection = CameraProjection.Perspective });
                    void Field() => Raylib.DrawModel(field, new(0, height, 0), 1, Color.White);
                    void Stadium()
                    {
                        for (int i = 0; i < stadium.MeshCount; i++)
                        {
                            int m = reverse ? stadium.MeshCount - 1 - i : i;
                            if (!floor && Raylib.GetMeshBoundingBox(stadium.Meshes[m]).Max.Y <= .05f) continue;
                            Raylib.DrawMesh(stadium.Meshes[m], stadium.Materials[stadium.MeshMaterial[m]], Matrix4x4.Identity);
                        }
                    }
                    if (reverse) { Stadium(); Field(); } else { Field(); Stadium(); }
                    if (overlays)
                    {
                        // Opaque diagnostic colours distinguish depth conflicts from expected alpha-order differences.
                        void Strip() => Raylib.DrawPlane(new(19.33325f, .025f, 0), new(14.6665f, 120), Color.Red);
                        void Glow() => Raylib.DrawPlane(new(12.9f, .035f, 0), new(1.8f, 120), Color.Yellow);
                        if (reverse) { Glow(); Strip(); } else { Strip(); Glow(); }
                    }
                    Raylib.EndMode3D();
                    Image image = Raylib.LoadImageFromScreen();
                    Raylib.EndDrawing();
                    var bytes = new byte[960 * 540 * 4];
                    var pixels = Raylib.LoadImageColors(image);
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)pixels, bytes, 0, bytes.Length);
                    Raylib.UnloadImageColors(pixels);
                    Raylib.UnloadImage(image);
                    return bytes;
                }
                foreach (double near in new[] { .05, .1 })
                {
                    Rlgl.SetClipPlanes(near, 4000);
                    foreach (float cameraZ in new[] { 36.75f, 0f, -35f })
                    foreach (float cameraX in new[] { -24f, 0f, 24f })
                    foreach (int direction in new[] { -1, 1 })
                    foreach (var scenario in new[] { (0f, false, true), (.1f, false, true), (0f, true, true), (0f, true, false) })
                    {
                        var a = Capture(scenario.Item1, false, cameraZ, scenario.Item2, scenario.Item3, cameraX, direction);
                        var b = Capture(scenario.Item1, true, cameraZ, scenario.Item2, scenario.Item3, cameraX, direction);
                        Assert.True(a.Distinct().Count() > 64, "Probe must capture a rendered scene, not an empty buffer.");
                        int changed = Enumerable.Range(0, 960 * 540).Count(p => Enumerable.Range(0, 3).Any(c => Math.Abs(a[p*4+c] - b[p*4+c]) > 8));
                        Assert.True(changed < 64, $"Large depth-order difference: {changed} pixels.");
                        output.WriteLine($"near={near}, camera=({cameraX},{cameraZ}), direction={direction}, scenario={scenario}: draw-order-dependent pixels={changed}");
                    }
                }
            }
            finally { Raylib.UnloadModel(field); Raylib.UnloadModel(stadium); Rlgl.SetClipPlanes(originalNear, originalFar); }
        }
        finally { Raylib.CloseWindow(); }
    }
}
