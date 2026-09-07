namespace WindowLift.Core;

public readonly record struct WindowBounds(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);
    public int Bottom => checked(Top + Height);
}
