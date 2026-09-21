namespace NativeEngine
{
	internal enum ButtonCode : int
	{
		BUTTON_CODE_INVALID = -1,
		BUTTON_CODE_NONE = 0,

		BUTTON_CODE_FIRST = 0,
		KEY_FIRST = 0,

		KEY_NONE = KEY_FIRST,
		KEY_0,
		KEY_1,
		KEY_2,
		KEY_3,
		KEY_4,
		KEY_5,
		KEY_6,
		KEY_7,
		KEY_8,
		KEY_9,
		KEY_A,
		KEY_B,
		KEY_C,
		KEY_D,
		KEY_E,
		KEY_F,
		KEY_G,
		KEY_H,
		KEY_I,
		KEY_J,
		KEY_K,
		KEY_L,
		KEY_M,
		KEY_N,
		KEY_O,
		KEY_P,
		KEY_Q,
		KEY_R,
		KEY_S,
		KEY_T,
		KEY_U,
		KEY_V,
		KEY_W,
		KEY_X,
		KEY_Y,
		KEY_Z,
		KEY_PAD_0,
		KEY_PAD_1,
		KEY_PAD_2,
		KEY_PAD_3,
		KEY_PAD_4,
		KEY_PAD_5,
		KEY_PAD_6,
		KEY_PAD_7,
		KEY_PAD_8,
		KEY_PAD_9,
		KEY_PAD_DIVIDE,
		KEY_PAD_MULTIPLY,
		KEY_PAD_MINUS,
		KEY_PAD_PLUS,
		KEY_PAD_ENTER,
		KEY_PAD_DECIMAL,
		KEY_LESS,
		KEY_LBRACKET,
		KEY_RBRACKET,
		KEY_SEMICOLON,
		KEY_APOSTROPHE,
		KEY_BACKQUOTE,
		KEY_COMMA,
		KEY_PERIOD,
		KEY_SLASH,
		KEY_BACKSLASH,
		KEY_MINUS,
		KEY_EQUAL,
		KEY_ENTER,
		KEY_SPACE,
		KEY_BACKSPACE,
		KEY_TAB,
		KEY_CAPSLOCK,
		KEY_NUMLOCK,
		KEY_ESCAPE,
		KEY_SCROLLLOCK,
		KEY_INSERT,
		KEY_DELETE,
		KEY_HOME,
		KEY_END,
		KEY_PAGEUP,
		KEY_PAGEDOWN,
		KEY_BREAK,
		KEY_LSHIFT,
		KEY_RSHIFT,
		KEY_LALT,
		KEY_RALT,
		KEY_LCONTROL,
		KEY_RCONTROL,
		KEY_LWIN,
		KEY_RWIN,
		KEY_APP,
		KEY_UP,
		KEY_LEFT,
		KEY_DOWN,
		KEY_RIGHT,
		KEY_F1,
		KEY_F2,
		KEY_F3,
		KEY_F4,
		KEY_F5,
		KEY_F6,
		KEY_F7,
		KEY_F8,
		KEY_F9,
		KEY_F10,
		KEY_F11,
		KEY_F12,
		KEY_CAPSLOCKTOGGLE,
		KEY_NUMLOCKTOGGLE,
		KEY_SCROLLLOCKTOGGLE,

		// Button codes that correspond to their SDLK_* equivalent from SDL
		KEY_AC_BACK,
		KEY_AC_BOOKMARKS,
		KEY_AC_FORWARD,
		KEY_AC_HOME,
		KEY_AC_REFRESH,
		KEY_AC_SEARCH,
		KEY_AC_STOP,
		KEY_AGAIN,
		KEY_ALTERASE,
		KEY_AMPERSAND,
		KEY_ASTERISK,
		KEY_AT,
		KEY_AUDIOMUTE,
		KEY_AUDIONEXT,
		KEY_AUDIOPLAY,
		KEY_AUDIOPREV,
		KEY_AUDIOSTOP,
		KEY_BRIGHTNESSDOWN,
		KEY_BRIGHTNESSUP,
		KEY_CALCULATOR,
		KEY_CANCEL,
		KEY_CARET,
		KEY_CLEAR,
		KEY_CLEARAGAIN,
		KEY_COLON,
		KEY_COMPUTER,
		KEY_COPY,
		KEY_CRSEL,
		KEY_CURRENCYSUBUNIT,
		KEY_CURRENCYUNIT,
		KEY_CUT,
		KEY_DECIMALSEPARATOR,
		KEY_DISPLAYSWITCH,
		KEY_DOLLAR,
		KEY_EJECT,
		KEY_EXCLAIM,

		KEY_BTN_EXECUTE, // KEY_EXECUTE is taken by winnt.h

		KEY_EXSEL,
		KEY_F13,
		KEY_F14,
		KEY_F15,
		KEY_F16,
		KEY_F17,
		KEY_F18,
		KEY_F19,
		KEY_F20,
		KEY_F21,
		KEY_F22,
		KEY_F23,
		KEY_F24,
		KEY_FIND,
		KEY_GREATER,
		KEY_HASH,
		KEY_HELP,
		KEY_KBDILLUMDOWN,
		KEY_KBDILLUMTOGGLE,
		KEY_KBDILLUMUP,
		KEY_KP_00,
		KEY_KP_000,
		KEY_KP_A,
		KEY_KP_AMPERSAND,
		KEY_KP_AT,
		KEY_KP_B,
		KEY_KP_BACKSPACE,
		KEY_KP_BINARY,
		KEY_KP_C,
		KEY_KP_CLEAR,
		KEY_KP_CLEARENTRY,
		KEY_KP_COLON,
		KEY_KP_COMMA,
		KEY_KP_D,
		KEY_KP_DBLAMPERSAND,
		KEY_KP_DBLVERTICALBAR,
		KEY_KP_DECIMAL,
		KEY_KP_E,
		KEY_KP_EQUALS,
		KEY_KP_EQUALSAS400,
		KEY_KP_EXCLAM,
		KEY_KP_F,
		KEY_KP_GREATER,
		KEY_KP_HASH,
		KEY_KP_HEXADECIMAL,
		KEY_KP_LEFTBRACE,
		KEY_KP_LEFTPAREN,
		KEY_KP_LESS,
		KEY_KP_MEMADD,
		KEY_KP_MEMCLEAR,
		KEY_KP_MEMDIVIDE,
		KEY_KP_MEMMULTIPLY,
		KEY_KP_MEMRECALL,
		KEY_KP_MEMSTORE,
		KEY_KP_MEMSUBTRACT,
		KEY_KP_OCTAL,
		KEY_KP_PERCENT,
		KEY_KP_PLUSMINUS,
		KEY_KP_POWER,
		KEY_KP_RIGHTBRACE,
		KEY_KP_RIGHTPAREN,
		KEY_KP_SPACE,
		KEY_KP_TAB,
		KEY_KP_VERTICALBAR,
		KEY_KP_XOR,
		KEY_LEFTPAREN,
		KEY_MAIL,
		KEY_MEDIASELECT,
		KEY_MODE,
		KEY_MUTE,
		KEY_OPER,
		KEY_OUT,
		KEY_PASTE,
		KEY_PERCENT,
		KEY_PLUS,
		KEY_POWER,
		KEY_PRINTSCREEN,
		KEY_PRIOR,
		KEY_QUESTION,
		KEY_QUOTEDBL,
		KEY_RETURN2,
		KEY_RIGHTPAREN,
		KEY_SELECT,
		KEY_SEPARATOR,
		KEY_SLEEP,
		KEY_STOP,
		KEY_SYSREQ,
		KEY_THOUSANDSSEPARATOR,
		KEY_UNDERSCORE,
		KEY_UNDO,
		KEY_VOLUMEDOWN,
		KEY_VOLUMEUP,
		KEY_WWW,

		// These are common unicode characters that SDL will send us that don't have corresponding SDLK_* constants
		// (Latin-1 Supplement)
		KEY_INVERTED_EXCLAMATION_MARK,                  // ¡ U+00A1
		KEY_CENT_SIGN,                                  // ¢ U+00A2
		KEY_POUND_SIGN,                                 // £ U+00A3
		KEY_CURRENCY_SIGN,                              // ¤ U+00A4
		KEY_YEN_SIGN,                                   // ¥ U+00A5
		KEY_BROKEN_BAR,                                 // ¦ U+00A6
		KEY_SECTION_SIGN,                               // § U+00A7
		KEY_DIAERESIS,                                  // ¨ U+00A8
		KEY_COPYRIGHT_SIGN,                             // © U+00A9
		KEY_FEMININE_ORDINAL_INDICATOR,                 // ª U+00AA
		KEY_LEFT_POINTING_DOUBLE_ANGLE_QUOTATION_MARK,  // « U+00AB
		KEY_NOT_SIGN,                                   // ¬ U+00AC
		KEY_REGISTERED_SIGN,                            // ® U+00AE
		KEY_MACRON,                                     // ¯ U+00AF
		KEY_DEGREE_SYMBOL,                              // ° U+00B0
		KEY_PLUS_MINUS_SIGN,                            // ± U+00B1
		KEY_SUPERSCRIPT_TWO,                            // ² U+00B2
		KEY_SUPERSCRIPT_THREE,                          // ³ U+00B3
		KEY_ACUTE_ACCENT,                               // ´ U+00B4
		KEY_MICRO_SIGN,                                 // µ U+00B5
		KEY_PILCROW_SIGN,                               // ¶ U+00B6
		KEY_MIDDLE_DOT,                                 // · U+00B7
		KEY_CEDILLA,                                    // ¸ U+00B8
		KEY_SUPERSCRIPT_ONE,                            // ¹ U+00B9
		KEY_MASCULINE_ORDINAL_INDICATOR,                // º U+00BA
		KEY_RIGHT_POINTING_DOUBLE_ANGLE_QUOTATION_MARK, // » U+00BB
		KEY_VULGAR_FRACTION_ONE_QUARTER,                // ¼ U+00BC
		KEY_VULGAR_FRACTION_ONE_HALF,                   // ½ U+00BD
		KEY_VULGAR_FRACTION_THREE_QUARTERS,             // ¾ U+00BE
		KEY_INVERTED_QUESTION_MARK,                     // ¿ U+00BF
		KEY_MULTIPLICATION_SIGN,                        // × U+00D7
		KEY_SHARP_S,                                    // ß U+00DF
		KEY_A_WITH_GRAVE,                               // à U+00E0
		KEY_A_WITH_ACUTE,                               // á U+00E1
		KEY_A_WITH_CIRCUMFLEX,                          // â U+00E2
		KEY_A_WITH_TILDE,                               // ã U+00E3
		KEY_A_WITH_DIAERESIS,                           // ä U+00E4
		KEY_A_WITH_RING_ABOVE,                          // å U+00E5
		KEY_AE,                                         // æ U+00E6
		KEY_C_WITH_CEDILLA,                             // ç U+00E7
		KEY_E_WITH_GRAVE,                               // è U+00E8
		KEY_E_WITH_ACUTE,                               // é U+00E9
		KEY_E_WITH_CIRCUMFLEX,                          // ê U+00EA
		KEY_E_WITH_DIAERESIS,                           // ë U+00EB
		KEY_I_WITH_GRAVE,                               // ì U+00EC
		KEY_I_WITH_ACUTE,                               // í U+00ED
		KEY_I_WITH_CIRCUMFLEX,                          // î U+00EE
		KEY_I_WITH_DIAERESIS,                           // ï U+00EF
		KEY_ETH,                                        // ð U+00F0
		KEY_N_WITH_TILDE,                               // ñ U+00F1
		KEY_O_WITH_GRAVE,                               // ò U+00F2
		KEY_O_WITH_ACUTE,                               // ó U+00F3
		KEY_O_WITH_CIRCUMFLEX,                          // ô U+00F4
		KEY_O_WITH_TILDE,                               // õ U+00F5
		KEY_O_WITH_DIAERESIS,                           // ö U+00F6
		KEY_DIVISION_SIGN,                              // ÷ U+00F7
		KEY_O_WITH_STROKE,                              // ø U+00F8
		KEY_U_WITH_GRAVE,                               // ù U+00F9
		KEY_U_WITH_ACUTE,                               // ú U+00FA
		KEY_U_WITH_CIRCUMFLEX,                          // û U+00FB
		KEY_U_WITH_DIAERESIS,                           // ü U+00FC
		KEY_Y_WITH_ACUTE,                               // ý U+00FD
		KEY_THORN,                                      // þ U+00FE
		KEY_Y_WITH_DIAERESIS,                           // ÿ U+00FF
		KEY_EURO_SIGN,                                  // € U+20AC

		KEY_TILDE,                                      // ~ U+007E
		KEY_LEFT_CURLY_BRACKET,                         // { U+007B
		KEY_RIGHT_CURLY_BRACKET,                        // } U+007D
		KEY_VERTICAL_BAR,                               // | U+007C

		// These come from SDL with a Windows Russian keyboard layout (Using a US101 keyboard) - other keys appear to show up as ASCII codes?
		KEY_CYRILLIC_YU,                                // U+044E
		KEY_CYRILLIC_E,                                 // U+044D
		KEY_CYRILLIC_HARD_SIGN,                         // U+044A
		KEY_CYRILLIC_HA,                                // U+0445
		KEY_CYRILLIC_IO,                                // U+0451
		KEY_CYRILLIC_ZHE,                               // U+0436
		KEY_CYRILLIC_BE,                                // U+0431

		KEY_LAST = KEY_CYRILLIC_BE,
		//KEY_COUNT = KEY_LAST - KEY_FIRST + 1,

		// Mouse
		MOUSE_FIRST = KEY_LAST + 1,

		MouseLeft = MOUSE_FIRST,
		MouseRight,
		MouseMiddle,
		MouseBack,
		MouseForward,
		MouseWheelUp,     // A fake button which is 'pressed' and 'released' when the wheel is moved up 
		MouseWheelDown,   // A fake button which is 'pressed' and 'released' when the wheel is moved down

		MOUSE_LAST = MouseWheelDown,
		MOUSE_COUNT = MOUSE_LAST - MOUSE_FIRST + 1,

		// Joystick
		JOYSTICK_FIRST = MOUSE_LAST + 1,

		JOYSTICK_FIRST_BUTTON = JOYSTICK_FIRST,
		JOYSTICK_LAST_BUTTON = JOYSTICK_FIRST_BUTTON + 31,
		JOYSTICK_FIRST_POV_BUTTON,
		JOYSTICK_LAST_POV_BUTTON = JOYSTICK_FIRST_POV_BUTTON + 3,
		JOYSTICK_FIRST_AXIS_BUTTON,
		JOYSTICK_LAST_AXIS_BUTTON = JOYSTICK_FIRST_AXIS_BUTTON + 11,

		JOYSTICK_LAST = JOYSTICK_LAST_AXIS_BUTTON,

		BUTTON_CODE_COUNT,
		BUTTON_CODE_LAST = BUTTON_CODE_COUNT - 1,

		// Helpers for XBox 360
		KEY_XBUTTON_UP = JOYSTICK_FIRST_POV_BUTTON, // POV buttons
		KEY_XBUTTON_RIGHT,
		KEY_XBUTTON_DOWN,
		KEY_XBUTTON_LEFT,

		KEY_XBUTTON_A = JOYSTICK_FIRST_BUTTON,      // Buttons
		KEY_XBUTTON_B,
		KEY_XBUTTON_X,
		KEY_XBUTTON_Y,
		KEY_XBUTTON_LEFT_SHOULDER,
		KEY_XBUTTON_RIGHT_SHOULDER,
		KEY_XBUTTON_BACK,
		KEY_XBUTTON_START,
		KEY_XBUTTON_STICK1,
		KEY_XBUTTON_STICK2,
		KEY_XBUTTON_INACTIVE_START,

		KEY_XSTICK1_RIGHT = JOYSTICK_FIRST_AXIS_BUTTON, // XAXIS POSITIVE
		KEY_XSTICK1_LEFT,                           // XAXIS NEGATIVE
		KEY_XSTICK1_DOWN,                           // YAXIS POSITIVE
		KEY_XSTICK1_UP,                             // YAXIS NEGATIVE
		KEY_XBUTTON_LTRIGGER,                       // ZAXIS POSITIVE
		KEY_XBUTTON_RTRIGGER,                       // ZAXIS NEGATIVE
		KEY_XSTICK2_RIGHT,                          // UAXIS POSITIVE
		KEY_XSTICK2_LEFT,                           // UAXIS NEGATIVE
		KEY_XSTICK2_DOWN,                           // VAXIS POSITIVE
		KEY_XSTICK2_UP,                             // VAXIS NEGATIVE
	};
}
