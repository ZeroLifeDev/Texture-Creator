using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TextureCreator.Core;
namespace TextureCreator.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--codex-bootstrap")
        {
            RunCodexBootstrap(e.Args[1]); Shutdown(); return;
        }
        if (e.Args.Length == 5 && e.Args[0] == "--codex-image-smoke")
        {
            RunCodexImageSmoke(e.Args); Shutdown(); return;
        }
        if (e.Args.Length == 5 && e.Args[0] == "--ui-smoke")
        {
            RunUiSmoke(e.Args); Shutdown(); return;
        }
        if ((e.Args.Length is 4 or 5) && (e.Args[0] == "--quick-export" || e.Args[0] == "--quick-export-uv"))
        {
            RunQuickExport(e.Args); Shutdown(); return;
        }
        var window = new MainWindow();
        if (e.Args.Length == 2 && e.Args[0] == "--screenshot")
        {
            window.ShowInTaskbar = false; window.Show(); window.UpdateLayout();
            var width = Math.Max(1, (int)window.ActualWidth); var height = Math.Max(1, (int)window.ActualHeight); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!); using (var stream = File.Create(e.Args[1])) encoder.Save(stream);
            window.Close(); Shutdown(); return;
        }
        window.Show();
    }

    private static void RunCodexBootstrap(string statusPath)
    {
        try { var status = Task.Run(async () => { var bridge = new CodexBridge(); var current = await bridge.GetStatusAsync(); if (!current.Installed || !current.RuntimeComplete) { await bridge.InstallAsync(); current = await bridge.GetStatusAsync(); } return current; }).GetAwaiter().GetResult(); File.WriteAllText(Path.GetFullPath(statusPath), $"Installed={status.Installed}; RuntimeComplete={status.RuntimeComplete}; LoggedIn={status.LoggedIn}; {status.Detail}"); Environment.ExitCode = status.Installed && status.RuntimeComplete ? 0 : 1; }
        catch (Exception ex) { File.WriteAllText(Path.GetFullPath(statusPath), "FAIL: " + ex); Environment.ExitCode = 1; }
    }

    private static void RunCodexImageSmoke(string[] args)
    {
        var statusPath = Path.GetFullPath(args[4]);
        try
        {
            var output = Task.Run(async () => { var bridge = new CodexBridge(); var status = await bridge.GetStatusAsync(); if (!status.RuntimeComplete || !status.LoggedIn) throw new InvalidOperationException(status.Detail); return await bridge.GenerateUvTextureAsync(args[1], args[2], Path.GetFullPath(args[3])); }).GetAwaiter().GetResult();
            _ = ImageIo.Load(output); File.WriteAllText(statusPath, "PASS: real Codex OAuth GPT image generation created and verified " + output); Environment.ExitCode = 0;
        }
        catch (Exception ex) { Directory.CreateDirectory(Path.GetDirectoryName(statusPath)!); File.WriteAllText(statusPath, "FAIL: " + ex); Environment.ExitCode = 1; }
    }

    private static void RunUiSmoke(string[] args)
    {
        var statusPath = Path.GetFullPath(args[4]);
        try
        {
            var window = new MainWindow { ShowInTaskbar = false }; window.Show(); window.LoadQuickUvLayout(args[1]); window.LoadQuickReference(args[2]); window.UpdateLayout();
            if (!window.HasQuickReference) throw new InvalidOperationException("Quick reference selection did not remain synchronized.");
            var width = Math.Max(1, (int)window.ActualWidth); var height = Math.Max(1, (int)window.ActualHeight); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!); using (var stream = File.Create(args[3])) encoder.Save(stream);
            File.WriteAllText(statusPath, "PASS: UV layout and reference loaded without a selection error."); window.Close(); Environment.ExitCode = 0;
        }
        catch (Exception ex) { Directory.CreateDirectory(Path.GetDirectoryName(statusPath)!); File.WriteAllText(statusPath, "FAIL: " + ex); Environment.ExitCode = 1; }
    }

    private static void RunQuickExport(string[] args)
    {
        var output = Path.GetFullPath(args[3]);
        try
        {
            var resolution = args.Length == 5 ? int.Parse(args[4]) : 2048; if (resolution is < 64 or > 8192) throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution must be from 64 to 8192.");
            var reference = ImageIo.Load(args[2]); ProjectionResult projection;
            if (args[0] == "--quick-export-uv") { var layout = ImageIo.Load(args[1]); projection = new UvLayoutProjector().Project(layout, reference, resolution); }
            else { var mesh = ModelImporter.Load(args[1]); if (!mesh.HasUvs) throw new InvalidOperationException("The supplied model has no complete UV coordinates."); projection = new ProjectionEngine().Project(mesh, [(new ReferenceImage { Path = args[2], Role = ReferenceRole.Front }, reference)], resolution); }
            var set = new PbrGenerator().Generate(projection.Surface, MaterialKind.Dielectric, .55f, 0, 1); set.Maps[MapKind.Coverage] = projection.Coverage; TextureExporter.ExportZip(set, output, Path.GetFileNameWithoutExtension(args[1]), [MapKind.Diffuse, MapKind.Albedo, MapKind.Roughness, MapKind.Normal, MapKind.Height, MapKind.Metalness]); Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(output + ".error.txt", ex.ToString()); Environment.ExitCode = 1;
        }
    }
}
