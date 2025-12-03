using Sandbox.Hashing;
using System.Data;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Sandbox;

public static partial class SandboxSystemExtensions
{
	/// <summary>
	/// Puts quote marks around a string. Internal quotes are backslashed.
	/// </summary>
	public static string QuoteSafe( this string str, bool optional = false )
	{
		if ( string.IsNullOrEmpty( str ) )
			return $"\"\"";

		// If it's optional we don't need quotes unless there are characters we're not expecting
		if ( optional && Regex.IsMatch( str, @"^[a-zA-Z0-9\:_\-\.\+\-\\\/\@]+$" ) )
			return str;

		str = str.Replace( "\"", "\\\"" ).TrimEnd( '\\' );
		return "\"" + str + "\"";
	}

	/// <inheritdoc cref="WebUtility.HtmlEncode(string)"/>
	public static string HtmlEncode( this string str )
	{
		return WebUtility.HtmlEncode( str );
	}

	/// <inheritdoc cref="WebUtility.UrlEncode(string)"/>
	public static string UrlEncode( this string str )
	{
		return WebUtility.UrlEncode( str );
	}

	[GeneratedRegex( "\\s+" )]
	private static partial Regex CollapseWhiteSpaceRegex();

	/// <summary>
	/// Collapse sequences of whitespace into a single whitespace
	/// </summary>
	/// <param name="str"></param>
	/// <returns></returns>
	public static string CollapseWhiteSpace( this string str )
	{
		if ( string.IsNullOrEmpty( str ) ) return str;

		str = CollapseWhiteSpaceRegex().Replace( str, " " );
		str = str.Trim();

		return str;
	}

	[GeneratedRegex( "[ \t]+" )]
	private static partial Regex CollapseSpacesAndTabsRegex();
	[GeneratedRegex( "(?<=\\n|\\u2029)[ \\t]+|[ \\t]+(?=\\n|\\u2029)" )]
	private static partial Regex RemoveSpacesAroundLineBreaksRegex();

	/// <summary>
	/// Collapse sequences of spaces and tabs into a single space, preserving newlines
	/// </summary>
	/// <param name="str"></param>
	/// <returns></returns>
	public static string CollapseSpacesAndPreserveLines( this string str )
	{
		if ( string.IsNullOrEmpty( str ) ) return str;

		str = CollapseSpacesAndTabsRegex().Replace( str, " " );
		str = RemoveSpacesAroundLineBreaksRegex().Replace( str, "" );

		return str.Trim();
	}

	/// <summary>
	/// Puts a filename into the format /path/filename.ext (from path\FileName.EXT)
	/// </summary>
	public static string NormalizeFilename( this string str, bool enforceInitialSlash = true ) => NormalizeFilename( str, enforceInitialSlash, true );

	/// <summary>
	/// Puts a filename into the format /path/filename.ext (from path\FileName.EXT)
	/// </summary>
	public static string NormalizeFilename( this string str, bool enforceInitialSlash, bool enforceLowerCase )
	{
		if ( enforceLowerCase )
		{
			str = str.ToLowerInvariant();
		}

		str = str.Replace( '\\', '/' );

		if ( enforceInitialSlash && !str.StartsWith( '/' ) )
			str = str.Insert( 0, "/" );

		return str;
	}

	/// <summary>
	/// Adds or replaces the extension of <paramref name="path"/> to <paramref name="ext"/>.
	/// </summary>
	/// <param name="path">A file path with or without an extension.</param>
	/// <param name="ext">A file extension with or without a leading period.</param>
	/// <returns></returns>
	public static string WithExtension( this string path, string ext )
	{
		ArgumentNullException.ThrowIfNull( path, nameof( path ) );
		ArgumentNullException.ThrowIfNull( ext, nameof( ext ) );

		if ( !ext.StartsWith( '.' ) ) ext = $".{ext}";

		var curExt = Path.GetExtension( path );

		if ( string.Equals( curExt, ext, StringComparison.OrdinalIgnoreCase ) )
		{
			return path;
		}

		return $"{path[..^curExt.Length]}{ext}";
	}

