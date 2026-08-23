using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TextureCreator.Core;

public static class ImageIo
{
    public static ImageBuffer Load(string path, int maxSize = 8192)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Image was not found.", path);
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > maxSize || frame.PixelHeight > maxSize) throw new InvalidDataException($"Image exceeds {maxSize}px safety limit.");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var bgra = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(bgra, converted.PixelWidth * 4, 0);
        for (var i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        return new(converted.PixelWidth, converted.PixelHeight, bgra);
    }

    public static void SavePng(ImageBuffer image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, EncodePng(image));
    }

    public static byte[] EncodePng(ImageBuffer image)
    {
        var bgra = (byte[])image.Pixels.Clone(); for (var i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        var bmp = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, bgra, image.Width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp)); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    public static BitmapSource ToBitmap(ImageBuffer image)
    {
        var bgra = (byte[])image.Pixels.Clone(); for (var i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        var bmp = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, bgra, image.Width * 4); bmp.Freeze(); return bmp;
    }
}

public sealed class PbrGenerator
{
    public TextureSet Generate(ImageBuffer source, MaterialKind material, float roughness, float metalness, float normalStrength, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var set = new TextureSet(); var albedo = CorrectAlbedo(source, ct); progress?.Report(.2);
        var height = Luminance(albedo); progress?.Report(.35); ct.ThrowIfCancellationRequested();
        set.Maps[MapKind.Albedo] = albedo;
        set.Maps[MapKind.Diffuse] = albedo.Clone();
        set.Maps[MapKind.Height] = Grayscale(height, source.Width, source.Height);
        set.Maps[MapKind.Normal] = Normal(height, source.Width, source.Height, normalStrength);
        set.Maps[MapKind.Roughness] = Constant(source.Width, source.Height, (byte)(Math.Clamp(MaterialRoughness(material, roughness), 0, 1) * 255));
        set.Maps[MapKind.Metalness] = Constant(source.Width, source.Height, (byte)(Math.Clamp(material is MaterialKind.BareMetal or MaterialKind.PaintedMetal ? Math.Max(metalness, material == MaterialKind.BareMetal ? .92f : .15f) : metalness, 0, 1) * 255));
        set.Maps[MapKind.AmbientOcclusion] = AmbientOcclusion(height, source.Width, source.Height);
        set.Maps[MapKind.Coverage] = Constant(source.Width, source.Height, 255);
        progress?.Report(1); return set;
    }

    public static ImageBuffer CorrectAlbedo(ImageBuffer source, CancellationToken ct = default)
    {
        var result = source.Clone(); var w = source.Width; var h = source.Height; var lum = Luminance(source); var blur = BoxBlur(lum, w, h, Math.Max(2, Math.Min(w, h) / 64));
        for (var y = 0; y < h; y++) { ct.ThrowIfCancellationRequested(); for (var x = 0; x < w; x++) { var p = (y * w + x) * 4; var gain = Math.Clamp(128f / Math.Max(32, blur[y * w + x]), .55f, 1.8f); for (var c = 0; c < 3; c++) result.Pixels[p + c] = (byte)Math.Clamp((source.Pixels[p + c] - 128) * .92f * gain + 128, 0, 255); result.Pixels[p + 3] = source.Pixels[p + 3]; } }
        return result;
    }
    private static float MaterialRoughness(MaterialKind m, float requested) => m switch { MaterialKind.Fabric => Math.Max(.72f, requested), MaterialKind.Rubber => Math.Max(.66f, requested), MaterialKind.Glass => Math.Min(.16f, requested), MaterialKind.BareMetal => Math.Min(.42f, requested), MaterialKind.Skin => Math.Max(.42f, requested), _ => requested };
    private static float[] Luminance(ImageBuffer s) { var r = new float[s.Width * s.Height]; for (var i = 0; i < r.Length; i++) { var p = i * 4; r[i] = s.Pixels[p] * .2126f + s.Pixels[p + 1] * .7152f + s.Pixels[p + 2] * .0722f; } return r; }
    private static float[] BoxBlur(float[] s, int w, int h, int radius) { var r = new float[s.Length]; for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) { float sum = 0; var n = 0; for (var yy = Math.Max(0, y - radius); yy <= Math.Min(h - 1, y + radius); yy += Math.Max(1, radius / 2)) for (var xx = Math.Max(0, x - radius); xx <= Math.Min(w - 1, x + radius); xx += Math.Max(1, radius / 2)) { sum += s[yy * w + xx]; n++; } r[y * w + x] = sum / n; } return r; }
    private static ImageBuffer Grayscale(float[] v, int w, int h) { var r = new ImageBuffer(w, h); for (var i = 0; i < v.Length; i++) { var b = (byte)Math.Clamp(v[i], 0, 255); r.Pixels[i * 4] = r.Pixels[i * 4 + 1] = r.Pixels[i * 4 + 2] = b; r.Pixels[i * 4 + 3] = 255; } return r; }
    private static ImageBuffer Constant(int w, int h, byte value) { var r = new ImageBuffer(w, h); for (var i = 0; i < w * h; i++) { r.Pixels[i * 4] = r.Pixels[i * 4 + 1] = r.Pixels[i * 4 + 2] = value; r.Pixels[i * 4 + 3] = 255; } return r; }
    private static ImageBuffer Normal(float[] v, int w, int h, float strength) { var r = new ImageBuffer(w, h); for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) { float V(int xx, int yy) => v[Math.Clamp(yy, 0, h - 1) * w + Math.Clamp(xx, 0, w - 1)] / 255f; var dx = (V(x + 1, y) - V(x - 1, y)) * strength; var dy = (V(x, y + 1) - V(x, y - 1)) * strength; var n = System.Numerics.Vector3.Normalize(new(-dx, -dy, 1)); var p = (y * w + x) * 4; r.Pixels[p] = (byte)((n.X * .5f + .5f) * 255); r.Pixels[p + 1] = (byte)((n.Y * .5f + .5f) * 255); r.Pixels[p + 2] = (byte)((n.Z * .5f + .5f) * 255); r.Pixels[p + 3] = 255; } return r; }
    private static ImageBuffer AmbientOcclusion(float[] v, int w, int h) { var blur = BoxBlur(v, w, h, Math.Max(2, Math.Min(w, h) / 128)); var r = new ImageBuffer(w, h); for (var i = 0; i < v.Length; i++) { var ao = (byte)Math.Clamp(245 + (v[i] - blur[i]) * .3f, 150, 255); r.Pixels[i * 4] = r.Pixels[i * 4 + 1] = r.Pixels[i * 4 + 2] = ao; r.Pixels[i * 4 + 3] = 255; } return r; }
}
