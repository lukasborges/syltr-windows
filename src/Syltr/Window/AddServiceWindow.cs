using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Syltr.Catalog;
using Syltr.Localization;
using Windows.Graphics;

namespace Syltr.Window;

/// <summary>
/// Owns the native-window behavior for the service catalog.
/// </summary>
public sealed class AddServiceWindow : Microsoft.UI.Xaml.Window
{
    private const int OwnerWindowIndex = -8;
    private readonly nint _ownerHandle;
    private readonly TaskCompletionSource<AddServiceWindowResult?> _completion = new();
    private readonly ServiceCatalogView _catalogView = new();
    private AddServiceWindowResult? _result;
    private bool _initialFocusSet;

    public AddServiceWindow(Microsoft.UI.Xaml.Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Title = AppText.Get("AddService_Title");
        _ownerHandle = ConfigureWindow(owner);
        Content = _catalogView;

        _catalogView.Completed += OnCatalogCompleted;
        _catalogView.CancelRequested += OnCancelRequested;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    public Task<AddServiceWindowResult?> ShowAsync()
    {
        EnableWindow(_ownerHandle, false);
        Activate();
        return _completion.Task;
    }

    private nint ConfigureWindow(Microsoft.UI.Xaml.Window owner)
    {
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.SetPresenter(OverlappedPresenter.CreateForDialog());
        AppWindow.Resize(new SizeInt32(500, 650));

        var ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        SetOwner(WinRT.Interop.WindowNative.GetWindowHandle(this), ownerHandle);
        CenterOverOwner(owner);
        return ownerHandle;
    }

    private void CenterOverOwner(Microsoft.UI.Xaml.Window owner)
    {
        var ownerPosition = owner.AppWindow.Position;
        var ownerSize = owner.AppWindow.Size;
        var dialogSize = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            ownerPosition.X + Math.Max(0, (ownerSize.Width - dialogSize.Width) / 2),
            ownerPosition.Y + Math.Max(0, (ownerSize.Height - dialogSize.Height) / 2)));
    }

    private void OnCatalogCompleted(object? sender, AddServiceWindowResult result)
    {
        _result = result;
        Close();
    }

    private void OnCancelRequested(object? sender, EventArgs args) => Close();

    private void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated)
        {
            return;
        }

        EnableWindow(_ownerHandle, false);
        if (!_initialFocusSet)
        {
            _initialFocusSet = true;
            DispatcherQueue.TryEnqueue(_catalogView.FocusSearch);
        }
    }

    private void OnClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        _catalogView.Completed -= OnCatalogCompleted;
        _catalogView.CancelRequested -= OnCancelRequested;
        Activated -= OnActivated;
        Closed -= OnClosed;
        EnableWindow(_ownerHandle, true);
        SetForegroundWindow(_ownerHandle);
        _completion.TrySetResult(_result);
    }

    private static void SetOwner(nint windowHandle, nint ownerHandle)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(windowHandle, OwnerWindowIndex, ownerHandle);
            return;
        }

        SetWindowLong32(windowHandle, OwnerWindowIndex, ownerHandle.ToInt32());
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(nint windowHandle, int index, int newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(nint windowHandle, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}

public sealed record AddServiceWindowResult(ServiceCatalogEntry? Entry, bool CustomRequested);
