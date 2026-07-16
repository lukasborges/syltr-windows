using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using Syltr.Config;
using Syltr.Engine;

namespace Syltr.Icon;

/// <summary>
/// Reusable visual state for a service tile, independent from the hosting rail.
/// </summary>
public sealed class ServiceTileState : INotifyPropertyChanged
{
    private string _name;
    private string _initial;
    private double _opacity;
    private string _unreadText = string.Empty;
    private Microsoft.UI.Xaml.Visibility _unreadVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    private ImageSource? _iconSource;
    private double _faviconSize = 24;

    public ServiceTileState(ServiceDefinition service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _name = service.Name;
        _initial = CreateInitial(service.Name);
        _opacity = service.Disabled ? 0.45 : 1;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        private set => SetField(ref _name, value);
    }

    public string Initial
    {
        get => _initial;
        private set => SetField(ref _initial, value);
    }

    public double Opacity
    {
        get => _opacity;
        private set => SetField(ref _opacity, value);
    }

    public string UnreadText
    {
        get => _unreadText;
        private set => SetField(ref _unreadText, value);
    }

    public Microsoft.UI.Xaml.Visibility UnreadVisibility
    {
        get => _unreadVisibility;
        private set => SetField(ref _unreadVisibility, value);
    }

    public ImageSource? IconSource
    {
        get => _iconSource;
        set
        {
            if (SetField(ref _iconSource, value))
            {
                OnPropertyChanged(nameof(IconVisibility));
                OnPropertyChanged(nameof(InitialVisibility));
            }
        }
    }

    public double FaviconSize
    {
        get => _faviconSize;
        set => SetField(ref _faviconSize, Math.Clamp(value, 16, 24));
    }

    public Microsoft.UI.Xaml.Visibility IconVisibility => IconSource is null
        ? Microsoft.UI.Xaml.Visibility.Collapsed
        : Microsoft.UI.Xaml.Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility InitialVisibility => IconSource is null
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public void UpdateService(ServiceDefinition service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Name = service.Name;
        Initial = CreateInitial(service.Name);
        Opacity = service.Disabled ? 0.45 : 1;
    }

    public void UpdateState(ServiceViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        UnreadText = state.UnreadCount switch
        {
            0 => string.Empty,
            > 99 => "99+",
            _ => state.UnreadCount.ToString()
        };
        UnreadVisibility = state.UnreadCount > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private static string CreateInitial(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? "?"
            : name.Trim()[..1].ToUpperInvariant();

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
