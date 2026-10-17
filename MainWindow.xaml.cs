using System.IO;
using System.Windows;
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
    private bool _isPanning;
    private Point _panStart;
    private double _panOriginHorizontal;
    private double _panOriginVertical;

    public MainWindow()
    {
        InitializeComponent();
        UpdateZoomUi();
    }

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
        FitsImageControl.Source = image.Preview;
        KeywordsGrid.ItemsSource = image.Keywords;
        PlaceholderText.Visibility = Visibility.Collapsed;

        Dispatcher.BeginInvoke(FitToWindow, System.Windows.Threading.DispatcherPriority.Loaded);

        StatusText.Text =
            $"{Path.GetFileName(path)}  |  {image.Width}×{image.Height}  |  BITPIX={image.BitPix}  |  {image.Keywords.Count} keywords";
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
