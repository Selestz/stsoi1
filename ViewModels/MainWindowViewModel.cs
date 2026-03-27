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
    private bool _isProcessing;

    public ObservableCollection<ImageOperation> Operations { get; } = new(Enum.GetValues<ImageOperation>());
    public ObservableCollection<ChannelMode> Channels { get; } = new(Enum.GetValues<ChannelMode>());
    public ObservableCollection<MaskShape> MaskShapes { get; } = new(Enum.GetValues<MaskShape>());

    public MainWindowViewModel()
    {
        Layers.CollectionChanged += Layers_CollectionChanged;
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

    private async Task UpdateResultAsync()
    {
        if (Layers.Count == 0)
        {
            var old = ResultImage;
            ResultImage = null;
            old?.Dispose();
            return;
        }
        
        IsProcessing = true;
        try
        {
            var oldImage = ResultImage;
            ResultImage = await ImageProcessor.ProcessLayersAsync(Layers.ToList());
            oldImage?.Dispose();
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