	static Regex simplifyregex = new Regex( @"[^\\/]+(?<!\.\.)[\\/]\.\.[\\/]", RegexOptions.Compiled );

	/// <summary>
	/// Gets rid of ../'s (from /path/folder/../file.txt to /path/file.txt)
	/// </summary>
	/// <param name="str"></param>
	/// <returns></returns>
	public static string SimplifyPath( this string str )
	{
		while ( true )
		{
			var newPath = simplifyregex.Replace( str, "" );
			if ( newPath == str ) break;
			str = newPath;
		}
		return str;
	}


	static Regex splitregex = new Regex( "\"(?<1>[^\"]+)?\"|'(?<1>[^']+)?'|(?<1>\\S+)", RegexOptions.Compiled );

	/// <summary>
	/// in  : I am "splitting a" string "because it's fun "
	/// out : ["I", "am", "splitting a", "string", "because it's fun"]
	/// </summary>
	public static string[] SplitQuotesStrings( this string input )
	{
		// Hide backslashed quotes - so we can retain them
		input = input.Replace( "\\\"", "&qute;" );

		MatchCollection collection = splitregex.Matches( input );

		string[] strArray = new string[collection.Count];
		for ( int i = 0; i < collection.Count; i++ )
		{
			strArray[i] = collection[i].Groups[1].Value;//.Trim( new char[] { ' ', '"' } );
			strArray[i] = strArray[i].Replace( "&qute;", "\"" );
		}

		return strArray;
	}

	#region To Number

	/// <summary>
	/// Convert to <see cref="float"/>, if not then return <paramref name="Default"/>.
	/// </summary>
	public static float ToFloat( this string str, float Default = 0 )
	{
		if ( float.TryParse( str, NumberStyles.Float, null, out var res ) )
			return res;

		return Default;
	}

	/// <summary>
	/// Convert to <see cref="float"/>. Might be a string formula. This is always going to be slower than a call to <see cref="ToFloat"/>.
	/// </summary>
	public static float ToFloatEval( this string expression, float Default = 0 )
	{
		try
		{
			var table = new DataTable();
			var value = table.Compute( expression, string.Empty );
			return Convert.ToSingle( value );
		}
		catch ( System.Exception )
		{
			return Default;
		}
	}

	/// <summary>
	/// Convert to <see cref="double"/>, if not then return <paramref name="Default"/>.
	/// </summary>
	public static float ToDouble( this string str, float Default = 0 )
	{
		if ( float.TryParse( str, NumberStyles.Float, null, out var res ) )
			return res;

		return Default;
	}

	/// <summary>
	/// Convert to <see cref="double"/>. Might be a string formula. This is always going to be slower than a call to <see cref="ToDouble"/>.
	/// </summary>
	public static double ToDoubleEval( this string expression, double Default = 0 )
	{
		try
		{
			var table = new DataTable();
			var value = table.Compute( expression, string.Empty );
			return Convert.ToDouble( value );
		}
		catch ( System.Exception )
		{
			return Default;
		}
	}

	/// <summary>
	/// 128-bit data type that returns sane results for almost any input.
	/// All other numeric types can cast from this.
	/// </summary>
	public static decimal ToDecimal( this string str, decimal Default = 0 )
	{
		if ( decimal.TryParse( str, out var res ) )
			return res;
		return Default;
	}

	/// <summary>
	/// Convert to <see cref="uint"/>, if not then return <paramref name="Default"/>.
	/// </summary>
	public static uint ToUInt( this string str, int Default = 0 )
	{
		const decimal min = (decimal)uint.MinValue;
		const decimal max = (decimal)uint.MaxValue;

		decimal num = str.ToDecimal( Default );

		return num <= min ? uint.MinValue : num >= max ? uint.MaxValue : (uint)num;
	}

