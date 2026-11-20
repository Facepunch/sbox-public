using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// A <c>.vmat</c> compiled for a shader only a module describes, as the material compiler compiles one for a <c>.shader</c>:
/// its parameters as the material sets them (features and ints, floats, vectors), its attributes, and the textures made
/// from its input images by the module's declarations (<see cref="MaterialInputs"/>), each a generated <c>.vtex</c> whose
/// definition is the <c>CDmeVtex</c> the material compiler writes for it. The material's data is KV3 text in the layout of
/// <c>MaterialResourceData_t</c>.
/// </summary>
public sealed class MaterialCompile
{
	/// <summary>A texture made for the material: its generated <c>.vtex</c> (relative), and the definition it compiles from.</summary>
	public sealed record GeneratedTexture( string Path, string Definition );

	/// <summary>The material's data, KV3 text without a header (<c>MaterialResourceData_t</c>).</summary>
	public string Data { get; init; }

	/// <summary>Textures to compile as children of the material, before it's used.</summary>
	public IReadOnlyList<GeneratedTexture> Generated { get; init; }

	/// <summary>Every texture the material references (<c>.vtex</c>, relative): what it loads with it.</summary>
	public IReadOnlyList<string> Textures { get; init; }

	/// <summary>Compiled textures the material names directly (<c>g_tColor "x.vtex_c"</c>), by their source (<c>.vtex</c>): read before it compiles.</summary>
	public IReadOnlyList<string> Prerequisites { get; init; }

	/// <summary>
	/// An image's width and height from its file (PNG, JPEG, TGA, PSD, EXR), as the material compiler reads a representative
	/// texture's; null for anything else.
	/// </summary>
	public static (int Width, int Height)? ImageSize( ReadOnlySpan<byte> data )
	{
		static int Be16( ReadOnlySpan<byte> d, int at ) => (d[at] << 8) | d[at + 1];
		static int Be32( ReadOnlySpan<byte> d, int at ) => (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3];
		static int Le16( ReadOnlySpan<byte> d, int at ) => d[at] | (d[at + 1] << 8);
		static int Le32( ReadOnlySpan<byte> d, int at ) => d[at] | (d[at + 1] << 8) | (d[at + 2] << 16) | (d[at + 3] << 24);

		// PNG: IHDR first
		if ( data.Length >= 24 && data[..8].SequenceEqual( (ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] ) ) return (Be32( data, 16 ), Be32( data, 20 ));

		// PSD: 8BPS, height then width
		if ( data.Length >= 22 && data[..4].SequenceEqual( "8BPS"u8 ) ) return (Be32( data, 18 ), Be32( data, 14 ));

		// JPEG: the first start-of-frame segment
		if ( data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 )
		{
			var at = 2;
			while ( at + 9 < data.Length )
			{
				if ( data[at] != 0xFF ) return null;
				var marker = data[at + 1];
				if ( marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC ) return (Be16( data, at + 7 ), Be16( data, at + 5 ));
				at += 2 + Be16( data, at + 2 );
			}

			return null;
		}

		// OpenEXR: the dataWindow attribute (box2i: xMin, yMin, xMax, yMax)
		if ( data.Length >= 8 && Le32( data, 0 ) == 20000630 )
		{
			var at = 8;
			while ( at < data.Length && data[at] != 0 )
			{
				var name = ReadName( data, ref at );
				var type = ReadName( data, ref at );
				if ( at + 4 > data.Length ) return null;
				var size = Le32( data, at );
				at += 4;
				if ( name == "dataWindow" && type == "box2i" && at + 16 <= data.Length ) return (Le32( data, at + 8 ) - Le32( data, at ) + 1, Le32( data, at + 12 ) - Le32( data, at + 4 ) + 1);
				at += size;
			}

			return null;
		}

		// TGA has no signature: an uncompressed or RLE colour, grey or mapped image type, sizes at 12 and 14
		if ( data.Length >= 18 && data[2] is 1 or 2 or 3 or 9 or 10 or 11 ) return (Le16( data, 12 ), Le16( data, 14 ));
		return null;

		static string ReadName( ReadOnlySpan<byte> d, ref int at )
		{
			var end = d[at..].IndexOf( (byte)0 );
			if ( end < 0 ) { at = d.Length; return ""; }
			var name = Encoding.ASCII.GetString( d.Slice( at, end ) );
			at += end + 1;
			return name;
		}
	}

