using System.Text.RegularExpressions;
using Sandbox.ShaderCompiler.Slang;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// What the compiler owns in an author's module: <c>[Material]</c> structs. Their <c>Texture2D</c> fields become
/// <c>MaterialTexture2D</c>, a bindless slot a record can hold (a struct of descriptors can't be loaded from a buffer),
/// so authors write <c>Texture2D Color;</c> and <c>Color.Sample( ... )</c> works either way. Lines are kept.
/// </summary>
internal static partial class MaterialSource
{
	[GeneratedRegex( @"\[\s*Material\s*(?:,[^\]]*)?\][^{;]*?\bstruct\s+(?<name>\w+)\s*(?::\s*(?<bases>[^{]*))?\{" )]
	private static partial Regex MaterialStruct();

	// A field declaration of a texture type at the start of a line: optional attributes, Texture2D or Texture2D<...>
	[GeneratedRegex( @"^(?<lead>[ \t]*(?:\[[^\]\r\n]*\][ \t]*)*)Texture2D(?:[ \t]*<[^>\r\n]*>)?(?=[ \t]+\w+[ \t]*;)", RegexOptions.Multiline )]
	private static partial Regex TextureField();

	/// <summary>A <c>[Material]</c> struct: its name and the interfaces it implements.</summary>
	public readonly record struct Declared( string Name, string[] Interfaces );

	/// <summary>Rewrite <paramref name="text"/>'s <c>[Material]</c> structs and say which there are.</summary>
	public static string Rewrite( string text, out List<Declared> materials )
	{
		materials = new List<Declared>();
		var result = new System.Text.StringBuilder( text.Length );
		var at = 0;
		foreach ( Match m in MaterialStruct().Matches( text ) )
		{
			if ( m.Index < at ) continue;
			var open = m.Index + m.Length - 1;
			var close = MatchingBrace( text, open );
			var interfaces = m.Groups["bases"].Success
				? m.Groups["bases"].Value.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
				: [];
			materials.Add( new Declared( m.Groups["name"].Value, interfaces ) );

			result.Append( text, at, open + 1 - at );
			result.Append( Fields( text.Substring( open + 1, close - open - 1 ) ) );
			at = close;
		}

		result.Append( text, at, text.Length - at );
		return result.ToString();
	}

	// A field declaration at the start of a line: optional attributes, a type, a name, an optional initializer
	[GeneratedRegex( @"^(?<lead>[ \t]*(?:\[[^\]\r\n]*\][ \t]*)*)(?<decl>(?!public\b|private\b|internal\b|static\b|return\b|typedef\b|typealias\b)[A-Za-z_][\w:]*(?:[ \t]*<[^>\r\n]*>)?[ \t]+\w+[ \t]*(?:=[^;\r\n]*)?;)" )]
	private static partial Regex Field();

	/// <summary>
	/// A [Material] struct's body with its fields rewritten: textures as bindless slots, and every field public, since the
	/// engine's passes read the record and the generated class writes it. Only lines at the body's own depth are fields.
	/// </summary>
	static string Fields( string body )
	{
		var lines = body.Split( '\n' );
		var depth = 0;
		for ( int i = 0; i < lines.Length; i++ )
		{
			if ( depth == 0 )
			{
				var line = TextureField().Replace( lines[i], "${lead}MaterialTexture2D" );
				lines[i] = Field().Replace( line, "${lead}public ${decl}" );
			}

			var code = lines[i];
			var comment = code.IndexOf( "//", StringComparison.Ordinal );
			if ( comment >= 0 ) code = code[..comment];
			foreach ( var c in code )
			{
				if ( c == '{' ) depth++;
				else if ( c == '}' ) depth--;
			}
		}

		return string.Join( '\n', lines );
	}

