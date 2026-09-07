namespace WindowLift.Core;

public readonly record struct WindowAdjustment(
    WindowBounds Target,
    bool PositionChanged,
    bool SizeChanged,
    bool FullyVisible)
{
    public bool Changed => PositionChanged || SizeChanged;
}
