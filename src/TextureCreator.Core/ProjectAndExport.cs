using System.IO.Compression;
using System.Text.Json;

namespace TextureCreator.Core;

public static class ProjectStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static void Save(ForgeProject project, string path) { project.ModifiedUtc = DateTimeOffset.UtcNow; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, JsonSerializer.Serialize(project, Options)); }
    public static ForgeProject Load(string path) { var p = JsonSerializer.Deserialize<ForgeProject>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Project file is empty."); if (p.FormatVersion > 1) throw new NotSupportedException("This project was made by a newer application version."); return p; }
}

public sealed record AppPreferences(bool ChatGptBrowserReady = false, DateTimeOffset? ConfirmedUtc = null);
public static class AppPreferenceStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static AppPreferences Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path), Options) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }
    public static void Save(AppPreferences preferences, string path) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, JsonSerializer.Serialize(preferences, Options)); }
}

public static class TextureExporter
{
    private static readonly Dictionary<MapKind, string> Names = new() { [MapKind.Diffuse] = "Diffuse", [MapKind.Albedo] = "Albedo", [MapKind.Roughness] = "Roughness", [MapKind.Normal] = "Normal", [MapKind.Height] = "Displacement", [MapKind.Metalness] = "Metalness", [MapKind.AmbientOcclusion] = "AO", [MapKind.Coverage] = "Coverage" };
    public static IReadOnlyList<string> Export(TextureSet set, string directory, string assetName)
    {
        assetName = string.Concat(assetName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)); Directory.CreateDirectory(directory); var paths = new List<string>();
        foreach (var (kind, image) in set.Maps) { var path = Path.Combine(directory, $"{assetName}_{Names[kind]}.png"); ImageIo.SavePng(image, path); paths.Add(path); }
        return paths;
    }

    public static IReadOnlyList<string> ExportZip(TextureSet set, string zipPath, string assetName, IEnumerable<MapKind> maps)
    {
        assetName = SafeAssetName(assetName); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!); var names = new List<string>();
        using var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None); using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var kind in maps.Distinct())
        {
            if (!set.Maps.TryGetValue(kind, out var image)) throw new InvalidOperationException($"Texture set does not contain {kind}.");
            var name = $"{assetName}_{Names[kind]}.png"; var entry = archive.CreateEntry(name, CompressionLevel.Optimal); using var output = entry.Open(); var bytes = ImageIo.EncodePng(image); output.Write(bytes); names.Add(name);
        }
        return names;
    }

    private static string SafeAssetName(string assetName) => string.Concat(assetName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

public interface IImageGenerationProvider
{
    string Name { get; }
    bool SendsDataExternally { get; }
    Task<ProviderResult> PrepareAsync(ImageBuffer image, ImageBuffer? mask, string operation, CancellationToken cancellationToken);
}
public sealed record ProviderResult(bool Completed, string Instructions, string? OutputPath = null);

public sealed class ChatGptWebAssistProvider : IImageGenerationProvider
{
    public const string MaterialReferencePrompt = "Prepare this image as a physically plausible material reference for deterministic PBR reconstruction. Return one flat, orthographic, edge-to-edge surface image with uniform scale and neutral diffuse lighting. Preserve the material's real colors, cracks, pores, aggregate and wear, but remove cast shadows, specular glare, ambient-occlusion darkening, perspective and lens distortion. Dark cracks and cavities must remain visually recessed; do not turn them into raised ridges. Do not invent large shapes or sharpen fine grain into spikes. No cube, room, scene, objects, labels, borders, text or watermark. Do not output a normal, height or roughness map; output only the corrected material reference.";
    public string Name => "ChatGPT Web Assist (experimental)";
    public bool SendsDataExternally => true;
    public Task<ProviderResult> PrepareAsync(ImageBuffer image, ImageBuffer? mask, string operation, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(Path.GetTempPath(), "TextureCreator", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); var input = Path.Combine(dir, "input.png"); ImageIo.SavePng(image, input); if (mask is not null) ImageIo.SavePng(mask, Path.Combine(dir, "mask.png"));
        return Task.FromResult(new ProviderResult(false, $"Files were prepared in {dir}. Open https://chatgpt.com in your normal browser, sign in interactively, attach the files, and use this prompt:\n\n{MaterialReferencePrompt}\n\nRequested repair: {operation}.\n\nSave the result, then import it into Texture Creator. No cookies, tokens, or private APIs are accessed."));
    }
}
