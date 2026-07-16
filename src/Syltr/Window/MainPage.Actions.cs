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

    private void OnBackClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SelectedReadyHost()?.GoBack();

    private void OnForwardClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SelectedReadyHost()?.GoForward();

    private void OnHomeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SelectedReadyHost()?.NavigateHome();

    private void OnReloadClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SelectedReadyHost()?.Reload();

    private async void OnAddServiceClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await ShowAddServiceDialogAsync();

    private async void OnAboutClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await _dialogs.ShowAboutAsync();

    private void OnExitClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        ((App)Microsoft.UI.Xaml.Application.Current).CloseMainWindow();

    private async void OnSpellcheckClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await _dialogs.ShowSpellcheckAsync();

    private async Task ShowAddServiceDialogAsync()
    {
        if (_environment is null || _contentMapping is null || _serviceStore is null || XamlRoot is null)
        {
            return;
        }

        var owner = ((App)Microsoft.UI.Xaml.Application.Current).MainWindowInstance;
        if (owner is null)
        {
            return;
        }

        _addServiceWindow = new AddServiceWindow(owner);
        AddServiceWindowResult? result;
        try
        {
            result = await _addServiceWindow.ShowAsync();
        }
        finally
        {
            _addServiceWindow = null;
        }

        if (result?.Entry is not null)
        {
            await AddServiceAsync(result.Entry.Name, result.Entry.Url, promptForDuplicateName: true);
        }
        else if (result?.CustomRequested == true)
        {
            await ShowCustomServiceDialogAsync();
        }
    }


    private async Task ShowCustomServiceDialogAsync()
    {
        if (await _dialogs.RequestCustomServiceAsync() is { } input)
        {
            await AddServiceAsync(input.Name, input.Url, promptForDuplicateName: false);
        }
    }

    private async Task AddServiceAsync(string requestedName, string requestedUrl, bool promptForDuplicateName)
    {
        if (_serviceStore is null || XamlRoot is null)
        {
            return;
        }

        if (!ServiceUrl.TryNormalize(requestedUrl, out var normalizedUrl))
        {
            ShowInvalidService(AppText.Get("Service_InvalidNameAddress"));
            return;
        }

        var name = await ResolveServiceNameAsync(
            requestedName,
            normalizedUrl,
            promptForDuplicateName);
        if (name is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowInvalidService(AppText.Get("Service_InvalidName"));
            return;
        }

        var service = CreateServiceDefinition(name, normalizedUrl);
        _services.Add(service);
        await _serviceStore.SaveAsync(_services);
        var (host, railItem) = AddServiceHost(service);
        SelectRailItem(railItem);
        ReportServiceAdded(service, host, await TryInitializeHostAsync(host));
    }

    private async Task<string?> ResolveServiceNameAsync(
        string requestedName,
        string normalizedUrl,
        bool promptForDuplicateName)
    {
        var name = requestedName.Trim();
        return promptForDuplicateName && _services.Any(service => service.Url == normalizedUrl)
            ? await _dialogs.RequestInstanceNameAsync(name)
            : name;
    }

    private ServiceDefinition CreateServiceDefinition(string name, string normalizedUrl) => new()
    {
        Id = ServiceIdGenerator.CreateUnique(_services, name),
        Name = name,
        Url = normalizedUrl
    };

    private void ReportServiceAdded(ServiceDefinition service, ServiceViewHost host, bool initialized)
    {
        if (initialized)
        {
            StatusInfoBar.Title = AppText.Format("Service_AddedTitle", service.Name);
            StatusInfoBar.Message = AppText.Format("Service_AddedMessage", host.ProfileName);
            StatusInfoBar.Severity = InfoBarSeverity.Success;
        }
        else
        {
            StatusInfoBar.Title = AppText.Format("Service_SavedNotLoadedTitle", service.Name);
            StatusInfoBar.Message = host.State.ErrorMessage ?? AppText.Get("Service_TryContextMenu");
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
        }
    }

    private void ShowInvalidService(string message)
    {
        StatusInfoBar.Title = AppText.Get("Service_InvalidTitle");
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = InfoBarSeverity.Error;
    }

    private async void OnEditServiceClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await ShowEditServiceDialogAsync();

    private async Task ShowEditServiceDialogAsync()
    {
        var context = FindSelectedService();
        if (context is null || _serviceStore is null || XamlRoot is null)
        {
            return;
        }

        var input = await _dialogs.RequestEditServiceAsync(context.Service);
        if (input is null || !TryCreateUpdatedService(context.Service, input, out var updated))
        {
            return;
        }

        await ApplyServiceUpdateAsync(context, updated);
    }

    private SelectedServiceContext? FindSelectedService()
    {
        var railItem = SelectedRailItem();
        var host = railItem?.Host;
        if (railItem is null || host is null)
        {
            return null;
        }

        var index = _services.FindIndex(service =>
            WebViewProfileName.FromServiceId(service.Id) == host.ProfileName);
        return index < 0
            ? null
            : new SelectedServiceContext(index, _services[index], railItem, host);
    }

    private bool TryCreateUpdatedService(
        ServiceDefinition service,
        EditServiceInput input,
        out ServiceDefinition updated)
    {
        var name = input.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || !ServiceUrl.TryNormalize(input.Url, out var normalizedUrl))
        {
            ShowInvalidService(AppText.Get("Service_InvalidNameAddress"));
            updated = service;
            return false;
        }

        updated = service with
        {
            Name = name,
            Url = normalizedUrl,
            UserAgent = string.IsNullOrWhiteSpace(input.UserAgent)
                ? null
                : input.UserAgent.Trim()
        };
        return true;
    }

    private async Task ApplyServiceUpdateAsync(
        SelectedServiceContext context,
        ServiceDefinition updated)
    {
        _services[context.Index] = updated;
        await _serviceStore!.SaveAsync(_services);
        context.RailItem.UpdateService(updated);
        RegroupRailItem(context.RailItem, updated);
        context.Host.UpdateHome(new Uri(updated.Url), navigate: false);
        context.Host.SetUserAgent(updated.UserAgent, reload: false);
        await ApplyUpdatedHostStateAsync(context, updated);
        ReportServiceUpdated(updated);
    }

    private async Task ApplyUpdatedHostStateAsync(
        SelectedServiceContext context,
        ServiceDefinition updated)
    {
        if (updated.Disabled)
        {
            context.Host.Suspend();
            context.RailItem.Content = CreateDisabledContent(updated);
            ServiceContentPresenter.Content = context.RailItem.Content;
        }
        else if (context.Service.Disabled)
        {
            context.RailItem.Content = context.Host.Content;
            ServiceContentPresenter.Content = context.RailItem.Content;
            await context.Host.ResumeAsync();
        }
        else if (context.Host.IsInitialized)
        {
            context.Host.NavigateHome();
        }
    }

    private void ReportServiceUpdated(ServiceDefinition updated)
    {
        StatusInfoBar.Title = AppText.Format("Service_UpdatedTitle", updated.Name);
        StatusInfoBar.Message = updated.Disabled
            ? AppText.Format("Service_UpdatedDisabledMessage", updated.Name)
            : AppText.Format("Service_UpdatedSessionPreservedMessage", updated.Name);
        StatusInfoBar.Severity = InfoBarSeverity.Success;
    }

    private sealed record SelectedServiceContext(
        int Index,
        ServiceDefinition Service,
        ServiceRailItem RailItem,
        ServiceViewHost Host);

    private async Task RecoverSelectedHostAsync()
    {
        var host = SelectedHost();
        if (host is null || host.State.Status != ServiceViewStatus.Failed)
        {
            StatusInfoBar.Title = AppText.Get("Recovery_NotRequiredTitle");
            StatusInfoBar.Message = AppText.Get("Recovery_NotRequiredMessage");
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            return;
        }

        try
        {
            await host.RecoverAsync();
            StatusInfoBar.Title = AppText.Get("Recovery_CompletedTitle");
            StatusInfoBar.Message = AppText.Format("Recovery_CompletedMessage", host.ProfileName);
            StatusInfoBar.Severity = InfoBarSeverity.Success;
        }
        catch (Exception exception)
        {
            StatusInfoBar.Title = AppText.Get("Recovery_FailedTitle");
            StatusInfoBar.Message = exception.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
        }
    }

    private async void OnAddServiceAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ShowAddServiceDialogAsync();
    }

    private async void OnEditServiceAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ShowEditServiceDialogAsync();
    }

    private void OnReloadAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectedReadyHost()?.Reload();
    }

    private void OnHomeAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectedReadyHost()?.NavigateHome();
    }

    private void OnBackAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectedReadyHost()?.GoBack();
    }

    private void OnForwardAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectedReadyHost()?.GoForward();
    }

    private void OnNextServiceAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectRelativeService(1);
    }

    private void OnPreviousServiceAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectRelativeService(-1);
    }

    private void OnDoNotDisturbAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        DoNotDisturbButton.IsChecked = DoNotDisturbButton.IsChecked != true;
    }

    private void OnGotoServiceAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        var index = (int)sender.Key - (int)Windows.System.VirtualKey.Number1;
        if (index >= 0 && index < _railGroups.Count)
        {
            ServiceRail.SelectedIndex = index;
            args.Handled = true;
        }
    }

    private void OnQuitAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ((App)Microsoft.UI.Xaml.Application.Current).CloseMainWindow();
    }

    private void SelectRelativeService(int offset)
    {
        if (_railGroups.Count == 0)
        {
            return;
        }

        var currentIndex = Math.Max(0, ServiceRail.SelectedIndex);
        ServiceRail.SelectedIndex = (currentIndex + offset + _railGroups.Count) % _railGroups.Count;
    }

    private void OnNavigateAllClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var target = (TargetSelector.SelectedItem as ComboBoxItem)?.Tag as string;
        if (target == "local")
        {
            foreach (var host in _hosts.Where(host => host.IsInitialized))
            {
                host.NavigateHome();
            }
        }
        else if (Uri.TryCreate(target, UriKind.Absolute, out var destination))
        {
            foreach (var host in _hosts.Where(host => host.IsInitialized))
            {
                host.Navigate(destination);
            }
        }

        StatusInfoBar.Title = AppText.Get("Diagnostics_AuthenticationTitle");
        StatusInfoBar.Message = AppText.Get("Diagnostics_AuthenticationMessage");
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
    }
}
