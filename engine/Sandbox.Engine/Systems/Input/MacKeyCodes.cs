namespace Sandbox.Engine;

/// <summary>
/// macOS virtual key codes (<c>NSEvent.keyCode</c>, which Qt reports as a key event's native virtual key) as SDL
/// scancodes: SDL's own table, for the keys <see cref="KeyTranslation.ScanCodeToButtonCode"/> knows. They name the key's
/// place on the keyboard, not what it types.
/// </summary>
internal static class MacKeyCodes
{
	// Indexed by virtual key code. 0 is no key
	static readonly ushort[] scanCodes =
	[
		4, 22, 7, 9, 11, 10, 29, 27, 6, 25, // 0-9: A S D F H G Z X C V
		100, 5, 20, 26, 8, 21, 28, 23, // 10-17: ISO section sign, B Q W E R Y T
		30, 31, 32, 33, 35, 34, 46, 38, 36, 45, 37, 39, // 18-29: 1 2 3 4 6 5 = 9 7 - 8 0
		48, 18, 24, 47, 12, 19, 40, // 30-36: ] O U [ I P Return
		15, 13, 52, 14, 51, 49, 54, 56, 17, 16, 55, // 37-47: L J ' K ; \ , / N M .
		43, 44, 53, 42, 88, 41, // 48-53: Tab Space ` Backspace, Enter on portables, Escape
		231, 227, 225, 57, 226, 224, 229, 230, 228, // 54-62: RGUI LGUI LShift Caps LAlt LCtrl RShift RAlt RCtrl
		0, // 63: fn, which SDL sends no events for
		108, 99, 0, 85, 0, 87, 0, 83, // 64-71: F17 KP. - KP* - KP+ - NumLock
		128, 129, 127, 84, 88, 0, 86, // 72-78: Volume up, Volume down, Mute, KP/ KPEnter - KP-
		109, 110, 103, // 79-81: F18 F19 KP=
		98, 89, 90, 91, 92, 93, 94, 95, 0, 96, 97, // 82-92: KP0 KP1 KP2 KP3 KP4 KP5 KP6 KP7 - KP8 KP9
		137, 135, 140, // 93-95: International 3 (Yen), 1 (Ro), 6 (JIS comma)
		62, 63, 64, 60, 65, 66, // 96-101: F5 F6 F7 F3 F8 F9
		145, 68, 144, // 102-104: Eisu, F11, Kana
		70, 107, 71, 0, 67, 101, 69, 0, 72, // 105-113: PrintScreen (F13), F16, ScrollLock (F14), -, F10, Application, F12, -, Pause (F15)
		73, 74, 75, 76, 61, 77, 59, 78, 58, // 114-122: Insert Home PageUp Delete F4 End F2 PageDown F1
		80, 79, 81, 82, // 123-126: Left Right Down Up
		102 // 127: Power
	];

	/// <summary>
	/// The SDL scancode SDL reports for a macOS virtual key code, or zero.
	/// </summary>
	internal static int ToScanCode( int key ) => (uint)key < scanCodes.Length ? scanCodes[key] : 0;
}