	/// <summary>
	/// The shader a material names, as the material compiler writes it: <c>shaders/x.shader</c>, from <c>x.vfx</c>,
	/// <c>shaders/x.shader_c</c> or a bare name.
	/// </summary>
	public static string ShaderName( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) ) return null;
		var name = value.Trim().Replace( '\\', '/' );
		if ( name.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) name = name[..^2];
		var dot = name.LastIndexOf( '.' );
		if ( dot > name.LastIndexOf( '/' ) ) name = name[..dot];
		if ( !name.Contains( '/' ) ) name = "shaders/" + name;
		return name + ".shader";
	}

	/// <summary>
	/// Compile <paramref name="material"/> (at <paramref name="materialPath"/>, relative) for <paramref name="shader"/>
	/// with the module's <paramref name="inputs"/> for its features. <paramref name="fileExists"/> says whether a relative
	/// file is in the content (the default input images); <paramref name="imageSize"/> is a relative image's width and
	/// height, or null (<see cref="ImageSize"/> of its file's start).
	/// </summary>
	public static MaterialCompile Compile( VmatSource material, string materialPath, string shader, MaterialInputs inputs, Func<string, bool> fileExists, Func<string, (int Width, int Height)?> imageSize = null )
	{
		materialPath = materialPath.Replace( '\\', '/' );
		var ints = new List<(string, int)>();
		var floats = new List<(string, float)>();
		var vectors = new List<(string, float[])>();
		foreach ( var (key, value) in material.Values )
		{
			if ( key.Equals( "shader", StringComparison.OrdinalIgnoreCase ) ) continue;
			switch ( VmatSource.TypeOf( value ) )
			{
				case VmatSource.Kind.Int: ints.Add( (key, int.Parse( value, CultureInfo.InvariantCulture )) ); break;
				case VmatSource.Kind.Float: floats.Add( (key, VmatSource.Float( value )) ); break;
				case VmatSource.Kind.Vector: vectors.Add( (key, VmatSource.Vector( value )) ); break;
			}
		}

		// Textures: a compiled one the material names for the variable, else one made from the inputs
		var textures = new List<(string Name, string Path)>();
		var generated = new List<GeneratedTexture>();
		var prerequisites = new List<string>();
		foreach ( var texture in inputs.Textures )
		{
			if ( material.Value( texture.Name ) is { } named && named.EndsWith( ".vtex_c", StringComparison.OrdinalIgnoreCase ) )
			{
				var source = named.Replace( '\\', '/' )[..^2];
				textures.Add( (texture.Name, source) );
				prerequisites.Add( source );
				continue;
			}

			var made = Generate( material, materialPath, texture, inputs, fileExists );
			if ( made is null ) continue;
			generated.Add( made );
			textures.Add( (texture.Name, made.Path) );
		}

		// Attributes: the material's own, then the shader's with a literal value, as native merges the shader's
		var intAttributes = new List<(string, int)>();
		var floatAttributes = new List<(string, float)>();
		var vectorAttributes = new List<(string, float[])>();
		var stringAttributes = new List<(string, string)>();
		foreach ( var (block, system) in new[] { (material.Attributes, false), (material.SystemAttributes, true) } )
		{
			foreach ( var (key, value) in block )
			{
				switch ( VmatSource.TypeOf( value ) )
				{
					case VmatSource.Kind.Int:
						intAttributes.Add( (key, int.Parse( value, CultureInfo.InvariantCulture )) );
						if ( !system ) vectorAttributes.Add( (key, [.. Enumerable.Repeat( (float)int.Parse( value, CultureInfo.InvariantCulture ), 4 )]) );
						break;
					case VmatSource.Kind.Float when !system: floatAttributes.Add( (key, VmatSource.Float( value )) ); break;
					case VmatSource.Kind.Vector when !system: vectorAttributes.Add( (key, VmatSource.Vector( value )) ); break;
					case VmatSource.Kind.String when !system: stringAttributes.Add( (key, value) ); break;
				}
			}
		}

		foreach ( var (name, kind, value) in inputs.Attributes )
		{
			if ( intAttributes.Exists( a => a.Item1.Equals( name, StringComparison.OrdinalIgnoreCase ) ) || floatAttributes.Exists( a => a.Item1.Equals( name, StringComparison.OrdinalIgnoreCase ) ) ) continue;
			if ( kind == "Bool" ) intAttributes.Add( (name, (int)value[0]) );
			else if ( kind == "Float" ) floatAttributes.Add( (name, value[0]) );
		}

		// The representative texture's first input's size, as the material compiler records it: a colour is one texel
		if ( inputs.RepresentativeTexture is { } representative && inputs.Textures.FirstOrDefault( t => t.Name == representative ) is { Channels: [{ Inputs: [var first, ..] }, ..] }
			&& material.Value( first ) is { Length: > 0 } file
			&& (file[0] == '[' ? (1, 1) : imageSize?.Invoke( file.Replace( '\\', '/' ) )) is var (width, height) )
		{
			intAttributes.Add( ("RepresentativeTextureWidth", width) );
			intAttributes.Add( ("RepresentativeTextureHeight", height) );
		}

		var kv = new StringBuilder();
		kv.Append( "{\n" );
		kv.Append( $"\tm_materialName = {Quote( materialPath )}\n" );
		kv.Append( $"\tm_shaderName = {Quote( shader )}\n" );
		List( kv, "m_intParams", ints, v => $"m_nValue = {v.ToString( CultureInfo.InvariantCulture )}" );
		List( kv, "m_floatParams", floats, v => $"m_flValue = {Number( v )}" );
		List( kv, "m_vectorParams", vectors, v => $"m_value = {VectorText( v )}" );
		List( kv, "m_textureParams", textures, v => $"m_pValue = resource:{Quote( v )}" );
		List( kv, "m_intAttributes", intAttributes, v => $"m_nValue = {v.ToString( CultureInfo.InvariantCulture )}" );
		List( kv, "m_floatAttributes", floatAttributes, v => $"m_flValue = {Number( v )}" );
		List( kv, "m_vectorAttributes", vectorAttributes, v => $"m_value = {VectorText( v )}" );
		List<string>( kv, "m_textureAttributes", [], v => "" );
		List( kv, "m_stringAttributes", stringAttributes, v => $"m_value = {Quote( v )}" );
		kv.Append( "\tm_renderAttributesUsed = [  ]\n" );
		kv.Append( "\tm_shaderVersion = 0\n" );
		kv.Append( "}\n" );

		return new MaterialCompile
		{
			Data = kv.ToString(),
			Generated = generated,
			Textures = [.. textures.Select( t => t.Path ).Distinct( StringComparer.OrdinalIgnoreCase )],
			Prerequisites = prerequisites,
		};
	}

	/// <summary>
	/// The texture made from inputs for <paramref name="texture"/>: each input the material names, else the default file the
	/// shader names, else <c>materials/default/default_ending.ext</c>, else the input's default colour. Named as the
	/// material compiler names them: after the first input that's an artist's image, with a hash of the definition.
	/// </summary>
	static GeneratedTexture Generate( VmatSource material, string materialPath, MaterialInputs.Texture texture, MaterialInputs inputs, Func<string, bool> fileExists )
	{
		var files = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( var channel in texture.Channels )
		{
			foreach ( var name in channel.Inputs )
			{
				if ( files.ContainsKey( name ) || !inputs.Inputs.TryGetValue( name, out var input ) ) continue;
				files[name] = InputFile( material.Value( name ), input, fileExists );
			}
		}

		if ( files.Count == 0 ) return null;

		var dmx = new StringBuilder();
		dmx.Append( "<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->\n\"CDmeVtex\"\n{\n" );
		dmx.Append( "\t\"m_inputTextureArray\" \"element_array\"\n\t[\n" );
		var first = true;
		foreach ( var channel in texture.Channels )
		{
			foreach ( var name in channel.Inputs )
			{
				var input = inputs.Inputs[name];
				if ( !first ) dmx.Append( ",\n" );
				first = false;
				dmx.Append( "\t\t\"CDmeInputTexture\"\n\t\t{\n" );
				Field( dmx, 3, "m_name", "string", name );
				Field( dmx, 3, "m_fileName", "string", files[name] );
				Field( dmx, 3, "m_colorSpace", "string", input.Srgb ? "srgb" : "linear" );
				Field( dmx, 3, "m_fileExt", "string", input.FileEnding.ToLowerInvariant() );
				Field( dmx, 3, "m_nMinBitsPerChannel", "int", input.MinBits.ToString( CultureInfo.InvariantCulture ) );
				Field( dmx, 3, "m_typeString", "string", input.Type );
				dmx.Append( "\t\t\t\"m_imageProcessorArray\" \"element_array\"\n\t\t\t[\n\t\t\t\t\"CDmeImageProcessor\"\n\t\t\t\t{\n" );
				Field( dmx, 5, "m_algorithm", "string", input.Processor );
				Field( dmx, 5, "m_stringArg", "string", "" );
				Field( dmx, 5, "m_vFloat4Arg", "vector4", "0 0 0 0" );
				dmx.Append( "\t\t\t\t}\n\t\t\t]\n\t\t}" );
			}
		}

		dmx.Append( "\n\t]\n" );
		Field( dmx, 1, "m_outputTypeString", "string", texture.Type );
		Field( dmx, 1, "m_outputFormat", "string", texture.Format );
		Field( dmx, 1, "m_outputClearColor", "vector4", "0 0 0 0" );
		Field( dmx, 1, "m_nOutputMinDimension", "int", "0" );
		Field( dmx, 1, "m_nOutputMaxDimension", "int", "0" );
		dmx.Append( "\t\"m_textureOutputChannelArray\" \"element_array\"\n\t[\n" );
		for ( int c = 0; c < texture.Channels.Count; c++ )
		{
			var channel = texture.Channels[c];
			dmx.Append( "\t\t\"CDmeTextureOutputChannel\"\n\t\t{\n\t\t\t\"m_inputTextureArray\" \"string_array\"\n\t\t\t[\n" );
			dmx.Append( string.Join( ",\n", channel.Inputs.Select( i => $"\t\t\t\t{Quote( i )}" ) ) );
			dmx.Append( "\n\t\t\t]\n" );
			Field( dmx, 3, "m_srcChannels", "string", "rgba"[..Math.Min( channel.Channels.Length, 4 )] );
			Field( dmx, 3, "m_dstChannels", "string", channel.Channels );
			dmx.Append( "\t\t\t\"m_mipAlgorithm\" \"CDmeImageProcessor\"\n\t\t\t{\n" );
			Field( dmx, 4, "m_algorithm", "string", channel.Algorithm );
			Field( dmx, 4, "m_stringArg", "string", "" );
			Field( dmx, 4, "m_vFloat4Arg", "vector4", "0 0 0 0" );
			dmx.Append( "\t\t\t}\n" );
			Field( dmx, 3, "m_outputColorSpace", "string", channel.Srgb ? "srgb" : "linear" );
			dmx.Append( c + 1 < texture.Channels.Count ? "\t\t},\n" : "\t\t}\n" );
		}

		dmx.Append( "\t]\n" );
		Field( dmx, 1, "m_vClamp", "vector3", texture.Type == "CUBE" ? "1 1 1" : "0 0 0" );
		Field( dmx, 1, "m_bNoLod", "bool", "0" );
		dmx.Append( "}\n" );
		var definition = dmx.ToString();

		// Named after its first input, preferring an artist's image to a colour and a colour to a default image, as the
		// material compiler names it; a colour's after the material and the variable
		var inputFiles = texture.Channels.SelectMany( c => c.Inputs ).Select( n => files[n] ).ToList();
		var basePath = inputFiles[0];
		foreach ( var file in inputFiles.Skip( 1 ) )
		{
			if ( IsDefault( basePath ) ) { if ( !IsDefault( file ) ) basePath = file; continue; }
			if ( basePath[0] == '[' ) { if ( file[0] != '[' && !IsDefault( file ) ) basePath = file; continue; }
			break;
		}

		if ( basePath[0] == '[' ) basePath = $"{materialPath}_{texture.Name}";
		var slash = basePath.LastIndexOf( '/' ) + 1;
		var stem = basePath[..slash] + basePath[slash..].Replace( '.', '_' );
		var hash = Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( definition ) ), 0, 4 ).ToLowerInvariant();
		var path = $"{stem.ToLowerInvariant()}_{hash}.generated.vtex";
		return new GeneratedTexture( path, definition );
	}

	static bool IsDefault( string file ) => file.Replace( '\\', '/' ).Contains( "materials/default/", StringComparison.OrdinalIgnoreCase );

	/// <summary>An input's file: the material's, the shader's default file, a default image for its ending, or its default colour.</summary>
	static string InputFile( string value, MaterialInputs.Input input, Func<string, bool> fileExists )
	{
		if ( !string.IsNullOrWhiteSpace( value ) ) return value.Replace( '\\', '/' );
		if ( !string.IsNullOrEmpty( input.DefaultFile ) ) return input.DefaultFile;

		foreach ( var extension in new[] { "psd", "tga", "tif", "pfm", "exr" } )
		{
			var candidate = input.FileEnding.Length > 0 ? $"materials/default/default_{input.FileEnding.ToLowerInvariant()}.{extension}" : $"materials/default/default.{extension}";
			if ( fileExists( candidate ) ) return candidate;
		}

		return string.Create( CultureInfo.InvariantCulture, $"[{input.Default[0]:F6} {input.Default[1]:F6} {input.Default[2]:F6} {input.Default[3]:F6}]" );
	}

	static void Field( StringBuilder text, int indent, string name, string type, string value ) =>
		text.Append( '\t', indent ).Append( Quote( name ) ).Append( ' ' ).Append( Quote( type ) ).Append( ' ' ).Append( Quote( value ) ).Append( '\n' );

	static void List<T>( StringBuilder text, string name, IReadOnlyList<(string Name, T Value)> items, Func<T, string> value )
	{
		text.Append( '\t' ).Append( name ).Append( " = [" );
		for ( int i = 0; i < items.Count; i++ )
			text.Append( i == 0 ? " " : ", " ).Append( "{ m_name = " ).Append( Quote( items[i].Name ) ).Append( ' ' ).Append( value( items[i].Value ) ).Append( " }" );
		text.Append( " ]\n" );
	}

	static string Number( float value ) => value.ToString( "R", CultureInfo.InvariantCulture ) is var s && (s.Contains( '.' ) || s.Contains( 'E' )) ? s : s + ".0";

	static string VectorText( float[] value ) => $"[ {string.Join( ", ", Enumerable.Range( 0, 4 ).Select( i => Number( i < value.Length ? value[i] : 0 ) ) )} ]";

	static string Quote( string text ) => "\"" + text.Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" ) + "\"";
}

