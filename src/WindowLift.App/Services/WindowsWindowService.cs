using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WindowLift.App.Interop;
using WindowLift.Core;

namespace WindowLift.App.Services;

internal sealed class WindowsWindowService
{
    internal nint ForegroundWindow => NativeMethods.GetForegroundWindow();

    internal IReadOnlyList<WindowDescriptor> EnumerateCandidateWindows()
    {
        var windows = new List<WindowDescriptor>();
        _ = NativeMethods.EnumWindows(
            (window, _) =>
            {
                var descriptor = DescribeCandidate(window);
                if (descriptor is not null)
                {
                    windows.Add(descriptor);
                }

                return true;
            },
            0);
        return windows;
    }

    internal WindowDescriptor? DescribeCandidate(nint window)
    {
        if (!NativeMethods.IsWindow(window)
            || !NativeMethods.IsWindowVisible(window)
            || NativeMethods.IsIconic(window)
            || NativeMethods.IsZoomed(window))
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == checked((uint)Environment.ProcessId) || IsCloaked(window))
        {
            return null;
        }

        var titleLength = NativeMethods.GetWindowTextLength(window);
        if (titleLength <= 0)
        {
            return null;
        }

        var titleBuffer = new StringBuilder(titleLength + 1);
        var copied = NativeMethods.GetWindowText(window, titleBuffer, titleBuffer.Capacity);
        if (copied <= 0)
        {
            return null;
        }

        return new WindowDescriptor(
            window,
            checked((int)processId),
            GetProcessName(processId),
            titleBuffer.ToString());
    }

    internal bool IsSameWindow(nint window, int processId)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(window, out var currentProcessId);
        return currentProcessId == processId;
    }

    internal bool IsTopmost(nint window) =>
        (NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64()
         & NativeMethods.WsExTopmost) != 0;

    internal WindowBounds GetBounds(nint window)
    {
        if (!NativeMethods.GetWindowRect(window, out var rect))
        {
            throw NewWin32Exception("Unable to read the window bounds.");
        }

        return ToBounds(rect);
    }

    internal MonitorAreas GetMonitorAreas(nint window)
    {
        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.MonitorInfo>())
        };

        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            throw NewWin32Exception("Unable to read the monitor bounds.");
        }

        return new MonitorAreas(ToBounds(info.Monitor), ToBounds(info.WorkArea));
    }

    internal int GetTitleBarHeight(nint window)
    {
        var dpi = NativeMethods.GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCyCaption, dpi)
               + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCyFrame, dpi)
               + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, dpi);
    }

    internal void MoveAndResize(nint window, WindowBounds bounds)
    {
        var flags = NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder;
        if (!NativeMethods.SetWindowPos(
                window,
                0,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                flags))
        {
            throw NewWin32Exception("Unable to adjust the window.");
        }
    }

    internal void SetTopmost(nint window, bool enabled)
    {
        var insertAfter = enabled ? NativeMethods.HwndTopmost : NativeMethods.HwndNoTopmost;
        var flags = NativeMethods.SwpNoActivate | NativeMethods.SwpNoMove | NativeMethods.SwpNoSize;
        if (!NativeMethods.SetWindowPos(window, insertAfter, 0, 0, 0, 0, flags))
        {
            throw NewWin32Exception("Unable to change the window's topmost state.");
        }
    }

    internal nint AddWinEventHook(uint eventType, NativeMethods.WinEventProc callback)
    {
        var hook = NativeMethods.SetWinEventHook(
            eventType,
            eventType,
            0,
            callback,
            0,
            0,
            NativeMethods.WineventOutOfContext);

        return hook != 0
            ? hook
            : throw NewWin32Exception("Unable to start Windows event monitoring.");
    }

    internal void RemoveWinEventHook(nint hook)
    {
        if (hook != 0)
        {
            _ = NativeMethods.UnhookWinEvent(hook);
        }
    }

    private static bool IsCloaked(nint window) =>
        NativeMethods.DwmGetWindowAttribute(
            window,
            NativeMethods.DwmwaCloaked,
            out var cloaked,
            sizeof(int)) == 0
        && cloaked != 0;

    private static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return "<unavailable>";
        }
    }

    private static WindowBounds ToBounds(NativeMethods.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static Win32Exception NewWin32Exception(string message) =>
        new(Marshal.GetLastWin32Error(), message);
}
