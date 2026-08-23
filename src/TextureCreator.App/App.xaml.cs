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