	static int MatchingBrace( string text, int open )
	{
		var depth = 0;
		for ( int i = open; i < text.Length; i++ )
		{
			var c = text[i];
			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '/' )
			{
				i = text.IndexOf( '\n', i );
				if ( i < 0 ) break;
				continue;
			}

			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '*' )
			{
				i = text.IndexOf( "*/", i + 2, StringComparison.Ordinal );
				if ( i < 0 ) break;
				i++;
				continue;
			}

			if ( c == '{' ) depth++;
			else if ( c == '}' && --depth == 0 ) return i;
		}

		throw new ShaderCompileException( null, "A [Material] struct has no closing brace" );
	}
}

/// <summary>A <c>[Material]</c> struct's fields from Slang's reflection, laid out as a record.</summary>
internal static unsafe class MaterialReflection
{
	public static MaterialLayout Read( IntPtr reflection, string type, string path )
	{
		var name = System.Text.Encoding.UTF8.GetBytes( type + "\0" );
		IntPtr structType;
		fixed ( byte* n = name ) structType = SlangApi.FindTypeByName( reflection, n );
		if ( structType == IntPtr.Zero || SlangApi.TypeGetKind( structType ) != SlangApi.TypeKindStruct )
			throw new ShaderCompileException( path, $"The [Material] struct {type} isn't a struct Slang can reflect" );

		var fields = new List<MaterialField>();
		var count = SlangApi.TypeGetFieldCount( structType );
		for ( uint i = 0; i < count; i++ )
		{
			var field = SlangApi.TypeGetFieldByIndex( structType, i );
			var fieldName = SlangApi.String( SlangApi.VariableGetName( field ) );
			var fieldType = SlangApi.VariableGetType( field );
			var (kind, components) = Classify( fieldType ) ?? throw new ShaderCompileException( path, $"{type}.{fieldName}: material fields are scalars, vectors of up to four, or Texture2D" );

			string group = null;
			float? min = null, max = null;
			float[] value = null;
			var srgb = false;
			var opacity = false;
			var specialize = false;
			string source = null;
			SurfaceStateRule[] renderStates = null;
			var attributes = SlangApi.VariableGetUserAttributeCount( field );
			for ( uint a = 0; a < attributes; a++ )
			{
				var attribute = SlangApi.VariableGetUserAttribute( field, a );
				var attributeName = SlangApi.String( SlangApi.UserAttributeGetName( attribute ) );
				switch ( attributeName )
				{
					case "Group":
						group = Text( attribute, 0 );
						break;
					case "Range":
						min = Number( attribute, 0 );
						max = Number( attribute, 1 );
						break;
					case "Default" or "Default2" or "Default3" or "Default4":
						var n = (int)SlangApi.UserAttributeGetArgumentCount( attribute );
						value = new float[n];
						for ( uint k = 0; k < n; k++ ) value[k] = Number( attribute, k ) ?? 0;
						break;
					case "Srgb":
						srgb = true;
						break;
					case "Opacity":
						opacity = true;
						break;
					case "Specialize":
						specialize = true;
						break;
					case "Source":
						source = Text( attribute, 0 );
						break;
					case "RenderState":
						try
						{
							renderStates = [.. renderStates ?? [], .. SurfaceStateRule.Parse( Text( attribute, 0 ) ?? "" )];
						}
						catch ( FormatException e )
						{
							throw new ShaderCompileException( path, $"{type}.{fieldName}: [RenderState]: {e.Message}" );
						}

						break;
				}
			}

			if ( value is null && SlangApi.VariableHasDefaultValue( field ) != 0 )
			{
				float f;
				long l;
				if ( kind == MaterialFieldKind.Float && SlangApi.VariableGetDefaultValueFloat( field, &f ) >= 0 ) value = [f];
				else if ( kind != MaterialFieldKind.Float && SlangApi.VariableGetDefaultValueInt( field, &l ) >= 0 ) value = [l];
			}

			if ( renderStates is not null && (kind == MaterialFieldKind.Texture2D || components != 1) )
				throw new ShaderCompileException( path, $"{type}.{fieldName}: a [RenderState] field is a bool, int, uint or float" );
			fields.Add( new MaterialField( fieldName, kind, components, 0, value, group, min, max, srgb ) { Opacity = opacity, Source = source, RenderStates = renderStates, Specialize = specialize } );
		}

		return MaterialLayout.Create( type, fields, RenderState( structType, type, path ) );
	}