	/// <summary>
	/// Convert to <see cref="int"/>, if not then return <paramref name="Default"/>.
	/// </summary>
	public static int ToInt( this string str, int Default = 0 )
	{
		const decimal min = (decimal)int.MinValue;
		const decimal max = (decimal)int.MaxValue;

		decimal num = str.ToDecimal( Default );

		return num <= min ? int.MinValue : num >= max ? int.MaxValue : (int)num;
	}

	/// <summary>
	/// Convert to <see cref="int"/>. Might be a string formula. This is always going to be slower than a call to <see cref="ToInt"/>.
	/// </summary>
	public static int ToIntEval( this string expression, int Default = 0 )
	{
		try
		{
			var table = new DataTable();
			var value = table.Compute( expression, string.Empty );
			return Convert.ToInt32( value );
		}
		catch ( System.Exception )
		{
			return Default;
		}
	}

	/// <summary>
	/// Convert to <see cref="ulong"/>, if not then return <paramref name="Default"/>.
	/// </summary>
	public static ulong ToULong( this string str, ulong Default = 0 )
	{
		if ( ulong.TryParse( str, out var t ) )
			return t;

		return Default;
	}

	/// <summary>
	/// Convert to <see cref="long"/>, if not then return <paramref name="Default"/>.
	/// </summary>
	public static long ToLong( this string str, long Default = 0 )
	{
		if ( long.TryParse( str, out var t ) )
			return t;

		return Default;
	}

	/// <summary>
	/// Convert to <see cref="long"/>. Might be a string formula. This is always going to be slower than a call to <see cref="ToLong"/>.
	/// </summary>
	public static long ToLongEval( this string expression, long Default = 0 )
	{
		try
		{
			var table = new DataTable();
			var value = table.Compute( expression, string.Empty );
			return Convert.ToInt64( value );
		}
		catch ( System.Exception )
		{
			return Default;
		}
	}

	#endregion

	/// <summary>
	/// Try to convert to bool. Inputs can be true, false, yes, no, 0, 1, null (caps insensitive)
	/// </summary>
	public static bool ToBool( this string str )
	{
		if ( str == null ) return false;
		if ( str.Length == 0 ) return false;
		if ( str == "0" ) return false;
		if ( char.IsDigit( str[0] ) && str[0] != '0' ) return true; // a non zero digit is always going to be true
		if ( str.Equals( "false", StringComparison.OrdinalIgnoreCase ) ) return false;
		if ( str.Equals( "no", StringComparison.OrdinalIgnoreCase ) ) return false;
		if ( str.Equals( "null", StringComparison.OrdinalIgnoreCase ) ) return false;

		if ( float.TryParse( str, out float f ) )
			return f != 0;

		return true;
	}

	/// <summary>
	/// If the string is longer than this amount of characters then truncate it
	/// If appendage is defined, it will be appended to the end of truncated strings (ie, "..")
	/// </summary>
	public static string Truncate( this string str, int maxLength, string appendage = null )
	{
		if ( string.IsNullOrEmpty( str ) ) return str;
		if ( str.Length <= maxLength ) return str;

		if ( appendage != null )
			maxLength -= appendage.Length;

		str = str.Substring( 0, maxLength );

		if ( appendage == null )
			return str;

		return string.Concat( str, appendage );
	}

	private static char[] FilenameDelim = new[] { '/', '\\' };

	/// <summary>
	/// If the string is longer than this amount of characters then truncate it
	/// If appendage is defined, it will be appended to the end of truncated strings (ie, "..")
	/// </summary>
	public static string TruncateFilename( this string str, int maxLength, string appendage = null )
	{
		if ( string.IsNullOrEmpty( str ) ) return str;
		if ( str.Length <= maxLength ) return str;

		maxLength -= 3; //account for delimiter spacing

		string final;
		List<string> parts;

		int loops = 0;
		while ( loops++ < 100 )
		{
			parts = str.Split( FilenameDelim ).ToList();
			parts.RemoveRange( parts.Count - 1 - loops, loops );
			if ( parts.Count == 1 )
			{
				return parts.Last();
			}

			parts.Insert( parts.Count - 1, "..." );
			final = string.Join( "/", parts.ToArray() );
			if ( final.Length < maxLength )
			{
				return final;
			}
		}

		return str.Split( FilenameDelim ).ToList().Last();
	}


