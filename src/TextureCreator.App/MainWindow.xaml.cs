using Microsoft.Win32;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using TextureCreator.Core;
using Vector3 = System.Numerics.Vector3;

namespace TextureCreator.App;

public partial class MainWindow : Window
{
    private ForgeProject project = new(); private MeshData? mesh; private TextureSet? textures; private string? projectPath; private Point dragStart; private double yaw = 25, pitch = -15, distance = 4; private bool dragging;
    private readonly string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBRReferenceForge", "logs", "app.log");
    public MainWindow()
    {
        InitializeComponent(); Directory.CreateDirectory(Path.GetDirectoryName(logPath)!); Log("Application started");
        MaterialCombo.ItemsSource = Enum.GetValues<MaterialKind>(); MaterialCombo.SelectedItem = MaterialKind.Dielectric; RoleCombo.ItemsSource = Enum.GetValues<ReferenceRole>(); RoleCombo.SelectedItem = ReferenceRole.Custom;
        AddLights(); Closing += (_, _) => { if (projectPath is not null) Try(() => ProjectStore.Save(project, projectPath + ".autosave")); };
    }
    private void AddLights() { var group = new Model3DGroup(); group.Children.Add(new AmbientLight(Color.FromRgb(85, 90, 100))); group.Children.Add(new DirectionalLight(Color.FromRgb(245, 240, 225), new(-.7, -1, -1))); group.Children.Add(new DirectionalLight(Color.FromRgb(90, 130, 160), new(.7, .2, .4))); Viewport.Children.Add(new ModelVisual3D { Content = group }); }

