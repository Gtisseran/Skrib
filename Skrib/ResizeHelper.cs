using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Windows.UI;

namespace Skrib;

/// <summary>
/// Smooths window resizing and removes the black bands that flash at the edges while
/// a border is dragged. The bands are a platform behavior: during a live resize the
/// XAML content trails the system window by a frame, and the strip not yet covered
/// shows the raw window-class background, which is black (tracked as
/// microsoft-ui-xaml#5148). Three standard Win32 mitigations, each independent:
/// 1. DwmExtendFrameIntoClientArea with {-1} margins composes the DWM sheet under the
///    whole client area (what WPF's WindowChrome does internally), so the trailing
///    strip shows the backdrop material (Mica / Acrylic) instead of black.
/// 2. WM_ERASEBKGND fills the client area with the app theme color (MainWindow
///    passes the editor background color, see SetBandColor) instead of the default
///    GDI erase, which paints the window-class black brush right over that sheet.
/// 3. While the drag lasts (WM_ENTERSIZEMOVE .. WM_EXITSIZEMOVE) WS_EX_COMPOSITED
///    routes the repaints through DWM double buffering, and every WM_SIZE gets one
///    coalesced layout pass so edge-anchored content (title bar buttons, bottom
///    bar) tracks the window live instead of stuttering between frames; one final
///    pass settles the content exactly on the final size.
/// </summary>
public static class ResizeHelper
{
    private const int GWLP_WNDPROC = -4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_COMPOSITED = 0x02000000;

    private const uint WM_SIZE = 0x0005;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_EXITSIZEMOVE = 0x0232;

    // WM_SIZE wParam code: the window was minimized (nothing visible to lay out).
    private const nint SIZE_MINIMIZED = 1;