	/// <summary>
	/// An extended Contains which takes a StringComparison.
	/// </summary>
	public static bool Contains( this string source, string toCheck, StringComparison comp )
	{
		return source.IndexOf( toCheck, comp ) >= 0;
	}

	/// <summary>
	/// Given a large string, find all occurrences of a substring and return them with padding.
	/// This is useful in situations where you're searching for a word in a hug body of text, and
	/// want to show how it's used without displaying the whole text.
	/// </summary>
	public static string Snippet( this string source, string find, int padding )
	{
		if ( string.IsNullOrEmpty( find ) ) return string.Empty;

		StringBuilder sb = new StringBuilder();

		for ( int index = 0; index < source.Length; index += find.Length )
		{
			index = source.IndexOf( find, index, StringComparison.InvariantCultureIgnoreCase );
			if ( index == -1 )
				break;

			var startPos = (index - padding).Clamp( 0, source.Length );
			var endPos = (startPos + find.Length + padding * 2).Clamp( 0, source.Length );
			index = endPos;

			if ( sb.Length > 0 )
				sb.Append( " ... " );

			sb.Append( source.Substring( startPos, endPos - startPos ) );
		}

		return sb.ToString();
	}


	/// <summary>
	/// Convert a variable name to something more user friendly.
	/// </summary>
	public static string ToTitleCase( this string source )
	{
		if ( source is null )
			return "";

		var builder = new StringBuilder();
		var lastWasWhiteSpace = true;

		for ( var i = 0; i < source.Length; ++i )
		{
			var next = source[i];

			// Replace separators with just a space

			switch ( next )
			{
				case '-':
					// Special case for date formats like yy-MM-dd
					if ( i == 0 || i >= source.Length - 1 )
					{
						goto case '_';
					}

					if ( char.IsDigit( source[i - 1] ) && char.IsDigit( source[i + 1] ) )
					{
						lastWasWhiteSpace = true;
						builder.Append( '-' );
						continue;
					}

					goto case '_';

				case '_':
				case '.':
				case '\t':
				case '\n':
				case ' ':
					if ( lastWasWhiteSpace ) continue;

					lastWasWhiteSpace = true;
					builder.Append( ' ' );
					continue;
			}

			// Insert a space to separate camelCase words

			if ( i > 0 )
			{
				var prev = source[i - 1];

				if ( char.IsUpper( next ) && char.IsLower( prev ) || char.IsDigit( next ) && !char.IsDigit( prev ) && !lastWasWhiteSpace )
				{
					builder.Append( ' ' );
				}
			}

			// Append character from original string

			lastWasWhiteSpace = char.IsWhiteSpace( next );
			builder.Append( next );
		}

		return CultureInfo.CurrentCulture.TextInfo.ToTitleCase( builder.ToString().Trim() );
	}

