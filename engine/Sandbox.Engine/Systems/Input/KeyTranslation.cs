using NativeEngine;

namespace Sandbox.Engine;

/// <summary>Engine key names and platform key-code translations.</summary>
[SkipHotload]
internal static class KeyTranslation
{
	static readonly string[] names =
	[
		"",
		"0",
		"1",
		"2",
		"3",
		"4",
		"5",
		"6",
		"7",
		"8",
		"9",
		"a",
		"b",
		"c",
		"d",
		"e",
		"f",
		"g",
		"h",
		"i",
		"j",
		"k",
		"l",
		"m",
		"n",
		"o",
		"p",
		"q",
		"r",
		"s",
		"t",
		"u",
		"v",
		"w",
		"x",
		"y",
		"z",
		"KP_0",
		"KP_1",
		"KP_2",
		"KP_3",
		"KP_4",
		"KP_5",
		"KP_6",
		"KP_7",
		"KP_8",
		"KP_9",
		"KP_DIVIDE",
		"KP_MULTIPLY",
		"KP_MINUS",
		"KP_PLUS",
		"KP_ENTER",
		"KP_DEL",
		"<",
		"[",
		"]",
		"SEMICOLON",
		"'",
		"`",
		",",
		".",
		"/",
		"\\",
		"-",
		"=",
		"ENTER",
		"SPACE",
		"BACKSPACE",
		"TAB",
		"CAPSLOCK",
		"NUMLOCK",
		"ESCAPE",
		"SCROLLLOCK",
		"INS",
		"DEL",
		"HOME",
		"END",
		"PGUP",
		"PGDN",
		"PAUSE",
		"SHIFT",
		"RSHIFT",
		"ALT",
		"RALT",
		"CTRL",
		"RCTRL",
		"LWIN",
		"RWIN",
		"APP",
		"UPARROW",
		"LEFTARROW",
		"DOWNARROW",
		"RIGHTARROW",
		"F1",
		"F2",
		"F3",
		"F4",
		"F5",
		"F6",
		"F7",
		"F8",
		"F9",
		"F10",
		"F11",
		"F12",
		"CAPSLOCKTOGGLE",
		"NUMLOCKTOGGLE",
		"SCROLLLOCKTOGGLE",
		"AC_BACK",
		"AC_BOOKMARKS",
		"AC_FORWARD",
		"AC_HOME",
		"AC_REFRESH",
		"AC_SEARCH",
		"AC_STOP",
		"AGAIN",
		"ALTERASE",
		"AMPERSAND",
		"ASTERISK",
		"AT",
		"AUDIOMUTE",
		"AUDIONEXT",
		"AUDIOPLAY",
		"AUDIOPREV",
		"AUDIOSTOP",
		"BRIGHTNESSDOWN",
		"BRIGHTNESSUP",
		"CALCULATOR",
		"CANCEL",
		"CARET",
		"CLEAR",
		"CLEARAGAIN",
		"COLON",
		"COMPUTER",
		"COPY",
		"CRSEL",
		"CURRENCYSUBUNIT",
		"CURRENCYUNIT",
		"CUT",
		"DECIMALSEPARATOR",
		"DISPLAYSWITCH",
		"DOLLAR",
		"EJECT",
		"EXCLAIM",
		"EXECUTE",
		"EXSEL",
		"F13",
		"F14",
		"F15",
		"F16",
		"F17",
		"F18",
		"F19",
		"F20",
		"F21",
		"F22",
		"F23",
		"F24",
		"FIND",
		"GREATER",
		"HASH",
		"HELP",
		"KBDILLUMDOWN",
		"KBDILLUMTOGGLE",
		"KBDILLUMUP",
		"KP_00",
		"KP_000",
		"KP_A",
		"KP_AMPERSAND",
		"KP_AT",
		"KP_B",
		"KP_BACKSPACE",
		"KP_BINARY",
		"KP_C",
		"KP_CLEAR",
		"KP_CLEARENTRY",
		"KP_COLON",
		"KP_COMMA",
		"KP_D",
		"KP_DBLAMPERSAND",
		"KP_DBLVERTICALBAR",
		"KP_DECIMAL",
		"KP_E",
		"KP_EQUALS",
		"KP_EQUALSAS400",
		"KP_EXCLAM",
		"KP_F",
		"KP_GREATER",
		"KP_HASH",
		"KP_HEXADECIMAL",
		"KP_LEFTBRACE",
		"KP_LEFTPAREN",
		"KP_LESS",
		"KP_MEMADD",
		"KP_MEMCLEAR",
		"KP_MEMDIVIDE",
		"KP_MEMMULTIPLY",
		"KP_MEMRECALL",
		"KP_MEMSTORE",
		"KP_MEMSUBTRACT",
		"KP_OCTAL",
		"KP_PERCENT",
		"KP_PLUSMINUS",
		"KP_POWER",
		"KP_RIGHTBRACE",
		"KP_RIGHTPAREN",
		"KP_SPACE",
		"KP_TAB",
		"KP_VERTICALBAR",
		"KP_XOR",
		"LEFTPAREN",
		"MAIL",
		"MEDIASELECT",
		"MODE",
		"MUTE",
		"OPER",
		"OUT",
		"PASTE",
		"PERCENT",
		"PLUS",
		"POWER",
		"PRINTSCREEN",
		"PRIOR",
		"QUESTION",
		"QUOTEDBL",
		"RETURN2",
		"RIGHTPAREN",
		"SELECT",
		"SEPARATOR",
		"SLEEP",
		"STOP",
		"SYSREQ",
		"THOUSANDSSEPARATOR",
		"UNDERSCORE",
		"UNDO",
		"VOLUMEDOWN",
		"VOLUMEUP",
		"WWW",
		"INVERTED_EXCLAMATION_MARK",
		"CENT_SIGN",
		"POUND_SIGN",
		"CURRENCY_SIGN",
		"YEN_SIGN",
		"BROKEN_BAR",
		"SECTION_SIGN",
		"DIAERESIS",
		"COPYRIGHT_SIGN",
		"FEMININE_ORDINAL_INDICATOR",
		"LEFT_POINTING_DOUBLE_ANGLE_QUOTATION_MARK",
		"NOT_SIGN",
		"REGISTERED_SIGN",
		"MACRON",
		"DEGREE_SYMBOL",
		"PLUS_MINUS_SIGN",
		"SUPERSCRIPT_TWO",
		"SUPERSCRIPT_THREE",
		"ACUTE_ACCENT",
		"MICRO_SIGN",
		"PILCROW_SIGN",
		"MIDDLE_DOT",
		"CEDILLA",
		"SUPERSCRIPT_ONE",
		"MASCULINE_ORDINAL_INDICATOR",
		"RIGHT_POINTING_DOUBLE_ANGLE_QUOTATION_MARK",
		"VULGAR_FRACTION_ONE_QUARTER",
		"VULGAR_FRACTION_ONE_HALF",
		"VULGAR_FRACTION_THREE_QUARTERS",
		"INVERTED_QUESTION_MARK",
		"MULTIPLICATION_SIGN",
		"SHARP_S",
		"A_WITH_GRAVE",
		"A_WITH_ACUTE",
		"A_WITH_CIRCUMFLEX",
		"A_WITH_TILDE",
		"A_WITH_DIAERESIS",
		"A_WITH_RING_ABOVE",
		"AE",
		"C_WITH_CEDILLA",
		"E_WITH_GRAVE",
		"E_WITH_ACUTE",
		"E_WITH_CIRCUMFLEX",
		"E_WITH_DIAERESIS",
		"I_WITH_GRAVE",
		"I_WITH_ACUTE",
		"I_WITH_CIRCUMFLEX",
		"I_WITH_DIAERESIS",
		"ETH",
		"N_WITH_TILDE",
		"O_WITH_GRAVE",
		"O_WITH_ACUTE",
		"O_WITH_CIRCUMFLEX",
		"O_WITH_TILDE",
		"O_WITH_DIAERESIS",
		"DIVISION_SIGN",
		"O_WITH_STROKE",
		"U_WITH_GRAVE",
		"U_WITH_ACUTE",
		"U_WITH_CIRCUMFLEX",
		"U_WITH_DIAERESIS",
		"Y_WITH_ACUTE",
		"THORN",
		"Y_WITH_DIAERESIS",
		"EURO_SIGN",
		"TILDE",
		"LEFT_CURLY_BRACKET",
		"RIGHT_CURLY_BRACKET",
		"VERTICAL_BAR",
		"KEY_CYRILLIC_YU",
		"KEY_CYRILLIC_E",
		"KEY_CYRILLIC_HARD_SIGN",
		"KEY_CYRILLIC_HA",
		"KEY_CYRILLIC_IO",
		"KEY_CYRILLIC_ZHE",
		"KEY_CYRILLIC_BE",
		"MOUSE1",
		"MOUSE2",
		"MOUSE3",
		"MOUSE4",
		"MOUSE5",
		"MWHEELUP",
		"MWHEELDOWN",
		"JOY1",
		"JOY2",
		"JOY3",
		"JOY4",
		"JOY5",
		"JOY6",
		"JOY7",
		"JOY8",
		"JOY9",
		"JOY10",
		"JOY11",
		"JOY12",
		"JOY13",
		"JOY14",
		"JOY15",
		"JOY16",
		"JOY17",
		"JOY18",
		"JOY19",
		"JOY20",
		"JOY21",
		"JOY22",
		"JOY23",
		"JOY24",
		"JOY25",
		"JOY26",
		"JOY27",
		"JOY28",
		"JOY29",
		"JOY30",
		"JOY31",
		"JOY32",
		"POV_UP",
		"POV_RIGHT",
		"POV_DOWN",
		"POV_LEFT",
		"X_AXIS_POS",
		"X_AXIS_NEG",
		"Y_AXIS_POS",
		"Y_AXIS_NEG",
		"Z_AXIS_POS",
		"Z_AXIS_NEG",
		"R_AXIS_POS",
		"R_AXIS_NEG",
		"U_AXIS_POS",
		"U_AXIS_NEG",
		"V_AXIS_POS",
		"V_AXIS_NEG",
	];

