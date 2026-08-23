using System.Numerics;

namespace TextureCreator.Core;

public static class UvServices
{
    public static IReadOnlyList<SeamPair> FindSeams(MeshData mesh)
    {
        var edges = new Dictionary<(int, int), List<(Vector2, Vector2)>>();
        for (var t = 0; t < mesh.Indices.Count; t += 3) for (var e = 0; e < 3; e++)
        {
            var va = mesh.Vertices[mesh.Indices[t + e]]; var vb = mesh.Vertices[mesh.Indices[t + (e + 1) % 3]];
            if (va.TexCoord < 0 || vb.TexCoord < 0) continue;
            var key = va.Position < vb.Position ? (va.Position, vb.Position) : (vb.Position, va.Position);
            var uv = va.Position < vb.Position ? (mesh.TexCoords[va.TexCoord], mesh.TexCoords[vb.TexCoord]) : (mesh.TexCoords[vb.TexCoord], mesh.TexCoords[va.TexCoord]);
            if (!edges.TryGetValue(key, out var list)) edges[key] = list = []; list.Add(uv);
        }
        var seams = new List<SeamPair>();
        foreach (var pair in edges.Values.Where(x => x.Count == 2)) if (Vector2.DistanceSquared(pair[0].Item1, pair[1].Item1) > 1e-8 || Vector2.DistanceSquared(pair[0].Item2, pair[1].Item2) > 1e-8) seams.Add(new(pair[0].Item1, pair[0].Item2, pair[1].Item1, pair[1].Item2));
        return seams;
    }

    public static ImageBuffer RasterizeUvCoverage(MeshData mesh, int size)
    {
        if (!mesh.HasUvs) throw new InvalidOperationException("Model has no complete UV set.");
        var image = new ImageBuffer(size, size);
        for (var t = 0; t < mesh.Indices.Count; t += 3)
        {
            var a = mesh.TexCoords[mesh.Vertices[mesh.Indices[t]].TexCoord] * (size - 1); var b = mesh.TexCoords[mesh.Vertices[mesh.Indices[t + 1]].TexCoord] * (size - 1); var c = mesh.TexCoords[mesh.Vertices[mesh.Indices[t + 2]].TexCoord] * (size - 1);
            var minX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, size - 1); var maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, size - 1);
            var minY = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0, size - 1); var maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, size - 1);
            for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++) if (Inside(new(x + .5f, y + .5f), a, b, c)) { var p = image.Offset(x, y); image.Pixels[p] = 72; image.Pixels[p + 1] = 210; image.Pixels[p + 2] = 150; image.Pixels[p + 3] = 255; }
        }
        return image;
    }
    private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c) { static float C(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y); var d1 = C(p, a, b); var d2 = C(p, b, c); var d3 = C(p, c, a); return !(d1 < 0 || d2 < 0 || d3 < 0) || !(d1 > 0 || d2 > 0 || d3 > 0); }

    public static void Dilate(ImageBuffer image, int pixels)
    {
        for (var pass = 0; pass < pixels; pass++) { var src = (byte[])image.Pixels.Clone(); for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++) { var p = image.Offset(x, y); if (src[p + 3] != 0) continue; for (var yy = Math.Max(0, y - 1); yy <= Math.Min(image.Height - 1, y + 1); yy++) for (var xx = Math.Max(0, x - 1); xx <= Math.Min(image.Width - 1, x + 1); xx++) { var q = (yy * image.Width + xx) * 4; if (src[q + 3] == 0) continue; Array.Copy(src, q, image.Pixels, p, 4); goto filled; } filled:; } }
    }
}

