namespace Sandbox;

public static partial class Input
{
	[ConVar( "ui_input_force_vendor", ConVarFlags.Protected )]
	private static bool ForceDefaultVendor { get; set; } = false;

	static Texture LoadGlyphTexture( string vendor, string key, bool outline = false, InputGlyphSize size = InputGlyphSize.Small )
	{
		key = key.ToLowerInvariant();
		var px = size.ToPixels();

		if ( outline )
		{
			var outlinePath = $"/ui/glyphs/{vendor}/outline/{key}.svg?w={px}&h={px}";
			var tx = Texture.Load( outlinePath, false );
			if ( tx is not null ) return tx;
		}

		var path = $"/ui/glyphs/{vendor}/{key}.svg?w={px}&h={px}";
		return Texture.Load( path, false );
	}

	/// <summary>
	/// Tries to load a glyph texture, will seek the current vendor controller (Xbox, PlayStation, Nintendo) and fall back to Xbox if not found.
	/// </summary>
	/// <param name="file"></param>
	/// <param name="size"></param>
	/// <param name="outline"></param>
	/// <param name="noController"></param>
	/// <returns></returns>
	static Texture LoadGlyphTexture( string file, InputGlyphSize size = InputGlyphSize.Small, bool outline = false, bool noController = false )
	{
		if ( UsingController && !noController )
		{
			var vendor = CurrentController?.GlyphVendor ?? "xbox";
			if ( ForceDefaultVendor ) vendor = "xbox";

			// Did we find our vendor's texture?
			if ( LoadGlyphTexture( vendor, file.ToLowerInvariant(), outline, size ) is Texture vendorTex ) return vendorTex;

			// Try using xbox
			if ( LoadGlyphTexture( "xbox", file.ToLowerInvariant(), outline, size ) is Texture xboxTex ) return xboxTex;
		}

		return LoadGlyphTexture( "default", file.ToLowerInvariant(), outline, size );
	}

	/// <summary>
	/// Some keys can't be parsed by files because they're symbols, so we change them into something readable
	/// </summary>
	/// <param name="key"></param>
	/// <returns></returns>
	static string GetButtonName( string key )
	{
		return key.ToLowerInvariant() switch
		{
			"/" => "slash",
			"\\" => "backslash",
			"." => "period",
			"," => "comma",
			"-" => "minus",
			"=" => "equals",
			"'" => "apostrophe",
			"`" => "backquote",
			"[" => "leftbracket",
			"]" => "rightbracket",
			"rwin" => "windows",
			"lwin" => "windows",
			_ => key
		};
	}

	/// <summary>
	/// Get a glyph texture from the controller bound to the action.
	/// If no binding is found will return an 'UNBOUND' glyph.
	/// </summary>
	/// <remarks>You should update your UI with this every frame, it's very cheap to call and context can change.</remarks>
	public static Texture GetGlyph( string name, InputGlyphSize size = InputGlyphSize.Small, bool outline = false )
	{
		var action = InputActions?
			.FirstOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase ) );

		if ( action is null )
		{
			return LoadGlyphTexture( "unknown", size, outline );
		}

		var key = GetButtonOrigin( action );
		if ( key is null )
		{
			return LoadGlyphTexture( "unknown", size, outline );
		}

		key = GetButtonName( key.ToLowerInvariant() );

		if ( string.IsNullOrEmpty( key ) ) key = "UNBOUND";

		if ( UsingController )
		{
			key = $"{action.GamepadCode.ToString().ToLowerInvariant()}";
		}

		// Find an existing texture
		var tx = LoadGlyphTexture( key, size, outline );
		if ( tx is not null ) return tx;

		// Fall back to an empty glyph
		return LoadGlyphTexture( "unknown", size, outline );
	}

	/// <inheritdoc cref="GetGlyph(string, InputGlyphSize, bool)"/>
	public static Texture GetGlyph( string name, InputGlyphSize size = InputGlyphSize.Small, GlyphStyle style = default )
	{
		return GetGlyph( name, size, false );
	}

	/// <summary>
	/// Get a glyph texture from an analog input on a controller.
	/// </summary>
	public static Texture GetGlyph( InputAnalog analog, InputGlyphSize size = InputGlyphSize.Small, bool outline = false )
	{
		return LoadGlyphTexture( analog.ToString(), size, outline );
	}

	/// <summary>
	/// Returns the name of the analog axis bound to this <see cref="InputAnalog"/>.
	/// <example>For example:
	/// <code>Input.GetButtonOrigin( InputAnalog.Move )</code>
	/// could return <c>Left Joystick</c>
	/// </example>
	/// </summary>
	public static string GetButtonOrigin( InputAnalog analog )
	{
		// TODO: better naming
		return analog.ToString();
	}

	/// <summary>
	/// Keyboard related glyph methods.
	/// </summary>
	public static partial class Keyboard
	{
		/// <summary>
		/// Get a glyph texture from a specific key name.
		/// </summary>
		/// <param name="key"></param>
		/// <param name="size"></param>
		/// <param name="outline"></param>
		/// <returns></returns>
		public static Texture GetGlyph( string key, InputGlyphSize size = InputGlyphSize.Small, bool outline = false )
		{
			if ( string.IsNullOrEmpty( key ) ) key = "UNBOUND";
			key = GetButtonName( key );
			return LoadGlyphTexture( key, size, outline, noController: true );
		}
	}
}

public enum InputGlyphSize
{
	/// <summary>
	/// Small 32x32 ( Keyboard glyphs can be wider for long key names )
	/// </summary>
	Small,
	/// <summary>
	/// Medium 128x128 ( Keyboard glyphs can be wider for long key names )
	/// </summary>
	Medium,
	/// <summary>
	/// Large 256x256 ( Keyboard glyphs can be wider for long key names )
	/// </summary>
	Large
}

public static partial class SandboxGameExtensions
{
	/// <summary>
	/// Translates this enum to pixel size.
	/// </summary>
	public static int ToPixels( this InputGlyphSize size ) => size switch
	{
		InputGlyphSize.Small => 32,
		InputGlyphSize.Medium => 128,
		InputGlyphSize.Large => 256,
		_ => 32,
	};
}

public struct GlyphStyle
{
	/// <summary>
	/// Face buttons will have colored labels/outlines on a knocked out background
	/// Rest of inputs will have white detail/borders on a knocked out background
	/// </summary>
	public static readonly GlyphStyle Knockout = new();
	/// <summary>
	/// Black detail/borders on a white background
	/// </summary>
	public static readonly GlyphStyle Light = new();

	/// <summary>
	/// White detail/borders on a black background
	/// </summary>
	public static readonly GlyphStyle Dark = new();

	//
	// Modifiers
	//
	// Default ABXY/PS equivalent glyphs have a solid fill w/ color matching the physical buttons on the device

	/// <summary>
	/// ABXY Buttons will match the base style color instead of their normal associated color
	/// </summary>
	public readonly GlyphStyle WithNeutralColorABXY() => new();
	/// <summary>
	/// ABXY Buttons will have a solid fill
	/// </summary>
	public readonly GlyphStyle WithSolidABXY() => new();
}

/// <summary>
/// Internal bit flags for glyph styles, matches Steam Input ones.
/// </summary>
[Flags]
internal enum GlyphStyleMask
{
	//
	// Base-styles - cannot mix
	//

	Knockout = 0x00,
	Light = 0x01,
	Dark = 0x02,

	//
	// Modifiers
	//

	NeutralColorABXY = 0x10,
	SolidABXY = 0x20,
}