	static readonly string[] controllerAliases =
	[
		"A_BUTTON",
		"B_BUTTON",
		"X_BUTTON",
		"Y_BUTTON",
		"L_SHOULDER",
		"R_SHOULDER",
		"BACK",
		"START",
		"STICK1",
		"STICK2",
		"JOY11",
		"JOY12",
		"JOY13",
		"JOY14",
		"JOY15",
		"JOY16",
		"JOY17",
		"JOY18",
		"JOY19",
		"JOY20",
		"JOY21",
		"JOY22",
		"JOY23",
		"JOY24",
		"JOY25",
		"JOY26",
		"JOY27",
		"JOY28",
		"JOY29",
		"JOY30",
		"JOY31",
		"JOY32",
		"UP",
		"RIGHT",
		"DOWN",
		"LEFT",
		"S1_RIGHT",
		"S1_LEFT",
		"S1_DOWN",
		"S1_UP",
		"L_TRIGGER",
		"R_TRIGGER",
		"S2_RIGHT",
		"S2_LEFT",
		"S2_DOWN",
		"S2_UP",
		"V_AXIS_POS",
		"V_AXIS_NEG",
	];

	static readonly Dictionary<string, ButtonCode> codes = CreateNames();

	static Dictionary<string, ButtonCode> CreateNames()
	{
		var result = new Dictionary<string, ButtonCode>( StringComparer.OrdinalIgnoreCase );
		for ( var i = 1; i < names.Length; i++ ) result.TryAdd( names[i], (ButtonCode)i );
		for ( var i = 0; i < controllerAliases.Length; i++ ) result.TryAdd( controllerAliases[i], ButtonCode.JOYSTICK_FIRST_BUTTON + i );
		return result;
	}

	internal static string CodeToString( ButtonCode code ) => (uint)code < names.Length ? names[(int)code] : "";

	internal static ButtonCode StringToButtonCode( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return ButtonCode.BUTTON_CODE_INVALID;
		if ( name.StartsWith( "aux", StringComparison.OrdinalIgnoreCase ) )
		{
			// Legacy names use atoi-style parsing, including an optional sign and trailing text.
			var suffix = name.AsSpan( 3 ).TrimStart();
			var length = suffix.Length > 0 && suffix[0] is '+' or '-' ? 1 : 0;
			while ( length < suffix.Length && char.IsAsciiDigit( suffix[length] ) ) length++;
			int.TryParse( suffix[..length], out var index );
			if ( index >= 0 && index < 29 ) return ButtonCode.JOYSTICK_FIRST_BUTTON + index;
			if ( index is >= 29 and <= 32 ) return ButtonCode.JOYSTICK_FIRST_POV_BUTTON + index - 29;
			return ButtonCode.BUTTON_CODE_INVALID;
		}
		return codes.GetValueOrDefault( name, ButtonCode.BUTTON_CODE_INVALID );
	}

	internal static string GetKeyDisplayName( ButtonCode code )
	{
		if ( code < ButtonCode.KEY_FIRST || code > ButtonCode.KEY_LAST ) return CodeToString( code );
		return Sdl.GetKeyName( Sdl.GetKeyFromScancode( ButtonCodeToScanCode( code ), 0, true ) );
	}