	private static readonly char[] _badCharacters =
	{
            // Ascii Table 0-31 - excluding tab, newline, return
            '\x00', '\x01', '\x02', '\x03', '\x04', '\x05', '\x06',
		'\x07', '\x08', '\x09', '\x0B', '\x0C', '\x0D',
		'\x0E', '\x0F', '\x10', '\x12', '\x13', '\x14',
		'\x16', '\x17', '\x18', '\x19', '\x1A', '\x1B',
		'\x1C', '\x1D', '\x1E', '\x1F',

		'\xA0', // Non breaking space
            '\xAD', // Soft hyphen

            '\u2000', // En quad
            '\u2001', // Em quad
            '\u2002', // En space
            '\u2003', // Em space
            '\u2004', // Three per em space
            '\u2005', // Four per em space
            '\u2006', // Six per em space
            '\u2007', // Figure space
            '\u2008', // Punctuation space
            '\u2009', // Thin space
            '\u200A', // Hair space
            '\u200B', // Zero width space
            '\u200C', // Zero width non-joiner
            '\u200D', // Zero width joiner
            '\u200E', '\u200F',

		'\u2010', // Hyphen
            '\u2011', // Non breaking hyphen
            '\u2012', // Figure dash
            '\u2013', // En dash
            '\u2014', // Em dash
            '\u2015', // Horizontal bar
            '\u2016', // Double vertical line
            '\u2017', // Double low line
            '\u2018', // Left single quotation mark
            '\u2019', // Right single quotation mark
            '\u201A', // Single low-9 quotation mark
            '\u201B', // Single high reversed-9 quotation mark
            '\u201C', // Left double quotation mark
            '\u201D', // Right double quotation mark
            '\u201E', // Double low-9 quotation mark
            '\u201F', // Double high reversed-9 quotation mark

            '\u2028', // Line separator
            '\u2029', // Paragraph separator
            '\u202F', // Narrow no-break space

            '\u205F', // Medium mathematical space
            '\u2060', // Word joiner

            '\u2420', // Symbol for space
            '\u2422', // Blank symbol
            '\u2423', // Open box

            '\u3000', // Ideographic space

            '\uFEFF'  // Zero width no-break space
        };

	/// <summary>
	/// Removes bad, invisible characters that are commonly used to exploit.
	/// https://en.wikipedia.org/wiki/Zero-width_non-joiner
	/// </summary>
	public static string RemoveBadCharacters( this string str )
	{
		str = new string( str.Where( x => !_badCharacters.Contains( x ) ).ToArray() );

		return str;
	}


	/// <summary>
	/// Convert to a base64 encoded string
	/// </summary>
	public static string Base64Encode( this string plainText )
	{
		var plainTextBytes = System.Text.Encoding.UTF8.GetBytes( plainText );
		return System.Convert.ToBase64String( plainTextBytes );
	}

	/// <summary>
	/// Convert from a base64 encoded string
	/// </summary>
	public static string Base64Decode( this string base64EncodedData )
	{
		var base64EncodedBytes = System.Convert.FromBase64String( base64EncodedData );
		return System.Text.Encoding.UTF8.GetString( base64EncodedBytes );
	}

	/// <summary>
	/// Try to politely convert from a string to another type
	/// </summary>
	public static object ToType( this string str, Type t )
	{
		if ( str.TryToType( t, out var output ) )
		{
			return output;
		}

		throw new System.Exception( "ToType - need to add the ability to change from string to " + t );
	}

	/// <summary>
	/// Try to politely convert from a string to another type
	/// </summary>
	public static bool TryToType( this string str, Type t, out object Value )
	{
		Value = null;

		t = Nullable.GetUnderlyingType( t ) ?? t;
		if ( t == typeof( decimal ) ) { Value = str.ToDecimal(); return true; }
		if ( t == typeof( float ) ) { Value = str.ToFloat(); return true; }
		if ( t == typeof( double ) ) { Value = (double)str.ToFloat(); return true; }
		if ( t == typeof( int ) ) { Value = str.ToInt(); return true; }
		if ( t == typeof( uint ) ) { Value = str.ToUInt(); return true; }
		if ( t == typeof( bool ) ) { Value = str.ToBool(); return true; }
		if ( t == typeof( string ) ) { Value = str; return true; }
		if ( t == typeof( ulong ) ) { Value = str.ToULong(); return true; }
		if ( t == typeof( long ) ) { Value = str.ToLong(); return true; }
		if ( t == typeof( Vector2 ) ) { Value = Vector2.Parse( str ); return true; }
		if ( t == typeof( Vector3 ) ) { Value = Vector3.Parse( str ); return true; }
		if ( t == typeof( Vector4 ) ) { Value = Vector4.Parse( str ); return true; }
		if ( t == typeof( Angles ) ) { Value = global::Angles.Parse( str ); return true; }
		if ( t == typeof( Color ) ) { Value = global::Color.Parse( str ); return true; }
		if ( t == typeof( RangedFloat ) ) { Value = RangedFloat.Parse( str ); return true; }
		if ( t.IsEnum ) { Value = Enum.Parse( t, str ); return true; }

		if ( t == typeof( Rotation ) )
		{
			Value = global::Rotation.Parse( str );

			// Special case when loading data from FGD angles
			if ( (Rotation)Value == global::Rotation.Identity )
			{
				var ang = global::Angles.Parse( str );
				Value = global::Rotation.From( ang );
			}

			return true;
		}

		// Try implicit operator from string
		MethodInfo op_Implicit = t.GetMethod( "op_Implicit", BindingFlags.Static | BindingFlags.Public, new Type[] { typeof( string ) } );
		if ( op_Implicit != null )
		{
			Value = op_Implicit.Invoke( null, new[] { str } );
			return true;
		}

		try
		{
			// Special case for textures
			if ( t.Name == "Texture" )
			{
				var meth = t.GetMethod( "Load", new Type[] { typeof( string ), typeof( bool ) } );
				if ( meth != null )
				{
					object obj = meth.Invoke( null, new object[] { str, true } );
					if ( obj != null ) { Value = obj; return true; }
				}
			}
		}
		catch ( Exception e ) { Log.Warning( e ); }

		return false;
	}

