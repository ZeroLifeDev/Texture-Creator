using System.Numerics;

namespace TextureCreator.Core;

public sealed record ProjectionResult(ImageBuffer Surface, ImageBuffer Coverage, int ObservedTexels, int WeakTexels);

public sealed class ProjectionEngine
{
    public ProjectionResult Project(MeshData mesh, IReadOnlyList<(ReferenceImage Reference, ImageBuffer Image)> sources, int size, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!mesh.HasUvs) throw new InvalidOperationException("Projection requires a complete UV set.");
        if (sources.Count == 0) throw new ArgumentException("At least one reference is required.", nameof(sources));
        if (size is < 64 or > 8192) throw new ArgumentOutOfRangeException(nameof(size));
        var count = checked(size * size); var rr = new float[count]; var gg = new float[count]; var bb = new float[count]; var weights = new float[count];
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue); foreach (var p in mesh.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        var diagonal = max - min; if (diagonal.LengthSquared() < 1e-10f) throw new InvalidDataException("Model bounds are degenerate.");
        for (var si = 0; si < sources.Count; si++)
        {
            ct.ThrowIfCancellationRequested(); var (reference, image) = sources[si]; var role = reference.Role == ReferenceRole.Custom ? ReferenceRole.Front : reference.Role;
            for (var t = 0; t < mesh.Indices.Count; t += 3)
            {
                var va = mesh.Vertices[mesh.Indices[t]]; var vb = mesh.Vertices[mesh.Indices[t + 1]]; var vc = mesh.Vertices[mesh.Indices[t + 2]];
                var pa = Transform(mesh.Positions[va.Position], reference.Alignment); var pb = Transform(mesh.Positions[vb.Position], reference.Alignment); var pc = Transform(mesh.Positions[vc.Position], reference.Alignment);
                var normal = Vector3.Normalize(Vector3.Cross(pb - pa, pc - pa)); var facing = MathF.Abs(Vector3.Dot(normal, ViewDirection(role))); if (float.IsNaN(facing) || facing < .08f) continue;
                var ua = mesh.TexCoords[va.TexCoord] * (size - 1); var ub = mesh.TexCoords[vb.TexCoord] * (size - 1); var uc = mesh.TexCoords[vc.TexCoord] * (size - 1);
                var sa = ProjectPoint(pa, min, max, role); var sb = ProjectPoint(pb, min, max, role); var sc = ProjectPoint(pc, min, max, role);
                Rasterize(ua, ub, uc, (x, y, bary) =>
                {
                    var suv = sa * bary.X + sb * bary.Y + sc * bary.Z; if (suv.X < 0 || suv.X > 1 || suv.Y < 0 || suv.Y > 1) return;
                    var sx = Math.Clamp((int)(suv.X * (image.Width - 1)), 0, image.Width - 1); var sy = Math.Clamp((int)(suv.Y * (image.Height - 1)), 0, image.Height - 1); var sp = image.Offset(sx, sy); var dp = y * size + x;
                    var edgeConfidence = Math.Clamp(facing * facing * Math.Max(.01f, reference.Priority), .01f, 4f); rr[dp] += image.Pixels[sp] * edgeConfidence; gg[dp] += image.Pixels[sp + 1] * edgeConfidence; bb[dp] += image.Pixels[sp + 2] * edgeConfidence; weights[dp] += edgeConfidence;
                }, size);
            }
            progress?.Report((si + 1d) / sources.Count);
        }
        var surface = new ImageBuffer(size, size); var coverage = new ImageBuffer(size, size); var observed = 0; var weak = 0;
        for (var i = 0; i < count; i++)
        {
            var p = i * 4; var w = weights[i];
            if (w > 0) { surface.Pixels[p] = (byte)Math.Clamp(rr[i] / w, 0, 255); surface.Pixels[p + 1] = (byte)Math.Clamp(gg[i] / w, 0, 255); surface.Pixels[p + 2] = (byte)Math.Clamp(bb[i] / w, 0, 255); surface.Pixels[p + 3] = 255; observed++; }
            coverage.Pixels[p + 3] = 255;
            if (w >= .45f) { coverage.Pixels[p] = 52; coverage.Pixels[p + 1] = 205; coverage.Pixels[p + 2] = 137; }
            else if (w > 0) { coverage.Pixels[p] = 239; coverage.Pixels[p + 1] = 178; coverage.Pixels[p + 2] = 67; weak++; }
            else { coverage.Pixels[p] = 184; coverage.Pixels[p + 1] = 65; coverage.Pixels[p + 2] = 74; }
        }
        UvServices.Dilate(surface, Math.Clamp(size / 128, 4, 32));
        return new(surface, coverage, observed, weak);
    }

    private static Vector3 Transform(Vector3 p, CameraAlignment a)
    {
        var r = Matrix4x4.CreateFromYawPitchRoll(a.Rotation.Y * MathF.PI / 180, a.Rotation.X * MathF.PI / 180, a.Rotation.Z * MathF.PI / 180);
        return Vector3.Transform(p * a.Scale, r) + a.Translation;
    }
    private static Vector3 ViewDirection(ReferenceRole role) => role switch { ReferenceRole.Back => -Vector3.UnitZ, ReferenceRole.Left => -Vector3.UnitX, ReferenceRole.Right => Vector3.UnitX, ReferenceRole.Top => Vector3.UnitY, ReferenceRole.Bottom => -Vector3.UnitY, _ => Vector3.UnitZ };
    private static Vector2 ProjectPoint(Vector3 p, Vector3 min, Vector3 max, ReferenceRole role)
    {
        static float N(float v, float a, float b) => MathF.Abs(b - a) < 1e-8f ? .5f : (v - a) / (b - a);
        return role switch
        {
            ReferenceRole.Back => new(1 - N(p.X, min.X, max.X), 1 - N(p.Y, min.Y, max.Y)),
            ReferenceRole.Left => new(N(p.Z, min.Z, max.Z), 1 - N(p.Y, min.Y, max.Y)),
            ReferenceRole.Right => new(1 - N(p.Z, min.Z, max.Z), 1 - N(p.Y, min.Y, max.Y)),
            ReferenceRole.Top => new(N(p.X, min.X, max.X), N(p.Z, min.Z, max.Z)),
            ReferenceRole.Bottom => new(N(p.X, min.X, max.X), 1 - N(p.Z, min.Z, max.Z)),
            _ => new(N(p.X, min.X, max.X), 1 - N(p.Y, min.Y, max.Y))
        };
    }
    private static void Rasterize(Vector2 a, Vector2 b, Vector2 c, Action<int, int, Vector3> pixel, int size)
    {
        var minX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, size - 1); var maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, size - 1); var minY = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0, size - 1); var maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, size - 1);
        var denom = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y); if (MathF.Abs(denom) < 1e-8f) return;
        for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++) { var px = x + .5f; var py = y + .5f; var u = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / denom; var v = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / denom; var w = 1 - u - v; if (u >= 0 && v >= 0 && w >= 0) pixel(x, y, new(u, v, w)); }
    }
}

