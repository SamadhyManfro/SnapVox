namespace SnapVox.Platform;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>Global key combination. <see cref="Key"/> is a letter or digit (A–Z, 0–9) or a virtual-key code.</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, char Key)
{
    // Win+Shift+R is taken by Windows 11 (Snipping Tool screen recording), so it isn't available.
    // Default: Win+Ctrl+R (R for "Read").
    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Win | HotkeyModifiers.Control, 'R');

    /// <summary>Esc alone — registered only while the island is open, so it can always be closed.</summary>
    public static HotkeyGesture Escape { get; } = new(HotkeyModifiers.None, '\u001B');

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(char.ToUpperInvariant(Key).ToString());
        return string.Join("+", parts);
    }
}

/// <summary>System-wide hotkey. Each operating system has its own implementation.</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Raised (on another thread) when the hotkey is pressed.</summary>
    event Action? Pressed;

    /// <summary>Registers the hotkey. Returns false if another app already uses that combination.</summary>
    bool Register(HotkeyGesture gesture);
}
