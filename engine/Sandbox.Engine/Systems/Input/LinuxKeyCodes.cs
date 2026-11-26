namespace Sandbox.Engine;

/// <summary>
/// Linux key codes (evdev's, which are X11's less 8) as SDL scancodes: SDL's own table, for the keys
/// <see cref="KeyTranslation.ScanCodeToButtonCode"/> knows.
/// </summary>
internal static class LinuxKeyCodes
{
	/// <summary>
	/// The SDL scancode SDL reports for a Linux key code, or zero.
	/// </summary>
	internal static int ToScanCode( int key ) => key switch
	{
		1 => 41, // KEY_ESC
		>= 2 and <= 11 => key + 28, // KEY_1 .. KEY_0
		12 => 45, // KEY_MINUS
		13 => 46, // KEY_EQUAL
		14 => 42, // KEY_BACKSPACE
		15 => 43, // KEY_TAB
		16 => 20, // KEY_Q
		17 => 26, // KEY_W
		18 => 8, // KEY_E
		19 => 21, // KEY_R
		20 => 23, // KEY_T
		21 => 28, // KEY_Y
		22 => 24, // KEY_U
		23 => 12, // KEY_I
		24 => 18, // KEY_O
		25 => 19, // KEY_P
		26 => 47, // KEY_LEFTBRACE
		27 => 48, // KEY_RIGHTBRACE
		28 => 40, // KEY_ENTER
		29 => 224, // KEY_LEFTCTRL
		30 => 4, // KEY_A
		31 => 22, // KEY_S
		32 => 7, // KEY_D
		33 => 9, // KEY_F
		34 => 10, // KEY_G
		35 => 11, // KEY_H
		36 => 13, // KEY_J
		37 => 14, // KEY_K
		38 => 15, // KEY_L
		39 => 51, // KEY_SEMICOLON
		40 => 52, // KEY_APOSTROPHE
		41 => 53, // KEY_GRAVE
		42 => 225, // KEY_LEFTSHIFT
		43 => 49, // KEY_BACKSLASH
		44 => 29, // KEY_Z
		45 => 27, // KEY_X
		46 => 6, // KEY_C
		47 => 25, // KEY_V
		48 => 5, // KEY_B
		49 => 17, // KEY_N
		50 => 16, // KEY_M
		51 => 54, // KEY_COMMA
		52 => 55, // KEY_DOT
		53 => 56, // KEY_SLASH
		54 => 229, // KEY_RIGHTSHIFT
		55 => 85, // KEY_KPASTERISK
		56 => 226, // KEY_LEFTALT
		57 => 44, // KEY_SPACE
		58 => 57, // KEY_CAPSLOCK
		>= 59 and <= 68 => key - 1, // KEY_F1 .. KEY_F10
		69 => 83, // KEY_NUMLOCK
		70 => 71, // KEY_SCROLLLOCK
		71 => 95, // KEY_KP7
		72 => 96, // KEY_KP8
		73 => 97, // KEY_KP9
		74 => 86, // KEY_KPMINUS
		75 => 92, // KEY_KP4
		76 => 93, // KEY_KP5
		77 => 94, // KEY_KP6
		78 => 87, // KEY_KPPLUS
		79 => 89, // KEY_KP1
		80 => 90, // KEY_KP2
		81 => 91, // KEY_KP3
		82 => 98, // KEY_KP0
		83 => 99, // KEY_KPDOT
		86 => 100, // KEY_102ND
		87 => 68, // KEY_F11
		88 => 69, // KEY_F12
		96 => 88, // KEY_KPENTER
		97 => 228, // KEY_RIGHTCTRL
		98 => 84, // KEY_KPSLASH
		99 => 70, // KEY_SYSRQ
		100 => 230, // KEY_RIGHTALT
		102 => 74, // KEY_HOME
		103 => 82, // KEY_UP
		104 => 75, // KEY_PAGEUP
		105 => 80, // KEY_LEFT
		106 => 79, // KEY_RIGHT
		107 => 77, // KEY_END
		108 => 81, // KEY_DOWN
		109 => 78, // KEY_PAGEDOWN
		110 => 73, // KEY_INSERT
		111 => 76, // KEY_DELETE
		117 => 103, // KEY_KPEQUAL
		119 => 72, // KEY_PAUSE
		121 => 133, // KEY_KPCOMMA
		125 => 227, // KEY_LEFTMETA
		126 => 231, // KEY_RIGHTMETA
		127 => 101, // KEY_COMPOSE
		139 => 118, // KEY_MENU
		_ => 0
	};
}