/// <summary>
/// A <c>.vmat</c>'s text (KeyValues, <c>Layer0 { shader "..." F_X 1 g_flY "0.5" Attributes { ... } }</c>): its values in
/// order and its attribute blocks. A value's type is the one KeyValues gives it: an int or float when the whole of it
/// reads as one, a vector when it starts with <c>[</c>, else a string.
/// </summary>
public sealed class VmatSource
{
	/// <summary>The values in the material's block, in order (not the attribute blocks).</summary>
	public IReadOnlyList<(string Key, string Value)> Values { get; init; } = [];

	/// <summary><c>Attributes { }</c>.</summary>
	public IReadOnlyList<(string Key, string Value)> Attributes { get; init; } = [];

	/// <summary><c>SystemAttributes { }</c>.</summary>
	public IReadOnlyList<(string Key, string Value)> SystemAttributes { get; init; } = [];

	/// <summary>The shader it names, as written.</summary>
	public string Shader => Value( "shader" );

	/// <summary>A value by key (case-insensitive), or null.</summary>
	public string Value( string key ) => Values.FirstOrDefault( v => v.Key.Equals( key, StringComparison.OrdinalIgnoreCase ) ).Value;

	/// <summary>The feature values it sets (<c>F_*</c> ints).</summary>
	public IReadOnlyDictionary<string, int> Features => Values
		.Where( v => v.Key.StartsWith( "F_", StringComparison.Ordinal ) && TypeOf( v.Value ) == Kind.Int )
		.GroupBy( v => v.Key ).ToDictionary( g => g.Key, g => int.Parse( g.Last().Value, CultureInfo.InvariantCulture ) );