	internal static ButtonCode KeyCodeToButtonCode( uint key ) => key switch
	{
		0x00000030 => ButtonCode.KEY_0, // SDLK_0
		0x00000031 => ButtonCode.KEY_1, // SDLK_1
		0x00000032 => ButtonCode.KEY_2, // SDLK_2
		0x00000033 => ButtonCode.KEY_3, // SDLK_3
		0x00000034 => ButtonCode.KEY_4, // SDLK_4
		0x00000035 => ButtonCode.KEY_5, // SDLK_5
		0x00000036 => ButtonCode.KEY_6, // SDLK_6
		0x00000037 => ButtonCode.KEY_7, // SDLK_7
		0x00000038 => ButtonCode.KEY_8, // SDLK_8
		0x00000039 => ButtonCode.KEY_9, // SDLK_9
		0x00000061 => ButtonCode.KEY_A, // SDLK_A
		0x00000062 => ButtonCode.KEY_B, // SDLK_B
		0x00000063 => ButtonCode.KEY_C, // SDLK_C
		0x00000064 => ButtonCode.KEY_D, // SDLK_D
		0x00000065 => ButtonCode.KEY_E, // SDLK_E
		0x00000066 => ButtonCode.KEY_F, // SDLK_F
		0x00000067 => ButtonCode.KEY_G, // SDLK_G
		0x00000068 => ButtonCode.KEY_H, // SDLK_H
		0x00000069 => ButtonCode.KEY_I, // SDLK_I
		0x0000006A => ButtonCode.KEY_J, // SDLK_J
		0x0000006B => ButtonCode.KEY_K, // SDLK_K
		0x0000006C => ButtonCode.KEY_L, // SDLK_L
		0x0000006D => ButtonCode.KEY_M, // SDLK_M
		0x0000006E => ButtonCode.KEY_N, // SDLK_N
		0x0000006F => ButtonCode.KEY_O, // SDLK_O
		0x00000070 => ButtonCode.KEY_P, // SDLK_P
		0x00000071 => ButtonCode.KEY_Q, // SDLK_Q
		0x00000072 => ButtonCode.KEY_R, // SDLK_R
		0x00000073 => ButtonCode.KEY_S, // SDLK_S
		0x00000074 => ButtonCode.KEY_T, // SDLK_T
		0x00000075 => ButtonCode.KEY_U, // SDLK_U
		0x00000076 => ButtonCode.KEY_V, // SDLK_V
		0x00000077 => ButtonCode.KEY_W, // SDLK_W
		0x00000078 => ButtonCode.KEY_X, // SDLK_X
		0x00000079 => ButtonCode.KEY_Y, // SDLK_Y
		0x0000007A => ButtonCode.KEY_Z, // SDLK_Z
		0x40000062 => ButtonCode.KEY_PAD_0, // SDLK_KP_0
		0x40000059 => ButtonCode.KEY_PAD_1, // SDLK_KP_1
		0x4000005A => ButtonCode.KEY_PAD_2, // SDLK_KP_2
		0x4000005B => ButtonCode.KEY_PAD_3, // SDLK_KP_3
		0x4000005C => ButtonCode.KEY_PAD_4, // SDLK_KP_4
		0x4000005D => ButtonCode.KEY_PAD_5, // SDLK_KP_5
		0x4000005E => ButtonCode.KEY_PAD_6, // SDLK_KP_6
		0x4000005F => ButtonCode.KEY_PAD_7, // SDLK_KP_7
		0x40000060 => ButtonCode.KEY_PAD_8, // SDLK_KP_8
		0x40000061 => ButtonCode.KEY_PAD_9, // SDLK_KP_9
		0x40000054 => ButtonCode.KEY_PAD_DIVIDE, // SDLK_KP_DIVIDE
		0x40000055 => ButtonCode.KEY_PAD_MULTIPLY, // SDLK_KP_MULTIPLY
		0x40000056 => ButtonCode.KEY_PAD_MINUS, // SDLK_KP_MINUS
		0x40000057 => ButtonCode.KEY_PAD_PLUS, // SDLK_KP_PLUS
		0x40000058 => ButtonCode.KEY_PAD_ENTER, // SDLK_KP_ENTER
		0x40000063 => ButtonCode.KEY_PAD_DECIMAL, // SDLK_KP_PERIOD
		0x0000005B => ButtonCode.KEY_LBRACKET, // SDLK_LEFTBRACKET
		0x0000005D => ButtonCode.KEY_RBRACKET, // SDLK_RIGHTBRACKET
		0x0000003B => ButtonCode.KEY_SEMICOLON, // SDLK_SEMICOLON
		0x00000027 => ButtonCode.KEY_APOSTROPHE, // SDLK_APOSTROPHE
		0x00000060 => ButtonCode.KEY_BACKQUOTE, // SDLK_GRAVE
		0x0000002C => ButtonCode.KEY_COMMA, // SDLK_COMMA
		0x0000002E => ButtonCode.KEY_PERIOD, // SDLK_PERIOD
		0x0000002F => ButtonCode.KEY_SLASH, // SDLK_SLASH
		0x0000005C => ButtonCode.KEY_BACKSLASH, // SDLK_BACKSLASH
		0x0000002D => ButtonCode.KEY_MINUS, // SDLK_MINUS
		0x0000003D => ButtonCode.KEY_EQUAL, // SDLK_EQUALS
		0x0000003C => ButtonCode.KEY_LESS, // SDLK_LESS
		0x0000000D => ButtonCode.KEY_ENTER, // SDLK_RETURN
		0x00000020 => ButtonCode.KEY_SPACE, // SDLK_SPACE
		0x00000008 => ButtonCode.KEY_BACKSPACE, // SDLK_BACKSPACE
		0x00000009 => ButtonCode.KEY_TAB, // SDLK_TAB
		0x40000039 => ButtonCode.KEY_CAPSLOCK, // SDLK_CAPSLOCK
		0x40000053 => ButtonCode.KEY_NUMLOCK, // SDLK_NUMLOCKCLEAR
		0x0000001B => ButtonCode.KEY_ESCAPE, // SDLK_ESCAPE
		0x40000047 => ButtonCode.KEY_SCROLLLOCK, // SDLK_SCROLLLOCK
		0x40000049 => ButtonCode.KEY_INSERT, // SDLK_INSERT
		0x0000007F => ButtonCode.KEY_DELETE, // SDLK_DELETE
		0x4000004A => ButtonCode.KEY_HOME, // SDLK_HOME
		0x4000004D => ButtonCode.KEY_END, // SDLK_END
		0x4000004B => ButtonCode.KEY_PAGEUP, // SDLK_PAGEUP
		0x4000004E => ButtonCode.KEY_PAGEDOWN, // SDLK_PAGEDOWN
		0x40000048 => ButtonCode.KEY_BREAK, // SDLK_PAUSE
		0x400000E1 => ButtonCode.KEY_LSHIFT, // SDLK_LSHIFT
		0x400000E2 => ButtonCode.KEY_LALT, // SDLK_LALT
		0x400000E0 => ButtonCode.KEY_LCONTROL, // SDLK_LCTRL
		0x400000E3 => ButtonCode.KEY_LWIN, // SDLK_LGUI
		0x400000E5 => ButtonCode.KEY_RSHIFT, // SDLK_RSHIFT
		0x400000E6 => ButtonCode.KEY_RALT, // SDLK_RALT
		0x400000E4 => ButtonCode.KEY_RCONTROL, // SDLK_RCTRL
		0x400000E7 => ButtonCode.KEY_RWIN, // SDLK_RGUI
		0x40000065 => ButtonCode.KEY_APP, // SDLK_APPLICATION
		0x40000076 => ButtonCode.KEY_APP, // SDLK_MENU
		0x40000052 => ButtonCode.KEY_UP, // SDLK_UP
		0x40000050 => ButtonCode.KEY_LEFT, // SDLK_LEFT
		0x40000051 => ButtonCode.KEY_DOWN, // SDLK_DOWN
		0x4000004F => ButtonCode.KEY_RIGHT, // SDLK_RIGHT
		0x4000003A => ButtonCode.KEY_F1, // SDLK_F1
		0x4000003B => ButtonCode.KEY_F2, // SDLK_F2
		0x4000003C => ButtonCode.KEY_F3, // SDLK_F3
		0x4000003D => ButtonCode.KEY_F4, // SDLK_F4
		0x4000003E => ButtonCode.KEY_F5, // SDLK_F5
		0x4000003F => ButtonCode.KEY_F6, // SDLK_F6
		0x40000040 => ButtonCode.KEY_F7, // SDLK_F7
		0x40000041 => ButtonCode.KEY_F8, // SDLK_F8
		0x40000042 => ButtonCode.KEY_F9, // SDLK_F9
		0x40000043 => ButtonCode.KEY_F10, // SDLK_F10
		0x40000044 => ButtonCode.KEY_F11, // SDLK_F11
		0x40000045 => ButtonCode.KEY_F12, // SDLK_F12
		0x4000011A => ButtonCode.KEY_AC_BACK, // SDLK_AC_BACK
		0x4000011E => ButtonCode.KEY_AC_BOOKMARKS, // SDLK_AC_BOOKMARKS
		0x4000011B => ButtonCode.KEY_AC_FORWARD, // SDLK_AC_FORWARD
		0x40000119 => ButtonCode.KEY_AC_HOME, // SDLK_AC_HOME
		0x4000011D => ButtonCode.KEY_AC_REFRESH, // SDLK_AC_REFRESH
		0x40000118 => ButtonCode.KEY_AC_SEARCH, // SDLK_AC_SEARCH
		0x4000011C => ButtonCode.KEY_AC_STOP, // SDLK_AC_STOP
		0x40000079 => ButtonCode.KEY_AGAIN, // SDLK_AGAIN
		0x40000099 => ButtonCode.KEY_ALTERASE, // SDLK_ALTERASE
		0x00000026 => ButtonCode.KEY_AMPERSAND, // SDLK_AMPERSAND
		0x0000002A => ButtonCode.KEY_ASTERISK, // SDLK_ASTERISK
		0x00000040 => ButtonCode.KEY_AT, // SDLK_AT
		0x4000010B => ButtonCode.KEY_AUDIONEXT, // SDLK_MEDIA_NEXT_TRACK
		0x40000106 => ButtonCode.KEY_AUDIOPLAY, // SDLK_MEDIA_PLAY
		0x4000010C => ButtonCode.KEY_AUDIOPREV, // SDLK_MEDIA_PREVIOUS_TRACK
		0x4000010D => ButtonCode.KEY_AUDIOSTOP, // SDLK_MEDIA_STOP
		0x4000009B => ButtonCode.KEY_CANCEL, // SDLK_CANCEL
		0x0000005E => ButtonCode.KEY_CARET, // SDLK_CARET
		0x4000009C => ButtonCode.KEY_CLEAR, // SDLK_CLEAR
		0x400000A2 => ButtonCode.KEY_CLEARAGAIN, // SDLK_CLEARAGAIN
		0x0000003A => ButtonCode.KEY_COLON, // SDLK_COLON
		0x4000007C => ButtonCode.KEY_COPY, // SDLK_COPY
		0x400000A3 => ButtonCode.KEY_CRSEL, // SDLK_CRSEL
		0x400000B5 => ButtonCode.KEY_CURRENCYSUBUNIT, // SDLK_CURRENCYSUBUNIT
		0x400000B4 => ButtonCode.KEY_CURRENCYUNIT, // SDLK_CURRENCYUNIT
		0x4000007B => ButtonCode.KEY_CUT, // SDLK_CUT
		0x400000B3 => ButtonCode.KEY_DECIMALSEPARATOR, // SDLK_DECIMALSEPARATOR
		0x00000024 => ButtonCode.KEY_DOLLAR, // SDLK_DOLLAR
		0x4000010E => ButtonCode.KEY_EJECT, // SDLK_MEDIA_EJECT
		0x00000021 => ButtonCode.KEY_EXCLAIM, // SDLK_EXCLAIM
		0x40000074 => ButtonCode.KEY_BTN_EXECUTE, // SDLK_EXECUTE
		0x400000A4 => ButtonCode.KEY_EXSEL, // SDLK_EXSEL
		0x40000068 => ButtonCode.KEY_F13, // SDLK_F13
		0x40000069 => ButtonCode.KEY_F14, // SDLK_F14
		0x4000006A => ButtonCode.KEY_F15, // SDLK_F15
		0x4000006B => ButtonCode.KEY_F16, // SDLK_F16
		0x4000006C => ButtonCode.KEY_F17, // SDLK_F17
		0x4000006D => ButtonCode.KEY_F18, // SDLK_F18
		0x4000006E => ButtonCode.KEY_F19, // SDLK_F19
		0x4000006F => ButtonCode.KEY_F20, // SDLK_F20
		0x40000070 => ButtonCode.KEY_F21, // SDLK_F21
		0x40000071 => ButtonCode.KEY_F22, // SDLK_F22
		0x40000072 => ButtonCode.KEY_F23, // SDLK_F23
		0x40000073 => ButtonCode.KEY_F24, // SDLK_F24
		0x4000007E => ButtonCode.KEY_FIND, // SDLK_FIND
		0x0000003E => ButtonCode.KEY_GREATER, // SDLK_GREATER
		0x00000023 => ButtonCode.KEY_HASH, // SDLK_HASH
		0x40000075 => ButtonCode.KEY_HELP, // SDLK_HELP
		0x400000B0 => ButtonCode.KEY_KP_00, // SDLK_KP_00
		0x400000B1 => ButtonCode.KEY_KP_000, // SDLK_KP_000
		0x400000BC => ButtonCode.KEY_KP_A, // SDLK_KP_A
		0x400000C7 => ButtonCode.KEY_KP_AMPERSAND, // SDLK_KP_AMPERSAND
		0x400000CE => ButtonCode.KEY_KP_AT, // SDLK_KP_AT
		0x400000BD => ButtonCode.KEY_KP_B, // SDLK_KP_B
		0x400000BB => ButtonCode.KEY_KP_BACKSPACE, // SDLK_KP_BACKSPACE
		0x400000DA => ButtonCode.KEY_KP_BINARY, // SDLK_KP_BINARY
		0x400000BE => ButtonCode.KEY_KP_C, // SDLK_KP_C
		0x400000D8 => ButtonCode.KEY_KP_CLEAR, // SDLK_KP_CLEAR
		0x400000D9 => ButtonCode.KEY_KP_CLEARENTRY, // SDLK_KP_CLEARENTRY
		0x400000CB => ButtonCode.KEY_KP_COLON, // SDLK_KP_COLON
		0x40000085 => ButtonCode.KEY_KP_COMMA, // SDLK_KP_COMMA
		0x400000BF => ButtonCode.KEY_KP_D, // SDLK_KP_D
		0x400000C8 => ButtonCode.KEY_KP_DBLAMPERSAND, // SDLK_KP_DBLAMPERSAND
		0x400000CA => ButtonCode.KEY_KP_DBLVERTICALBAR, // SDLK_KP_DBLVERTICALBAR
		0x400000DC => ButtonCode.KEY_KP_DECIMAL, // SDLK_KP_DECIMAL
		0x400000C0 => ButtonCode.KEY_KP_E, // SDLK_KP_E
		0x40000067 => ButtonCode.KEY_KP_EQUALS, // SDLK_KP_EQUALS
		0x40000086 => ButtonCode.KEY_KP_EQUALSAS400, // SDLK_KP_EQUALSAS400
		0x400000CF => ButtonCode.KEY_KP_EXCLAM, // SDLK_KP_EXCLAM
		0x400000C1 => ButtonCode.KEY_KP_F, // SDLK_KP_F
		0x400000C6 => ButtonCode.KEY_KP_GREATER, // SDLK_KP_GREATER
		0x400000CC => ButtonCode.KEY_KP_HASH, // SDLK_KP_HASH
		0x400000DD => ButtonCode.KEY_KP_HEXADECIMAL, // SDLK_KP_HEXADECIMAL
		0x400000B8 => ButtonCode.KEY_KP_LEFTBRACE, // SDLK_KP_LEFTBRACE
		0x400000B6 => ButtonCode.KEY_KP_LEFTPAREN, // SDLK_KP_LEFTPAREN
		0x400000C5 => ButtonCode.KEY_KP_LESS, // SDLK_KP_LESS
		0x400000D3 => ButtonCode.KEY_KP_MEMADD, // SDLK_KP_MEMADD
		0x400000D2 => ButtonCode.KEY_KP_MEMCLEAR, // SDLK_KP_MEMCLEAR
		0x400000D6 => ButtonCode.KEY_KP_MEMDIVIDE, // SDLK_KP_MEMDIVIDE
		0x400000D5 => ButtonCode.KEY_KP_MEMMULTIPLY, // SDLK_KP_MEMMULTIPLY
		0x400000D1 => ButtonCode.KEY_KP_MEMRECALL, // SDLK_KP_MEMRECALL
		0x400000D0 => ButtonCode.KEY_KP_MEMSTORE, // SDLK_KP_MEMSTORE
		0x400000D4 => ButtonCode.KEY_KP_MEMSUBTRACT, // SDLK_KP_MEMSUBTRACT
		0x400000DB => ButtonCode.KEY_KP_OCTAL, // SDLK_KP_OCTAL
		0x400000C4 => ButtonCode.KEY_KP_PERCENT, // SDLK_KP_PERCENT
		0x400000D7 => ButtonCode.KEY_KP_PLUSMINUS, // SDLK_KP_PLUSMINUS
		0x400000C3 => ButtonCode.KEY_KP_POWER, // SDLK_KP_POWER
		0x400000B9 => ButtonCode.KEY_KP_RIGHTBRACE, // SDLK_KP_RIGHTBRACE
		0x400000B7 => ButtonCode.KEY_KP_RIGHTPAREN, // SDLK_KP_RIGHTPAREN
		0x400000CD => ButtonCode.KEY_KP_SPACE, // SDLK_KP_SPACE
		0x400000BA => ButtonCode.KEY_KP_TAB, // SDLK_KP_TAB
		0x400000C9 => ButtonCode.KEY_KP_VERTICALBAR, // SDLK_KP_VERTICALBAR
		0x400000C2 => ButtonCode.KEY_KP_XOR, // SDLK_KP_XOR
		0x00000028 => ButtonCode.KEY_LEFTPAREN, // SDLK_LEFTPAREN
		0x40000110 => ButtonCode.KEY_MEDIASELECT, // SDLK_MEDIA_SELECT
		0x40000101 => ButtonCode.KEY_MODE, // SDLK_MODE
		0x4000007F => ButtonCode.KEY_MUTE, // SDLK_MUTE
		0x400000A1 => ButtonCode.KEY_OPER, // SDLK_OPER
		0x400000A0 => ButtonCode.KEY_OUT, // SDLK_OUT
		0x4000007D => ButtonCode.KEY_PASTE, // SDLK_PASTE
		0x00000025 => ButtonCode.KEY_PERCENT, // SDLK_PERCENT
		0x0000002B => ButtonCode.KEY_PLUS, // SDLK_PLUS
		0x40000066 => ButtonCode.KEY_POWER, // SDLK_POWER
		0x40000046 => ButtonCode.KEY_PRINTSCREEN, // SDLK_PRINTSCREEN
		0x4000009D => ButtonCode.KEY_PRIOR, // SDLK_PRIOR
		0x0000003F => ButtonCode.KEY_QUESTION, // SDLK_QUESTION
		0x00000022 => ButtonCode.KEY_QUOTEDBL, // SDLK_DBLAPOSTROPHE
		0x4000009E => ButtonCode.KEY_RETURN2, // SDLK_RETURN2
		0x00000029 => ButtonCode.KEY_RIGHTPAREN, // SDLK_RIGHTPAREN
		0x40000077 => ButtonCode.KEY_SELECT, // SDLK_SELECT
		0x4000009F => ButtonCode.KEY_SEPARATOR, // SDLK_SEPARATOR
		0x40000102 => ButtonCode.KEY_SLEEP, // SDLK_SLEEP
		0x40000078 => ButtonCode.KEY_STOP, // SDLK_STOP
		0x4000009A => ButtonCode.KEY_SYSREQ, // SDLK_SYSREQ
		0x400000B2 => ButtonCode.KEY_THOUSANDSSEPARATOR, // SDLK_THOUSANDSSEPARATOR
		0x0000005F => ButtonCode.KEY_UNDERSCORE, // SDLK_UNDERSCORE
		0x4000007A => ButtonCode.KEY_UNDO, // SDLK_UNDO
		0x40000081 => ButtonCode.KEY_VOLUMEDOWN, // SDLK_VOLUMEDOWN
		0x40000080 => ButtonCode.KEY_VOLUMEUP, // SDLK_VOLUMEUP
		0x000000A1 => ButtonCode.KEY_INVERTED_EXCLAMATION_MARK, // SDL_Keycode(0x00A1)
		0x000000A2 => ButtonCode.KEY_CENT_SIGN, // SDL_Keycode(0x00A2)
		0x000000A3 => ButtonCode.KEY_POUND_SIGN, // SDL_Keycode(0x00A3)
		0x000000A4 => ButtonCode.KEY_CURRENCY_SIGN, // SDL_Keycode(0x00A4)
		0x000000A5 => ButtonCode.KEY_YEN_SIGN, // SDL_Keycode(0x00A5)
		0x000000A6 => ButtonCode.KEY_BROKEN_BAR, // SDL_Keycode(0x00A6)
		0x000000A7 => ButtonCode.KEY_SECTION_SIGN, // SDL_Keycode(0x00A7)
		0x000000A8 => ButtonCode.KEY_DIAERESIS, // SDL_Keycode(0x00A8)
		0x000000A9 => ButtonCode.KEY_COPYRIGHT_SIGN, // SDL_Keycode(0x00A9)
		0x000000AA => ButtonCode.KEY_FEMININE_ORDINAL_INDICATOR, // SDL_Keycode(0x00AA)
		0x000000AB => ButtonCode.KEY_LEFT_POINTING_DOUBLE_ANGLE_QUOTATION_MARK, // SDL_Keycode(0x00AB)
		0x000000AC => ButtonCode.KEY_NOT_SIGN, // SDL_Keycode(0x00AC)
		0x000000AE => ButtonCode.KEY_REGISTERED_SIGN, // SDL_Keycode(0x00AE)
		0x000000AF => ButtonCode.KEY_MACRON, // SDL_Keycode(0x00AF)
		0x000000B0 => ButtonCode.KEY_DEGREE_SYMBOL, // SDL_Keycode(0x00B0)
		0x000000B1 => ButtonCode.KEY_PLUS_MINUS_SIGN, // SDL_Keycode(0x00B1)
		0x000000B2 => ButtonCode.KEY_SUPERSCRIPT_TWO, // SDL_Keycode(0x00B2)
		0x000000B3 => ButtonCode.KEY_SUPERSCRIPT_THREE, // SDL_Keycode(0x00B3)
		0x000000B4 => ButtonCode.KEY_ACUTE_ACCENT, // SDL_Keycode(0x00B4)
		0x000000B5 => ButtonCode.KEY_MICRO_SIGN, // SDL_Keycode(0x00B5)
		0x000000B6 => ButtonCode.KEY_PILCROW_SIGN, // SDL_Keycode(0x00B6)
		0x000000B7 => ButtonCode.KEY_MIDDLE_DOT, // SDL_Keycode(0x00B7)
		0x000000B8 => ButtonCode.KEY_CEDILLA, // SDL_Keycode(0x00B8)
		0x000000B9 => ButtonCode.KEY_SUPERSCRIPT_ONE, // SDL_Keycode(0x00B9)
		0x000000BA => ButtonCode.KEY_MASCULINE_ORDINAL_INDICATOR, // SDL_Keycode(0x00BA)
		0x000000BB => ButtonCode.KEY_RIGHT_POINTING_DOUBLE_ANGLE_QUOTATION_MARK, // SDL_Keycode(0x00BB)
		0x000000BC => ButtonCode.KEY_VULGAR_FRACTION_ONE_QUARTER, // SDL_Keycode(0x00BC)
		0x000000BD => ButtonCode.KEY_VULGAR_FRACTION_ONE_HALF, // SDL_Keycode(0x00BD)
		0x000000BE => ButtonCode.KEY_VULGAR_FRACTION_THREE_QUARTERS, // SDL_Keycode(0x00BE)
		0x000000BF => ButtonCode.KEY_INVERTED_QUESTION_MARK, // SDL_Keycode(0x00BF)
		0x000000D7 => ButtonCode.KEY_MULTIPLICATION_SIGN, // SDL_Keycode(0x00D7)
		0x000000DF => ButtonCode.KEY_SHARP_S, // SDL_Keycode(0x00DF)
		0x000000E0 => ButtonCode.KEY_A_WITH_GRAVE, // SDL_Keycode(0x00E0)
		0x000000E1 => ButtonCode.KEY_A_WITH_ACUTE, // SDL_Keycode(0x00E1)
		0x000000E2 => ButtonCode.KEY_A_WITH_CIRCUMFLEX, // SDL_Keycode(0x00E2)
		0x000000E3 => ButtonCode.KEY_A_WITH_TILDE, // SDL_Keycode(0x00E3)
		0x000000E4 => ButtonCode.KEY_A_WITH_DIAERESIS, // SDL_Keycode(0x00E4)
		0x000000E5 => ButtonCode.KEY_A_WITH_RING_ABOVE, // SDL_Keycode(0x00E5)
		0x000000E6 => ButtonCode.KEY_AE, // SDL_Keycode(0x00E6)
		0x000000E7 => ButtonCode.KEY_C_WITH_CEDILLA, // SDL_Keycode(0x00E7)
		0x000000E8 => ButtonCode.KEY_E_WITH_GRAVE, // SDL_Keycode(0x00E8)
		0x000000E9 => ButtonCode.KEY_E_WITH_ACUTE, // SDL_Keycode(0x00E9)
		0x000000EA => ButtonCode.KEY_E_WITH_CIRCUMFLEX, // SDL_Keycode(0x00EA)
		0x000000EB => ButtonCode.KEY_E_WITH_DIAERESIS, // SDL_Keycode(0x00EB)
		0x000000EC => ButtonCode.KEY_I_WITH_GRAVE, // SDL_Keycode(0x00EC)
		0x000000ED => ButtonCode.KEY_I_WITH_ACUTE, // SDL_Keycode(0x00ED)
		0x000000EE => ButtonCode.KEY_I_WITH_CIRCUMFLEX, // SDL_Keycode(0x00EE)
		0x000000EF => ButtonCode.KEY_I_WITH_DIAERESIS, // SDL_Keycode(0x00EF)
		0x000000F0 => ButtonCode.KEY_ETH, // SDL_Keycode(0x00F0)
		0x000000F1 => ButtonCode.KEY_N_WITH_TILDE, // SDL_Keycode(0x00F1)
		0x000000F2 => ButtonCode.KEY_O_WITH_GRAVE, // SDL_Keycode(0x00F2)
		0x000000F3 => ButtonCode.KEY_O_WITH_ACUTE, // SDL_Keycode(0x00F3)
		0x000000F4 => ButtonCode.KEY_O_WITH_CIRCUMFLEX, // SDL_Keycode(0x00F4)
		0x000000F5 => ButtonCode.KEY_O_WITH_TILDE, // SDL_Keycode(0x00F5)
		0x000000F6 => ButtonCode.KEY_O_WITH_DIAERESIS, // SDL_Keycode(0x00F6)
		0x000000F7 => ButtonCode.KEY_DIVISION_SIGN, // SDL_Keycode(0x00F7)
		0x000000F8 => ButtonCode.KEY_O_WITH_STROKE, // SDL_Keycode(0x00F8)
		0x000000F9 => ButtonCode.KEY_U_WITH_GRAVE, // SDL_Keycode(0x00F9)
		0x000000FA => ButtonCode.KEY_U_WITH_ACUTE, // SDL_Keycode(0x00FA)
		0x000000FB => ButtonCode.KEY_U_WITH_CIRCUMFLEX, // SDL_Keycode(0x00FB)
		0x000000FC => ButtonCode.KEY_U_WITH_DIAERESIS, // SDL_Keycode(0x00FC)
		0x000000FD => ButtonCode.KEY_Y_WITH_ACUTE, // SDL_Keycode(0x00FD)
		0x000000FE => ButtonCode.KEY_THORN, // SDL_Keycode(0x00FE)
		0x000000FF => ButtonCode.KEY_Y_WITH_DIAERESIS, // SDL_Keycode(0x00FF)
		0x000020AC => ButtonCode.KEY_EURO_SIGN, // SDL_Keycode(0x20AC)
		0x0000007E => ButtonCode.KEY_TILDE, // SDL_Keycode(0x007E)
		0x0000007B => ButtonCode.KEY_LEFT_CURLY_BRACKET, // SDL_Keycode(0x007B)
		0x0000007D => ButtonCode.KEY_RIGHT_CURLY_BRACKET, // SDL_Keycode(0x007D)
		0x0000007C => ButtonCode.KEY_VERTICAL_BAR, // SDL_Keycode(0x007C)
		0x0000044E => ButtonCode.KEY_CYRILLIC_YU, // SDL_Keycode(0x044E)
		0x0000044D => ButtonCode.KEY_CYRILLIC_E, // SDL_Keycode(0x044D)
		0x0000044A => ButtonCode.KEY_CYRILLIC_HARD_SIGN, // SDL_Keycode(0x044A)
		0x00000445 => ButtonCode.KEY_CYRILLIC_HA, // SDL_Keycode(0x0445)
		0x00000451 => ButtonCode.KEY_CYRILLIC_IO, // SDL_Keycode(0x0451)
		0x00000436 => ButtonCode.KEY_CYRILLIC_ZHE, // SDL_Keycode(0x0436)
		0x00000431 => ButtonCode.KEY_CYRILLIC_BE, // SDL_Keycode(0x0431)
		_ => ButtonCode.KEY_NONE
	};

