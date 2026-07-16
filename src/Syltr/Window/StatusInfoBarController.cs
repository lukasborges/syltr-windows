using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Syltr.Engine;
using Syltr.Localization;

namespace Syltr.Window;

internal sealed class StatusInfoBarController
{
    private readonly InfoBar _infoBar;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Func<string, bool> _selectProfile;
    private DispatcherQueueTimer? _dismissTimer;
    private ServiceNotificationReceivedEventArgs? _activeNotification;

    public StatusInfoBarController(
        InfoBar infoBar,
        DispatcherQueue dispatcherQueue,
        Func<string, bool> selectProfile)
    {
        _infoBar = infoBar;
        _dispatcherQueue = dispatcherQueue;
        _selectProfile = selectProfile;
        _infoBar.RegisterPropertyChangedCallback(InfoBar.TitleProperty, OnStatusChanged);
        _infoBar.RegisterPropertyChangedCallback(InfoBar.SeverityProperty, OnStatusChanged);
    }

    public void ShowNotification(
        ServiceNotification notification,
        ServiceNotificationReceivedEventArgs interaction)
    {
        CloseActiveNotification();
        _activeNotification = interaction;
        _infoBar.Title = NotificationTitle(notification);
        _infoBar.Message = NotificationMessage(notification);
        _infoBar.Severity = InfoBarSeverity.Informational;
        _infoBar.ActionButton = CreateOpenButton(notification, interaction);
        _infoBar.IsClosable = true;
        _infoBar.IsOpen = true;
    }

    public void OnClosed()
    {
        _dismissTimer?.Stop();
        CloseActiveNotification();
        _infoBar.ActionButton = null;
        _infoBar.IsClosable = true;
    }

    public void Stop()
    {
        _dismissTimer?.Stop();
        CloseActiveNotification();
    }

    private Button CreateOpenButton(
        ServiceNotification notification,
        ServiceNotificationReceivedEventArgs interaction)
    {
        var button = new Button { Content = AppText.Get("Notification_OpenService") };
        button.Click += (_, _) =>
        {
            _selectProfile(notification.ProfileName);
            interaction.Click();
            if (ReferenceEquals(_activeNotification, interaction))
            {
                _activeNotification = null;
                _infoBar.ActionButton = null;
                _infoBar.IsClosable = false;
            }
        };
        return button;
    }

    private void OnStatusChanged(
        Microsoft.UI.Xaml.DependencyObject sender,
        Microsoft.UI.Xaml.DependencyProperty property)
    {
        _infoBar.IsOpen = true;
        _infoBar.IsClosable = true;
        if (_activeNotification is not null || _infoBar.Severity == InfoBarSeverity.Error)
        {
            _dismissTimer?.Stop();
            return;
        }

        _dismissTimer ??= _dispatcherQueue.CreateTimer();
        _dismissTimer.Stop();
        _dismissTimer.Interval = TimeSpan.FromSeconds(
            _infoBar.Severity == InfoBarSeverity.Warning ? 8 : 5);
        _dismissTimer.IsRepeating = false;
        _dismissTimer.Tick -= OnDismissTimerTick;
        _dismissTimer.Tick += OnDismissTimerTick;
        _dismissTimer.Start();
    }

    private void OnDismissTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_activeNotification is null)
        {
            _infoBar.IsOpen = false;
        }
    }

    private void CloseActiveNotification()
    {
        _activeNotification?.Close();
        _activeNotification = null;
    }

    private static string NotificationTitle(ServiceNotification notification) =>
        string.IsNullOrWhiteSpace(notification.Title)
            ? notification.SenderOrigin.Host
            : notification.Title;

    private static string NotificationMessage(ServiceNotification notification) =>
        string.IsNullOrWhiteSpace(notification.Body)
            ? AppText.Format(
                "Notification_FallbackMessage",
                notification.SenderOrigin.Host,
                notification.ProfileName)
            : AppText.Format(
                "Notification_BodyWithProfile",
                notification.Body,
                notification.ProfileName);
}
