using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace TextureCreator.Core;

public sealed record CodexStatus(bool Installed, bool RuntimeComplete, bool LoggedIn, string Detail);

public sealed class CodexBridge
{
    public const string OfficialDownload = "https://github.com/openai/codex/releases/latest/download/codex-x86_64-pc-windows-msvc.exe.zip";
    public static readonly IReadOnlyDictionary<string, string> RequiredAssets = new Dictionary<string, string>
    {
        ["codex-x86_64-pc-windows-msvc.exe.zip"] = "codex.exe",
        ["codex-code-mode-host-x86_64-pc-windows-msvc.exe.zip"] = "codex-code-mode-host.exe",
        ["codex-command-runner-x86_64-pc-windows-msvc.exe.zip"] = "codex-command-runner.exe",
        ["codex-windows-sandbox-setup-x86_64-pc-windows-msvc.exe.zip"] = "codex-windows-sandbox-setup.exe"
    };
    public string RuntimeDirectory { get; }
    public string ExecutablePath => Path.Combine(RuntimeDirectory, "codex.exe");

    public CodexBridge(string? runtimeDirectory = null) => RuntimeDirectory = runtimeDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBRReferenceForge", "codex-runtime");

    public bool HasCompleteRuntime() => RequiredAssets.Values.All(name => File.Exists(Path.Combine(RuntimeDirectory, name)));

    public async Task<CodexStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ExecutablePath)) return new(false, false, false, "Codex CLI is not installed for PBR Reference Forge.");
        if (!HasCompleteRuntime()) return new(true, false, false, "Codex is installed but its GPT image runtime is incomplete. Click Set Up Codex + GPT to repair it.");
        var result = await RunAsync(["login", "status"], null, ct, TimeSpan.FromSeconds(20));
        var loggedIn = result.ExitCode == 0 && (result.Output + result.Error).Contains("Logged in using ChatGPT", StringComparison.OrdinalIgnoreCase);
        return new(true, true, loggedIn, loggedIn ? "Codex and the GPT image runtime are ready." : "Codex is installed but not signed in.");
    }

    public async Task InstallAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(RuntimeDirectory);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("PBR-Reference-Forge/0.4.1");
        using var release = await client.GetAsync("https://api.github.com/repos/openai/codex/releases/latest", ct); release.EnsureSuccessStatusCode(); using var document = JsonDocument.Parse(await release.Content.ReadAsStreamAsync(ct));
        var available = document.RootElement.GetProperty("assets").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!, x => x.GetProperty("browser_download_url").GetString()!);
        var completed = 0;
        foreach (var (assetName, installedName) in RequiredAssets)
        {
            if (!available.TryGetValue(assetName, out var assetUrl)) throw new InvalidDataException($"The official Codex release does not contain required component {assetName}.");
            await InstallAssetAsync(client, assetUrl, assetName[..^4], installedName, new Progress<double>(value => progress?.Report((completed + value) / RequiredAssets.Count)), ct);
            completed++;
        }
        if (!HasCompleteRuntime()) throw new InvalidOperationException("Codex GPT image runtime installation was incomplete.");
        var version = await RunAsync(["--version"], null, ct, TimeSpan.FromSeconds(20)); if (version.ExitCode != 0) throw new InvalidOperationException("Installed Codex CLI did not start: " + version.Error);
    }

    private async Task InstallAssetAsync(HttpClient client, string assetUrl, string archivedName, string installedName, IProgress<double> progress, CancellationToken ct)
    {
        var staging = Path.Combine(RuntimeDirectory, "install-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging); var zip = Path.Combine(staging, "download.zip");
        try
        {
            using var response = await client.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength; await using (var input = await response.Content.ReadAsStreamAsync(ct)) await using (var output = File.Create(zip)) { var buffer = new byte[1024 * 128]; long copied = 0; int read; while ((read = await input.ReadAsync(buffer, ct)) > 0) { await output.WriteAsync(buffer.AsMemory(0, read), ct); copied += read; if (total > 0) progress.Report(copied / (double)total.Value); } }
            var extract = Path.Combine(staging, "extract"); ZipFile.ExtractToDirectory(zip, extract); var files = Directory.GetFiles(extract, "*.exe", SearchOption.AllDirectories); var found = files.FirstOrDefault(path => string.Equals(Path.GetFileName(path), archivedName, StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault(path => string.Equals(Path.GetFileName(path), installedName, StringComparison.OrdinalIgnoreCase)) ?? (files.Length == 1 ? files[0] : null) ?? throw new InvalidDataException($"Official Codex archive did not contain {archivedName}."); File.Copy(found, Path.Combine(RuntimeDirectory, installedName), true);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public async Task LoginAsync(CancellationToken ct = default)
    {
        var result = await RunAsync(["login"], null, ct, TimeSpan.FromMinutes(5)); if (result.ExitCode != 0) throw new InvalidOperationException("Codex sign-in failed: " + result.Error);
    }

    public async Task<string> GenerateUvTextureAsync(string uvImage, string referenceImage, string outputDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory); var output = Path.Combine(outputDirectory, "gpt-albedo.png"); var last = Path.Combine(outputDirectory, "codex-result.txt"); if (File.Exists(output)) File.Delete(output);
        var prompt = $"Use the installed imagegen skill and actually invoke GPT image generation. Image 1 is the target UV layout and Image 2 is the texture reference. Generate one square albedo texture that strictly preserves every UV island position and shape from Image 1, painting only those islands with the appearance from Image 2. Outside the UV islands must be solid black. Flat material color, neutral lighting, no 3D object, no scene, no text. Save the final PNG exactly as {output}. Do not merely describe it. Verify the file exists before finishing.";
        string[] args = ["-a", "never", "exec", "--ephemeral", "--skip-git-repo-check", "--sandbox", "workspace-write", "-C", outputDirectory, "-i", Path.GetFullPath(uvImage), "-i", Path.GetFullPath(referenceImage), "-o", last, prompt];
        var result = await RunAsync(args, outputDirectory, ct, TimeSpan.FromMinutes(12)); if (result.ExitCode != 0 || !File.Exists(output)) throw new InvalidOperationException("Codex GPT image generation failed. " + result.Error + Environment.NewLine + result.Output);
        _ = ImageIo.Load(output); return output;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(IEnumerable<string> args, string? workingDirectory, CancellationToken ct, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = workingDirectory ?? RuntimeDirectory }; foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Codex CLI."); var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct); using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct); timeoutCts.CancelAfter(timeout); try { await process.WaitForExitAsync(timeoutCts.Token); } catch { try { process.Kill(true); } catch { } throw new TimeoutException("Codex did not finish within " + timeout); } return (process.ExitCode, await stdout, await stderr);
    }
}

public static class ImageResizer
{
    public static ImageBuffer Resize(ImageBuffer source, int width, int height)
    {
        if (source.Width == width && source.Height == height) return source.Clone(); var result = new ImageBuffer(width, height);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var sx = Math.Clamp((int)((x + .5) * source.Width / width), 0, source.Width - 1); var sy = Math.Clamp((int)((y + .5) * source.Height / height), 0, source.Height - 1); Array.Copy(source.Pixels, source.Offset(sx, sy), result.Pixels, result.Offset(x, y), 4); }
        return result;
    }
}