	internal static ButtonCode ScanCodeToButtonCode( int scan ) => scan switch
	{
		4 => ButtonCode.KEY_A, // SDL_SCANCODE_A
		5 => ButtonCode.KEY_B, // SDL_SCANCODE_B
		6 => ButtonCode.KEY_C, // SDL_SCANCODE_C
		7 => ButtonCode.KEY_D, // SDL_SCANCODE_D
		8 => ButtonCode.KEY_E, // SDL_SCANCODE_E
		9 => ButtonCode.KEY_F, // SDL_SCANCODE_F
		10 => ButtonCode.KEY_G, // SDL_SCANCODE_G
		11 => ButtonCode.KEY_H, // SDL_SCANCODE_H
		12 => ButtonCode.KEY_I, // SDL_SCANCODE_I
		13 => ButtonCode.KEY_J, // SDL_SCANCODE_J
		14 => ButtonCode.KEY_K, // SDL_SCANCODE_K
		15 => ButtonCode.KEY_L, // SDL_SCANCODE_L
		16 => ButtonCode.KEY_M, // SDL_SCANCODE_M
		17 => ButtonCode.KEY_N, // SDL_SCANCODE_N
		18 => ButtonCode.KEY_O, // SDL_SCANCODE_O
		19 => ButtonCode.KEY_P, // SDL_SCANCODE_P
		20 => ButtonCode.KEY_Q, // SDL_SCANCODE_Q
		21 => ButtonCode.KEY_R, // SDL_SCANCODE_R
		22 => ButtonCode.KEY_S, // SDL_SCANCODE_S
		23 => ButtonCode.KEY_T, // SDL_SCANCODE_T
		24 => ButtonCode.KEY_U, // SDL_SCANCODE_U
		25 => ButtonCode.KEY_V, // SDL_SCANCODE_V
		26 => ButtonCode.KEY_W, // SDL_SCANCODE_W
		27 => ButtonCode.KEY_X, // SDL_SCANCODE_X
		28 => ButtonCode.KEY_Y, // SDL_SCANCODE_Y
		29 => ButtonCode.KEY_Z, // SDL_SCANCODE_Z
		30 => ButtonCode.KEY_1, // SDL_SCANCODE_1
		31 => ButtonCode.KEY_2, // SDL_SCANCODE_2
		32 => ButtonCode.KEY_3, // SDL_SCANCODE_3
		33 => ButtonCode.KEY_4, // SDL_SCANCODE_4
		34 => ButtonCode.KEY_5, // SDL_SCANCODE_5
		35 => ButtonCode.KEY_6, // SDL_SCANCODE_6
		36 => ButtonCode.KEY_7, // SDL_SCANCODE_7
		37 => ButtonCode.KEY_8, // SDL_SCANCODE_8
		38 => ButtonCode.KEY_9, // SDL_SCANCODE_9
		39 => ButtonCode.KEY_0, // SDL_SCANCODE_0
		40 => ButtonCode.KEY_ENTER, // SDL_SCANCODE_RETURN
		41 => ButtonCode.KEY_ESCAPE, // SDL_SCANCODE_ESCAPE
		42 => ButtonCode.KEY_BACKSPACE, // SDL_SCANCODE_BACKSPACE
		43 => ButtonCode.KEY_TAB, // SDL_SCANCODE_TAB
		44 => ButtonCode.KEY_SPACE, // SDL_SCANCODE_SPACE
		45 => ButtonCode.KEY_MINUS, // SDL_SCANCODE_MINUS
		46 => ButtonCode.KEY_EQUAL, // SDL_SCANCODE_EQUALS
		47 => ButtonCode.KEY_LBRACKET, // SDL_SCANCODE_LEFTBRACKET
		48 => ButtonCode.KEY_RBRACKET, // SDL_SCANCODE_RIGHTBRACKET
		49 => ButtonCode.KEY_BACKSLASH, // SDL_SCANCODE_BACKSLASH
		50 => ButtonCode.KEY_BACKSLASH, // SDL_SCANCODE_NONUSHASH
		51 => ButtonCode.KEY_SEMICOLON, // SDL_SCANCODE_SEMICOLON
		52 => ButtonCode.KEY_APOSTROPHE, // SDL_SCANCODE_APOSTROPHE
		53 => ButtonCode.KEY_BACKQUOTE, // SDL_SCANCODE_GRAVE
		54 => ButtonCode.KEY_COMMA, // SDL_SCANCODE_COMMA
		55 => ButtonCode.KEY_PERIOD, // SDL_SCANCODE_PERIOD
		56 => ButtonCode.KEY_SLASH, // SDL_SCANCODE_SLASH
		57 => ButtonCode.KEY_CAPSLOCK, // SDL_SCANCODE_CAPSLOCK
		58 => ButtonCode.KEY_F1, // SDL_SCANCODE_F1
		59 => ButtonCode.KEY_F2, // SDL_SCANCODE_F2
		60 => ButtonCode.KEY_F3, // SDL_SCANCODE_F3
		61 => ButtonCode.KEY_F4, // SDL_SCANCODE_F4
		62 => ButtonCode.KEY_F5, // SDL_SCANCODE_F5
		63 => ButtonCode.KEY_F6, // SDL_SCANCODE_F6
		64 => ButtonCode.KEY_F7, // SDL_SCANCODE_F7
		65 => ButtonCode.KEY_F8, // SDL_SCANCODE_F8
		66 => ButtonCode.KEY_F9, // SDL_SCANCODE_F9
		67 => ButtonCode.KEY_F10, // SDL_SCANCODE_F10
		68 => ButtonCode.KEY_F11, // SDL_SCANCODE_F11
		69 => ButtonCode.KEY_F12, // SDL_SCANCODE_F12
		71 => ButtonCode.KEY_SCROLLLOCK, // SDL_SCANCODE_SCROLLLOCK
		72 => ButtonCode.KEY_BREAK, // SDL_SCANCODE_PAUSE
		73 => ButtonCode.KEY_INSERT, // SDL_SCANCODE_INSERT
		74 => ButtonCode.KEY_HOME, // SDL_SCANCODE_HOME
		75 => ButtonCode.KEY_PAGEUP, // SDL_SCANCODE_PAGEUP
		76 => ButtonCode.KEY_DELETE, // SDL_SCANCODE_DELETE
		77 => ButtonCode.KEY_END, // SDL_SCANCODE_END
		78 => ButtonCode.KEY_PAGEDOWN, // SDL_SCANCODE_PAGEDOWN
		79 => ButtonCode.KEY_RIGHT, // SDL_SCANCODE_RIGHT
		80 => ButtonCode.KEY_LEFT, // SDL_SCANCODE_LEFT
		81 => ButtonCode.KEY_DOWN, // SDL_SCANCODE_DOWN
		82 => ButtonCode.KEY_UP, // SDL_SCANCODE_UP
		83 => ButtonCode.KEY_NUMLOCKTOGGLE, // SDL_SCANCODE_NUMLOCKCLEAR
		84 => ButtonCode.KEY_PAD_DIVIDE, // SDL_SCANCODE_KP_DIVIDE
		85 => ButtonCode.KEY_PAD_MULTIPLY, // SDL_SCANCODE_KP_MULTIPLY
		86 => ButtonCode.KEY_PAD_MINUS, // SDL_SCANCODE_KP_MINUS
		87 => ButtonCode.KEY_PAD_PLUS, // SDL_SCANCODE_KP_PLUS
		88 => ButtonCode.KEY_PAD_ENTER, // SDL_SCANCODE_KP_ENTER
		89 => ButtonCode.KEY_PAD_1, // SDL_SCANCODE_KP_1
		90 => ButtonCode.KEY_PAD_2, // SDL_SCANCODE_KP_2
		91 => ButtonCode.KEY_PAD_3, // SDL_SCANCODE_KP_3
		92 => ButtonCode.KEY_PAD_4, // SDL_SCANCODE_KP_4
		93 => ButtonCode.KEY_PAD_5, // SDL_SCANCODE_KP_5
		94 => ButtonCode.KEY_PAD_6, // SDL_SCANCODE_KP_6
		95 => ButtonCode.KEY_PAD_7, // SDL_SCANCODE_KP_7
		96 => ButtonCode.KEY_PAD_8, // SDL_SCANCODE_KP_8
		97 => ButtonCode.KEY_PAD_9, // SDL_SCANCODE_KP_9
		98 => ButtonCode.KEY_PAD_0, // SDL_SCANCODE_KP_0
		99 => ButtonCode.KEY_PAD_DECIMAL, // SDL_SCANCODE_KP_PERIOD
		101 => ButtonCode.KEY_APP, // SDL_SCANCODE_APPLICATION
		103 => ButtonCode.KEY_EQUAL, // SDL_SCANCODE_KP_EQUALS
		118 => ButtonCode.KEY_LALT, // SDL_SCANCODE_MENU
		133 => ButtonCode.KEY_COMMA, // SDL_SCANCODE_KP_COMMA
		224 => ButtonCode.KEY_LCONTROL, // SDL_SCANCODE_LCTRL
		225 => ButtonCode.KEY_LSHIFT, // SDL_SCANCODE_LSHIFT
		226 => ButtonCode.KEY_LALT, // SDL_SCANCODE_LALT
		227 => ButtonCode.KEY_LWIN, // SDL_SCANCODE_LGUI
		228 => ButtonCode.KEY_RCONTROL, // SDL_SCANCODE_RCTRL
		229 => ButtonCode.KEY_RSHIFT, // SDL_SCANCODE_RSHIFT
		230 => ButtonCode.KEY_RALT, // SDL_SCANCODE_RALT
		231 => ButtonCode.KEY_RWIN, // SDL_SCANCODE_RGUI
		_ => ButtonCode.KEY_NONE
	};

