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
    private ForgeProject project = new(); private MeshData? mesh; private TextureSet? textures; private string? projectPath; private string? uvLayoutImagePath; private Point dragStart; private double yaw = 25, pitch = -15, distance = 4; private bool dragging; private bool synchronizingReferenceUi;
    private readonly string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBRReferenceForge", "logs", "app.log");
    private readonly string preferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBRReferenceForge", "preferences.json");
    private AppPreferences preferences = new(); private readonly CodexBridge codexBridge = new();
    public MainWindow()
    {
        InitializeComponent(); Directory.CreateDirectory(Path.GetDirectoryName(logPath)!); Log("Application started");
        MaterialCombo.ItemsSource = Enum.GetValues<MaterialKind>(); MaterialCombo.SelectedItem = MaterialKind.Dielectric; QuickMaterialCombo.ItemsSource = Enum.GetValues<MaterialKind>(); QuickMaterialCombo.SelectedItem = MaterialKind.Dielectric; RoleCombo.ItemsSource = Enum.GetValues<ReferenceRole>(); RoleCombo.SelectedItem = ReferenceRole.Custom;
        preferences = AppPreferenceStore.Load(preferencesPath); UpdateAccountUi(); AddLights(); Closing += (_, _) => { if (projectPath is not null) Try(() => ProjectStore.Save(project, projectPath + ".autosave")); };
    }
    private void AddLights() { var group = new Model3DGroup(); group.Children.Add(new AmbientLight(Color.FromRgb(85, 90, 100))); group.Children.Add(new DirectionalLight(Color.FromRgb(245, 240, 225), new(-.7, -1, -1))); group.Children.Add(new DirectionalLight(Color.FromRgb(90, 130, 160), new(.7, .2, .4))); Viewport.Children.Add(new ModelVisual3D { Content = group }); }

    private void ImportModel_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "3D Models|*.obj;*.glb;*.gltf|Wavefront OBJ|*.obj|glTF|*.glb;*.gltf" }; if (d.ShowDialog() != true) return;
        Try(() => { mesh = ModelImporter.Load(d.FileName); uvLayoutImagePath = null; project.ModelPath = d.FileName; project.Name = Path.GetFileNameWithoutExtension(d.FileName); ModelName.Text = Path.GetFileName(d.FileName); MeshStats.Text = $"{mesh.Positions.Count:N0} vertices  •  {mesh.TriangleCount:N0} triangles\n{mesh.TexCoords.Count:N0} UVs  •  {mesh.MaterialSlots.Count} materials"; QuickModelLabel.Text = Path.GetFileName(d.FileName); QuickModelDetails.Text = mesh.HasUvs ? $"3D model  •  UVs verified  •  {mesh.TriangleCount:N0} triangles" : "Missing UV coordinates — choose another source"; QuickModelDetails.Foreground = new SolidColorBrush(mesh.HasUvs ? Color.FromRgb(83, 203, 161) : Color.FromRgb(220, 95, 103)); RenderMesh(); RenderUv(); Status.Text = mesh.HasUvs ? "Model imported — UVs validated" : "Warning: this model has no complete UV coordinates"; QuickStatus.Text = Status.Text; if (!mesh.HasUvs) MessageBox.Show("The model is missing UV coordinates. Preview is available, but generation/export will remain disabled until a UV-mapped model or UV layout image is imported.", "Missing UVs", MessageBoxButton.OK, MessageBoxImage.Warning); Log($"Imported model {d.FileName}"); });
    }
    private void QuickUvImage_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "UV layout image|*.png;*.jpg;*.jpeg;*.webp" }; if (d.ShowDialog() != true) return;
        Try(() => LoadQuickUvLayout(d.FileName));
    }
    private void AddReference_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Reference images|*.png;*.jpg;*.jpeg;*.webp", Multiselect = true }; if (d.ShowDialog() != true) return;
        foreach (var f in d.FileNames) Try(() => { _ = ImageIo.Load(f); var role = project.References.Count switch { 0 => ReferenceRole.Front, 1 => ReferenceRole.Back, 2 => ReferenceRole.Left, 3 => ReferenceRole.Right, 4 => ReferenceRole.Top, 5 => ReferenceRole.Bottom, _ => ReferenceRole.Custom }; project.References.Add(new() { Path = f, Role = role }); ReferenceList.Items.Add($"{Path.GetFileName(f)}  [{role}]"); QuickReferenceLabel.Text = Path.GetFileName(f); SetQuickReferencePreview(f); }); if (project.References.Count > 0) ReferenceList.SelectedIndex = project.References.Count - 1; Status.Text = $"{project.References.Count} reference image(s) loaded";
    }
    private void QuickReference_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Texture reference|*.png;*.jpg;*.jpeg;*.webp" }; if (d.ShowDialog() != true) return;
        Try(() => LoadQuickReference(d.FileName));
    }
    private void SetQuickReferencePreview(string path) { var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new(Path.GetFullPath(path), UriKind.Absolute); bmp.EndInit(); QuickReferencePreview.Source = bmp; }
    private void ReferenceList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (synchronizingReferenceUi) return; var index = ReferenceList.SelectedIndex; if (index < 0 || index >= project.References.Count) return; synchronizingReferenceUi = true; try { RoleCombo.SelectedItem = project.References[index].Role; } finally { synchronizingReferenceUi = false; } Try(() => { var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new(Path.GetFullPath(project.References[index].Path), UriKind.Absolute); bmp.EndInit(); ReferenceBackdrop.Source = bmp; }); }
    private void RoleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (synchronizingReferenceUi || ReferenceList is null || ReferenceList.SelectedIndex < 0 || ReferenceList.SelectedIndex >= project.References.Count || RoleCombo.SelectedItem is not ReferenceRole role) return; var index = ReferenceList.SelectedIndex; project.References[index].Role = role; synchronizingReferenceUi = true; try { ReferenceList.Items[index] = $"{Path.GetFileName(project.References[index].Path)}  [{role}]"; ReferenceList.SelectedIndex = index; } finally { synchronizingReferenceUi = false; } }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "PBR Reference Forge Project|*.tforge" }; if (d.ShowDialog() != true) return;
        Try(() => { project = ProjectStore.Load(d.FileName); projectPath = d.FileName; textures = null; MapStrip.Items.Clear(); ReferenceList.Items.Clear(); foreach (var r in project.References) ReferenceList.Items.Add($"{Path.GetFileName(r.Path)}  [{r.Role}]"); MaterialCombo.SelectedItem = project.DefaultMaterial; QuickMaterialCombo.SelectedItem = project.DefaultMaterial; RoughnessSlider.Value = project.Roughness; MetalSlider.Value = project.Metalness; NormalSlider.Value = project.NormalStrength; var wanted = ResolutionCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Content?.ToString() == project.TextureResolution.ToString()); if (wanted is not null) ResolutionCombo.SelectedItem = wanted; var quickWanted = QuickResolutionCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Content?.ToString() == project.TextureResolution.ToString()); if (quickWanted is not null) QuickResolutionCombo.SelectedItem = quickWanted; if (!string.IsNullOrWhiteSpace(project.ModelPath) && File.Exists(project.ModelPath)) { mesh = ModelImporter.Load(project.ModelPath); ModelName.Text = Path.GetFileName(project.ModelPath); QuickModelLabel.Text = ModelName.Text; QuickModelDetails.Text = mesh.HasUvs ? $"UVs verified  •  {mesh.TriangleCount:N0} triangles" : "Missing UV coordinates"; MeshStats.Text = $"{mesh.Positions.Count:N0} vertices  •  {mesh.TriangleCount:N0} triangles\n{mesh.TexCoords.Count:N0} UVs  •  {mesh.MaterialSlots.Count} materials"; RenderMesh(); RenderUv(); } else { mesh = null; ModelName.Text = "Source model is missing"; QuickModelLabel.Text = ModelName.Text; } if (project.References.Count > 0) { ReferenceList.SelectedIndex = 0; QuickReferenceLabel.Text = Path.GetFileName(project.References[0].Path); if (File.Exists(project.References[0].Path)) SetQuickReferencePreview(project.References[0].Path); } Status.Text = "Project loaded"; QuickStatus.Text = Status.Text; Log($"Loaded {d.FileName}"); });
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
    private async void QuickGenerate_Click(object sender, RoutedEventArgs e)
    {
        if ((mesh is null || !mesh.HasUvs) && string.IsNullOrWhiteSpace(uvLayoutImagePath)) { MessageBox.Show("Choose either a UV-mapped 3D model or a UV layout image.", "UV source required", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (project.References.Count == 0) { MessageBox.Show("Choose a texture reference image.", "Reference required", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var resolution = int.Parse(((ComboBoxItem)QuickResolutionCombo.SelectedItem).Content.ToString()!);
        if (resolution == 8192 && GC.GetGCMemoryInfo().TotalAvailableMemoryBytes < 20L * 1024 * 1024 * 1024) { MessageBox.Show("8K generation needs substantial memory. Choose 4K or run on a machine with at least 20 GB of available managed memory.", "Insufficient memory for 8K", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var save = new SaveFileDialog { Filter = "PBR texture ZIP|*.zip", FileName = $"{project.Name}-PBR-Textures.zip" }; if (save.ShowDialog() != true) return;
        project.TextureResolution = resolution; project.DefaultMaterial = (MaterialKind)(QuickMaterialCombo.SelectedItem ?? MaterialKind.Dielectric); MaterialCombo.SelectedItem = project.DefaultMaterial;
        QuickGenerateButton.IsEnabled = false; QuickProgress.Visibility = Visibility.Visible; QuickProgress.Value = 0; QuickStatus.Text = "Projecting the reference into UV space…";
        try
        {
            var codex = await codexBridge.GetStatusAsync(); if (!codex.Installed || !codex.LoggedIn) throw new InvalidOperationException("Codex GPT is not ready. Click Set Up Codex + GPT first. Local fallback is disabled.");
            var job = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBRReferenceForge", "jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job); string layoutPath; ImageBuffer layout;
            if (!string.IsNullOrWhiteSpace(uvLayoutImagePath)) { layoutPath = Path.GetFullPath(uvLayoutImagePath); layout = ImageIo.Load(layoutPath); }
            else { layout = UvServices.RasterizeUvCoverage(mesh!, Math.Min(resolution, 2048)); layoutPath = Path.Combine(job, "uv-layout.png"); ImageIo.SavePng(layout, layoutPath); }
            QuickStatus.Text = "Codex is generating the UV texture with GPT Image…"; QuickProgress.Value = 25; var generatedPath = await codexBridge.GenerateUvTextureAsync(layoutPath, project.References[0].Path, job); QuickProgress.Value = 72; var generated = ImageResizer.Resize(ImageIo.Load(generatedPath), resolution, resolution); var coverage = new UvLayoutProjector().Project(layout, generated, resolution).Coverage;
            QuickStatus.Text = "Deriving PBR maps from the GPT texture…"; textures = await Task.Run(() => new PbrGenerator().Generate(generated, project.DefaultMaterial, project.Roughness, project.Metalness, project.NormalStrength)); textures.Maps[MapKind.Coverage] = coverage; QuickProgress.Value = 88;
            var requested = new[] { MapKind.Diffuse, MapKind.Albedo, MapKind.Roughness, MapKind.Normal, MapKind.Height, MapKind.Metalness }; var names = await Task.Run(() => TextureExporter.ExportZip(textures, save.FileName, project.Name, requested)); QuickProgress.Value = 100; ShowMaps(); QuickStatus.Text = $"Done — {names.Count} maps saved in {Path.GetFileName(save.FileName)}"; Status.Text = QuickStatus.Text; Log($"Quick exported {save.FileName}"); MessageBox.Show($"Your PBR texture ZIP is ready.\n\n{save.FileName}\n\nDiffuse, Albedo, Roughness, Normal, Displacement and Metalness are included.", "PBR ZIP created", MessageBoxButton.OK, MessageBoxImage.Information); Process.Start("explorer.exe", $"/select,\"{save.FileName}\"");
        }
        catch (Exception ex) { Error(ex); QuickStatus.Text = "Generation failed — details were written to the log"; }
        finally { QuickGenerateButton.IsEnabled = true; QuickProgress.Visibility = Visibility.Collapsed; }
    }
    private async void WebAssist_Click(object sender, RoutedEventArgs e) { if (textures is null) { MessageBox.Show("Generate maps first so Web Assist has controlled input.", "Web Assist"); return; } if (MessageBox.Show("Web Assist sends files outside this computer only after you manually attach them in your browser. Continue preparing files?", "External processing disclosure", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; var r = await new ChatGptWebAssistProvider().PrepareAsync(textures[MapKind.Albedo], textures.Maps.GetValueOrDefault(MapKind.Coverage), "clean lighting and repair only masked unseen regions", default); MessageBox.Show(r.Instructions, "Semi-automatic Web Assist", MessageBoxButton.OK, MessageBoxImage.Information); Process.Start(new ProcessStartInfo("https://chatgpt.com") { UseShellExecute = true }); }
    private void Logs_Click(object sender, RoutedEventArgs e) { Process.Start("explorer.exe", $"/select,\"{logPath}\""); }
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show("PBR Reference Forge v0.4.0-alpha\n\nUses real GPT image generation through Codex OAuth. No API key and no silent local fallback.", "About");
    private async void ChatGptSignIn_Click(object sender, RoutedEventArgs e)
    {
        ChatGptAccountButton.IsEnabled = false; try { var status = await codexBridge.GetStatusAsync(); if (!status.Installed) { if (MessageBox.Show("Codex CLI is required for real GPT image generation. Download and install the official OpenAI Codex CLI now?", "Install Codex", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; QuickStatus.Text = "Downloading official Codex CLI…"; await codexBridge.InstallAsync(new Progress<double>(v => QuickStatus.Text = $"Downloading Codex… {v:P0}")); status = await codexBridge.GetStatusAsync(); } if (!status.LoggedIn) { QuickStatus.Text = "Opening secure Codex sign-in…"; await codexBridge.LoginAsync(); status = await codexBridge.GetStatusAsync(); } if (!status.LoggedIn) throw new InvalidOperationException("Codex sign-in was not completed."); ChatGptAccountButton.Content = "Codex + GPT Ready  ✓"; QuickStatus.Text = "Codex OAuth connected — real GPT generation is ready"; Log("Codex OAuth verified"); } catch (Exception ex) { Error(ex); QuickStatus.Text = "Codex setup failed — see error and logs"; } finally { ChatGptAccountButton.IsEnabled = true; }
    }
    private void ShowAdvanced_Click(object sender, RoutedEventArgs e) { QuickWorkspace.Visibility = Visibility.Collapsed; }
    private void ShowQuick_Click(object sender, RoutedEventArgs e) { QuickWorkspace.Visibility = Visibility.Visible; }
    private void ReadSettings() { project.DefaultMaterial = (MaterialKind)(MaterialCombo.SelectedItem ?? MaterialKind.Dielectric); project.TextureResolution = int.Parse(((ComboBoxItem)ResolutionCombo.SelectedItem).Content.ToString()!); project.Roughness = (float)RoughnessSlider.Value; project.Metalness = (float)MetalSlider.Value; project.NormalStrength = (float)NormalSlider.Value; }

    internal void LoadQuickUvLayout(string path)
    {
        var image = ImageIo.Load(path); uvLayoutImagePath = path; mesh = null; project.ModelPath = null; project.Name = Path.GetFileNameWithoutExtension(path); QuickModelLabel.Text = Path.GetFileName(path); QuickModelDetails.Text = $"UV layout image  •  {image.Width} × {image.Height}  •  island detection enabled"; QuickModelDetails.Foreground = new SolidColorBrush(Color.FromRgb(83, 203, 161)); ModelName.Text = "Image-only UV source"; MeshStats.Text = Path.GetFileName(path); UvImage.Source = ImageIo.ToBitmap(image); UvEmpty.Visibility = Visibility.Collapsed; QuickStatus.Text = "UV layout loaded — image-based projection mode"; Log($"Imported UV layout image {path}");
    }
    internal void LoadQuickReference(string path)
    {
        _ = ImageIo.Load(path); synchronizingReferenceUi = true; try { ReferenceList.SelectedIndex = -1; ReferenceList.Items.Clear(); project.References.Clear(); project.References.Add(new() { Path = path, Role = ReferenceRole.Front }); ReferenceList.Items.Add($"{Path.GetFileName(path)}  [Front]"); QuickReferenceLabel.Text = Path.GetFileName(path); SetQuickReferencePreview(path); RoleCombo.SelectedItem = ReferenceRole.Front; ReferenceList.SelectedIndex = 0; } finally { synchronizingReferenceUi = false; } QuickStatus.Text = "Reference loaded — ready to generate"; Log($"Imported quick reference {path}");
    }
    internal bool HasQuickReference => project.References.Count == 1 && ReferenceList.Items.Count == 1 && ReferenceList.SelectedIndex == 0;
    private void UpdateAccountUi() { ChatGptAccountButton.Content = "Set Up Codex + GPT"; }

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
