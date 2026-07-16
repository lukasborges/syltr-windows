using System.Collections.ObjectModel;
using System.ComponentModel;
using Syltr.Localization;

namespace Syltr.Window;

internal sealed class ServiceRailGroupItem : INotifyPropertyChanged
{
    private ServiceRailItem _activeItem;
    private string _displayName;
    private string _unreadText = string.Empty;
    private ulong _unreadCount;
    private Microsoft.UI.Xaml.Visibility _unreadVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;

    public ServiceRailGroupItem(string key, ServiceRailItem firstItem)
    {
        Key = key;
        Items = [firstItem];
        _activeItem = firstItem;
        _displayName = firstItem.Tile.Name;
        RefreshUnread();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public ObservableCollection<ServiceRailItem> Items { get; }

    public ServiceRailItem ActiveItem
    {
        get => _activeItem;
        set
        {
            if (ReferenceEquals(_activeItem, value))
            {
                return;
            }

            _activeItem = value;
            NotifyChanged(nameof(ActiveItem));
            RefreshDisplayName();
        }
    }

    public string DisplayName
    {
        get => _displayName;
        private set
        {
            if (_displayName == value)
            {
                return;
            }

            _displayName = value;
            NotifyChanged(nameof(DisplayName), nameof(AccessibleName));
        }
    }

    public string AccessibleName => _unreadCount > 0
        ? AppText.Format("ServiceRail_ItemWithUnreadAutomationName", DisplayName, _unreadCount)
        : DisplayName;

    public string UnreadText
    {
        get => _unreadText;
        private set
        {
            if (_unreadText == value)
            {
                return;
            }

            _unreadText = value;
            NotifyChanged(nameof(UnreadText));
        }
    }

    public Microsoft.UI.Xaml.Visibility UnreadVisibility
    {
        get => _unreadVisibility;
        private set
        {
            if (_unreadVisibility == value)
            {
                return;
            }

            _unreadVisibility = value;
            NotifyChanged(nameof(UnreadVisibility));
        }
    }

    public Microsoft.UI.Xaml.Visibility StackVisibility => Items.Count > 1
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public void RefreshDisplayName()
    {
        DisplayName = Items.Count > 1
            ? AppText.Format("ServiceGroup_DisplayName", ActiveItem.Tile.Name, Items.Count)
            : ActiveItem.Tile.Name;
        NotifyChanged(nameof(StackVisibility));
    }

    public void RefreshUnread()
    {
        _unreadCount = Items.Aggregate(0UL, (sum, item) => sum + item.UnreadCount);
        UnreadText = _unreadCount switch
        {
            0 => string.Empty,
            > 99 => "99+",
            _ => _unreadCount.ToString()
        };
        UnreadVisibility = _unreadCount > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        NotifyChanged(nameof(AccessibleName));
    }

    private void NotifyChanged(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
