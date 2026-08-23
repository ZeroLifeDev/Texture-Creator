using System.IO.Compression;
using System.Text.Json;

namespace TextureCreator.Core;

public static class ProjectStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static void Save(ForgeProject project, string path) { project.ModifiedUtc = DateTimeOffset.UtcNow; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, JsonSerializer.Serialize(project, Options)); }
    public static ForgeProject Load(string path) { var p = JsonSerializer.Deserialize<ForgeProject>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Project file is empty."); if (p.FormatVersion > 1) throw new NotSupportedException("This project was made by a newer application version."); return p; }
}

public static class TextureExporter
{
    private static readonly Dictionary<MapKind, string> Names = new() { [MapKind.Albedo] = "BaseColor", [MapKind.Roughness] = "Roughness", [MapKind.Normal] = "Normal", [MapKind.Height] = "Height", [MapKind.Metalness] = "Metallic", [MapKind.AmbientOcclusion] = "AO", [MapKind.Coverage] = "Coverage" };
    public static IReadOnlyList<string> Export(TextureSet set, string directory, string assetName)
    {
        assetName = string.Concat(assetName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)); Directory.CreateDirectory(directory); var paths = new List<string>();
        foreach (var (kind, image) in set.Maps) { var path = Path.Combine(directory, $"{assetName}_{Names[kind]}.png"); ImageIo.SavePng(image, path); paths.Add(path); }
        return paths;
    }
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
    public string Name => "ChatGPT Web Assist (experimental)";
    public bool SendsDataExternally => true;
    public Task<ProviderResult> PrepareAsync(ImageBuffer image, ImageBuffer? mask, string operation, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(Path.GetTempPath(), "TextureCreator", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); var input = Path.Combine(dir, "input.png"); ImageIo.SavePng(image, input); if (mask is not null) ImageIo.SavePng(mask, Path.Combine(dir, "mask.png"));
        return Task.FromResult(new ProviderResult(false, $"Files were prepared in {dir}. Open https://chatgpt.com in your normal browser, sign in interactively, attach the files, and request: {operation}. Save the result, then import it into Texture Creator. No cookies, tokens, or private APIs are accessed."));
    }
}

