namespace Basalt.Sdl;

/// <summary>
/// What an <see cref="SdlEvent"/> is about.
/// </summary>
public enum SdlEventKind
{
	/// <summary>An event Basalt.Sdl doesn't map; <see cref="SdlEvent.Type"/> holds SDL's own type.</summary>
	Other,

	/// <summary>The application was asked to quit (the last window closed, or the OS asked).</summary>
	Quit,

	/// <summary>A window was shown.</summary>
	WindowShown,

	/// <summary>A window was hidden.</summary>
	WindowHidden,

	/// <summary>A window needs redrawing.</summary>
	WindowExposed,

	/// <summary>A window moved: <see cref="SdlEvent.X"/>, <see cref="SdlEvent.Y"/> are its position.</summary>
	WindowMoved,

	/// <summary>A window's size changed, in window units: <see cref="SdlEvent.Width"/>, <see cref="SdlEvent.Height"/>.</summary>
	WindowResized,

	/// <summary>A window's size in pixels changed: <see cref="SdlEvent.Width"/>, <see cref="SdlEvent.Height"/>. Resize swap chains on this.</summary>
	WindowPixelSizeChanged,

	/// <summary>A window was minimised.</summary>
	WindowMinimized,

	/// <summary>A window was maximised.</summary>
	WindowMaximized,

	/// <summary>A window was restored from minimised or maximised.</summary>
	WindowRestored,

	/// <summary>The mouse entered a window.</summary>
	WindowMouseEnter,

	/// <summary>The mouse left a window.</summary>
	WindowMouseLeave,

	/// <summary>A window gained keyboard focus.</summary>
	WindowFocusGained,

	/// <summary>A window lost keyboard focus.</summary>
	WindowFocusLost,

	/// <summary>The user asked to close a window.</summary>
	WindowCloseRequested,

	/// <summary>A window moved to another display.</summary>
	WindowDisplayChanged,

	/// <summary>A window's display scale changed (moved to a display with another DPI, or the setting changed).</summary>
	WindowDisplayScaleChanged,

	/// <summary>A window is completely covered by others.</summary>
	WindowOccluded,

	/// <summary>A window went fullscreen.</summary>
	WindowEnterFullscreen,

	/// <summary>A window left fullscreen.</summary>
	WindowLeaveFullscreen,

	/// <summary>A window is being destroyed.</summary>
	WindowDestroyed,

	/// <summary>A key went down, or repeated (<see cref="SdlEvent.Repeat"/>).</summary>
	KeyDown,

	/// <summary>A key went up.</summary>
	KeyUp,

	/// <summary>Text was typed: <see cref="SdlEvent.Text"/>. Only while text input is on for the window.</summary>
	TextInput,

	/// <summary>The mouse moved: <see cref="SdlEvent.X"/>, <see cref="SdlEvent.Y"/> and <see cref="SdlEvent.DeltaX"/>, <see cref="SdlEvent.DeltaY"/>.</summary>
	MouseMotion,

	/// <summary>A mouse button went down: <see cref="SdlEvent.Button"/>.</summary>
	MouseButtonDown,

	/// <summary>A mouse button went up: <see cref="SdlEvent.Button"/>.</summary>
	MouseButtonUp,

	/// <summary>The wheel turned: <see cref="SdlEvent.DeltaX"/>, <see cref="SdlEvent.DeltaY"/>, positive away from the user and to the right.</summary>
	MouseWheel,

	/// <summary>A display was connected: <see cref="SdlEvent.DisplayId"/>.</summary>
	DisplayAdded,

	/// <summary>A display was disconnected: <see cref="SdlEvent.DisplayId"/>.</summary>
	DisplayRemoved,

	/// <summary>A display moved, changed mode, orientation or content scale: <see cref="SdlEvent.DisplayId"/>.</summary>
	DisplayChanged,
}

/// <summary>
/// Mouse buttons.
/// </summary>
public enum SdlMouseButton : byte
{
	/// <summary>No button.</summary>
	None = 0,

	/// <summary>The left button.</summary>
	Left = 1,

	/// <summary>The middle button or wheel click.</summary>
	Middle = 2,

	/// <summary>The right button.</summary>
	Right = 3,

	/// <summary>The first side button (back).</summary>
	X1 = 4,

	/// <summary>The second side button (forward).</summary>
	X2 = 5,
}

/// <summary>
/// Modifier keys held during a key event. The combined values (<see cref="Shift"/>...) match either side.
/// </summary>
[Flags]
public enum SdlKeyModifiers : ushort
{
	/// <summary>None held.</summary>
	None = 0,

	/// <summary>Left shift.</summary>
	LeftShift = 0x0001,

	/// <summary>Right shift.</summary>
	RightShift = 0x0002,

	/// <summary>Left control.</summary>
	LeftCtrl = 0x0040,

	/// <summary>Right control.</summary>
	RightCtrl = 0x0080,

	/// <summary>Left alt.</summary>
	LeftAlt = 0x0100,

	/// <summary>Right alt (AltGr).</summary>
	RightAlt = 0x0200,

	/// <summary>Left Windows, Command or Super key.</summary>
	LeftGui = 0x0400,

