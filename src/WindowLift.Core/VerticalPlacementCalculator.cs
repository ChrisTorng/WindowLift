namespace WindowLift.Core;

public static class VerticalPlacementCalculator
{
    public static bool OverlapsVerticalTaskbar(WindowBounds window, MonitorAreas areas)
    {
        ValidateBounds(window, nameof(window));
        ValidateBounds(areas.Monitor, nameof(areas));
        ValidateBounds(areas.WorkArea, nameof(areas));

        var overlapsTopTaskbar = areas.WorkArea.Top > areas.Monitor.Top
                                 && window.Top < areas.WorkArea.Top;
        var overlapsBottomTaskbar = areas.WorkArea.Bottom < areas.Monitor.Bottom
                                    && window.Bottom > areas.WorkArea.Bottom;
        return overlapsTopTaskbar || overlapsBottomTaskbar;
    }

    public static WindowAdjustment Calculate(
        WindowBounds window,
        MonitorAreas areas,
        WindowAdjustmentOptions options)
    {
        ValidateBounds(window, nameof(window));
        ValidateBounds(areas.Monitor, nameof(areas));
        ValidateBounds(areas.WorkArea, nameof(areas));

        if (options.TitleBarHeight < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The title bar height cannot be negative.");
        }

        var normalTop = options.AllowTaskbarOverlap
            ? areas.Monitor.Top
            : areas.WorkArea.Top;
        var bottomLimit = options.AllowTaskbarOverlap
            ? areas.Monitor.Bottom
            : areas.WorkArea.Bottom;

        if (bottomLimit <= normalTop)
        {
            throw new ArgumentOutOfRangeException(
                nameof(areas),
                "The selected monitor has no usable vertical area.");
        }

        var normalAvailableHeight = bottomLimit - normalTop;
        var windowIsTooTall = window.Height > normalAvailableHeight;
        var canLiftPastMonitor = options.AllowTitleBarLift
                                 && windowIsTooTall
                                 && (options.AllowTaskbarOverlap
                                     || areas.WorkArea.Top <= areas.Monitor.Top);
        var topLimit = canLiftPastMonitor
            ? checked(areas.Monitor.Top - options.TitleBarHeight)
            : normalTop;
        var availableHeight = bottomLimit - topLimit;

        var targetHeight = options.ShrinkToFit
            ? Math.Min(window.Height, availableHeight)
            : window.Height;

        int targetTop;
        if (targetHeight <= availableHeight)
        {
            targetTop = Math.Clamp(window.Top, topLimit, bottomLimit - targetHeight);
        }
        else
        {
            // Without resizing there is no fully visible placement. Keep the title
            // reachable and expose as much of the lower edge as the policy permits.
            targetTop = topLimit;
        }

        var target = new WindowBounds(window.Left, targetTop, window.Width, targetHeight);
        return new WindowAdjustment(
            target,
            PositionChanged: target.Top != window.Top,
            SizeChanged: target.Height != window.Height,
            FullyVisible: target.Top >= topLimit && target.Bottom <= bottomLimit);
    }

    private static void ValidateBounds(WindowBounds bounds, string parameterName)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Bounds must have a positive size.");
        }
    }
}
