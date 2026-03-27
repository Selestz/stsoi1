using System;
using CommunityToolkit.Mvvm.ComponentModel;
using AvaloniaApp.Services;
using Avalonia.Media.Imaging;
using System.IO;
using System.Collections.ObjectModel;

namespace AvaloniaApp.ViewModels;

public partial class LayerViewModel : ViewModelBase, IDisposable
{
    public string ImagePath { get; }

    public ObservableCollection<ImageOperation> Operations { get; } = new(Enum.GetValues<ImageOperation>());
    public ObservableCollection<ChannelMode> Channels { get; } = new(Enum.GetValues<ChannelMode>());
    public ObservableCollection<MaskShape> MaskShapes { get; } = new(Enum.GetValues<MaskShape>());

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private double _opacity = 1.0;

    [ObservableProperty]
    private ImageOperation _blendMode = ImageOperation.Sum;

    [ObservableProperty]
    private ChannelMode _selectedChannelMode = ChannelMode.RGB;

    [ObservableProperty]
    private MaskShape _selectedMaskShape = MaskShape.Circle;

    public WriteableBitmap? BitmapCache { get; private set; }

    public LayerViewModel(string path, string name)
    {
        ImagePath = path;
        Name = name;
        try 
        {
            using var fs = File.OpenRead(path);
            BitmapCache = WriteableBitmap.Decode(fs);
        } 
        catch { }
    }

    public void Dispose()
    {
        BitmapCache?.Dispose();
        BitmapCache = null;
    }
}
