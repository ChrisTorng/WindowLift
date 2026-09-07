using System.ComponentModel;
using System.Windows.Threading;
using WindowLift.App.Interop;
using WindowLift.Core;

namespace WindowLift.App.Services;

internal sealed class WindowMonitor : IDisposable
{
    private const long SelfChangeSuppressionMilliseconds = 350;

    private readonly WindowsWindowService _windowService;
    private readonly Dispatcher _dispatcher;
    private readonly Func<WindowAdjustmentOptions> _getOptions;
    private readonly NativeMethods.WinEventProc _eventCallback;
    private readonly Dictionary<nint, int> _taskbarOverlapWindows = [];
    private readonly Dictionary<nint, long> _suppressedUntil = [];
    private readonly Dictionary<nint, AdjustmentAttempt> _attempts = [];
    private nint _foregroundHook;
    private nint _showHook;
    private nint _locationHook;
    private nint _topmostChangedWindow;
    private int _topmostChangedProcessId;
    private int _optionsVersion;
    private int _adjustmentCount;
    private volatile bool _isRunning;

    internal WindowMonitor(
        WindowsWindowService windowService,
        Dispatcher dispatcher,
        Func<WindowAdjustmentOptions> getOptions)
    {
        _windowService = windowService;
        _dispatcher = dispatcher;
        _getOptions = getOptions;
        _eventCallback = OnWindowEvent;
    }

    internal event Action<WindowMonitorUpdate>? Updated;

    internal bool IsRunning => _isRunning;

    internal void Start()
    {
        if (_isRunning)
        {
            return;
        }

        try
        {
            _foregroundHook = _windowService.AddWinEventHook(
                NativeMethods.EventSystemForeground,
                _eventCallback);
            _showHook = _windowService.AddWinEventHook(
                NativeMethods.EventObjectShow,
                _eventCallback);
            _locationHook = _windowService.AddWinEventHook(
                NativeMethods.EventObjectLocationChange,
                _eventCallback);
            _isRunning = true;
        }
        catch
        {
            _windowService.RemoveWinEventHook(_foregroundHook);
            _windowService.RemoveWinEventHook(_showHook);
            _foregroundHook = 0;
            _showHook = 0;
            throw;
        }

        ScanNow(announceCompletion: false);
        Updated?.Invoke(new WindowMonitorUpdate(
            "Monitoring is active. Activate or move an oversized window to adjust it immediately."));
    }