	/// <summary>
	/// Generate xxhash3 hash from given string.
	/// </summary>
	public static int FastHash( this string str )
	{
		// Must Match the version in Sandbox,CodeGen. Should only be changed after careful benchmarking.
		return (int)XxHash3.HashToUInt64( GetUtf16Bytes( str ) );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static ReadOnlySpan<byte> GetUtf16Bytes( string s )
	{
		ref char firstChar = ref MemoryMarshal.GetReference( s.AsSpan() );
		return MemoryMarshal.CreateReadOnlySpan(
			ref Unsafe.As<char, byte>( ref firstChar ),
			s.Length * sizeof( char ) );
	}

	/// <summary>
	/// Take a string and create a deterministic guid.
	/// </summary>
	public static Guid ToGuid( this string input )
	{
		using var provider = System.Security.Cryptography.MD5.Create();
		byte[] hash = provider.ComputeHash( System.Text.Encoding.UTF8.GetBytes( input ) );
		return new Guid( hash );
	}

	/// <summary>
	/// Get the md5 hash of a string.
	/// </summary>
	public static string Md5( this string input )
	{
		using var md5 = System.Security.Cryptography.MD5.Create();
		byte[] hash = md5.ComputeHash( System.Text.Encoding.UTF8.GetBytes( input ) );
		return Convert.ToHexString( hash ).ToLowerInvariant();
	}

	/// <summary>
	/// The seed is what the engine uses for STRINGTOKEN_MURMURHASH_SEED
	/// </summary>
	internal static unsafe uint MurmurHash2( this string str, bool lowercase = false, uint seed = 0x31415926 ) // 
	{
		if ( lowercase )
			str = str.ToLowerInvariant();

		// Convert the string to an ASCII byte array
		byte[] bytes = Encoding.ASCII.GetBytes( str );
		uint len = (uint)bytes.Length;
		const uint m = 0x5bd1e995;
		const int r = 24;

		// Initialize the hash to a 'random' value
		uint h = seed ^ len;

		// Mix 4 bytes at a time into the hash
		fixed ( byte* data = bytes )
		{
			uint* data32 = (uint*)data;
			while ( len >= 4 )
			{
				uint k = *data32;

				k *= m;
				k ^= k >> r;
				k *= m;

				h *= m;
				h ^= k;

				data32++;
				len -= 4;
			}

			// Handle the last few bytes of the input array
			byte* dataRemaining = (byte*)data32;
			switch ( len )
			{
				case 3: h ^= (uint)dataRemaining[2] << 16; goto case 2;
				case 2: h ^= (uint)dataRemaining[1] << 8; goto case 1;
				case 1:
					h ^= dataRemaining[0];
					h *= m;
					break;
			}

			// Do a few final mixes of the hash to ensure the last few bytes are well-incorporated.
			h ^= h >> 13;
			h *= m;
			h ^= h >> 15;
		}

		return h;
	}

	/// <summary>
	/// convert "string" into "string       " or "      string"
	/// </summary>
	public static string Columnize( this string str, int maxLength, bool right = false )
	{
		if ( string.IsNullOrEmpty( str ) ) return str;
		if ( str.Length >= maxLength )
			return str.Substring( 0, maxLength );

		var spaces = new string( ' ', maxLength - str.Length );

		if ( right )
		{
			return $"{spaces}{str}";
		}

		return $"{str}{spaces}";
	}

	/// <summary>
	/// Returns true if this string matches a wildcard match. Check is case insensitive.
	/// </summary>
	public static bool WildcardMatch( this string str, string wildcard )
	{
		if ( str == null ) return false;
		if ( wildcard == null ) return false;

		if ( wildcard.Contains( '*' ) )
		{
			wildcard = Regex.Escape( wildcard ).Replace( "\\*", ".*" );
			wildcard = $"^{wildcard}$";
			return Regex.IsMatch( str, wildcard, RegexOptions.IgnoreCase );
		}

		return string.Equals( str, wildcard, StringComparison.OrdinalIgnoreCase );
	}


	/// <summary>
	/// The string might start and end in quotes ( ", ' ), remove those if that is the case.
	/// </summary>
	public static string TrimQuoted( this string str, bool ignoreSurroundingSpaces = false )
	{
		if ( ignoreSurroundingSpaces )
			str = str.Trim();

		if ( str.Length >= 2 && str[0] == '"' && str[^1] == '"' )
		{
			return str[1..^1];
		}

		if ( str.Length >= 2 && str[0] == '\'' && str[^1] == '\'' )
		{
			return str[1..^1];
		}

		return str;
	}

	/// <summary>
	/// Return the distance between two strings. Useful for ordering strings by similarity
	/// </summary>
	public static int Distance( this string source, string target )
	{
		if ( string.IsNullOrEmpty( source ) )
		{
			if ( string.IsNullOrEmpty( target ) ) return 0;
			return target.Length;
		}
		if ( string.IsNullOrEmpty( target ) ) return source.Length;

		if ( source.Length > target.Length )
		{
			var temp = target;
			target = source;
			source = temp;
		}

		var m = target.Length;
		var n = source.Length;
		var distance = new int[2, m + 1];
		// Initialize the distance 'matrix'
		for ( var j = 1; j <= m; j++ ) distance[0, j] = j;

		var currentRow = 0;
		for ( var i = 1; i <= n; ++i )
		{
			currentRow = i & 1;
			distance[currentRow, 0] = i;
			var previousRow = currentRow ^ 1;
			for ( var j = 1; j <= m; j++ )
			{
				var cost = (target[j - 1] == source[i - 1] ? 0 : 1);
				distance[currentRow, j] = Math.Min( Math.Min(
					distance[previousRow, j] + 1,
					distance[currentRow, j - 1] + 1 ),
					distance[previousRow, j - 1] + cost );
			}
		}
		return distance[currentRow, m];
	}

	/// <summary>
	/// Is this string a valid Tag. This is a way to check if a string is a valid tag, project wide. So our logic is always the same.
	/// 
	/// - not null
	/// - between 1 and 32 chars
	/// - a-z
	/// </summary>
	public static bool IsValidTag( this string source )
	{
		if ( source is null ) return false;
		if ( source.Length < 1 ) return false;
		if ( source.Length > 32 ) return false;

		if ( !Regex.IsMatch( source, "^[a-zA-Z0-9\\._-]{1,32}$" ) )
			return false;

		return true;
	}

	/// <summary>
	/// Make the passed in string filename safe. This replaces any invalid characters with "_".
	/// </summary>
	public static string GetFilenameSafe( this string input )
	{
		// Get the array of invalid characters
		char[] invalidChars = Path.GetInvalidFileNameChars();

		// Replace invalid characters with an underscore
		return new string( input.Select( ch => invalidChars.Contains( ch ) ? '_' : ch ).ToArray() );
	}

}
