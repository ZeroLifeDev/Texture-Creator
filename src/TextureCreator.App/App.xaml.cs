using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace TextureCreator.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); var window = new MainWindow();
        if (e.Args.Length == 2 && e.Args[0] == "--screenshot")
        {
            window.ShowInTaskbar = false; window.Show(); window.UpdateLayout();
            var width = Math.Max(1, (int)window.ActualWidth); var height = Math.Max(1, (int)window.ActualHeight); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!); using (var stream = File.Create(e.Args[1])) encoder.Save(stream);
            window.Close(); Shutdown(); return;
        }
        window.Show();
    }
}
