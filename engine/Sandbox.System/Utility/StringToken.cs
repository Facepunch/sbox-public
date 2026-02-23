using System.Collections.Concurrent;
namespace Sandbox;

/// <summary>
/// Strings are commonly converted to tokens in engine, to save space and speed up things like comparisons.
/// We wrap this functionality up in the StringToken struct, because we can apply a bunch of compile time 
/// optimizations to speed up the conversion.
/// </summary>
public struct StringToken : IEquatable<StringToken>
{
	static ConcurrentDictionary<string, uint> Cache = new( StringComparer.OrdinalIgnoreCase );
	static ConcurrentDictionary<uint, string> CacheReverse = new();

	public uint Value;

	public bool Equals( StringToken other ) => Value == other.Value;
	public override bool Equals( object obj ) => obj is StringToken other && Value == other.Value;
	public override int GetHashCode() => (int)Value;

	public static bool operator ==( StringToken left, StringToken right ) => left.Value == right.Value;
	public static bool operator !=( StringToken left, StringToken right ) => left.Value != right.Value;

	public StringToken( string value )
	{
		if ( value is null || value.Length == 0 )
			return;

		Value = Cache.GetOrAdd( value, Calculate );
	}

	public StringToken( uint token )
	{
		this.Value = token;
	}

	static public implicit operator StringToken( string value ) => new StringToken( value );

	static uint Calculate( string value )
	{
		if ( value is null ) return 0;
		if ( value.Length == 0 ) return 0;

		var token = value.MurmurHash2( true );
		CacheReverse[token] = value;
		return token;
	}

	/// <summary>
	/// called by interop
	/// </summary>
	internal static uint FindOrCreate( string str )
	{
		return new StringToken( str ).Value;
	}

	internal static string GetValue( uint token )
	{
		if ( CacheReverse.TryGetValue( token, out string val ) )
			return val;

		return null;
	}

	/// <summary>
	/// This is used by codegen. String literals are replaced by this function call, which
	/// avoids having to create or lookup the string token.
	/// </summary>
	public static StringToken Literal( string value, uint token )
	{
		return new StringToken( token );
	}

	internal static bool TryLookup( uint token, out string outVal )
	{
		if ( CacheReverse.TryGetValue( token, out outVal ) )
			return true;

		// If we don't find the StringToken, assume that it was passed through native
		// (not through FindOrCreate), and try to grab it from the native database
		//var str = NativeEngine.EngineGlue.GetStringTokenValue( token );
		//if ( !string.IsNullOrEmpty( str ) )
		//{
		// Add to to the cache so next time we don't have to check again
		//	Cache.TryAdd( str, token );
		//	CacheReverse.TryAdd( token, str );
		//	outVal = str;
		//	return true;
		//}

		return false;
	}


	/// <summary>
	/// A bunch of values we want to exist in the reverse lookup.
	/// I don't know if this is still strictly needed, but we used to need these to deserialize entities properly.
	/// </summary>
	static string[] defaults = new[]
	{
		"angles", "origin", "model", "owner", "velocity", "spawnflags", "avelocity", "ownername", "disableshadows",
		"disablereceiveshadows", "nodamageforces", "message", "gametitle", "targetname", "globalname", "rendercolor",
		"lightgroup", "rendertocubemaps", "lightmapstatic", "rendermode", "startdisabled", "skin", "bodygroups",
		"scale"
	};

	static StringToken()
	{
		foreach ( var str in defaults )
		{
			FindOrCreate( str );
		}
	}

	/// <summary>
	/// To allow redirecting in the case where a class has both a string and StringToken version of a method.
	/// We should be able to remove this when we're compiling on demand instead of keeping the string versions around for compatibility.
	/// </summary>
	public class ConvertAttribute : System.Attribute
	{

	}

}
