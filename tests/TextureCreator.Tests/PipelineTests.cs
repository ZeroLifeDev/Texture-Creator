using TextureCreator.Core;

namespace TextureCreator.Tests;

public sealed class PipelineTests
{
    private static string TempFile(string ext) => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ext);
    [Fact] public void ObjImportExtractsUvsAndTriangulates()
    {
        var p = TempFile(".obj"); File.WriteAllText(p, "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nf 1/1 2/2 3/3 4/4\n");
        var m = ModelImporter.Load(p); Assert.True(m.HasUvs); Assert.Equal(2, m.TriangleCount); Assert.Equal(4, m.Positions.Count);
    }
    [Fact] public void MissingUvsAreReported() { var p = TempFile(".obj"); File.WriteAllText(p, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n"); Assert.False(ModelImporter.Load(p).HasUvs); }
    [Fact] public void MalformedObjFails() { var p = TempFile(".obj"); File.WriteAllText(p, "this is not geometry"); Assert.Throws<InvalidDataException>(() => ModelImporter.Load(p)); }
    [Fact] public void ProjectRoundTrips() { var p = TempFile(".tforge"); var input = new ForgeProject { Name = "Test", TextureResolution = 4096, References = [new() { Path = "ref.png", Role = ReferenceRole.Front }] }; ProjectStore.Save(input, p); var output = ProjectStore.Load(p); Assert.Equal("Test", output.Name); Assert.Equal(4096, output.TextureResolution); Assert.Equal(ReferenceRole.Front, output.References[0].Role); }
    [Fact] public void PbrMapsAreConsistentDimensions() { var src = Pattern(32); var set = new PbrGenerator().Generate(src, MaterialKind.BareMetal, .3f, .8f, 1); Assert.Equal(8, set.Maps.Count); Assert.All(set.Maps.Values, x => { Assert.Equal(32, x.Width); Assert.Equal(32, x.Height); }); Assert.True(set[MapKind.Metalness].Pixels[0] > 200); Assert.Equal(set[MapKind.Albedo].Pixels, set[MapKind.Diffuse].Pixels); }
    [Fact] public void HeightMapSuppressesLightingGradientAndRecessesDarkCracks()
    {
        var source = new ImageBuffer(128, 128);
        for (var y = 0; y < 128; y++) for (var x = 0; x < 128; x++) { var p = source.Offset(x, y); var value = (byte)(95 + x / 3); if (Math.Abs(x - 64) <= 2) value = 28; source.Pixels[p] = source.Pixels[p + 1] = source.Pixels[p + 2] = value; source.Pixels[p + 3] = 255; }
        var height = new PbrGenerator().Generate(source, MaterialKind.Dielectric, .7f, 0, 1)[MapKind.Height];
        byte H(int x, int y) => height.Pixels[height.Offset(x, y)];
        Assert.True(H(64, 64) + 12 < (H(48, 64) + H(80, 64)) / 2);
        Assert.InRange(Math.Abs(H(24, 64) - H(104, 64)), 0, 12);
        Assert.All(height.Pixels.Chunk(4), pixel => Assert.InRange(pixel[0], (byte)88, (byte)158));
    }
    [Fact] public void WebAssistPromptRequestsDepthFriendlyNeutralReference()
    {
        Assert.Contains("neutral diffuse lighting", ChatGptWebAssistProvider.MaterialReferencePrompt);
        Assert.Contains("visually recessed", ChatGptWebAssistProvider.MaterialReferencePrompt);
        Assert.Contains("do not turn them into raised ridges", ChatGptWebAssistProvider.MaterialReferencePrompt);
    }
    [Fact] public void CoverageRasterizesUvTriangle() { var p = TempFile(".obj"); File.WriteAllText(p, "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nf 1/1 2/2 3/3\n"); var c = UvServices.RasterizeUvCoverage(ModelImporter.Load(p), 32); Assert.Contains(c.Pixels.Chunk(4), px => px[3] == 255); }
    [Fact] public void UvSeamsAreDetected() { var p = TempFile(".obj"); File.WriteAllText(p, "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\nvt 0 0\nvt .5 0\nvt 0 1\nvt .6 0\nvt 1 1\nvt .6 1\nf 1/1 2/2 3/3\nf 2/4 4/5 3/6\n"); Assert.Single(UvServices.FindSeams(ModelImporter.Load(p))); }
    [Fact] public void ExportWritesPngSet() { var set = new PbrGenerator().Generate(Pattern(8), MaterialKind.Wood, .6f, 0, 1); var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var files = TextureExporter.Export(set, dir, "Cube"); Assert.Equal(8, files.Count); Assert.All(files, f => Assert.True(new FileInfo(f).Length > 20)); }
    [Fact] public void QuickZipContainsRequestedSixMaps() { var set = new PbrGenerator().Generate(Pattern(8), MaterialKind.Wood, .6f, 0, 1); var zip = TempFile(".zip"); var maps = new[] { MapKind.Diffuse, MapKind.Albedo, MapKind.Roughness, MapKind.Normal, MapKind.Height, MapKind.Metalness }; var files = TextureExporter.ExportZip(set, zip, "Cube", maps); Assert.Equal(6, files.Count); using var archive = System.IO.Compression.ZipFile.OpenRead(zip); Assert.Equal(new[] { "Cube_Albedo.png", "Cube_Diffuse.png", "Cube_Displacement.png", "Cube_Metalness.png", "Cube_Normal.png", "Cube_Roughness.png" }, archive.Entries.Select(x => x.FullName).Order().ToArray()); }
    [Fact] public void CompleteQuickPipelineProducesUsableZip()
    {
        var modelPath = TempFile(".obj"); File.WriteAllText(modelPath, "v -1 -1 0\nv 1 -1 0\nv 1 1 0\nv -1 1 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nf 1/1 2/2 3/3 4/4\n"); var mesh = ModelImporter.Load(modelPath);
        var projection = new ProjectionEngine().Project(mesh, [(new ReferenceImage { Role = ReferenceRole.Front }, Pattern(64))], 128); var set = new PbrGenerator().Generate(projection.Surface, MaterialKind.Dielectric, .55f, 0, 1); var zip = TempFile(".zip"); TextureExporter.ExportZip(set, zip, "QuickTest", [MapKind.Diffuse, MapKind.Albedo, MapKind.Roughness, MapKind.Normal, MapKind.Height, MapKind.Metalness]);
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip); Assert.Equal(6, archive.Entries.Count); Assert.All(archive.Entries, entry => { Assert.EndsWith(".png", entry.Name); Assert.True(entry.Length > 100); using var stream = entry.Open(); Span<byte> signature = stackalloc byte[8]; Assert.Equal(8, stream.Read(signature)); Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, signature.ToArray()); });
    }
    [Fact] public void UvLayoutImageDetectsAndFillsClosedIsland()
    {
        var layout = new ImageBuffer(64, 64); for (var i = 0; i < 64 * 64; i++) { layout.Pixels[i * 4] = layout.Pixels[i * 4 + 1] = layout.Pixels[i * 4 + 2] = layout.Pixels[i * 4 + 3] = 255; } for (var x = 12; x <= 51; x++) { SetBlack(x, 12); SetBlack(x, 51); } for (var y = 12; y <= 51; y++) { SetBlack(12, y); SetBlack(51, y); }
        var result = new UvLayoutProjector().Project(layout, Pattern(32), 64); Assert.InRange(result.ObservedTexels, 1500, 1800); Assert.Equal(255, result.Surface.Pixels[result.Surface.Offset(32, 32) + 3]); Assert.Equal(0, result.Surface.Pixels[result.Surface.Offset(2, 2) + 3]);
        void SetBlack(int x, int y) { var p = layout.Offset(x, y); layout.Pixels[p] = layout.Pixels[p + 1] = layout.Pixels[p + 2] = 0; }
    }
    [Fact] public void ImageLoadRoundTripsPng() { var p = TempFile(".png"); ImageIo.SavePng(Pattern(12), p); var i = ImageIo.Load(p); Assert.Equal(12, i.Width); Assert.Equal(12, i.Height); }
    [Fact] public void ProjectionMapsReferenceIntoUvSpace() { var p = TempFile(".obj"); File.WriteAllText(p, "v -1 -1 0\nv 1 -1 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt .5 1\nf 1/1 2/2 3/3\n"); var m = ModelImporter.Load(p); var result = new ProjectionEngine().Project(m, [(new ReferenceImage { Role = ReferenceRole.Front }, Pattern(16))], 64); Assert.True(result.ObservedTexels > 1000); Assert.Equal(255, result.Surface.Pixels[32 * 64 * 4 + 32 * 4 + 3]); }
    [Fact] public void GlbImportReadsTriangleAndUvs()
    {
        var bin = new List<byte>(); void F(params float[] values) { foreach (var v in values) bin.AddRange(BitConverter.GetBytes(v)); } F(0, 0, 0, 1, 0, 0, 0, 1, 0); F(0, 0, 1, 0, 0, 1); bin.AddRange(new byte[] { 0, 0, 1, 0, 2, 0 }); while (bin.Count % 4 != 0) bin.Add(0);
        var json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":" + bin.Count + "}],\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":24},{\"buffer\":0,\"byteOffset\":60,\"byteLength\":6}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"},{\"bufferView\":1,\"componentType\":5126,\"count\":3,\"type\":\"VEC2\"},{\"bufferView\":2,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}],\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"TEXCOORD_0\":1},\"indices\":2}]}]}"; var jb = System.Text.Encoding.UTF8.GetBytes(json); while (jb.Length % 4 != 0) jb = [.. jb, (byte)' '];
        var file = new List<byte>(); file.AddRange(System.Text.Encoding.ASCII.GetBytes("glTF")); file.AddRange(BitConverter.GetBytes(2)); file.AddRange(BitConverter.GetBytes(12 + 8 + jb.Length + 8 + bin.Count)); file.AddRange(BitConverter.GetBytes(jb.Length)); file.AddRange(BitConverter.GetBytes(0x4E4F534A)); file.AddRange(jb); file.AddRange(BitConverter.GetBytes(bin.Count)); file.AddRange(BitConverter.GetBytes(0x004E4942)); file.AddRange(bin); var p = TempFile(".glb"); File.WriteAllBytes(p, file.ToArray()); var m = ModelImporter.Load(p); Assert.True(m.HasUvs); Assert.Equal(1, m.TriangleCount);
    }
    private static ImageBuffer Pattern(int n) { var b = new ImageBuffer(n, n); for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) { var p = b.Offset(x, y); b.Pixels[p] = (byte)(x * 255 / n); b.Pixels[p + 1] = (byte)(y * 255 / n); b.Pixels[p + 2] = 120; b.Pixels[p + 3] = 255; } return b; }
}
