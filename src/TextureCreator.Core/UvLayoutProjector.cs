namespace TextureCreator.Core;

public sealed class UvLayoutProjector
{
    public ProjectionResult Project(ImageBuffer uvLayout, ImageBuffer reference, int size, CancellationToken ct = default)
    {
        if (size is < 64 or > 8192) throw new ArgumentOutOfRangeException(nameof(size));
        var layout = Resize(uvLayout, size, size); var texture = ResizeCover(reference, size, size); var boundary = DetectBoundary(layout); var exterior = FloodExterior(boundary, size, ct); var mask = new bool[size * size]; var observed = 0;
        for (var i = 0; i < mask.Length; i++) { mask[i] = !exterior[i]; if (mask[i]) observed++; }
        var fallback = observed < mask.Length / 100 || observed > mask.Length * .97;
        if (fallback) { Array.Fill(mask, true); observed = mask.Length; }
        var surface = new ImageBuffer(size, size); var coverage = new ImageBuffer(size, size);
        for (var i = 0; i < mask.Length; i++)
        {
            var p = i * 4; coverage.Pixels[p + 3] = 255;
            if (mask[i]) { Array.Copy(texture.Pixels, p, surface.Pixels, p, 4); surface.Pixels[p + 3] = 255; coverage.Pixels[p] = fallback ? (byte)230 : (byte)52; coverage.Pixels[p + 1] = fallback ? (byte)174 : (byte)205; coverage.Pixels[p + 2] = fallback ? (byte)62 : (byte)137; }
            else { coverage.Pixels[p] = 184; coverage.Pixels[p + 1] = 65; coverage.Pixels[p + 2] = 74; }
        }
        UvServices.Dilate(surface, Math.Clamp(size / 128, 4, 32));
        return new(surface, coverage, observed, fallback ? observed : 0);
    }

    private static bool[] DetectBoundary(ImageBuffer image)
    {
        var count = image.Width * image.Height; var result = new bool[count]; byte minA = 255, maxA = 0;
        for (var i = 0; i < count; i++) { var a = image.Pixels[i * 4 + 3]; minA = Math.Min(minA, a); maxA = Math.Max(maxA, a); }
        var alphaLayout = maxA - minA > 48;
        var corners = new[] { 0, image.Width - 1, (image.Height - 1) * image.Width, count - 1 }; var bgR = corners.Average(i => image.Pixels[i * 4]); var bgG = corners.Average(i => image.Pixels[i * 4 + 1]); var bgB = corners.Average(i => image.Pixels[i * 4 + 2]);
        for (var i = 0; i < count; i++)
        {
            var p = i * 4; if (alphaLayout) result[i] = image.Pixels[p + 3] > minA + (maxA - minA) * .35;
            else { var dr = image.Pixels[p] - bgR; var dg = image.Pixels[p + 1] - bgG; var db = image.Pixels[p + 2] - bgB; result[i] = dr * dr + dg * dg + db * db > 42 * 42; }
        }
        return result;
    }

    private static bool[] FloodExterior(bool[] boundary, int size, CancellationToken ct)
    {
        var outside = new bool[boundary.Length]; var queue = new Queue<int>();
        void Add(int x, int y) { var i = y * size + x; if (!boundary[i] && !outside[i]) { outside[i] = true; queue.Enqueue(i); } }
        for (var x = 0; x < size; x++) { Add(x, 0); Add(x, size - 1); } for (var y = 0; y < size; y++) { Add(0, y); Add(size - 1, y); }
        while (queue.Count > 0) { ct.ThrowIfCancellationRequested(); var i = queue.Dequeue(); var x = i % size; var y = i / size; if (x > 0) Add(x - 1, y); if (x < size - 1) Add(x + 1, y); if (y > 0) Add(x, y - 1); if (y < size - 1) Add(x, y + 1); }
        return outside;
    }

    private static ImageBuffer Resize(ImageBuffer source, int width, int height)
    {
        var result = new ImageBuffer(width, height); for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var sx = Math.Clamp((int)((x + .5) * source.Width / width), 0, source.Width - 1); var sy = Math.Clamp((int)((y + .5) * source.Height / height), 0, source.Height - 1); Array.Copy(source.Pixels, source.Offset(sx, sy), result.Pixels, result.Offset(x, y), 4); } return result;
    }
    private static ImageBuffer ResizeCover(ImageBuffer source, int width, int height)
    {
        var result = new ImageBuffer(width, height); var sourceAspect = source.Width / (double)source.Height; var targetAspect = width / (double)height; var cropW = source.Width; var cropH = source.Height; if (sourceAspect > targetAspect) cropW = (int)(source.Height * targetAspect); else cropH = (int)(source.Width / targetAspect); var left = (source.Width - cropW) / 2; var top = (source.Height - cropH) / 2;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var sx = Math.Clamp(left + (int)((x + .5) * cropW / width), 0, source.Width - 1); var sy = Math.Clamp(top + (int)((y + .5) * cropH / height), 0, source.Height - 1); Array.Copy(source.Pixels, source.Offset(sx, sy), result.Pixels, result.Offset(x, y), 4); } return result;
    }
}
