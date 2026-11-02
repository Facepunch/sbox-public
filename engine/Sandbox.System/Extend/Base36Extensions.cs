using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sandbox;

public static partial class SandboxSystemExtensions
{
	private const string CharList = "0123456789abcdefghijklmnopqrstuvwxyz";
	private static char[] CharArray = CharList.ToCharArray();

	/// <summary>
	/// Encode the given number into a Base36 string
	/// </summary>
	public static string ToBase36<T>( this T i ) where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable
	{
		var input = (long)Convert.ToDecimal( i );

		if ( input < 0 ) throw new ArgumentOutOfRangeException( "input", input, "input cannot be negative" );

		char[] clistarr = CharList.ToCharArray();
		var result = new Stack<char>();
		while ( input != 0 )
		{
			result.Push( clistarr[input % 36] );
			input /= 36;
		}

		return new string( result.ToArray() );
	}

	/// <summary>
	/// Decode the Base36 Encoded string into a number
	/// </summary>
	/// <param name="input"></param>
	/// <returns></returns>
	public static long FromBase36( this string input )
	{
		var reversed = input.ToLowerInvariant().Reverse();
		long result = 0;
		int pos = 0;
		foreach ( char c in reversed )
		{
			result += CharList.IndexOf( c ) * (long)System.Math.Pow( 36, pos );
			pos++;
		}

		return result;
	}
}
