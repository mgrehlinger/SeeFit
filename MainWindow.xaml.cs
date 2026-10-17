using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SeeFit.Fits;

namespace SeeFit;

public partial class MainWindow : Window
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 32.0;
    private const double ZoomStep = 1.25;

    private double _zoom = 1.0;
    private FitsImage? _currentImage;
    private string? _currentPath;
    private bool _isPanning;
    private Point _panStart;
    private double _panOriginHorizontal;
    private double _panOriginVertical;
    private bool _updatingDebayerUi;
    private bool _updatingScnrUi;

    public MainWindow()
    {
        InitializeComponent();
        UpdateZoomUi();
        UpdateProcessingControlsEnabled();
    }

    private bool DebayerEnabled => DebayerCheckBox.IsChecked == true;

    private bool ScnrEnabled => ScnrCheckBox.IsChecked == true;

    private void OpenFits_Click(object sender, RoutedEventArgs e) => OpenFitsFile();

    private void OpenFits_Executed(object sender, ExecutedRoutedEventArgs e) => OpenFitsFile();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(ZoomStep);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(1.0 / ZoomStep);

    private void ZoomFit_Click(object sender, RoutedEventArgs e) => FitToWindow();

    private void ZoomActual_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e) => ZoomBy(ZoomStep);

    private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e) => ZoomBy(1.0 / ZoomStep);

    private void ZoomFit_Executed(object sender, ExecutedRoutedEventArgs e) => FitToWindow();

    private void Debayer_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingDebayerUi)
        {
            return;
        }

        _updatingDebayerUi = true;
        DebayerCheckBox.IsChecked = DebayerMenuItem.IsChecked;
        _updatingDebayerUi = false;
        UpdateProcessingControlsEnabled();
        RefreshPreview();
    }

    private void Debayer_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingDebayerUi)
        {
            return;
        }

        _updatingDebayerUi = true;
        DebayerMenuItem.IsChecked = DebayerEnabled;
        _updatingDebayerUi = false;
        UpdateProcessingControlsEnabled();
        RefreshPreview();
    }

    private void BayerPatternCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !DebayerEnabled)
        {
            return;
        }

        RefreshPreview();
    }

    private void Scnr_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingScnrUi)
        {
            return;
        }

        _updatingScnrUi = true;
        ScnrCheckBox.IsChecked = ScnrMenuItem.IsChecked;
        _updatingScnrUi = false;
        RefreshPreview();
    }

    private void Scnr_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingScnrUi || !IsLoaded)
        {
            return;
        }

        _updatingScnrUi = true;
        ScnrMenuItem.IsChecked = ScnrEnabled;
        _updatingScnrUi = false;
        UpdateProcessingControlsEnabled();
        RefreshPreview();
    }

    private void ScnrSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !ScnrEnabled || !DebayerEnabled)
        {
            return;
        }

        RefreshPreview();
    }

    private void ScnrAmount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ScnrAmountText.Text = $"{ScnrAmountSlider.Value * 100:0}%";
        if (!ScnrEnabled || !DebayerEnabled)
        {
            return;
        }

        RefreshPreview();
    }

    private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_currentImage is null)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            ZoomBy(e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep);
            e.Handled = true;
        }
    }

    private void ImageScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentImage is null)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(ImageScrollViewer);
        _panOriginHorizontal = ImageScrollViewer.HorizontalOffset;
        _panOriginVertical = ImageScrollViewer.VerticalOffset;
        ImageScrollViewer.CaptureMouse();
        ImageScrollViewer.Cursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void ImageScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || !ImageScrollViewer.IsMouseCaptured)
        {
            return;
        }

        var current = e.GetPosition(ImageScrollViewer);
        var delta = current - _panStart;
        ImageScrollViewer.ScrollToHorizontalOffset(_panOriginHorizontal - delta.X);
        ImageScrollViewer.ScrollToVerticalOffset(_panOriginVertical - delta.Y);
        e.Handled = true;
    }

    private void ImageScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        EndPan();
        e.Handled = true;
    }

    private void ImageScrollViewer_LostMouseCapture(object sender, MouseEventArgs e) => EndPan();

    private void EndPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        if (ImageScrollViewer.IsMouseCaptured)
        {
            ImageScrollViewer.ReleaseMouseCapture();
        }

        ImageScrollViewer.Cursor = Cursors.Hand;
    }

    private void OpenFitsFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open FITS File",
            Filter = "FITS files (*.fits;*.fit;*.fts)|*.fits;*.fit;*.fts|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var image = FitsReader.Load(dialog.FileName);
            DisplayImage(image, dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not open FITS file:\n\n{ex.Message}",
                "SeeFit",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Failed to open file.";
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void DisplayImage(FitsImage image, string path)
    {
        _currentImage = image;
        _currentPath = path;
        KeywordsGrid.ItemsSource = image.Keywords;
        PlaceholderText.Visibility = Visibility.Collapsed;
        UpdateProcessingControlsEnabled();
        RefreshPreview();
        Dispatcher.BeginInvoke(FitToWindow, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RefreshPreview()
    {
        if (_currentImage is null)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var pattern = ResolveBayerPattern(_currentImage);
            var scnr = BuildScnrOptions();
            FitsImageControl.Source = _currentImage.CreatePreview(DebayerEnabled, pattern, scnr);
            UpdateStatusText(pattern, scnr);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not render image:\n\n{ex.Message}",
                "SeeFit",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private ScnrOptions BuildScnrOptions() => new()
    {
        Enabled = DebayerEnabled && ScnrEnabled,
        Color = ScnrColorCombo.SelectedIndex switch
        {
            1 => ScnrColor.Red,
            2 => ScnrColor.Blue,
            _ => ScnrColor.Green
        },
        Method = ScnrMethodCombo.SelectedIndex switch
        {
            1 => ScnrMethod.MaximumNeutral,
            2 => ScnrMethod.MinimumNeutral,
            3 => ScnrMethod.MaximumMask,
            4 => ScnrMethod.AdditiveMask,
            _ => ScnrMethod.AverageNeutral
        },
        Amount = ScnrAmountSlider.Value,
        PreserveLightness = ScnrPreserveLightnessCheckBox.IsChecked == true
    };

    private BayerPattern ResolveBayerPattern(FitsImage image)
    {
        if (BayerPatternCombo.SelectedIndex <= 0)
        {
            return image.HeaderBayerPattern ?? BayerPattern.Rggb;
        }

        return BayerPatternCombo.SelectedIndex switch
        {
            1 => BayerPattern.Rggb,
            2 => BayerPattern.Bggr,
            3 => BayerPattern.Grbg,
            4 => BayerPattern.Gbrg,
            _ => BayerPattern.Rggb
        };
    }

    private void UpdateProcessingControlsEnabled()
    {
        var hasImage = _currentImage is not null;
        DebayerCheckBox.IsEnabled = hasImage;
        DebayerMenuItem.IsEnabled = hasImage;
        BayerPatternCombo.IsEnabled = hasImage;

        var scnrAvailable = hasImage && DebayerEnabled;
        ScnrCheckBox.IsEnabled = scnrAvailable;
        ScnrMenuItem.IsEnabled = scnrAvailable;
        ScnrColorCombo.IsEnabled = scnrAvailable;
        ScnrMethodCombo.IsEnabled = scnrAvailable;
        ScnrAmountSlider.IsEnabled = scnrAvailable;
        ScnrPreserveLightnessCheckBox.IsEnabled = scnrAvailable;

        if (!scnrAvailable && ScnrEnabled)
        {
            _updatingScnrUi = true;
            ScnrCheckBox.IsChecked = false;
            ScnrMenuItem.IsChecked = false;
            _updatingScnrUi = false;
        }
    }

    private void UpdateStatusText(BayerPattern pattern, ScnrOptions scnr)
    {
        if (_currentImage is null || _currentPath is null)
        {
            return;
        }

        var debayerInfo = DebayerEnabled
            ? $"  |  Debayer {BayerPatternParser.ToDisplayName(pattern)}"
            : string.Empty;

        var scnrInfo = scnr.Enabled
            ? $"  |  SCNR {scnr.Color} {scnr.Amount * 100:0}%"
            : string.Empty;

        StatusText.Text =
            $"{Path.GetFileName(_currentPath)}  |  {_currentImage.Width}×{_currentImage.Height}  |  BITPIX={_currentImage.BitPix}  |  {_currentImage.Keywords.Count} keywords{debayerInfo}{scnrInfo}";
    }

    private void ZoomBy(double factor) => SetZoom(_zoom * factor);

    private void SetZoom(double zoom)
    {
        if (_currentImage is null)
        {
            return;
        }

        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        ImageScale.ScaleX = _zoom;
        ImageScale.ScaleY = _zoom;
        UpdateZoomUi();
    }

    private void FitToWindow()
    {
        if (_currentImage is null || FitsImageControl.Source is null)
        {
            return;
        }

        var viewportWidth = Math.Max(1, ImageScrollViewer.ViewportWidth);
        var viewportHeight = Math.Max(1, ImageScrollViewer.ViewportHeight);
        if (viewportWidth <= 1 || viewportHeight <= 1)
        {
            viewportWidth = Math.Max(1, ImageScrollViewer.ActualWidth - 20);
            viewportHeight = Math.Max(1, ImageScrollViewer.ActualHeight - 20);
        }

        var scaleX = viewportWidth / _currentImage.Width;
        var scaleY = viewportHeight / _currentImage.Height;
        SetZoom(Math.Min(scaleX, scaleY));
    }

    private void UpdateZoomUi()
    {
        ZoomText.Text = _currentImage is null
            ? "Zoom: —"
            : $"Zoom: {_zoom * 100:0.#}%";
    }
}
