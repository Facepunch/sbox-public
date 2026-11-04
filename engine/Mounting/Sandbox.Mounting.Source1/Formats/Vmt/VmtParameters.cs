using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Sandbox.Mounting.Source1;

/// <summary>
/// Typed Source material values, independent of renderer parameter names.
/// </summary>
internal readonly struct VmtParameters( IReadOnlyDictionary<string, string> values, string path )
{
	private enum TransformOperation
	{
		Invalid,
		Center,
		Scale,
		Translate,
		Rotate
	}

	public string String( string key, string fallback = null ) => values.TryGetValue( key, out var value ) ? value : fallback;
	public bool Contains( string key ) => values.ContainsKey( key );

	internal float[] Components( string key )
	{
		if ( !values.TryGetValue( key, out var text ) ) return [];
		Span<float> components = stackalloc float[4];
		var count = ReadComponents( text, key, components, out var byteColor );
		if ( count is < 1 or > 4 ) throw Invalid( key, "expected one to four components" );
		var result = components[..count].ToArray();
		if ( byteColor )
			for ( var i = 0; i < result.Length; i++ ) result[i] /= 255;
		return result;
	}

	public float Float( string key, float fallback = 0 )
	{
		if ( !values.TryGetValue( key, out var value ) ) return fallback;
		var text = value.AsSpan().Trim();
		if ( text.StartsWith( "[" ) || text.StartsWith( "{" ) )
		{
			Span<float> components = stackalloc float[4];
			var count = ReadComponents( text, key, components, out var byteColor );
			if ( count is < 1 or > 4 )
				throw Invalid( key, "expected one to four scalar-conversion components" );
			return byteColor ? components[0] / 255 : components[0];
		}
		for ( var length = text.Length; length > 0; length-- )
		{
			if ( !float.TryParse( text[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed ) )
				continue;
			if ( !float.IsFinite( parsed ) )
				throw Invalid( key, $"invalid finite numeric value '{text}'" );
			if ( length != text.Length )
				Log.Trace( $"{path}: {key}: ignoring the nonnumeric suffix in '{text.ToString()}' as Source does." );
			return parsed;
		}
		Log.Trace( $"{path}: {key}: '{text.ToString()}' is not numeric; using Source's numeric conversion to zero." );
		return 0;
	}

	public int Integer( string key, int fallback = 0 )
	{
		var value = Float( key, fallback );
		if ( value < int.MinValue || value >= 2147483648.0f )
			throw Invalid( key, "expected an integer" );
		return (int)value;
	}

	public bool Boolean( string key, bool fallback = false )
	{
		if ( !values.TryGetValue( key, out var value ) ) return fallback;
		if ( bool.TryParse( value, out var boolean ) ) return boolean;
		return Float( key ) != 0;
	}

	public Vector3 Vector( string key, Vector3 fallback )
	{
		if ( !values.TryGetValue( key, out var text ) ) return fallback;
		if ( string.IsNullOrWhiteSpace( text ) )
		{
			Log.Trace( $"{path}: {key}: empty vector; using Source's numeric conversion to zero." );
			return Vector3.Zero;
		}
		Span<float> components = stackalloc float[3];
		components.Clear();
		var count = ReadComponents( text, key, components, out var byteColor );
		if ( count is < 1 or > 4 )
			throw Invalid( key, "expected one to four components" );
		var scalar = count == 1 && !text.AsSpan().TrimStart().StartsWith( "[" ) && !byteColor;
		var value = scalar ? new Vector3( components[0] ) : new Vector3( components[0], components[1], components[2] );
		return byteColor ? value / 255.0f : value;
	}

	public Vector2 Pair( string key, Vector2 fallback )
	{
		if ( !values.TryGetValue( key, out var text ) ) return fallback;
		Span<float> components = stackalloc float[4];
		var count = ReadComponents( text, key, components, out _ );
		if ( count < 2 || count > 4 )
			throw Invalid( key, "expected two to four components" );
		return new Vector2( components[0], components[1] );
	}

	public Matrix Transform( string key )
	{
		if ( !values.TryGetValue( key, out var text ) ) return Matrix.Identity;
		if ( text.AsSpan().Trim().StartsWith( "[" ) )
		{
			Span<float> components = stackalloc float[16];
			if ( ReadComponents( text, key, components, out _ ) != 16 )
				throw Invalid( key, "expected sixteen texture matrix components" );
			return new Matrix(
				components[0], components[4], 0, 0,
				components[1], components[5], 0, 0,
				0, 0, 1, 0,
				components[3], components[7], 0, 1 );
		}
		var remaining = text.AsSpan();
		if ( remaining.IsWhiteSpace() )
			throw Invalid( key, "empty texture transform" );
		var center = new Vector2( 0.5f );
		var scale = Vector2.One;
		var translation = Vector2.Zero;
		var rotation = 0.0f;
		while ( !remaining.IsWhiteSpace() )
		{
			var name = ReadToken( ref remaining );
			var operation = name.Equals( "center", StringComparison.OrdinalIgnoreCase ) ? TransformOperation.Center
				: name.Equals( "scale", StringComparison.OrdinalIgnoreCase ) ? TransformOperation.Scale
				: name.Equals( "translate", StringComparison.OrdinalIgnoreCase ) ? TransformOperation.Translate
				: name.Equals( "rotate", StringComparison.OrdinalIgnoreCase ) ? TransformOperation.Rotate : TransformOperation.Invalid;
			var xToken = ReadToken( ref remaining );
			var yToken = operation == TransformOperation.Rotate ? default : ReadToken( ref remaining );
			if ( operation == TransformOperation.Invalid || xToken.IsEmpty || (operation != TransformOperation.Rotate && yToken.IsEmpty) )
				throw Invalid( key, $"invalid texture transform operation '{name.ToString().ToLowerInvariant()}'" );
			var x = ParseFloat( xToken, key );
			var y = operation == TransformOperation.Rotate ? 0 : ParseFloat( yToken, key );
			switch ( operation )
			{
				case TransformOperation.Center: center = new Vector2( x, y ); break;
				case TransformOperation.Scale: scale = new Vector2( x, y ); break;
				case TransformOperation.Translate: translation = new Vector2( x, y ); break;
				case TransformOperation.Rotate: rotation = x; break;
			}
		}
		var matrix = Matrix.CreateTranslation( new Vector3( -center.x, -center.y, 0 ) )
			* Matrix.CreateScale( new Vector3( scale.x, scale.y, 1 ) )
			* Matrix.CreateRotationZ( rotation )
			* Matrix.CreateTranslation( new Vector3( center.x + translation.x, center.y + translation.y, 0 ) );
		if ( !float.IsFinite( matrix.M11 ) || !float.IsFinite( matrix.M12 )
			|| !float.IsFinite( matrix.M21 ) || !float.IsFinite( matrix.M22 )
			|| !float.IsFinite( matrix.M41 ) || !float.IsFinite( matrix.M42 ) )
			throw Invalid( key, "texture transform overflows" );
		return matrix;
	}

	private static ReadOnlySpan<char> ReadToken( ref ReadOnlySpan<char> text )
	{
		text = text.TrimStart();
		var length = 0;
		while ( length < text.Length && !char.IsWhiteSpace( text[length] ) )
			length++;
		var token = text[..length];
		text = text[length..];
		return token;
	}

	private int ReadComponents( ReadOnlySpan<char> text, string key, Span<float> components, out bool byteColor )
	{
		text = text.Trim();
		byteColor = !text.IsEmpty && text[0] == '{';
		if ( byteColor || (!text.IsEmpty && text[0] == '[') )
		{
			var terminated = text[^1] is '}' or ']';
			if ( !terminated )
				Log.Trace( $"{path}: {key}: unterminated vector; using the authored components." );
			text = terminated ? text[1..^1] : text[1..];
		}
		var count = 0;
		while ( !text.IsEmpty )
		{
			text = text.TrimStart( " \t\r\n," );
			if ( text.IsEmpty ) break;
			var length = text[0] is '+' or '-' ? 1 : 0;
			var digits = 0;
			while ( length < text.Length && char.IsAsciiDigit( text[length] ) ) { length++; digits++; }
			if ( length < text.Length && text[length] == '.' )
			{
				length++;
				while ( length < text.Length && char.IsAsciiDigit( text[length] ) ) { length++; digits++; }
			}
			if ( digits == 0 )
				throw Invalid( key, $"invalid finite numeric value '{text}'" );
			if ( length < text.Length && text[length] is 'e' or 'E' )
			{
				var exponent = length++;
				if ( length < text.Length && text[length] is '+' or '-' ) length++;
				var start = length;
				while ( length < text.Length && char.IsAsciiDigit( text[length] ) ) length++;
				if ( length == start ) length = exponent;
			}
			var token = text[..length];
			text = text[length..];
			var value = ParseFloat( token, key );
			if ( count < components.Length )
				components[count] = value;
			count++;
		}
		return count;
	}

	private float ParseFloat( ReadOnlySpan<char> text, string key )
	{
		if ( !float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) || !float.IsFinite( value ) )
			throw Invalid( key, $"invalid finite numeric value '{text}'" );
		return value;
	}

	private InvalidDataException Invalid( string key, string reason ) => new( $"{path}: {key}: {reason}." );
}
