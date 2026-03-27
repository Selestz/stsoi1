using System;
using System.Linq;
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

    private CurvePointViewModel? _draggingPoint = null;
    private bool _draggingIsEdge = false;

    private void GraphCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var canvas = sender as Canvas;
        if (canvas == null) return;
        var pos = e.GetPosition(canvas);
        
        var pts = vm.EditorPoints;
        bool isRight = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;

        // Check if clicking on an existing point (hit radius = 12px)
        foreach (var p in pts)
        {
            if (Math.Abs(p.X - pos.X) < 12 && Math.Abs(p.Y - pos.Y) < 12)
            {
                if (isRight)
                {
                    // Right click: delete non-edge points
                    if (!p.IsEdge)
                    {
                        pts.Remove(p);
                        vm.UpdateFromEditor();
                    }
                    return;
                }

                // Left click: begin drag
                _draggingPoint = p;
                _draggingIsEdge = p.IsEdge;
                e.Pointer.Capture(canvas);
                return;
            }
        }

        // Left click on empty area: add new point
        if (!isRight)
        {
            double nx = Math.Clamp(pos.X, 1, 254); // Can't be exactly at X=0 or X=255 (edge reserved)
            double ny = Math.Clamp(pos.Y, 0, 255);
            var np = new CurvePointViewModel(nx, ny, isEdge: false);
            pts.Add(np);
            _draggingPoint = np;
            _draggingIsEdge = false;
            e.Pointer.Capture(canvas);
            vm.UpdateFromEditor();
        }
    }

    private void GraphCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        var canvas = sender as Canvas;
        if (canvas == null) return;
        var pos = e.GetPosition(canvas);

        // Update cursor when hovering over a point (even without dragging)
        if (_draggingPoint == null && DataContext is MainWindowViewModel vmHover)
        {
            bool onPoint = vmHover.EditorPoints.Any(p => Math.Abs(p.X - pos.X) < 12 && Math.Abs(p.Y - pos.Y) < 12);
            canvas.Cursor = onPoint ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) : Avalonia.Input.Cursor.Default;
            return;
        }

        if (_draggingPoint == null || DataContext is not MainWindowViewModel vm) return;

        double newX = Math.Clamp(pos.X, 0, 255);
        double newY = Math.Clamp(pos.Y, 0, 255);

        // Edge points: lock X to 0 or 255
        if (_draggingIsEdge)
        {
            newX = _draggingPoint.X < 128 ? 0 : 255;
        }
        else
        {
            // Clamp X between neighboring edge points (prevent overtaking)
            var sorted = vm.EditorPoints.OrderBy(p => p.X).ToList();
            int idx = sorted.IndexOf(_draggingPoint);
            double minX = idx > 0 ? sorted[idx - 1].X + 1 : 1;
            double maxX = idx < sorted.Count - 1 ? sorted[idx + 1].X - 1 : 254;
            newX = Math.Clamp(newX, minX, maxX);
        }

        _draggingPoint.X = newX;
        _draggingPoint.Y = newY;
        vm.UpdateFromEditor();
    }

    private void GraphCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingPoint != null)
        {
            _draggingPoint = null;
            _draggingIsEdge = false;
            e.Pointer.Capture(null);
        }
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