	/// <summary>A value's type, as KeyValues reads it.</summary>
	public enum Kind
	{
		/// <summary>Anything else: a file, a name.</summary>
		String,
		/// <summary>The whole of it an integer.</summary>
		Int,
		/// <summary>The whole of it a number.</summary>
		Float,
		/// <summary><c>[x y z w]</c>.</summary>
		Vector,
	}

	/// <summary>A value's type as KeyValues reads it.</summary>
	public static Kind TypeOf( string value )
	{
		if ( string.IsNullOrEmpty( value ) ) return Kind.String;
		if ( value[0] == '[' ) return Kind.Vector;
		if ( long.TryParse( value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l ) && l is >= int.MinValue and <= int.MaxValue ) return Kind.Int;
		if ( double.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out _ ) && !value.Any( char.IsWhiteSpace ) ) return Kind.Float;
		return Kind.String;
	}

	internal static float Float( string value ) => (float)double.Parse( value, NumberStyles.Float, CultureInfo.InvariantCulture );

	/// <summary><c>[x y z w]</c>, missing components zero.</summary>
	internal static float[] Vector( string value )
	{
		var result = new float[4];
		var parts = value.Trim( '[', ']', ' ' ).Split( (char[])[' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries );
		for ( int i = 0; i < Math.Min( 4, parts.Length ); i++ )
			result[i] = float.TryParse( parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f ) ? f : 0;
		return result;
	}

	/// <summary>Read a material's text.</summary>
	public static VmatSource Parse( string text )
	{
		var tokens = Tokens( text ).ToList();
		var at = 0;

		// The root block (Layer0, or none)
		if ( at + 1 < tokens.Count && tokens[at + 1] == "{" && tokens[at] != "{" ) at += 2;
		else if ( at < tokens.Count && tokens[at] == "{" ) at++;

		var values = new List<(string, string)>();
		var attributes = new List<(string, string)>();
		var system = new List<(string, string)>();
		while ( at < tokens.Count && tokens[at] != "}" )
		{
			var key = tokens[at++];
			if ( at >= tokens.Count ) break;
			if ( tokens[at] == "{" )
			{
				at++;
				var block = key.Equals( "Attributes", StringComparison.OrdinalIgnoreCase ) ? attributes : key.Equals( "SystemAttributes", StringComparison.OrdinalIgnoreCase ) ? system : null;
				var depth = 1;
				while ( at < tokens.Count && depth > 0 )
				{
					if ( tokens[at] == "{" ) { depth++; at++; continue; }
					if ( tokens[at] == "}" ) { depth--; at++; continue; }
					if ( depth == 1 && at + 1 < tokens.Count && tokens[at + 1] != "{" && tokens[at + 1] != "}" )
					{
						block?.Add( (tokens[at], tokens[at + 1]) );
						at += 2;
						continue;
					}

					at++;
				}

				continue;
			}

			values.Add( (key, tokens[at++]) );
		}

		return new VmatSource { Values = values, Attributes = attributes, SystemAttributes = system };
	}

	/// <summary>KeyValues tokens: quoted strings, bare words, braces; <c>//</c> comments skipped.</summary>
	static IEnumerable<string> Tokens( string text )
	{
		var i = 0;
		while ( i < text.Length )
		{
			var c = text[i];
			if ( char.IsWhiteSpace( c ) ) { i++; continue; }
			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '/' )
			{
				while ( i < text.Length && text[i] != '\n' ) i++;
				continue;
			}

			if ( c is '{' or '}' )
			{
				yield return c.ToString();
				i++;
				continue;
			}

			if ( c == '"' )
			{
				var end = i + 1;
				var token = new StringBuilder();
				while ( end < text.Length && text[end] != '"' )
				{
					token.Append( text[end++] );
				}

				yield return token.ToString();
				i = end + 1;
				continue;
			}

			var start = i;
			while ( i < text.Length && !char.IsWhiteSpace( text[i] ) && text[i] is not ('{' or '}' or '"') ) i++;
			yield return text[start..i];
		}
	}
}