    // DWM frame tone; ignored (harmless error) on Windows versions without it.
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);

    // GetWindowLongPtrW does not exist on x86, hence the per-word-size dispatch.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(nint hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint previousProc, nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint hdc, ref RECT rect, nint brush);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint brush);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    // Rooted for the life of the process: the OS keeps a raw pointer to this delegate.
    private static WndProcDelegate? _wndProc;
    private static readonly Dictionary<nint, nint> OriginalWndProcs = new();
    private static DispatcherQueue? _dispatcherQueue;
    // Coalesced layout pass for the XAML island: queued at most once per dispatcher
    // tick during a drag (WM_SIZE), plus once when the drag ends (WM_EXITSIZEMOVE).
    private static Action? _layoutTick;
    private static bool _dragging;
    private static bool _layoutTickQueued;
    // Trailing-strip color, cached as a GDI brush (COLORREF 0x00BBGGRR + HBRUSH).
    private static uint _bandColorRef;
    private static nint _bandBrush;

    /// <summary>
    /// Applies the resize treatments once, on the UI thread, at startup. The optional
    /// <paramref name="layoutTick"/> runs a coalesced layout pass during and after a
    /// resize drag so the XAML content tracks the window.
    /// </summary>
    public static void Attach(nint hwnd, Action? layoutTick)
    {
        ExtendDwmSheet(hwnd);
        _layoutTick = layoutTick;
        SubclassWndProc(hwnd);
    }

    /// <summary>
    /// Keeps the DWM sheet visible behind the content (the resize trailing strip) in
    /// the app theme instead of the system-registry theme.
    /// </summary>
    public static void SetDarkSheet(nint hwnd, bool dark)
    {
        int value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    /// <summary>
    /// Sets the color painted in the strip where the XAML content trails the window
    /// during a resize. MainWindow passes the editor background color so the strip
    /// blends with the largest surface of the window.
    /// </summary>
    public static void SetBandColor(Windows.UI.Color color)
    {
        // COLORREF layout is 0x00BBGGRR.
        uint colorRef = (uint)color.R | ((uint)color.G << 8) | ((uint)color.B << 16);
        if (colorRef == _bandColorRef && _bandBrush != 0) return;
        if (_bandBrush != 0) DeleteObject(_bandBrush);
        _bandColorRef = colorRef;
        _bandBrush = CreateSolidBrush(colorRef);
    }

    private static void ExtendDwmSheet(nint hwnd)
    {
        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    private static void SubclassWndProc(nint hwnd)
    {
        if (OriginalWndProcs.ContainsKey(hwnd)) return;

        _wndProc ??= WndProc;
        // Returns the previous window proc; 0 means the call failed (the DWM sheet
        // above still applies).
        nint previous = SetWindowLong(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));
        if (previous == 0) return;

        OriginalWndProcs[hwnd] = previous;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (!OriginalWndProcs.TryGetValue(hwnd, out nint original))
        {
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        switch (msg)
        {
            case WM_ERASEBKGND:
                // Paint the trailing strip in the app theme color instead of letting
                // the default GDI erase (window-class black) or the DWM sheet tone
                // show through.
                FillBand(hwnd, wParam);
                return (nint)1;

            case WM_SIZE:
                // While the drag lasts, give the XAML island one layout pass per
                // dispatcher tick: edge-anchored content (top-right buttons, bottom
                // bar) then tracks the window every frame instead of stuttering
                // between frames or snapping when the drag ends.
                if (_dragging && wParam != SIZE_MINIMIZED && !_layoutTickQueued)
                {
                    _layoutTickQueued = true;
                    QueueLayoutTick();
                }
                break;

            case WM_ENTERSIZEMOVE:
                _dragging = true;
                ToggleComposited(hwnd, on: true);
                break;

            case WM_EXITSIZEMOVE:
                _dragging = false;
                ToggleComposited(hwnd, on: false);
                // Fires after both moves and resizes; one final pass settles the
                // content exactly on the final size (a no-op when the per-frame
                // ticks already caught up).
                if (!_layoutTickQueued)
                {
                    _layoutTickQueued = true;
                    QueueLayoutTick();
                }
                break;
        }

        return CallWindowProc(original, hwnd, msg, wParam, lParam);
    }

    // Runs the layout tick on the UI thread and releases the queue slot. The tick
    // runs outside WndProc, so a failure cannot reenter the message handler.
    private static void QueueLayoutTick()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            _layoutTickQueued = false;
            try
            {
                _layoutTick?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error during resize layout tick: {ex.Message}");
            }
        });
    }

    private static void ToggleComposited(nint hwnd, bool on)
    {
        long exStyle = (long)GetWindowLong(hwnd, GWL_EXSTYLE);
        long updated = on ? exStyle | WS_EX_COMPOSITED : exStyle & ~WS_EX_COMPOSITED;
        _ = SetWindowLong(hwnd, GWL_EXSTYLE, (nint)updated);
    }

    // Fills the client area with the theme band color. The XAML island is a child
    // window composited by DWM above the parent surface, so this fill never covers
    // the visible content; it only colors the strip the content has not reached yet.
    private static void FillBand(nint hwnd, nint hdc)
    {
        if (_bandBrush == 0) return;
        nint dc = hdc != 0 ? hdc : GetDC(hwnd);
        if (dc != 0 && GetClientRect(hwnd, out RECT rect))
        {
            _ = FillRect(dc, ref rect, _bandBrush);
        }
        if (hdc == 0 && dc != 0) _ = ReleaseDC(hwnd, dc);
    }

    private static nint GetWindowLong(nint hwnd, int index)
    {
        return nint.Size == sizeof(int)
            ? GetWindowLong32(hwnd, index)
            : GetWindowLongPtr64(hwnd, index);
    }

    private static nint SetWindowLong(nint hwnd, int index, nint value)
    {
        return nint.Size == sizeof(int)
            ? SetWindowLong32(hwnd, index, (int)value)
            : SetWindowLongPtr64(hwnd, index, value);
    }
}
