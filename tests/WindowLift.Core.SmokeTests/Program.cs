using WindowLift.Core;

var failures = new List<string>();
var monitor = new WindowBounds(0, 0, 1920, 1080);
var bottomTaskbar = new MonitorAreas(monitor, new WindowBounds(0, 0, 1920, 1040));

Check(
    Calculate(new WindowBounds(200, 100, 900, 700), bottomTaskbar) is
        { Changed: false, FullyVisible: true },
    "A visible window is unchanged.");

Check(
    Calculate(new WindowBounds(200, -20, 900, 700), bottomTaskbar).Target
        == new WindowBounds(200, 0, 900, 700),
    "A normal-height window cannot hide above the top edge.");

Check(
    Calculate(new WindowBounds(200, 700, 900, 500), bottomTaskbar).Target
        == new WindowBounds(200, 540, 900, 500),
    "A window is lifted without changing its width, height, or horizontal position.");

Check(
    Calculate(
        new WindowBounds(200, 600, 900, 470),
        bottomTaskbar,
        allowTaskbarOverlap: true).Target
        == new WindowBounds(200, 600, 900, 470),
    "Taskbar overlap leaves a window unchanged when it fits within the monitor.");

Check(
    Calculate(
        new WindowBounds(200, 600, 900, 470),
        bottomTaskbar,
        allowTaskbarOverlap: false).Target
        == new WindowBounds(200, 570, 900, 470),
    "Disabling taskbar overlap keeps the same window inside the work area.");

Check(
    Calculate(
        new WindowBounds(200, 0, 900, 1200),
        bottomTaskbar,
        allowTaskbarOverlap: false,
        allowTitleBarLift: false,
        shrinkToFit: true).Target
        == new WindowBounds(200, 0, 900, 1040),
    "Shrink-to-fit uses the work area when taskbar overlap is disabled.");

Check(
    Calculate(
        new WindowBounds(200, 0, 900, 1200),
        bottomTaskbar,
        allowTaskbarOverlap: true,
        allowTitleBarLift: false,
        shrinkToFit: true).Target
        == new WindowBounds(200, 0, 900, 1080),
    "Taskbar overlap uses the full monitor height but never crosses its bottom edge.");

Check(
    Calculate(
        new WindowBounds(200, 0, 900, 1200),
        bottomTaskbar,
        allowTaskbarOverlap: true,
        allowTitleBarLift: true,
        shrinkToFit: true,
        titleBarHeight: 32).Target
        == new WindowBounds(200, -32, 900, 1112),
    "Title-bar lift contributes one title-bar height to the usable height.");

Check(
    Calculate(
        new WindowBounds(200, 0, 900, 1200),
        bottomTaskbar,
        allowTaskbarOverlap: false,
        allowTitleBarLift: true,
        shrinkToFit: true,
        titleBarHeight: 32).Target
        == new WindowBounds(200, -32, 900, 1072),
    "Title-bar lift can preserve content while still stopping above a bottom taskbar.");

var topTaskbar = new MonitorAreas(monitor, new WindowBounds(0, 40, 1920, 1040));
Check(
    Calculate(
        new WindowBounds(200, 0, 900, 1200),
        topTaskbar,
        allowTaskbarOverlap: false,
        allowTitleBarLift: true,
        shrinkToFit: true,
        titleBarHeight: 32).Target
        == new WindowBounds(200, 40, 900, 1040),
    "Title-bar lift never covers a top taskbar when overlap is disabled.");

Check(
    VerticalPlacementCalculator.OverlapsVerticalTaskbar(
        new WindowBounds(200, -32, 900, 1000),
        bottomTaskbar) == false,
    "A lifted title bar is not mistaken for overlap with a bottom taskbar.");
Check(
    VerticalPlacementCalculator.OverlapsVerticalTaskbar(
        new WindowBounds(200, 100, 900, 950),
        bottomTaskbar),
    "A lower edge inside the bottom taskbar area is detected.");
Check(
    VerticalPlacementCalculator.OverlapsVerticalTaskbar(
        new WindowBounds(200, 20, 900, 900),
        topTaskbar),
    "A top edge inside the top taskbar area is detected.");

var leftMonitor = new MonitorAreas(
    new WindowBounds(-1920, -100, 1920, 1080),
    new WindowBounds(-1920, -100, 1920, 1040));
Check(
    Calculate(
        new WindowBounds(-1500, -100, 800, 1200),
        leftMonitor,
        allowTaskbarOverlap: true,
        allowTitleBarLift: true,
        shrinkToFit: true,
        titleBarHeight: 30).Target
        == new WindowBounds(-1500, -130, 800, 1110),
    "Negative monitor coordinates are preserved.");

var unresolved = Calculate(
    new WindowBounds(10, 10, 900, 1200),
    bottomTaskbar,
    allowTaskbarOverlap: false,
    allowTitleBarLift: false,
    shrinkToFit: false);
Check(
    unresolved.Target == new WindowBounds(10, 0, 900, 1200) && !unresolved.FullyVisible,
    "Disabling resizing preserves height and reports an unresolved overflow.");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} smoke test(s) failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("All WindowLift core smoke tests passed.");
return 0;

WindowAdjustment Calculate(
    WindowBounds window,
    MonitorAreas areas,
    bool allowTaskbarOverlap = false,
    bool allowTitleBarLift = false,
    bool shrinkToFit = false,
    int titleBarHeight = 0) =>
    VerticalPlacementCalculator.Calculate(
        window,
        areas,
        new WindowAdjustmentOptions(
            allowTaskbarOverlap,
            allowTitleBarLift,
            shrinkToFit,
            titleBarHeight));

void Check(bool condition, string name)
{
    if (!condition)
    {
        failures.Add(name);
    }
}
