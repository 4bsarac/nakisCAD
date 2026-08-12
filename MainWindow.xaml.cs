using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using NakisCAD.Core.DST;
using NakisCAD.Core.Digitizing;
using NakisCAD.Core.Models;
using OpenCvSharp;
using Window = System.Windows.Window;
using Point = System.Windows.Point;
using Path = System.IO.Path;

namespace NakisCAD.App;

public partial class MainWindow : Window
{
    private EmbroideryDesign? _currentDesign;
    private string? _currentFilePath;
    private List<StitchItem> _allStitchItems = new();
    private List<ColorObjectItem> _colorObjectItems = new();
    private bool _showStitches = true;
    private bool _showOutlines = true;
    private bool _showGrid = true;
    private double _zoomLevel = 1.0;

    // Pan state
    private bool _isPanning;
    private Point _lastPanPoint;

    // Commands (static for XAML binding)
    public static RoutedCommand NewProjectCmd { get; } = new();
    public static RoutedCommand OpenDstCmd { get; } = new();
    public static RoutedCommand LoadImageCmd { get; } = new();
    public static RoutedCommand AutoDigitizeCmd { get; } = new();
    public static RoutedCommand SaveCmd { get; } = new();
    public static RoutedCommand SaveAsCmd { get; } = new();
    public static RoutedCommand ZoomInCmd { get; } = new();
    public static RoutedCommand ZoomOutCmd { get; } = new();
    public static RoutedCommand ZoomFitCmd { get; } = new();
    public static RoutedCommand Zoom100Cmd { get; } = new();
    public static RoutedCommand ToggleGridCmd { get; } = new();
    public static RoutedCommand ExportDstCmd { get; } = new();

    // Default thread colors (Madeira Polyneon inspired)
    private readonly Color[] _defaultColors = {
        Color.FromRgb(220, 20, 20),    // Red
        Color.FromRgb(0, 70, 180),     // Blue
        Color.FromRgb(0, 150, 50),     // Green
        Color.FromRgb(255, 200, 0),    // Yellow
        Color.FromRgb(255, 110, 0),    // Orange
        Color.FromRgb(140, 30, 160),   // Purple
        Color.FromRgb(139, 69, 19),    // Brown
        Color.FromRgb(0, 0, 0),        // Black
        Color.FromRgb(255, 255, 255),  // White
        Color.FromRgb(255, 105, 180),  // Pink
        Color.FromRgb(0, 180, 180),    // Teal
        Color.FromRgb(200, 50, 0),     // Dark Red
        Color.FromRgb(0, 100, 0),      // Dark Green
        Color.FromRgb(100, 0, 180),    // Violet
        Color.FromRgb(255, 150, 50),   // Light Orange
        Color.FromRgb(0, 150, 200),    // Cyan
    };

    public MainWindow()
    {
        InitializeComponent();
        RegisterCommands();
        InitializeColorBar();
        Log("NakisCAD v1.0 baslatildi.");
        Log("Ctrl+I ile resim yukleyin, Ctrl+D veya F5 ile digitize edin.");
    }

    private void RegisterCommands()
    {
        CommandBindings.Add(new CommandBinding(NewProjectCmd, (s, e) => OnNewProject(s!, e)));
        CommandBindings.Add(new CommandBinding(OpenDstCmd, (s, e) => OnOpenDst(s!, e)));
        CommandBindings.Add(new CommandBinding(LoadImageCmd, (s, e) => OnLoadImage(s!, e)));
        CommandBindings.Add(new CommandBinding(AutoDigitizeCmd, (s, e) => OnAutoDigitize(s!, e)));
        CommandBindings.Add(new CommandBinding(SaveCmd, (s, e) => OnSave(s!, e)));
        CommandBindings.Add(new CommandBinding(SaveAsCmd, (s, e) => OnSaveAs(s!, e)));
        CommandBindings.Add(new CommandBinding(ZoomInCmd, (s, e) => OnZoomIn(s!, e)));
        CommandBindings.Add(new CommandBinding(ZoomOutCmd, (s, e) => OnZoomOut(s!, e)));
        CommandBindings.Add(new CommandBinding(ZoomFitCmd, (s, e) => OnZoomFit(s!, e)));
        CommandBindings.Add(new CommandBinding(Zoom100Cmd, (s, e) => OnZoom100(s!, e)));
        CommandBindings.Add(new CommandBinding(ToggleGridCmd, (s, e) => OnToggleGrid(s!, e)));
        CommandBindings.Add(new CommandBinding(ExportDstCmd, (s, e) => OnExportDst(s!, e)));
    }

