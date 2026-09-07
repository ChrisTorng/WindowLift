namespace WindowLift.Core;

public sealed record WindowDescriptor(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string Title);
