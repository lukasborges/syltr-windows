using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;
using Syltr.Catalog;
using Syltr.Config;
using Syltr.Config.Notifications;
using Syltr.Engine;
using Syltr.Engine.Spike;
using Syltr.Icon;
using Syltr.Localization;

namespace Syltr.Window;

public sealed partial class MainPage
{

    private void OnHostStateChanged(object? sender, ServiceViewStateChangedEventArgs e)
    {
        if (sender is ServiceViewHost host)
        {
            _states[host.ProfileName] = e.State;
            var railItem = _railItems.FirstOrDefault(item => ReferenceEquals(item.Host, host));
            railItem?.UpdateState(e.State);
            if (railItem is not null)
            {
                _railGroups.First(group => group.Items.Contains(railItem)).RefreshUnread();
            }
            UpdateSelectedProfileStatus();
            if (ReferenceEquals(SelectedHost(), host))
            {
                UpdateServiceOverlay();
            }
        }
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectRailContent();

    private void OnRailItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ServiceRailGroupItem { Items.Count: > 1 } group ||
            ServiceRail.ContainerFromItem(group) is not Microsoft.UI.Xaml.FrameworkElement anchor)
        {
            return;
        }

        var flyout = new MenuFlyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.RightEdgeAlignedTop
        };
        foreach (var instance in group.Items)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = instance.UnreadCount > 0
                    ? $"{instance.Tile.Name}  ({instance.UnreadCount})"
                    : instance.Tile.Name,
                IsChecked = ReferenceEquals(instance, group.ActiveItem)
            };
            item.Click += (_, _) => SelectRailItem(instance);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var addInstance = new MenuFlyoutItem { Text = AppText.Get("Context_AddInstance") };
        addInstance.Click += async (_, _) =>
        {
            var service = _services.FirstOrDefault(configured =>
                WebViewProfileName.FromServiceId(configured.Id) == group.ActiveItem.Host.ProfileName);
            if (service is not null)
            {
                await AddServiceAsync(service.Name, service.Url, promptForDuplicateName: true);
            }
        };
        flyout.Items.Add(addInstance);
        flyout.ShowAt(anchor);
    }

    private void OnServiceRailRightTapped(
        object sender,
        Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        var current = e.OriginalSource as Microsoft.UI.Xaml.DependencyObject;
        while (current is not null and not ListViewItem)
        {
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        if (current is not ListViewItem container ||
            ServiceRail.ItemFromContainer(container) is not ServiceRailGroupItem group)
        {
            return;
        }

        ServiceRail.SelectedItem = group;
        SelectRailContent();
        ShowServiceContextMenu(group, container);
        e.Handled = true;
    }

    private void OnServiceContextMenuAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ServiceRail.SelectedItem is ServiceRailGroupItem group
            && ServiceRail.ContainerFromItem(group) is ListViewItem container)
        {
            ShowServiceContextMenu(group, container);
            args.Handled = true;
        }
    }

    private void ShowServiceContextMenu(ServiceRailGroupItem group, ListViewItem container)
    {
        var railItem = group.ActiveItem;
        var service = _services.FirstOrDefault(configured =>
            WebViewProfileName.FromServiceId(configured.Id) == railItem.Host.ProfileName);
        if (service is null)
        {
            return;
        }

        var menu = new MenuFlyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.RightEdgeAlignedTop
        };
        var reload = new MenuFlyoutItem { Text = AppText.Get("Context_Reload") };
        reload.Click += async (_, _) =>
        {
            if (SelectedReadyHost() is { } readyHost)
            {
                readyHost.Reload();
            }
            else
            {
                await RecoverSelectedHostAsync();
            }
        };
        var home = new MenuFlyoutItem { Text = AppText.Get("Context_Home") };
        home.Click += (_, _) => SelectedReadyHost()?.NavigateHome();
        var edit = new MenuFlyoutItem { Text = AppText.Get("Context_Edit") };
        edit.Click += async (_, _) => await ShowEditServiceDialogAsync();
        var mute = new MenuFlyoutItem
        {
            Text = AppText.Get(service.Muted ? "Context_Unmute" : "Context_Mute")
        };
        mute.Click += async (_, _) => await SetSelectedServiceMutedAsync(!service.Muted);
        var disable = new MenuFlyoutItem
        {
            Text = AppText.Get(service.Disabled ? "Context_Enable" : "Context_Disable")
        };
        disable.Click += async (_, _) => await SetSelectedServiceDisabledAsync(!service.Disabled);
        var remove = new MenuFlyoutItem
        {
            Text = AppText.Get(group.Items.Count > 1 ? "Context_RemoveAccount" : "Context_Remove")
        };
        remove.Click += async (_, _) => await DeleteSelectedProfileAsync();

        menu.Items.Add(reload);
        menu.Items.Add(home);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(edit);
        menu.Items.Add(mute);
        menu.Items.Add(disable);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(remove);
        menu.ShowAt(container);
    }

    private async Task SetSelectedServiceMutedAsync(bool muted)
    {
        var railItem = SelectedRailItem();
        if (railItem is null || _serviceStore is null)
        {
            return;
        }

        var index = _services.FindIndex(service =>
            WebViewProfileName.FromServiceId(service.Id) == railItem.Host.ProfileName);
        if (index < 0)
        {
            return;
        }

        var updated = _services[index] with { Muted = muted };
        _services[index] = updated;
        railItem.UpdateService(updated);
        await _serviceStore.SaveAsync(_services);
        StatusInfoBar.Title = AppText.Get(muted
            ? "Notifications_MutedTitle"
            : "Notifications_UnmutedTitle");
        StatusInfoBar.Message = updated.Name;
        StatusInfoBar.Severity = InfoBarSeverity.Success;
    }

    private async Task SetSelectedServiceDisabledAsync(bool disabled)
    {
        var railItem = SelectedRailItem();
        if (railItem is null || _serviceStore is null)
        {
            return;
        }

        var host = railItem.Host;
        var index = _services.FindIndex(service =>
            WebViewProfileName.FromServiceId(service.Id) == host.ProfileName);
        if (index < 0)
        {
            return;
        }

        var updated = _services[index] with { Disabled = disabled };
        _services[index] = updated;
        railItem.UpdateService(updated);
        await _serviceStore.SaveAsync(_services);
        if (disabled)
        {
            host.Suspend();
            railItem.Content = CreateDisabledContent(updated);
        }
        else
        {
            railItem.Content = host.Content;
            await host.ResumeAsync();
        }

        SelectRailContent();
        StatusInfoBar.Title = AppText.Get(disabled
            ? "Service_DisabledTitle"
            : "Service_EnabledTitle");
        StatusInfoBar.Message = disabled
            ? AppText.Get("Service_DisabledSessionPreserved")
            : updated.Name;
        StatusInfoBar.Severity = InfoBarSeverity.Success;
    }

    private void SelectRailContent()
    {
        var group = ServiceRail.SelectedItem as ServiceRailGroupItem;
        _changingInstanceSelection = true;
        InstanceSelector.ItemsSource = group?.Items;
        InstanceSelector.SelectedItem = group?.ActiveItem;
        InstanceSelector.Visibility = group is { Items.Count: > 1 }
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        _changingInstanceSelection = false;
        UpdateWebViewMemoryTargets();
        ServiceContentPresenter.Content = group?.ActiveItem.Content;
        UpdateSelectedProfileStatus();
        UpdateServiceOverlay();
    }

    private void OnInstanceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingInstanceSelection ||
            ServiceRail.SelectedItem is not ServiceRailGroupItem group ||
            InstanceSelector.SelectedItem is not ServiceRailItem item)
        {
            return;
        }

        group.ActiveItem = item;
        UpdateWebViewMemoryTargets();
        ServiceContentPresenter.Content = item.Content;
        UpdateSelectedProfileStatus();
        UpdateServiceOverlay();
    }

    private async void OnRailDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_serviceStore is null)
        {
            return;
        }

        var servicesByProfile = _services.ToDictionary(
            service => WebViewProfileName.FromServiceId(service.Id),
            StringComparer.Ordinal);
        var reordered = _railGroups
            .SelectMany(group => group.Items)
            .Select(item => servicesByProfile[item.Host.ProfileName])
            .ToArray();
        if (reordered.Length != _services.Count)
        {
            return;
        }

        _services.Clear();
        _services.AddRange(reordered);
        await _serviceStore.SaveAsync(_services);
    }
}