	/// <summary>The struct's render-state attributes (<c>[Blend( "Alpha" )]</c>, <c>[NoDepthWrite]</c>, ...) as defaults.</summary>
	static SurfaceRenderDefaults RenderState( IntPtr structType, string type, string path )
	{
		var state = SurfaceRenderDefaults.None;
		var count = SlangApi.TypeGetUserAttributeCount( structType );
		for ( uint a = 0; a < count; a++ )
		{
			var attribute = SlangApi.TypeGetUserAttribute( structType, a );
			switch ( SlangApi.String( SlangApi.UserAttributeGetName( attribute ) ) )
			{
				case "Blend":
					try
					{
						state = state with { Blend = SurfaceStateRule.ParseBlend( Text( attribute, 0 ) ?? "" ) };
					}
					catch ( FormatException e )
					{
						throw new ShaderCompileException( path, $"{type}: [Blend]: {e.Message}" );
					}

					break;
				case "DepthWrite": state = state with { DepthWrite = true }; break;
				case "NoDepthWrite": state = state with { DepthWrite = false }; break;
				case "NoDepthTest": state = state with { DepthTest = false }; break;
				case "DepthBias": state = state with { DepthBias = Number( attribute, 0 ) ?? 0, SlopeDepthBias = Number( attribute, 1 ) ?? 0 }; break;
				case "TwoSided": state = state with { TwoSided = true }; break;
				case "AlphaTest": state = state with { AlphaTest = true }; break;
				case "Translucent": state = state with { Translucent = true }; break;
			}
		}

		return state;
	}

	static (MaterialFieldKind Kind, int Components)? Classify( IntPtr type )
	{
		switch ( SlangApi.TypeGetKind( type ) )
		{
			case SlangApi.TypeKindScalar:
				return Scalar( SlangApi.TypeGetScalarType( type ) ) is { } s ? (s, 1) : null;
			case SlangApi.TypeKindVector:
				var count = (int)SlangApi.TypeGetElementCount( type );
				return count <= 4 && Scalar( SlangApi.TypeGetScalarType( SlangApi.TypeGetElementType( type ) ) ) is { } v ? (v, count) : null;
			case SlangApi.TypeKindStruct:
				return SlangApi.String( SlangApi.TypeGetName( type ) ) == "MaterialTexture2D" ? (MaterialFieldKind.Texture2D, 1) : null;
			default:
				return null;
		}
	}

	static MaterialFieldKind? Scalar( int scalar ) => scalar switch
	{
		SlangApi.ScalarFloat32 => MaterialFieldKind.Float,
		SlangApi.ScalarInt32 => MaterialFieldKind.Int,
		SlangApi.ScalarUInt32 => MaterialFieldKind.UInt,
		SlangApi.ScalarBool => MaterialFieldKind.Bool,
		_ => null,
	};

	static float? Number( IntPtr attribute, uint index )
	{
		float f;
		if ( SlangApi.UserAttributeGetArgumentValueFloat( attribute, index, &f ) >= 0 ) return f;
		int i;
		if ( SlangApi.UserAttributeGetArgumentValueInt( attribute, index, &i ) >= 0 ) return i;
		return null;
	}

	static string Text( IntPtr attribute, uint index )
	{
		nuint size;
		var text = SlangApi.UserAttributeGetArgumentValueString( attribute, index, &size );
		return text is null ? null : System.Text.Encoding.UTF8.GetString( text, (int)size ).Trim( '"' );
	}
}