	static readonly int[] scanCodes = CreateScanCodes();
	static int[] CreateScanCodes()
	{
		var result = new int[(int)ButtonCode.BUTTON_CODE_COUNT];
		for ( var i = 0; i < 512; i++ )
		{
			var code = ScanCodeToButtonCode( i );
			if ( code != ButtonCode.KEY_NONE ) result[(int)code] = i;
		}
		return result;
	}
	internal static int ButtonCodeToScanCode( ButtonCode code ) => (uint)code < scanCodes.Length ? scanCodes[(int)code] : 0;

	internal static ButtonCode VirtualKeyToButtonCode( int key ) => key switch
	{
		0x30 => ButtonCode.KEY_0, // '0'
		0x31 => ButtonCode.KEY_1, // '1'
		0x32 => ButtonCode.KEY_2, // '2'
		0x33 => ButtonCode.KEY_3, // '3'
		0x34 => ButtonCode.KEY_4, // '4'
		0x35 => ButtonCode.KEY_5, // '5'
		0x36 => ButtonCode.KEY_6, // '6'
		0x37 => ButtonCode.KEY_7, // '7'
		0x38 => ButtonCode.KEY_8, // '8'
		0x39 => ButtonCode.KEY_9, // '9'
		0x41 => ButtonCode.KEY_A, // 'A'
		0x42 => ButtonCode.KEY_B, // 'B'
		0x43 => ButtonCode.KEY_C, // 'C'
		0x44 => ButtonCode.KEY_D, // 'D'
		0x45 => ButtonCode.KEY_E, // 'E'
		0x46 => ButtonCode.KEY_F, // 'F'
		0x47 => ButtonCode.KEY_G, // 'G'
		0x48 => ButtonCode.KEY_H, // 'H'
		0x49 => ButtonCode.KEY_I, // 'I'
		0x4A => ButtonCode.KEY_J, // 'J'
		0x4B => ButtonCode.KEY_K, // 'K'
		0x4C => ButtonCode.KEY_L, // 'L'
		0x4D => ButtonCode.KEY_M, // 'M'
		0x4E => ButtonCode.KEY_N, // 'N'
		0x4F => ButtonCode.KEY_O, // 'O'
		0x50 => ButtonCode.KEY_P, // 'P'
		0x51 => ButtonCode.KEY_Q, // 'Q'
		0x52 => ButtonCode.KEY_R, // 'R'
		0x53 => ButtonCode.KEY_S, // 'S'
		0x54 => ButtonCode.KEY_T, // 'T'
		0x55 => ButtonCode.KEY_U, // 'U'
		0x56 => ButtonCode.KEY_V, // 'V'
		0x57 => ButtonCode.KEY_W, // 'W'
		0x58 => ButtonCode.KEY_X, // 'X'
		0x59 => ButtonCode.KEY_Y, // 'Y'
		0x5A => ButtonCode.KEY_Z, // 'Z'
		0x60 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_0, // VK_NUMPAD0
		0x61 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_1, // VK_NUMPAD1
		0x62 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_2, // VK_NUMPAD2
		0x63 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_3, // VK_NUMPAD3
		0x64 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_4, // VK_NUMPAD4
		0x65 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_5, // VK_NUMPAD5
		0x66 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_6, // VK_NUMPAD6
		0x67 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_7, // VK_NUMPAD7
		0x68 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_8, // VK_NUMPAD8
		0x69 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_9, // VK_NUMPAD9
		0x6F when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_DIVIDE, // VK_DIVIDE
		0x6A when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_MULTIPLY, // VK_MULTIPLY
		0x6D when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_MINUS, // VK_SUBTRACT
		0x6B when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_PLUS, // VK_ADD
		0x6E when OperatingSystem.IsWindows() => ButtonCode.KEY_PAD_DECIMAL, // VK_DECIMAL
		0xDB => ButtonCode.KEY_LBRACKET, // 0xdb
		0xDD => ButtonCode.KEY_RBRACKET, // 0xdd
		0xBA => ButtonCode.KEY_SEMICOLON, // 0xba
		0xDE => ButtonCode.KEY_APOSTROPHE, // 0xde
		0xC0 => ButtonCode.KEY_BACKQUOTE, // 0xc0
		0xBC => ButtonCode.KEY_COMMA, // 0xbc
		0xBE => ButtonCode.KEY_PERIOD, // 0xbe
		0xBF => ButtonCode.KEY_SLASH, // 0xbf
		0xDC => ButtonCode.KEY_BACKSLASH, // 0xdc
		0xBD => ButtonCode.KEY_MINUS, // 0xbd
		0xBB => ButtonCode.KEY_EQUAL, // 0xbb
		0x3C => ButtonCode.KEY_LESS, // 0x3c
		0x0D when OperatingSystem.IsWindows() => ButtonCode.KEY_ENTER, // VK_RETURN
		0x20 when OperatingSystem.IsWindows() => ButtonCode.KEY_SPACE, // VK_SPACE
		0x08 when OperatingSystem.IsWindows() => ButtonCode.KEY_BACKSPACE, // VK_BACK
		0x09 when OperatingSystem.IsWindows() => ButtonCode.KEY_TAB, // VK_TAB
		0x14 when OperatingSystem.IsWindows() => ButtonCode.KEY_CAPSLOCK, // VK_CAPITAL
		0x90 when OperatingSystem.IsWindows() => ButtonCode.KEY_NUMLOCK, // VK_NUMLOCK
		0x1B when OperatingSystem.IsWindows() => ButtonCode.KEY_ESCAPE, // VK_ESCAPE
		0x91 when OperatingSystem.IsWindows() => ButtonCode.KEY_SCROLLLOCK, // VK_SCROLL
		0x2D when OperatingSystem.IsWindows() => ButtonCode.KEY_INSERT, // VK_INSERT
		0x2E when OperatingSystem.IsWindows() => ButtonCode.KEY_DELETE, // VK_DELETE
		0x24 when OperatingSystem.IsWindows() => ButtonCode.KEY_HOME, // VK_HOME
		0x23 when OperatingSystem.IsWindows() => ButtonCode.KEY_END, // VK_END
		0x21 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAGEUP, // VK_PRIOR
		0x22 when OperatingSystem.IsWindows() => ButtonCode.KEY_PAGEDOWN, // VK_NEXT
		0x13 when OperatingSystem.IsWindows() => ButtonCode.KEY_BREAK, // VK_PAUSE
		0x10 when OperatingSystem.IsWindows() => ButtonCode.KEY_LSHIFT, // VK_SHIFT
		0x12 when OperatingSystem.IsWindows() => ButtonCode.KEY_LALT, // VK_MENU
		0x11 when OperatingSystem.IsWindows() => ButtonCode.KEY_LCONTROL, // VK_CONTROL
		0x5B when OperatingSystem.IsWindows() => ButtonCode.KEY_LWIN, // VK_LWIN
		0x5C when OperatingSystem.IsWindows() => ButtonCode.KEY_RWIN, // VK_RWIN
		0x5D when OperatingSystem.IsWindows() => ButtonCode.KEY_APP, // VK_APPS
		0x26 when OperatingSystem.IsWindows() => ButtonCode.KEY_UP, // VK_UP
		0x25 when OperatingSystem.IsWindows() => ButtonCode.KEY_LEFT, // VK_LEFT
		0x28 when OperatingSystem.IsWindows() => ButtonCode.KEY_DOWN, // VK_DOWN
		0x27 when OperatingSystem.IsWindows() => ButtonCode.KEY_RIGHT, // VK_RIGHT
		0x70 when OperatingSystem.IsWindows() => ButtonCode.KEY_F1, // VK_F1
		0x71 when OperatingSystem.IsWindows() => ButtonCode.KEY_F2, // VK_F2
		0x72 when OperatingSystem.IsWindows() => ButtonCode.KEY_F3, // VK_F3
		0x73 when OperatingSystem.IsWindows() => ButtonCode.KEY_F4, // VK_F4
		0x74 when OperatingSystem.IsWindows() => ButtonCode.KEY_F5, // VK_F5
		0x75 when OperatingSystem.IsWindows() => ButtonCode.KEY_F6, // VK_F6
		0x76 when OperatingSystem.IsWindows() => ButtonCode.KEY_F7, // VK_F7
		0x77 when OperatingSystem.IsWindows() => ButtonCode.KEY_F8, // VK_F8
		0x78 when OperatingSystem.IsWindows() => ButtonCode.KEY_F9, // VK_F9
		0x79 when OperatingSystem.IsWindows() => ButtonCode.KEY_F10, // VK_F10
		0x7A when OperatingSystem.IsWindows() => ButtonCode.KEY_F11, // VK_F11
		0x7B when OperatingSystem.IsWindows() => ButtonCode.KEY_F12, // VK_F12
		_ => ButtonCode.KEY_NONE
	};

	static readonly int[] virtualKeys = CreateVirtualKeys();
	static int[] CreateVirtualKeys()
	{
		var result = new int[(int)ButtonCode.BUTTON_CODE_COUNT];
		for ( var i = 0; i < 256; i++ ) result[(int)VirtualKeyToButtonCode( i )] = i;
		if ( OperatingSystem.IsWindows() )
		{
			result[(int)ButtonCode.KEY_RSHIFT] = 0x10;
			result[(int)ButtonCode.KEY_RALT] = 0x12;
			result[(int)ButtonCode.KEY_RCONTROL] = 0x11;
		}
		result[0] = 0;
		return result;
	}
	internal static int ButtonCodeToVirtualKey( ButtonCode code ) => (uint)code < virtualKeys.Length ? virtualKeys[(int)code] : 0;
}