	/// <summary>Right Windows, Command or Super key.</summary>
	RightGui = 0x0800,

	/// <summary>Num lock is on.</summary>
	NumLock = 0x1000,

	/// <summary>Caps lock is on.</summary>
	CapsLock = 0x2000,

	/// <summary>Either shift.</summary>
	Shift = LeftShift | RightShift,

	/// <summary>Either control.</summary>
	Ctrl = LeftCtrl | RightCtrl,

	/// <summary>Either alt.</summary>
	Alt = LeftAlt | RightAlt,

	/// <summary>Either Windows, Command or Super key.</summary>
	Gui = LeftGui | RightGui,
}

/// <summary>
/// A physical key, by position on a US keyboard whatever the layout (SDL's scancodes, which are USB HID usages). Keys
/// not named here still arrive, as their number. <see cref="SdlEvent.Keycode"/> is the layout's meaning of the key.
/// </summary>
public enum SdlKey
{
#pragma warning disable CS1591 // The names say what they are
	Unknown = 0,
	A = 4, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
	D1 = 30, D2, D3, D4, D5, D6, D7, D8, D9, D0,
	Return = 40,
	Escape = 41,
	Backspace = 42,
	Tab = 43,
	Space = 44,
	Minus = 45,
	Equals = 46,
	LeftBracket = 47,
	RightBracket = 48,
	Backslash = 49,
	Semicolon = 51,
	Apostrophe = 52,
	Grave = 53,
	Comma = 54,
	Period = 55,
	Slash = 56,
	CapsLock = 57,
	F1 = 58, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
	PrintScreen = 70,
	ScrollLock = 71,
	Pause = 72,
	Insert = 73,
	Home = 74,
	PageUp = 75,
	Delete = 76,
	End = 77,
	PageDown = 78,
	Right = 79,
	Left = 80,
	Down = 81,
	Up = 82,
	NumLock = 83,
	KeypadDivide = 84,
	KeypadMultiply = 85,
	KeypadMinus = 86,
	KeypadPlus = 87,
	KeypadEnter = 88,
	Keypad1 = 89, Keypad2, Keypad3, Keypad4, Keypad5, Keypad6, Keypad7, Keypad8, Keypad9, Keypad0,
	KeypadPeriod = 99,
	LeftCtrl = 224,
	LeftShift = 225,
	LeftAlt = 226,
	LeftGui = 227,
	RightCtrl = 228,
	RightShift = 229,
	RightAlt = 230,
	RightGui = 231,
#pragma warning restore CS1591
}

/// <summary>
/// One event from SDL as a plain value. Which members are set depends on <see cref="Kind"/>; the rest are zero. Mouse
/// positions are in window units (multiply by <see cref="SdlWindow.PixelDensity"/> for pixels).
/// </summary>
public readonly record struct SdlEvent
{
	/// <summary>What it's about.</summary>
	public SdlEventKind Kind { get; init; }

	/// <summary>SDL's own event type number (<c>SDL_EventType</c>), for events Basalt.Sdl doesn't map.</summary>
	public uint Type { get; init; }

	/// <summary>When it happened, in nanoseconds since SDL started.</summary>
	public ulong Timestamp { get; init; }

	/// <summary>The window it's for (<see cref="SdlWindow.Id"/>), or 0.</summary>
	public uint WindowId { get; init; }

	/// <summary>The display it's for, for display events.</summary>
	public uint DisplayId { get; init; }

	/// <summary>The physical key, for key events.</summary>
	public SdlKey Key { get; init; }

	/// <summary>The key's meaning in the current layout (SDL's <c>SDL_Keycode</c>: the character for printable keys).</summary>
	public uint Keycode { get; init; }

	/// <summary>Modifier keys held, for key events.</summary>
	public SdlKeyModifiers Modifiers { get; init; }

	/// <summary>Whether a key-down is the key repeating.</summary>
	public bool Repeat { get; init; }

	/// <summary>The button, for mouse button events.</summary>
	public SdlMouseButton Button { get; init; }

	/// <summary>1 for a single click, 2 for a double click..., for mouse button events.</summary>
	public int Clicks { get; init; }

	/// <summary>The mouse position for mouse events, or the window's position for <see cref="SdlEventKind.WindowMoved"/>.</summary>
	public float X { get; init; }

	/// <summary>The mouse position for mouse events, or the window's position for <see cref="SdlEventKind.WindowMoved"/>.</summary>
	public float Y { get; init; }

	/// <summary>How far the mouse moved, or the wheel turned.</summary>
	public float DeltaX { get; init; }

	/// <summary>How far the mouse moved, or the wheel turned (positive away from the user).</summary>
	public float DeltaY { get; init; }

	/// <summary>The new size, for <see cref="SdlEventKind.WindowResized"/> and <see cref="SdlEventKind.WindowPixelSizeChanged"/>.</summary>
	public int Width { get; init; }

	/// <summary>The new size, for <see cref="SdlEventKind.WindowResized"/> and <see cref="SdlEventKind.WindowPixelSizeChanged"/>.</summary>
	public int Height { get; init; }

	/// <summary>The text typed, for <see cref="SdlEventKind.TextInput"/>.</summary>
	public string Text { get; init; }
}
