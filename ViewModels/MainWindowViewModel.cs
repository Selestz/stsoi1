using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AvaloniaApp.Services;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace AvaloniaApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<LayerViewModel> Layers { get; } = new();

    [ObservableProperty]
    private WriteableBitmap? _resultImage;

    [ObservableProperty]
    private string _pointsText = "0,0; 128,128; 255,255";

    [ObservableProperty]
    private ObservableCollection<CurvePointViewModel> _editorPoints = new();

    [ObservableProperty]
    private ObservableCollection<Point> _curvePoints = new();

    [ObservableProperty]
    private ObservableCollection<Point> _histogramPoints = new();

    private bool _isUpdatingFromEditor = false;

    partial void OnPointsTextChanged(string value)
    {
        _ = UpdateResultAsync();
        if (!_isUpdatingFromEditor)
        {
            UpdateEditorPointsFromText();
        }
    }

    /// <summary>Called from code-behind after user drags a point. Updates PointsText and redraws curve preview immediately.</summary>
    public void UpdateFromEditor()
    {
        _isUpdatingFromEditor = true;
        var sorted = EditorPoints.OrderBy(p => p.X).ToList();
        
        // Rebuild CurvePoints immediately for instant visual feedback
        var lut = GetLutFromSortedEditorPoints(sorted);
        if (lut != null) RebuildCurvePoints(lut);

        // Update text (triggers image rerender with throttle)
        PointsText = string.Join("; ", sorted.Select(p => $"{p.X:F0},{(255 - p.Y):F0}"));
        _isUpdatingFromEditor = false;
    }

    private byte[]? GetLutFromSortedEditorPoints(List<CurvePointViewModel> sorted)
    {
        if (sorted.Count == 0) return null;
        double[] xs = sorted.Select(p => Math.Clamp(p.X, 0, 255)).ToArray();
        double[] ys = sorted.Select(p => Math.Clamp(255 - p.Y, 0, 255)).ToArray();
        return SplineInterpolator.CreateLut(xs, ys);
    }

    private void RebuildCurvePoints(byte[] lut)
    {
        var curvePts = new ObservableCollection<Point>();
        for (int i = 0; i < 256; i++)
            curvePts.Add(new Point(i, 255 - lut[i]));
        CurvePoints = curvePts;
    }

    private void UpdateEditorPointsFromText()
    {
        try
        {
            var parts = PointsText.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var parsedPoints = new List<(double x, double y)>();
            foreach (var p in parts)
            {
                var coords = p.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (coords.Length == 2 && double.TryParse(coords[0].Trim(), out double x) && double.TryParse(coords[1].Trim(), out double y))
                    parsedPoints.Add((x, y));
            }
            
            // Ensure edge points always exist
            if (!parsedPoints.Any(p => p.x <= 0)) parsedPoints.Insert(0, (0, 0));
            if (!parsedPoints.Any(p => p.x >= 255)) parsedPoints.Add((255, 255));

            EditorPoints.Clear();
            foreach (var (x, y) in parsedPoints.OrderBy(p => p.x))
            {
                bool isEdge = x <= 0 || x >= 255;
                EditorPoints.Add(new CurvePointViewModel(Math.Clamp(x, 0, 255), 255 - Math.Clamp(y, 0, 255), isEdge));
            }
        }
        catch { }
    }

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isBinarizationEnabled = false;
    partial void OnIsBinarizationEnabledChanged(bool value) => _ = UpdateResultAsync();

    [ObservableProperty]
    private BinarizationMethod _selectedBinarizationMethod = BinarizationMethod.Gavrilov;
    partial void OnSelectedBinarizationMethodChanged(BinarizationMethod value) => _ = UpdateResultAsync();

    [ObservableProperty]
    private int _binarizationWindowSize = 15;
    partial void OnBinarizationWindowSizeChanged(int value) => _ = UpdateResultAsync();

    [ObservableProperty]
    private double _binarizationK = 0.2;
    partial void OnBinarizationKChanged(double value) => _ = UpdateResultAsync();

    public ObservableCollection<BinarizationMethod> BinarizationMethods { get; } = new(Enum.GetValues<BinarizationMethod>());

    public ObservableCollection<ImageOperation> Operations { get; } = new(Enum.GetValues<ImageOperation>());
    public ObservableCollection<ChannelMode> Channels { get; } = new(Enum.GetValues<ChannelMode>());
    public ObservableCollection<MaskShape> MaskShapes { get; } = new(Enum.GetValues<MaskShape>());

    public MainWindowViewModel()
    {
        Layers.CollectionChanged += Layers_CollectionChanged;
        UpdateEditorPointsFromText();
    }

    private void Layers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (LayerViewModel item in e.NewItems)
            {
                item.PropertyChanged += Layer_PropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (LayerViewModel item in e.OldItems)
            {
                item.PropertyChanged -= Layer_PropertyChanged;
                item.Dispose();
            }
        }
        _ = UpdateResultAsync();
    }

    private void Layer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LayerViewModel.Name))
        {
            _ = UpdateResultAsync();
        }
    }

    [RelayCommand]
    private async Task AddLayer()
    {
        var topLevel = TopLevel.GetTopLevel(((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).MainWindow);
        if (topLevel == null) return;
        
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Добавить слой",
            AllowMultiple = true,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        foreach (var file in files)
        {
            AddDroppedFile(file.Path.LocalPath, file.Name);
        }
    }

    public void AddDroppedFile(string path, string name)
    {
        var layer = new LayerViewModel(path, name);
        if (layer.BitmapCache != null) 
        {
            Layers.Add(layer);
        }
    }

    [RelayCommand]
    private void RemoveLayer(LayerViewModel layer)
    {
        if (layer != null)
        {
            Layers.Remove(layer);
        }
    }



    [RelayCommand]
    private async Task SaveResult()
    {
        if (ResultImage == null) return;
        
        var topLevel = TopLevel.GetTopLevel(((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).MainWindow);
        if (topLevel == null) return;
        
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить результат",
            DefaultExtension = "png",
            FileTypeChoices = new[] { FilePickerFileTypes.ImagePng }
        });

        if (file != null)
        {
            using var stream = await file.OpenWriteAsync();
            ResultImage.Save(stream);
        }
    }

    private byte[]? GetLutAndCurve()
    {
        if (string.IsNullOrWhiteSpace(PointsText)) return null;
        try
        {
            var parts = PointsText.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var parsedPoints = new List<Point>();
            foreach (var p in parts)
            {
                var coords = p.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (coords.Length == 2 && double.TryParse(coords[0].Trim(), out double x) && double.TryParse(coords[1].Trim(), out double y))
                {
                    parsedPoints.Add(new Point(x, y));
                }
            }

            if (parsedPoints.Count == 0) return null;

            parsedPoints = parsedPoints.OrderBy(p => p.X).ToList();
            
            double[] xs = parsedPoints.Select(p => p.X).ToArray();
            double[] ys = parsedPoints.Select(p => p.Y).ToArray();

            var lut = SplineInterpolator.CreateLut(xs, ys);

            var curvePts = new ObservableCollection<Point>();
            for (int i = 0; i < 256; i++)
            {
                curvePts.Add(new Point(i, 255 - lut[i])); 
            }
            CurvePoints = curvePts;

            return lut;
        }
        catch 
        {
            return null;
        }
    }

    private void UpdateHistogram(int[]? hist)
    {
        if (hist == null)
        {
            HistogramPoints = new ObservableCollection<Point>();
            return;
        }

        int max = hist.Max();
        if (max == 0) max = 1;

        var pts = new ObservableCollection<Point>();
        pts.Add(new Point(0, 255));
        for (int i = 0; i < 256; i++)
        {
            double h = (double)hist[i] / max * 255.0;
            pts.Add(new Point(i, 255 - h));
        }
        pts.Add(new Point(255, 255));

        HistogramPoints = pts;
    }

    private int _updateId = 0;
    private async Task UpdateResultAsync()
    {
        int currentId = ++_updateId;
        await Task.Delay(30);
        if (currentId != _updateId) return;

        if (Layers.Count == 0)
        {
            var old = ResultImage;
            ResultImage = null;
            UpdateHistogram(null);
            CurvePoints.Clear();
            old?.Dispose();
            return;
        }
        
        IsProcessing = true;
        try
        {
            var oldImage = ResultImage;
            byte[]? lut = GetLutAndCurve();
            var processResult = await ImageProcessor.ProcessLayersAsync(
                Layers.ToList(), 
                lut,
                IsBinarizationEnabled,
                SelectedBinarizationMethod,
                BinarizationWindowSize,
                BinarizationK);
            
            ResultImage = processResult.Image;
            UpdateHistogram(processResult.Histogram);

            oldImage?.Dispose();
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
