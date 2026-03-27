using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaApp.ViewModels;
using Avalonia.Platform.Storage;
using System.Collections.Generic;

namespace AvaloniaApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, DropHandler);
    }

    private void DropHandler(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (DataContext is MainWindowViewModel vm && files != null)
            {
                foreach (IStorageItem file in files)
                {
                    vm.AddDroppedFile(file.Path.LocalPath, file.Name);
                }
            }
        }
    }
}