    private void ImportModel_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "3D Models|*.obj;*.glb;*.gltf|Wavefront OBJ|*.obj|glTF|*.glb;*.gltf" }; if (d.ShowDialog() != true) return;
        Try(() => { mesh = ModelImporter.Load(d.FileName); project.ModelPath = d.FileName; project.Name = Path.GetFileNameWithoutExtension(d.FileName); ModelName.Text = Path.GetFileName(d.FileName); MeshStats.Text = $"{mesh.Positions.Count:N0} vertices  •  {mesh.TriangleCount:N0} triangles\n{mesh.TexCoords.Count:N0} UVs  •  {mesh.MaterialSlots.Count} materials"; RenderMesh(); RenderUv(); Status.Text = mesh.HasUvs ? "Model imported — UVs validated" : "Warning: this model has no complete UV coordinates"; if (!mesh.HasUvs) MessageBox.Show("The model is missing UV coordinates. Preview is available, but generation/export will remain disabled until a UV-mapped model is imported.", "Missing UVs", MessageBoxButton.OK, MessageBoxImage.Warning); Log($"Imported model {d.FileName}"); });
    }
    private void AddReference_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Reference images|*.png;*.jpg;*.jpeg;*.webp", Multiselect = true }; if (d.ShowDialog() != true) return;
        foreach (var f in d.FileNames) Try(() => { _ = ImageIo.Load(f); var role = project.References.Count switch { 0 => ReferenceRole.Front, 1 => ReferenceRole.Back, 2 => ReferenceRole.Left, 3 => ReferenceRole.Right, 4 => ReferenceRole.Top, 5 => ReferenceRole.Bottom, _ => ReferenceRole.Custom }; project.References.Add(new() { Path = f, Role = role }); ReferenceList.Items.Add($"{Path.GetFileName(f)}  [{role}]"); }); if (project.References.Count > 0) ReferenceList.SelectedIndex = project.References.Count - 1; Status.Text = $"{project.References.Count} reference image(s) loaded";
    }
    private void ReferenceList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ReferenceList.SelectedIndex < 0) return; RoleCombo.SelectedItem = project.References[ReferenceList.SelectedIndex].Role; Try(() => { var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new(project.References[ReferenceList.SelectedIndex].Path); bmp.EndInit(); ReferenceBackdrop.Source = bmp; }); }
    private void RoleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ReferenceList is null || ReferenceList.SelectedIndex < 0 || RoleCombo.SelectedItem is not ReferenceRole role) return; project.References[ReferenceList.SelectedIndex].Role = role; ReferenceList.Items[ReferenceList.SelectedIndex] = $"{Path.GetFileName(project.References[ReferenceList.SelectedIndex].Path)}  [{role}]"; }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "PBR Reference Forge Project|*.tforge" }; if (d.ShowDialog() != true) return;
        Try(() => { project = ProjectStore.Load(d.FileName); projectPath = d.FileName; textures = null; MapStrip.Items.Clear(); ReferenceList.Items.Clear(); foreach (var r in project.References) ReferenceList.Items.Add($"{Path.GetFileName(r.Path)}  [{r.Role}]"); MaterialCombo.SelectedItem = project.DefaultMaterial; RoughnessSlider.Value = project.Roughness; MetalSlider.Value = project.Metalness; NormalSlider.Value = project.NormalStrength; var wanted = ResolutionCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Content?.ToString() == project.TextureResolution.ToString()); if (wanted is not null) ResolutionCombo.SelectedItem = wanted; if (!string.IsNullOrWhiteSpace(project.ModelPath) && File.Exists(project.ModelPath)) { mesh = ModelImporter.Load(project.ModelPath); ModelName.Text = Path.GetFileName(project.ModelPath); MeshStats.Text = $"{mesh.Positions.Count:N0} vertices  •  {mesh.TriangleCount:N0} triangles\n{mesh.TexCoords.Count:N0} UVs  •  {mesh.MaterialSlots.Count} materials"; RenderMesh(); RenderUv(); } else { mesh = null; ModelName.Text = "Source model is missing"; } if (project.References.Count > 0) ReferenceList.SelectedIndex = 0; Status.Text = "Project loaded"; Log($"Loaded {d.FileName}"); });
    }
    private void Save_Click(object sender, RoutedEventArgs e) { var d = new SaveFileDialog { Filter = "PBR Reference Forge Project|*.tforge", FileName = project.Name + ".tforge" }; if (d.ShowDialog() != true) return; projectPath = d.FileName; Try(() => { ReadSettings(); ProjectStore.Save(project, d.FileName); Status.Text = "Project saved"; Log($"Saved {d.FileName}"); }); }
    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (mesh is null || !mesh.HasUvs) { MessageBox.Show("Import a UV-mapped model first.", "Cannot generate", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (project.References.Count == 0) { MessageBox.Show("Add at least one reference image first.", "Cannot generate", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        ReadSettings(); Progress.Visibility = Visibility.Visible; Progress.Value = 0; Status.Text = "Generating consistent PBR maps…";
        try { var sourceFiles = project.References.Select(r => (r, ImageIo.Load(r.Path))).ToArray(); var progress = new Progress<double>(v => Progress.Value = v * 70); var projection = await Task.Run(() => new ProjectionEngine().Project(mesh, sourceFiles, project.TextureResolution, progress)); Progress.Value = 75; textures = await Task.Run(() => new PbrGenerator().Generate(projection.Surface, project.DefaultMaterial, project.Roughness, project.Metalness, project.NormalStrength)); textures.Maps[MapKind.Coverage] = projection.Coverage; ShowMaps(); var total = project.TextureResolution * project.TextureResolution; Status.Text = $"PBR set generated — {projection.ObservedTexels * 100d / total:F1}% observed, {projection.WeakTexels * 100d / total:F1}% weak"; Log($"Generated texture set; observed={projection.ObservedTexels}, weak={projection.WeakTexels}"); }
        catch (Exception ex) { Error(ex); } finally { Progress.Visibility = Visibility.Collapsed; }
    }
    private void Export_Click(object sender, RoutedEventArgs e) { if (textures is null) { MessageBox.Show("Generate a texture set first.", "Nothing to export"); return; } var d = new OpenFolderDialog { Title = "Export PBR texture set", Multiselect = false }; if (d.ShowDialog() != true) return; Try(() => { var paths = TextureExporter.Export(textures, d.FolderName, project.Name); Status.Text = $"Exported {paths.Count} maps to {d.FolderName}"; Process.Start("explorer.exe", d.FolderName); }); }
    private async void WebAssist_Click(object sender, RoutedEventArgs e) { if (textures is null) { MessageBox.Show("Generate maps first so Web Assist has controlled input.", "Web Assist"); return; } if (MessageBox.Show("Web Assist sends files outside this computer only after you manually attach them in your browser. Continue preparing files?", "External processing disclosure", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; var r = await new ChatGptWebAssistProvider().PrepareAsync(textures[MapKind.Albedo], textures.Maps.GetValueOrDefault(MapKind.Coverage), "clean lighting and repair only masked unseen regions", default); MessageBox.Show(r.Instructions, "Semi-automatic Web Assist", MessageBoxButton.OK, MessageBoxImage.Information); Process.Start(new ProcessStartInfo("https://chatgpt.com") { UseShellExecute = true }); }
    private void Logs_Click(object sender, RoutedEventArgs e) { Process.Start("explorer.exe", $"/select,\"{logPath}\""); }
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show("PBR Reference Forge v0.1.0-alpha\n\nArtist-oriented local PBR texture reconstruction for UV-mapped assets. Reconstruction is inferred and not physically measured.", "About");
    private void ReadSettings() { project.DefaultMaterial = (MaterialKind)(MaterialCombo.SelectedItem ?? MaterialKind.Dielectric); project.TextureResolution = int.Parse(((ComboBoxItem)ResolutionCombo.SelectedItem).Content.ToString()!); project.Roughness = (float)RoughnessSlider.Value; project.Metalness = (float)MetalSlider.Value; project.NormalStrength = (float)NormalSlider.Value; }

    private void RenderMesh()
    {
        if (mesh is null) return; foreach (var v in Viewport.Children.OfType<ModelVisual3D>().Skip(1).ToArray()) Viewport.Children.Remove(v); var geo = new MeshGeometry3D();
        foreach (var v in mesh.Vertices) { var p = mesh.Positions[v.Position]; geo.Positions.Add(new(p.X, p.Y, p.Z)); if (v.TexCoord >= 0) { var uv = mesh.TexCoords[v.TexCoord]; geo.TextureCoordinates.Add(new(uv.X, uv.Y)); } }
        foreach (var i in mesh.Indices) geo.TriangleIndices.Add(i); var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(145, 151, 158))); Viewport.Children.Add(new ModelVisual3D { Content = new GeometryModel3D(geo, mat) { BackMaterial = mat } }); FitCamera();
    }
    private void RenderUv() { if (mesh is null || !mesh.HasUvs) { UvEmpty.Visibility = Visibility.Visible; return; } var coverage = UvServices.RasterizeUvCoverage(mesh, 512); UvImage.Source = ImageIo.ToBitmap(coverage); UvEmpty.Visibility = Visibility.Collapsed; }
    private void ShowMaps() { MapStrip.Items.Clear(); if (textures is null) return; foreach (var (kind, image) in textures.Maps) { var panel = new StackPanel { Margin = new(5) }; panel.Children.Add(new Image { Source = ImageIo.ToBitmap(image), Width = 92, Height = 92, Stretch = Stretch.UniformToFill }); panel.Children.Add(new TextBlock { Text = kind.ToString(), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 }); panel.MouseLeftButtonUp += (_, _) => { ReferenceBackdrop.Source = ImageIo.ToBitmap(image); ReferenceBackdrop.Opacity = 1; ViewportMode.Text = $"Map solo: {kind}"; }; MapStrip.Items.Add(panel); } MapHint.Text = "Click a map to solo it in the viewport"; }
    private void FitCamera() { if (mesh is null) return; var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue); foreach (var p in mesh.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); } distance = Math.Max(1.5, (max - min).Length() * 1.4); UpdateCamera(); }
    private void UpdateCamera() { var yr = yaw * Math.PI / 180; var pr = pitch * Math.PI / 180; var pos = new Point3D(distance * Math.Cos(pr) * Math.Sin(yr), distance * Math.Sin(pr), distance * Math.Cos(pr) * Math.Cos(yr)); Camera.Position = pos; Camera.LookDirection = new(-pos.X, -pos.Y, -pos.Z); }
    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e) { dragging = true; dragStart = e.GetPosition(Viewport); Viewport.CaptureMouse(); }
    private void Viewport_MouseMove(object sender, MouseEventArgs e) { if (!dragging) return; var p = e.GetPosition(Viewport); yaw += (p.X - dragStart.X) * .35; pitch = Math.Clamp(pitch - (p.Y - dragStart.Y) * .35, -89, 89); dragStart = p; UpdateCamera(); }
    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e) { dragging = false; Viewport.ReleaseMouseCapture(); }
    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e) { distance = Math.Clamp(distance * (e.Delta > 0 ? .88 : 1.14), .05, 10000); UpdateCamera(); }
    private void OverlaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ReferenceBackdrop is not null) ReferenceBackdrop.Opacity = e.NewValue; }
    private void ViewOption_Changed(object sender, RoutedEventArgs e) { if (ViewportMode is null) return; var modes = new List<string> { "PBR Preview" }; if (WireframeCheck.IsChecked == true) modes.Add("Wireframe"); if (SeamsCheck.IsChecked == true && mesh is not null) modes.Add($"{UvServices.FindSeams(mesh).Count} seams"); ViewportMode.Text = string.Join("  •  ", modes); }
    private void Try(Action action) { try { action(); } catch (Exception ex) { Error(ex); } }
    private void Error(Exception ex) { Log(ex.ToString()); Status.Text = "Operation failed — see logs"; MessageBox.Show(ex.Message, "PBR Reference Forge", MessageBoxButton.OK, MessageBoxImage.Error); }
    private void Log(string value) { File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {value}{Environment.NewLine}"); }
}
