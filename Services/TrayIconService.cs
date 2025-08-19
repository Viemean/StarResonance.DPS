using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using StarResonance.DPS.ViewModels;

namespace StarResonance.DPS.Services;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct Notifyicondata
{
    public int cbSize;
    public IntPtr hWnd;
    public int uID;
    public uint uFlags;
    public int uCallbackMessage;
    public IntPtr hIcon;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Point
{
    public int X;
    public int Y;
}

/// <summary>
///     使用 P/Invoke 实现系统托盘图标功能的服务。
///     这个类是 partial，因为它包含了由 LibraryImport 源生成器实现的方法。
/// </summary>
public partial class TrayIconService(Window window, MainViewModel viewModel) : IDisposable
{
    private const int WmTrayIcon = 0x8001;
    private const int WmCommand = 0x0111;

    private HwndSource? _hwndSource;
    private Notifyicondata _notifyIconData;

    public void Dispose()
    {
        RemoveIcon();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Initialize()
    {
        var windowInteropHelper = new WindowInteropHelper(window);
        _hwndSource = HwndSource.FromHwnd(windowInteropHelper.EnsureHandle());
        _hwndSource?.AddHook(WndProc);

        _notifyIconData = new Notifyicondata
        {
            cbSize = Marshal.SizeOf<Notifyicondata>(),
            hWnd = _hwndSource!.Handle,
            uID = 1,
            uFlags = NifIcon | NifMessage | NifTip,
            uCallbackMessage = WmTrayIcon,
            szTip = viewModel.Localization["Tray_Tooltip"] ?? "Star Resonance DPS (Double-click to show)"
        };

        var iconUri = new Uri("pack://application:,,,/Assets/icon.ico", UriKind.RelativeOrAbsolute);
        using var stream = Application.GetResourceStream(iconUri)?.Stream;
        if (stream == null) return;
        using var icon = new Icon(stream);
        _notifyIconData.hIcon = icon.Handle;
        AddIcon();
    }

    private void AddIcon()
    {
        Shell_NotifyIcon(NimAdd, ref _notifyIconData);
    }

    private void RemoveIcon()
    {
        Shell_NotifyIcon(NimDelete, ref _notifyIconData);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmTrayIcon:
                switch (lParam.ToInt32())
                {
                    case WmLbuttondblclk:
                        RestoreWindow();
                        handled = true;
                        break;
                    case WmRbuttonup:
                        ShowContextMenu();
                        handled = true;
                        break;
                }

                break;
            case WmCommand:
                OnContextMenuCommand(wParam.ToInt32());
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        var menu = CreateMenu();
        GetCursorPos(out var point);
        SetForegroundWindow(_notifyIconData.hWnd);
        TrackPopupMenu(menu, TpmRightalign, point.X, point.Y, 0, _notifyIconData.hWnd, IntPtr.Zero);
        DestroyMenu(menu);
    }

    private IntPtr CreateMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, 1, viewModel.Localization["RestoreWindow"] ?? "Restore");
        AppendMenu(menu, MfString, 2, viewModel.Localization["Minimize"] ?? "Minimize");
        AppendMenu(menu, MfString, 3, viewModel.LockMenuHeaderText);
        AppendMenu(menu, MfSeparator, 0, string.Empty);
        AppendMenu(menu, MfString, 4, viewModel.Localization["Exit"] ?? "Exit");
        return menu;
    }

    private void OnContextMenuCommand(int commandId)
    {
        switch (commandId)
        {
            case 1: RestoreWindow(); break;
            case 2: window.WindowState = WindowState.Minimized; break;
            case 3: viewModel.ToggleLock(); break;
            case 4: viewModel.ExitApplicationCommand.Execute(null); break;
        }
    }

    private void RestoreWindow()
    {
        if (window.IsVisible)
        {
            // 隐藏窗口
            window.Hide();
        }
        else
        {
            // 显示窗口并激活
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }
    }
    #region P/Invoke Definitions

    // 保留 DllImport 用于有问题的方法
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref Notifyicondata lpData);


    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIdNewItem, string lpNewItem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial void GetCursorPos(out Point lpPoint);

    [LibraryImport("user32.dll")]
    private static partial void TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd,
        IntPtr prcRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial void DestroyMenu(IntPtr hMenu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial void SetForegroundWindow(IntPtr hWnd);

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const int WmLbuttondblclk = 0x0203;
    private const int WmRbuttonup = 0x0205;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightalign = 0x0008;

    #endregion
}