    // ===================== INITIALIZATION =====================

    private void InitializeColorBar()
    {
        colorBar.Children.Clear();
        for (int i = 0; i < _defaultColors.Length; i++)
        {
            var rect = new Border
            {
                Width = 32,
                Height = 24,
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(_defaultColors[i]),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"Renk {i + 1}"
            };
            colorBar.Children.Add(rect);
        }
    }

    // ===================== MENU & TOOLBAR =====================

    private void OnNewProject(object sender, RoutedEventArgs e)
    {
        _currentDesign = new EmbroideryDesign { Name = "Yeni Desen" };
        _currentFilePath = null;
        RefreshUI();
        Log("Yeni proje olusturuldu.");
        txtStatus.Text = "Yeni proje olusturuldu - dikisleri cizmeye baslayabilirsiniz";
    }

    private void OnOpenDst(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "DST Dosyalari (*.dst)|*.dst|Tum Dosyalar (*.*)|*.*",
            Title = "DST Dosyasi Sec"
        };

        if (dlg.ShowDialog() == true)
        {
            LoadDstFile(dlg.FileName);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_currentDesign == null) { ShowWarning("Once bir dosya acin veya yeni proje olusturun."); return; }
        if (_currentFilePath != null) SaveDstFile(_currentFilePath);
        else OnSaveAs(sender, e);
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        if (_currentDesign == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "DST Dosyalari (*.dst)|*.dst",
            Title = "DST Dosyasi Kaydet",
            FileName = _currentDesign.Name + ".dst"
        };
        if (dlg.ShowDialog() == true)
        {
            SaveDstFile(dlg.FileName);
            _currentFilePath = dlg.FileName;
        }
    }

    private void OnCreateSample(object sender, RoutedEventArgs e)
    {
        var design = new EmbroideryDesign { Name = "OrnekDesen" };
        int step = 10;
        int side = 500;

        design.Stitches.Add(new StitchCommand(0, 0, StitchType.Jump));
        design.Stitches.Add(new StitchCommand(0, 0, StitchType.Normal));

        for (int x = 0; x < side; x += step)
            design.Stitches.Add(new StitchCommand((short)step, 0, StitchType.Normal));
        for (int y = 0; y < side; y += step)
            design.Stitches.Add(new StitchCommand(0, (short)step, StitchType.Normal));
        for (int x = 0; x < side; x += step)
            design.Stitches.Add(new StitchCommand((short)(-step), 0, StitchType.Normal));
        for (int y = 0; y < side; y += step)
            design.Stitches.Add(new StitchCommand(0, (short)(-step), StitchType.Normal));

        design.Stitches.Add(new StitchCommand(0, 0, StitchType.End));

        string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ornek.dst");
        new DstWriter().Write(path, design);

        _currentDesign = design;
        _currentFilePath = path;
        RefreshUI();
        DrawDesign();
        Log($"Ornek desen olusturuldu: {design.Stitches.Count} dikis");
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    // ===================== IMAGE LOADING & AUTO-DIGITIZE =====================

    private string? _currentImagePath;

    private void OnLoadImage(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Goruntu Dosyalari|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tum Dosyalar|*.*",
            Title = "Goruntu Yukle"
        };

        if (dlg.ShowDialog() == true)
        {
            _currentImagePath = dlg.FileName;
            Log($"Goruntu yuklendi: {Path.GetFileName(dlg.FileName)}");
            txtStatus.Text = $"Goruntu yuklendi: {Path.GetFileName(dlg.FileName)}";

            // Oncelikle onizlemeyi goster
            ShowImagePreview(dlg.FileName);

            // Kullaniciya onay sor
            var result = MessageBox.Show(
                $"Goruntu yuklendi:\n{Path.GetFileName(dlg.FileName)}\n\n" +
                $"Digitize edilsin mi?\n\n" +
                $"Hedef boyut: {100}x{100} mm\n" +
                $"Renk sayisi: {8}\n\n" +
                $"Evet = Digitize Baslat\n" +
                $"Hayir = Sadece Onizle",
                "Digitize Onay",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                RunAutoDigitize();
            }
            else
            {
                Log("Digitize iptal edildi - sadece onizleme modu");
                txtStatus.Text = $"Onizleme: {Path.GetFileName(dlg.FileName)} - Digitize icin Ctrl+D'ye basin";
            }
        }
    }

    private void ShowImagePreview(string imagePath)
    {
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imagePath);
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            designCanvas.Visibility = Visibility.Visible;
            canvasPlaceholder.Visibility = Visibility.Hidden;
            designCanvas.Children.Clear();

            var image = new System.Windows.Controls.Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            designCanvas.Children.Add(image);
            Log("Goruntu onizlemede gosterildi");
        }
        catch (Exception ex)
        {
            Log($"Goruntu yuklenemedi: {ex.Message}");
        }
    }

    private void OnAutoDigitize(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentImagePath))
        {
            ShowWarning("Once bir goruntu yukleyin (Ctrl+I)");
            return;
        }
        RunAutoDigitize();
    }

    private void RunAutoDigitize()
    {
        if (string.IsNullOrEmpty(_currentImagePath)) return;

        try
        {
            txtStatus.Text = "Otomatik digitizing baslatiliyor...";
            Log("========================================");
            Log("OTOMATIK DIGITIZING BASLADI");
            Log($"Kaynak: {Path.GetFileName(_currentImagePath)}");
            Log("========================================");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var pipeline = new DigitizingPipeline();
            pipeline.OnProgress += msg => Log(msg);

            // Pipeline ayarlari - endustriyel standartlar
            pipeline.Options.TargetWidthMm = 100.0;    // 100mm (4 inç)
            pipeline.Options.TargetHeightMm = 100.0;   // 100mm (4 inç)
            pipeline.Options.ResolutionPxPerMm = 8.0;  // 8 piksel/mm (200 DPI)
            pipeline.Options.ColorCount = 8;
            pipeline.Options.SimplifyTolerance = 1.0;
            pipeline.Options.MinColorPercentage = 2.0;  // %2 altindakileri atla
            pipeline.Options.MinStitchLengthMm = 0.4;
            pipeline.Options.JumpThresholdMm = 5.0;     // 5mm ustune jump
            pipeline.Options.Classify.SatinMaxWidth = 10.0; // 10mm alti satin

            // Calistir
            _currentDesign = pipeline.Process(_currentImagePath);
            sw.Stop();

            // DST'ye kaydet
            string dstPath = Path.ChangeExtension(_currentImagePath, ".dst");
            new DstWriter().Write(dstPath, _currentDesign);
            _currentDesign.Name = Path.GetFileNameWithoutExtension(dstPath);
            _currentFilePath = dstPath;

            // ===== DETAYLI ANALIZ =====
            var s = _currentDesign.Stitches;
            int normalCount = s.Count(st => st.Type == StitchType.Normal);
            int jumpCount = s.Count(st => st.Type == StitchType.Jump);
            int colorChangeCount = s.Count(st => st.Type == StitchType.ColorChange);
            int endCount = s.Count(st => st.Type == StitchType.End);

            Log("");
            Log("========== DIGITIZING RAPOR ==========");
            Log($"Dosya: {Path.GetFileName(dstPath)}");
            Log($"Sure: {sw.Elapsed.TotalSeconds:F1} saniye");
            Log($"------------------------------------");
            Log($"TOPLAM DIKIS:      {normalCount:N0}");
            Log($"ATLAMA (Jump):     {jumpCount:N0}");
            Log($"RENK DEGISIMI:     {colorChangeCount}");
            Log($"BITIS (End):        {endCount}");
            Log($"------------------------------------");
            Log($"BOYUT: {_currentDesign.WidthMm:F1} x {_currentDesign.HeightMm:F1} mm");
            Log($"RENK SAYISI: {_currentDesign.ColorPalette.Count}");
            Log($"------------------------------------");

            // Renk dagilimi
            Log("RENK DAGILIMI:");
            for (int i = 0; i < _currentDesign.ColorPalette.Count; i++)
            {
                var c = _currentDesign.ColorPalette[i];
                Log($"  Renk {i + 1}: RGB({c.R},{c.G},{c.B})");
            }

            Log($"------------------------------------");
            Log($"DST dosyasi kaydedildi: {Path.GetFileName(dstPath)}");
            Log("========================================");
            Log("DIGITIZING TAMAMLANDI!");
            Log("========================================");

            // Arayuzu guncelle
            RefreshUI();
            DrawDesign();

            // Canvas render edildikten sonra zoom yap
            Dispatcher.BeginInvoke(new Action(() =>
            {
                OnZoomFit(this, new RoutedEventArgs());
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // Status bar guncelle
            txtStatus.Text = $"Tamamlandi! {normalCount:N0} dikiş | {jumpCount} atlama | {colorChangeCount} renk degisimi | {_currentDesign.WidthMm:F1}x{_currentDesign.HeightMm:F1}mm | {sw.Elapsed.TotalSeconds:F1}s";
        }
        catch (Exception ex)
        {
            Log($"!!! HATA !!!");
            Log($"Tur: {ex.GetType().Name}");
            Log($"Mesaj: {ex.Message}");
            if (ex.InnerException != null)
                Log($"Ic Hata: {ex.InnerException.Message}");
            Log(ex.StackTrace ?? "");
            Log("========================================");
            MessageBox.Show($"Digitizing hatasi:\n\n{ex.Message}\n\n{ex.InnerException?.Message}",
                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "Digitizing hatasi - loglari kontrol edin";
        }
    }

    private void OnDstAnalysis(object sender, RoutedEventArgs e)
    {
        if (_currentDesign == null) { ShowWarning("Once bir DST dosyasi acin."); return; }
        var d = _currentDesign;
        var s = d.Stitches;
        int normalCount = s.Count(st => st.Type == StitchType.Normal);
        int jumpCount = s.Count(st => st.Type == StitchType.Jump);
        int colorChangeCount = s.Count(st => st.Type == StitchType.ColorChange);

        string info = $"=== DST ANALIZ ===\n";
        info += $"Desen: {d.Name}\n";
        info += $"Boyut: {d.WidthMm:F1} x {d.HeightMm:F1} mm\n\n";
        info += $"--- DIKIS ISTATISTIKLERI ---\n";
        info += $"Toplam Dikis:     {d.TotalStitches:N0}\n";
        info += $"Normal Dikis:     {normalCount:N0}\n";
        info += $"Atlama (Jump):    {jumpCount:N0}\n";
        info += $"Renk Degisimi:    {colorChangeCount}\n";
        info += $"Bitis (End):      {s.Count(st => st.Type == StitchType.End)}\n\n";
        info += $"Renk Sayisi:      {d.ColorPalette.Count}\n";
        info += $"Dikis Yogunlugu:  {(d.WidthMm > 0 && d.HeightMm > 0 ? (normalCount / (d.WidthMm * d.HeightMm) * 100).ToString("F1") : "N/A")} dikiş/cm²\n";
        MessageBox.Show(info, "DST Analiz", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnCountStitches(object sender, RoutedEventArgs e)
    {
        if (_currentDesign == null) return;
        var s = _currentDesign.Stitches;
        int normalCount = s.Count(st => st.Type == StitchType.Normal);
        int jumpCount = s.Count(st => st.Type == StitchType.Jump);
        Log($"--- DIKIS ISTATISTIKLERI ---");
        Log($"Toplam: {s.Count:N0} | Normal: {normalCount:N0} | Jump: {jumpCount:N0}");
        Log($"Boyut: {_currentDesign.WidthMm:F1} x {_currentDesign.HeightMm:F1} mm");
    }

    private void OnExportDst(object sender, RoutedEventArgs e)
    {
        // Eger desen yoksa ama resim yukluyse, once digitize et
        if (_currentDesign == null && !string.IsNullOrEmpty(_currentImagePath))
        {
            Log("Desen yok, once digitize ediliyor...");
            RunAutoDigitize();
        }

        if (_currentDesign == null)
        {
            ShowWarning("Once bir goruntu yukleyin (Ctrl+I)");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "DST Dosyalari (*.dst)|*.dst|Tum Dosyalar (*.*)|*.*",
            Title = "DST Olarak Disa Aktar",
            FileName = _currentDesign.Name + ".dst"
        };
        if (dlg.ShowDialog() == true)
        {
            SaveDstFile(dlg.FileName);
            _currentFilePath = dlg.FileName;
        }
    }

    // ===================== VIEW TOGGLES =====================

    private void OnToggleTrueView(object sender, RoutedEventArgs e) => Log("TrueView modu (yakinda)");
    private void OnToggleStitches(object sender, RoutedEventArgs e)
    {
        _showStitches = !_showStitches;
        if (_currentDesign != null) DrawDesign();
    }
    private void OnToggleOutlines(object sender, RoutedEventArgs e)
    {
        _showOutlines = !_showOutlines;
        if (_currentDesign != null) DrawDesign();
    }
    private void OnToggleGrid(object sender, RoutedEventArgs e)
    {
        _showGrid = !_showGrid;
        Log(_showGrid ? "Grid gorunur" : "Grid gizli");
    }

    // ===================== ZOOM =====================

    private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoomLevel * 1.2);
    private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoomLevel / 1.2);
    private void OnZoomFit(object sender, RoutedEventArgs e)
    {
        if (_currentDesign == null) return;
        var points = _currentDesign.GetAbsolutePoints();
        if (points.Count < 2) return;

        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double dw = maxX - minX, dh = maxY - minY;
        if (dw < 1 || dh < 1) return;

        double cw = designCanvas.ActualWidth > 0 ? designCanvas.ActualWidth : 800;
        double ch = designCanvas.ActualHeight > 0 ? designCanvas.ActualHeight : 500;

        double scale = Math.Min((cw - 80) / dw, (ch - 80) / dh);
        SetZoom(scale);

        double ox = (cw - dw * scale) / 2 - minX * scale;
        double oy = (ch - dh * scale) / 2 - minY * scale;
        canvasTranslate.X = ox;
        canvasTranslate.Y = oy;
    }
    private void OnZoom100(object sender, RoutedEventArgs e) { SetZoom(1.0); canvasTranslate.X = 0; canvasTranslate.Y = 0; }

    private void SetZoom(double zoom)
    {
        _zoomLevel = Math.Clamp(zoom, 0.05, 50.0);
        canvasScale.ScaleX = _zoomLevel;
        canvasScale.ScaleY = _zoomLevel;
        txtZoomLevel.Text = $"{(_zoomLevel * 100):F0}%";
        txtZoomStatus.Text = $"{(_zoomLevel * 100):F0}%";
    }

    // ===================== FILE OPERATIONS =====================

    private void LoadDstFile(string path)
    {
        try
        {
            Log($"Okunuyor: {System.IO.Path.GetFileName(path)}");
            var reader = new DstReader();
            _currentDesign = reader.Read(path);
            _currentFilePath = path;

            var colors = SidecarRgbReader.Read(path);
            if (colors.Count > 0)
            {
                _currentDesign.ColorPalette = colors;
                Log($"Yoldas RGB bulundu: {colors.Count} renk");
            }

            RefreshUI();
            DrawDesign();
            Log($"Yuklendi: {_currentDesign.TotalStitches:N0} dikis, {_currentDesign.TotalColorChanges} renk degisimi");
            Log($"Boyut: {_currentDesign.WidthMm:F1} x {_currentDesign.HeightMm:F1} mm");
            txtStatus.Text = $"Yuklendi: {_currentDesign.Name} | {_currentDesign.TotalStitches:N0} dikis | {_currentDesign.WidthMm:F1}x{_currentDesign.HeightMm:F1}mm";
            txtFilePath.Text = path;
        }
        catch (Exception ex)
        {
            Log($"HATA: {ex.Message}");
            MessageBox.Show($"Dosya okunamadi:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveDstFile(string path)
    {
        try
        {
            new DstWriter().Write(path, _currentDesign!);
            Log($"Kaydedildi: {System.IO.Path.GetFileName(path)}");
            txtStatus.Text = $"Kaydedildi: {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Log($"Kayit hatasi: {ex.Message}");
            MessageBox.Show($"Kaydedilemedi:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===================== UI REFRESH =====================

    private void RefreshUI()
    {
        if (_currentDesign == null) return;

        txtDesignName.Text = _currentDesign.Name;
        txtTotalStitches.Text = _currentDesign.TotalStitches.ToString("N0");
        txtColorChanges.Text = _currentDesign.ColorPalette.Count.ToString();
        txtWidth.Text = _currentDesign.WidthMm.ToString("F1");
        txtHeight.Text = _currentDesign.HeightMm.ToString("F1");

        var s = _currentDesign.Stitches;
        txtNormalCount.Text = s.Count(st => st.Type == StitchType.Normal).ToString("N0");
        txtJumpCount.Text = s.Count(st => st.Type == StitchType.Jump).ToString("N0");
        txtColorChangeCount.Text = s.Count(st => st.Type == StitchType.ColorChange).ToString();
        txtEndCount.Text = s.Count(st => st.Type == StitchType.End).ToString();

        // Stitch list
        _allStitchItems.Clear();
        for (int i = 0; i < s.Count; i++)
            _allStitchItems.Add(new StitchItem { Index = i, TypeName = s[i].Type.ToString(), DeltaX = s[i].DeltaX, DeltaY = s[i].DeltaY });
        lstStitches.ItemsSource = _allStitchItems;

        // Color-Object list
        _colorObjectItems.Clear();
        int colorIdx = 0;
        int blockStart = 0;
        for (int i = 0; i < s.Count; i++)
        {
            if (s[i].Type == StitchType.ColorChange || s[i].Type == StitchType.End || i == s.Count - 1)
            {
                int count = i - blockStart;
                if (count > 0)
                {
                    bool hasColor = colorIdx < _currentDesign.ColorPalette.Count;
                    var color = hasColor ? _currentDesign.ColorPalette[colorIdx] : default;

                    _colorObjectItems.Add(new ColorObjectItem
                    {
                        Name = hasColor
                            ? $"Renk {colorIdx + 1} - RGB({color.R},{color.G},{color.B})"
                            : $"Renk {colorIdx + 1}",
                        StitchType = "Dikis",
                        Count = count,
                        ColorBrush = hasColor
                            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B))
                            : new SolidColorBrush(_defaultColors[colorIdx % _defaultColors.Length])
                    });
                }
                colorIdx++;
                blockStart = i + 1;
            }
        }
        lstColorObjects.ItemsSource = _colorObjectItems;

        // Renk paletini guncelle
        UpdateColorBar();

        Title = $"NakisCAD - {_currentDesign.Name}";
    }

    private void UpdateColorBar()
    {
        if (_currentDesign == null || _currentDesign.ColorPalette.Count == 0) return;

        colorBar.Children.Clear();
        for (int i = 0; i < _currentDesign.ColorPalette.Count; i++)
        {
            var c = _currentDesign.ColorPalette[i];
            var rect = new Border
            {
                Width = 32,
                Height = 24,
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"Renk {i + 1}: RGB({c.R},{c.G},{c.B})"
            };
            colorBar.Children.Add(rect);
        }
    }

    // ===================== CANVAS DRAWING =====================

    private void DrawDesign()
    {
        if (_currentDesign == null || _currentDesign.Stitches.Count == 0) return;

        designCanvas.Visibility = Visibility.Visible;
        canvasPlaceholder.Visibility = Visibility.Hidden;
        designCanvas.Children.Clear();

        var points = _currentDesign.GetAbsolutePoints();
        if (points.Count < 2) return;

        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double dw = maxX - minX, dh = maxY - minY;
        if (dw < 0.1 || dh < 0.1) return;

        // Grid ciz
        DrawGrid(minX, minY, maxX, maxY);

        // Cerceve ciz (100x100mm varsayilan)
        double frameW = 100;
        double frameH = 100;
        var frameRect = new System.Windows.Shapes.Rectangle
        {
            Width = frameW,
            Height = frameH,
            Stroke = new SolidColorBrush(Color.FromArgb(40, 100, 200, 100)),
            StrokeThickness = 0.5,
            StrokeDashArray = new DoubleCollection { 4, 4 }
        };
        Canvas.SetLeft(frameRect, 0);
        Canvas.SetTop(frameRect, 0);
        designCanvas.Children.Add(frameRect);

        // Dikisleri ciz
        var stitches = _currentDesign.Stitches;
        int colorIndex = 0;
        Point2D prevPoint = points[0];

        for (int i = 1; i < points.Count && i < stitches.Count; i++)
        {
            var stitch = stitches[i - 1];

            if (stitch.Type == StitchType.ColorChange)
            {
                colorIndex = (colorIndex + 1) % _defaultColors.Length;
                prevPoint = points[i];
                continue;
            }

            if (stitch.Type == StitchType.Jump && _showOutlines)
            {
                // Jump cizgisi (kesik cizgi)
                var jumpLine = new Line
                {
                    X1 = prevPoint.X, Y1 = prevPoint.Y,
                    X2 = points[i].X, Y2 = points[i].Y,
                    Stroke = new SolidColorBrush(Color.FromArgb(60, 200, 200, 0)),
                    StrokeThickness = 0.2,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                designCanvas.Children.Add(jumpLine);
                prevPoint = points[i];
                continue;
            }

            if (stitch.Type == StitchType.Normal && _showStitches)
            {
                var threadColor = _defaultColors[colorIndex % _defaultColors.Length];
                var line = new Line
                {
                    X1 = prevPoint.X, Y1 = prevPoint.Y,
                    X2 = points[i].X, Y2 = points[i].Y,
                    Stroke = new SolidColorBrush(threadColor),
                    StrokeThickness = 0.3
                };
                designCanvas.Children.Add(line);
            }

            prevPoint = points[i];
        }

        int normalCount = stitches.Count(s => s.Type == StitchType.Normal);
        int jumpCount = stitches.Count(s => s.Type == StitchType.Jump);
        Log($"Canvas'a {normalCount:N0} dikiş, {jumpCount} jump cizildi");
    }

    private void DrawGrid(double minX, double minY, double maxX, double maxY)
    {
        if (!_showGrid) return;

        double gridStep = 5; // 5mm grid (mm koordinatlari)
        var gridBrush = new SolidColorBrush(Color.FromArgb(25, 100, 100, 150));
        var majorBrush = new SolidColorBrush(Color.FromArgb(40, 100, 100, 150));

        for (double x = Math.Floor(minX / gridStep) * gridStep; x <= maxX + gridStep; x += gridStep)
        {
            bool isMajor = Math.Abs(x % (gridStep * 5)) < 0.01;
            var line = new Line
            {
                X1 = x, Y1 = minY - gridStep,
                X2 = x, Y2 = maxY + gridStep,
                Stroke = isMajor ? majorBrush : gridBrush,
                StrokeThickness = isMajor ? 0.4 : 0.15
            };
            designCanvas.Children.Add(line);
        }

        for (double y = Math.Floor(minY / gridStep) * gridStep; y <= maxY + gridStep; y += gridStep)
        {
            bool isMajor = Math.Abs(y % (gridStep * 5)) < 0.01;
            var line = new Line
            {
                X1 = minX - gridStep, Y1 = y,
                X2 = maxX + gridStep, Y2 = y,
                Stroke = isMajor ? majorBrush : gridBrush,
                StrokeThickness = isMajor ? 0.4 : 0.15
            };
            designCanvas.Children.Add(line);
        }
    }

    // ===================== CANVAS MOUSE =====================

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _lastPanPoint = e.GetPosition(designCanvas);
        designCanvas.CaptureMouse();
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        designCanvas.ReleaseMouseCapture();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            Point current = e.GetPosition(designCanvas);
            canvasTranslate.X += current.X - _lastPanPoint.X;
            canvasTranslate.Y += current.Y - _lastPanPoint.Y;
            _lastPanPoint = current;
        }

        Point mouse = e.GetPosition(designCanvas);
        double worldX = (mouse.X - canvasTranslate.X) / _zoomLevel;
        double worldY = (mouse.Y - canvasTranslate.Y) / _zoomLevel;
        txtCoords.Text = $"X: {worldX:F1}  Y: {worldY:F1}";
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.1 : 0.9;
        Point mouse = e.GetPosition(designCanvas);

        double newZoom = _zoomLevel * factor;
        newZoom = Math.Clamp(newZoom, 0.05, 50.0);

        // Zoom toward mouse position
        double ratio = newZoom / _zoomLevel;
        canvasTranslate.X = mouse.X - ratio * (mouse.X - canvasTranslate.X);
        canvasTranslate.Y = mouse.Y - ratio * (mouse.Y - canvasTranslate.Y);

        SetZoom(newZoom);
    }

    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) { }
    private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e) { }

    // ===================== TOOL BUTTONS =====================

    private void OnToolSelect(object sender, RoutedEventArgs e) => Log("Secim araci aktif");
    private void OnDigitizeOpen(object sender, RoutedEventArgs e) => Log("Acik sekil cizim araci secildi");
    private void OnDigitizeClosed(object sender, RoutedEventArgs e) => Log("Kapali sekil cizim araci secildi");
    private void OnDigitizeRect(object sender, RoutedEventArgs e) => Log("Dikdortgen araci secildi");
    private void OnDigitizeCircle(object sender, RoutedEventArgs e) => Log("Daire araci secildi");
    private void OnDigitizeFreehand(object sender, RoutedEventArgs e) => Log("Serbest cizim araci secildi");
    private void OnDigitizeSatinCol(object sender, RoutedEventArgs e) => Log("Saten kolon araci secildi");

    // ===================== SEARCH & LIST =====================

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtSearch.Text))
            lstStitches.ItemsSource = _allStitchItems;
        else
            lstStitches.ItemsSource = _allStitchItems.Where(x =>
                x.TypeName.Contains(txtSearch.Text, StringComparison.OrdinalIgnoreCase) ||
                x.DeltaX.ToString().Contains(txtSearch.Text) ||
                x.DeltaY.ToString().Contains(txtSearch.Text) ||
                x.Index.ToString().Contains(txtSearch.Text)).ToList();
    }

    private void OnStitchSelected(object sender, SelectionChangedEventArgs e)
    {
        if (lstStitches.SelectedItem is StitchItem item)
            txtStitchInfo.Text = $"Dikis #{item.Index}: [{item.TypeName}] dX={item.DeltaX} dY={item.DeltaY}";
    }

    // ===================== LOG & MISC =====================

    private void Log(string message)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        txtLog.Text += $"[{ts}] {message}\n";
        logScroll.ScrollToEnd();
    }

    private void OnClearLog(object sender, RoutedEventArgs e) { txtLog.Text = ""; }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "NakisCAD v1.0\n\n" +
            "Endustriyel Nakis Yazilimi\n" +
            "Tajima DST Okuyucu/Yazici\n\n" +
            "Gelistirici: Bostanci\n\n" +
            "Wilcom benzeri profesyonel arayuz\n" +
            "Dark tema destegi",
            "Hakkinda", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnDensityMap(object sender, RoutedEventArgs e) => Log("Yoğunluk haritasi (yakinda)");
    private void OnSettings(object sender, RoutedEventArgs e) => Log("Ayarlar penceresi (yakinda)");
    private void OnMachineConnect(object sender, RoutedEventArgs e) => Log("Makine baglantisi (yakinda)");

    private void ShowWarning(string msg)
    {
        MessageBox.Show(msg, "Uyari", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

// ===================== HELPER CLASSES =====================

public class StitchItem
{
    public int Index { get; set; }
    public string TypeName { get; set; } = "";
    public short DeltaX { get; set; }
    public short DeltaY { get; set; }
}

public class ColorObjectItem
{
    public string Name { get; set; } = "";
    public string StitchType { get; set; } = "";
    public int Count { get; set; }
    public SolidColorBrush ColorBrush { get; set; } = new(Colors.White);
}
