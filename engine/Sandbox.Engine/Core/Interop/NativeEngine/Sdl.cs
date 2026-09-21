using System.Runtime.InteropServices;

namespace NativeEngine;

/// <summary>
/// SDL3 bindings and handwritten ABI mirrors. Interop initialization validates native structure sizes.
/// </summary>
internal static partial class Sdl
{
	internal const int WindowPositionCentered = 0x2fff0000;
	internal static uint PixelFormatRgba32 => BitConverter.IsLittleEndian ? 0x16762004u : 0x16462004u;

	internal enum SystemCursor
	{
		Default, Text, Wait, Crosshair, Progress, NwseResize, NeswResize,
		EwResize, NsResize, Move, NotAllowed, Pointer
	}

	internal enum FlashOperation { Cancel, Briefly, UntilFocused }

	[Flags]
	internal enum InitFlags : uint { Gamepad = 0x00002000 }

	internal enum EventType : uint
	{
		Quit = 0x100,
		WindowExposed = 0x204,
		WindowMoved = 0x205,
		WindowResized = 0x206,
		WindowPixelSizeChanged = 0x207,
		WindowMinimized = 0x209,
		WindowMaximized = 0x20a,
		WindowRestored = 0x20b,
		WindowMouseLeave = 0x20d,
		WindowFocusGained = 0x20e,
		WindowFocusLost = 0x20f,
		WindowCloseRequested = 0x210,
		WindowDisplayChanged = 0x213,
		WindowDisplayScaleChanged = 0x214,
		KeyDown = 0x300,
		KeyUp = 0x301,
		TextEditing = 0x302,
		TextInput = 0x303,
		MouseMotion = 0x400,
		MouseButtonDown = 0x401,
		MouseButtonUp = 0x402,
		MouseWheel = 0x403,
		GamepadAxisMotion = 0x650,
		GamepadButtonDown = 0x651,
		GamepadButtonUp = 0x652,
		GamepadAdded = 0x653,
		GamepadRemoved = 0x654,
		DropFile = 0x1000,
		DropText = 0x1001,
		DropComplete = 0x1003,
	}

	[Flags]
	internal enum WindowFlags : ulong
	{
		Fullscreen = 0x1,
		Hidden = 0x8,
		Borderless = 0x10,
		Resizable = 0x20,
		Minimized = 0x40,
		Maximized = 0x80,
		InputFocus = 0x200,
		HighPixelDensity = 0x2000,
		MouseCapture = 0x4000,
		PopupMenu = 0x80000,
		Vulkan = 0x10000000,
		NotFocusable = 0x80000000,
	}

	internal enum GamepadType
	{
		Unknown,
		Standard,
		Xbox360,
		XboxOne,
		PS3,
		PS4,
		PS5,
		SwitchPro,
		JoyConLeft,
		JoyConRight,
		JoyConPair,
		GameCube
	}

	internal enum SensorType
	{
		Accelerometer = 1,
		Gyroscope = 2
	}

	internal enum HitTestResult
	{
		Normal = 0,
		Draggable = 1,
		ResizeTopLeft = 2,
		ResizeTop = 3,
		ResizeTopRight = 4,
		ResizeRight = 5,
		ResizeBottomRight = 6,
		ResizeBottom = 7,
		ResizeBottomLeft = 8,
		ResizeLeft = 9,
	}

	internal struct DisplayMode
	{
		public uint Display;
		public uint Format;
		public int Width;
		public int Height;
		public float PixelDensity;
		public float RefreshRate;
		public int RefreshRateNumerator;
		public int RefreshRateDenominator;
		public IntPtr Internal;
	}

	internal struct Rect
	{
		public int X;
		public int Y;
		public int Width;
		public int Height;
	}

	internal struct Point
	{
		public int X;
		public int Y;
	}

	/// <summary>
	/// SDL3 event union; Type selects the active member. Pointer data is borrowed from SDL until the next poll.
	/// </summary>
	[StructLayout( LayoutKind.Explicit, Size = 128 )]
	internal struct Event
	{
		[FieldOffset( 0 )] public EventType Type;
		[FieldOffset( 0 )] public KeyboardEvent Key;
		[FieldOffset( 0 )] public TextInputEvent Text;
		[FieldOffset( 0 )] public TextEditingEvent Edit;
		[FieldOffset( 0 )] public MouseMotionEvent Motion;
		[FieldOffset( 0 )] public MouseButtonEvent Button;
		[FieldOffset( 0 )] public MouseWheelEvent Wheel;
		[FieldOffset( 0 )] public DropEvent Drop;
		[FieldOffset( 0 )] public GamepadDeviceEvent GamepadDevice;
		[FieldOffset( 0 )] public GamepadButtonEvent GamepadButton;
		[FieldOffset( 0 )] public GamepadAxisEvent GamepadAxis;
	}

	internal struct CommonEvent
	{
		public EventType Type;
		public uint Reserved;
		public ulong Timestamp;
	}

	internal struct KeyboardEvent
	{
		public CommonEvent Common;
		public uint WindowID, Which;
		public int Scancode;
		public uint Key;
		public ushort Mod, Raw;
		public byte Down, Repeat;
	}

	internal struct TextInputEvent
	{
		public CommonEvent Common;
		public uint WindowID;
		public IntPtr Text;
	}

	internal struct TextEditingEvent
	{
		public CommonEvent Common;
		public uint WindowID;
		public IntPtr Text;
		public int Start, Length;
	}

	internal struct MouseMotionEvent
	{
		public CommonEvent Common;
		public uint WindowID, Which, State;
		public float X, Y, XRel, YRel;
	}

	internal struct MouseButtonEvent
	{
		public CommonEvent Common;
		public uint WindowID, Which;
		public byte Button, Down, Clicks, Padding;
		public float X, Y;
	}

	internal struct MouseWheelEvent
	{
		public CommonEvent Common;
		public uint WindowID, Which;
		public float X, Y;
		public uint Direction;
		public float MouseX, MouseY;
		public int IntegerX, IntegerY;
	}

	internal struct DropEvent
	{
		public CommonEvent Common;
		public uint WindowID;
		public float X, Y;
		public IntPtr Source, Data;
	}

	internal struct ExternalDropCallbacks
	{
		public IntPtr OnEnter, OnOver, OnLeave, OnDrop, OnDragFrame;
	}

	internal struct DialogFileFilter
	{
		public IntPtr Name, Pattern;
	}

	internal struct GamepadDeviceEvent
	{
		public CommonEvent Common;
		public uint Which;
	}

	internal struct GamepadButtonEvent
	{
		public CommonEvent Common;
		public uint Which;
		public byte Button, Down, Padding1, Padding2;
	}

	internal struct GamepadAxisEvent
	{
		public CommonEvent Common;
		public uint Which;
		public byte Axis, Padding1, Padding2, Padding3;
		public short Value;
		public ushort Padding4;
	}
}
