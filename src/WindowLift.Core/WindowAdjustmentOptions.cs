namespace WindowLift.Core;

public readonly record struct WindowAdjustmentOptions(
    bool AllowTaskbarOverlap,
    bool AllowTitleBarLift,
    bool ShrinkToFit,
    int TitleBarHeight);
