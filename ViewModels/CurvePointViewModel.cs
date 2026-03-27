using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaApp.ViewModels;

public partial class CurvePointViewModel : ViewModelBase
{
    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    public bool IsEdge { get; init; }

    public bool IsNotEdge => !IsEdge;

    public CurvePointViewModel(double x, double y, bool isEdge = false)
    {
        _x = x;
        _y = y;
        IsEdge = isEdge;
    }
}