    internal void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _windowService.RemoveWinEventHook(_foregroundHook);
        _windowService.RemoveWinEventHook(_showHook);
        _windowService.RemoveWinEventHook(_locationHook);
        _foregroundHook = 0;
        _showHook = 0;
        _locationHook = 0;
        Win32Exception? releaseError = null;
        try
        {
            ReleaseTopmostChange();
        }
        catch (Win32Exception ex)
        {
            releaseError = ex;
        }
        finally
        {
            _taskbarOverlapWindows.Clear();
            _suppressedUntil.Clear();
            _attempts.Clear();
        }
        Updated?.Invoke(releaseError is null
            ? new WindowMonitorUpdate("Monitoring is stopped.")
            : new WindowMonitorUpdate(
                $"Monitoring stopped, but the last topmost state could not be released: {releaseError.Message}",
                IsError: true));
    }

    internal void OptionsChanged()
    {
        _optionsVersion++;
        _attempts.Clear();
        if (_isRunning)
        {
            ScanNow(announceCompletion: false);
        }
    }

    internal void ScanNow(bool announceCompletion = true)
    {
        if (!_isRunning)
        {
            return;
        }

        var adjustedThisScan = 0;
        foreach (var window in _windowService.EnumerateCandidateWindows())
        {
            try
            {
                if (AdjustWindow(window))
                {
                    adjustedThisScan++;
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                Updated?.Invoke(new WindowMonitorUpdate(
                    $"Could not adjust {DisplayName(window)}: {ex.Message}",
                    IsError: true));
            }
        }

        try
        {
            UpdateForeground(_windowService.ForegroundWindow);
        }
        catch (Win32Exception ex)
        {
            Updated?.Invoke(new WindowMonitorUpdate(ex.Message, IsError: true));
        }
        RemoveClosedWindows();

        if (announceCompletion)
        {
            Updated?.Invoke(new WindowMonitorUpdate(
                adjustedThisScan == 0
                    ? "Scan complete. No windows needed adjustment."
                    : $"Scan complete. Adjusted {adjustedThisScan} window(s). Total adjustments: {_adjustmentCount}."));
        }
    }

    public void Dispose() => Stop();

    private void OnWindowEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        _ = hook;
        _ = childId;
        _ = eventThread;
        _ = eventTime;

        if (!_isRunning || window == 0)
        {
            return;
        }

        if ((eventType == NativeMethods.EventObjectShow
             || eventType == NativeMethods.EventObjectLocationChange)
            && objectId != NativeMethods.ObjidWindow)
        {
            return;
        }

        try
        {
            if (!_dispatcher.HasShutdownStarted)
            {
                _ = _dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => HandleWindowEvent(eventType, window)));
            }
        }
        catch (InvalidOperationException)
        {
            // The WPF dispatcher can finish shutting down between the state check
            // and BeginInvoke. Native callbacks must never escape into user32.
        }
    }

    private void HandleWindowEvent(uint eventType, nint window)
    {
        if (!_isRunning)
        {
            return;
        }

        try
        {
            if (eventType == NativeMethods.EventSystemForeground)
            {
                UpdateForeground(window);
                return;
            }

            if (eventType == NativeMethods.EventObjectLocationChange && IsSuppressed(window))
            {
                return;
            }

            var descriptor = _windowService.DescribeCandidate(window);
            if (descriptor is null)
            {
                ForgetWindow(window);
            }
            else
            {
                _ = AdjustWindow(descriptor);
            }

            if (window == _windowService.ForegroundWindow)
            {
                UpdateForeground(window);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Updated?.Invoke(new WindowMonitorUpdate(ex.Message, IsError: true));
        }
    }

    private void UpdateForeground(nint foregroundWindow)
    {
        if (_topmostChangedWindow != 0 && _topmostChangedWindow != foregroundWindow)
        {
            ReleaseTopmostChange();
        }

        var descriptor = _windowService.DescribeCandidate(foregroundWindow);
        if (descriptor is null)
        {
            ReleaseTopmostChange();
            return;
        }

        _ = AdjustWindow(descriptor);
        var options = _getOptions();
        var shouldBeTopmost = options.AllowTaskbarOverlap
                              && _taskbarOverlapWindows.ContainsKey(foregroundWindow);

        if (!shouldBeTopmost)
        {
            ReleaseTopmostChange();
            return;
        }

        if (_topmostChangedWindow == foregroundWindow)
        {
            return;
        }

        if (!_windowService.IsTopmost(foregroundWindow))
        {
            _windowService.SetTopmost(foregroundWindow, enabled: true);
            _topmostChangedWindow = foregroundWindow;
            _topmostChangedProcessId = descriptor.ProcessId;
        }
    }

    private bool AdjustWindow(WindowDescriptor window)
    {
        var current = _windowService.GetBounds(window.Handle);
        var areas = _windowService.GetMonitorAreas(window.Handle);
        var options = _getOptions() with
        {
            TitleBarHeight = _windowService.GetTitleBarHeight(window.Handle)
        };
        var adjustment = VerticalPlacementCalculator.Calculate(current, areas, options);

        if (_attempts.TryGetValue(window.Handle, out var previousAttempt)
            && previousAttempt.ProcessId == window.ProcessId
            && previousAttempt.Observed == current
            && previousAttempt.Target == adjustment.Target
            && previousAttempt.OptionsVersion == _optionsVersion)
        {
            UpdateTaskbarOverlapState(window, current, areas);
            return false;
        }

        if (!adjustment.Changed)
        {
            _attempts.Remove(window.Handle);
            UpdateTaskbarOverlapState(window, current, areas);
            return false;
        }

        _attempts[window.Handle] = new AdjustmentAttempt(
            window.ProcessId,
            current,
            adjustment.Target,
            _optionsVersion);
        _suppressedUntil[window.Handle] = checked(
            Environment.TickCount64 + SelfChangeSuppressionMilliseconds);
        _windowService.MoveAndResize(window.Handle, adjustment.Target);

        var actual = _windowService.GetBounds(window.Handle);
        UpdateTaskbarOverlapState(window, actual, areas);
        if (actual == adjustment.Target)
        {
            _attempts.Remove(window.Handle);
        }
        else
        {
            _attempts[window.Handle] = new AdjustmentAttempt(
                window.ProcessId,
                actual,
                adjustment.Target,
                _optionsVersion);
        }

        _adjustmentCount++;
        var action = adjustment.SizeChanged ? "moved and resized" : "moved";
        var suffix = actual == adjustment.Target
            ? string.Empty
            : " The app did not accept the full requested bounds; WindowLift will not retry until its bounds or options change.";
        Updated?.Invoke(new WindowMonitorUpdate(
            $"{DisplayName(window)} was {action}. Total adjustments: {_adjustmentCount}.{suffix}"));
        return true;
    }

    private void UpdateTaskbarOverlapState(
        WindowDescriptor window,
        WindowBounds bounds,
        MonitorAreas areas)
    {
        if (VerticalPlacementCalculator.OverlapsVerticalTaskbar(bounds, areas))
        {
            _taskbarOverlapWindows[window.Handle] = window.ProcessId;
        }
        else
        {
            _taskbarOverlapWindows.Remove(window.Handle);
        }
    }

    private bool IsSuppressed(nint window)
    {
        if (!_suppressedUntil.TryGetValue(window, out var until))
        {
            return false;
        }

        if (Environment.TickCount64 <= until)
        {
            return true;
        }

        _suppressedUntil.Remove(window);
        return false;
    }

    private void ReleaseTopmostChange()
    {
        var window = _topmostChangedWindow;
        var processId = _topmostChangedProcessId;
        _topmostChangedWindow = 0;
        _topmostChangedProcessId = 0;
        if (window != 0
            && _windowService.IsSameWindow(window, processId)
            && _windowService.IsTopmost(window))
        {
            _windowService.SetTopmost(window, enabled: false);
        }
    }

    private void ForgetWindow(nint window)
    {
        if (_topmostChangedWindow == window)
        {
            ReleaseTopmostChange();
        }

        _taskbarOverlapWindows.Remove(window);
        _suppressedUntil.Remove(window);
        _attempts.Remove(window);
    }

    private void RemoveClosedWindows()
    {
        foreach (var pair in _taskbarOverlapWindows.ToArray())
        {
            if (!_windowService.IsSameWindow(pair.Key, pair.Value))
            {
                ForgetWindow(pair.Key);
            }
        }
    }

    private static string DisplayName(WindowDescriptor window) =>
        string.IsNullOrWhiteSpace(window.Title)
            ? $"{window.ProcessName}.exe"
            : $"{window.ProcessName}.exe — {window.Title}";

    private sealed record AdjustmentAttempt(
        int ProcessId,
        WindowBounds Observed,
        WindowBounds Target,
        int OptionsVersion);
}
