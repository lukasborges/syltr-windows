using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Syltr.Config.Notifications;

/// <summary>
/// Owns Windows App SDK registration, display and activation parsing.
/// </summary>
public sealed class WindowsAppNotificationService : IDisposable
{
    private readonly Queue<WindowsNotificationActivation> _pendingActivations = [];
    private readonly object _activationLock = new();
    private bool _registered;
    private bool _disposed;

    public WindowsAppNotificationService()
    {
        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
    }

    public event EventHandler<WindowsNotificationActivation>? Activated;

    public bool IsRegistered => _registered;

    public string? RegistrationError { get; private set; }

    public bool TryRegister()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_registered)
        {
            return true;
        }

        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
            RegistrationError = null;
            return true;
        }
        catch (Exception exception)
        {
            RegistrationError = exception.Message;
            return false;
        }
    }

    public Guid Show(string profileName, string title, string body)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        if (!_registered)
        {
            throw new InvalidOperationException(
                RegistrationError ?? "Windows app notifications are not registered.");
        }

        var notificationId = Guid.NewGuid();
        var builder = new AppNotificationBuilder()
            .AddArgument("notificationId", notificationId.ToString("D"))
            .AddArgument("profile", profileName)
            .AddText(LimitText(title, 200));
        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.AddText(LimitText(body, 400));
        }

        AppNotificationManager.Default.Show(builder.BuildNotification());
        return notificationId;
    }

    public IReadOnlyList<WindowsNotificationActivation> DrainPendingActivations()
    {
        lock (_activationLock)
        {
            var pending = _pendingActivations.ToArray();
            _pendingActivations.Clear();
            return pending;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        if (_registered)
        {
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch
            {
                // Process shutdown must continue even if Windows registration cleanup fails.
            }
        }

        _registered = false;
        _disposed = true;
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        if (!TryParseActivation(args.Argument, out var activation))
        {
            return;
        }

        var handler = Activated;
        if (handler is null)
        {
            lock (_activationLock)
            {
                _pendingActivations.Enqueue(activation);
            }

            return;
        }

        handler.Invoke(this, activation);
    }

    public static bool TryParseActivation(
        string arguments,
        out WindowsNotificationActivation activation)
    {
        activation = default!;
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var part in arguments.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2)
                {
                    values[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1]);
                }
            }
        }
        catch (UriFormatException)
        {
            return false;
        }
        if (!values.TryGetValue("notificationId", out var notificationId) ||
            !Guid.TryParse(notificationId, out var id) ||
            !values.TryGetValue("profile", out var profileName) ||
            string.IsNullOrWhiteSpace(profileName))
        {
            return false;
        }

        activation = new WindowsNotificationActivation(id, profileName);
        return true;
    }

    private static string LimitText(string? text, int maximumLength)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "Syltr" : text.Trim();
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
