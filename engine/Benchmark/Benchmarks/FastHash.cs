using BenchmarkDotNet.Attributes;
using Sandbox.Hashing;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;


[MemoryDiagnoser]
public class StringHashing
{
	[Params( 10, 50, 200, 1000, 3000, 5000, 10000, 200000 )]
	public int StringSize { get; set; }

	private string _testString;

	[GlobalSetup]
	public void Setup()
	{
		_testString = GenerateDeterministicString( StringSize );
	}

	private static string GenerateDeterministicString( int length )
	{
		var sb = new StringBuilder( length );

		// First part: Generate some alphabet characters with variation
		for ( int i = 0; i < length; i++ )
		{
			if ( i % 5 == 0 && i > 0 )
			{
				// Add a space every 5 characters for readability in shorter strings
				sb.Append( ' ' );
			}
			else if ( i % 64 == 63 )
			{
				// Add some numeric variation every 64 characters
				sb.Append( (i / 64) % 10 );
			}
			else
			{
				// Generate a deterministic pattern using the index
				char c = (char)('a' + (i % 26));

				// Capitalize some letters for variety
				if ( i % 7 == 0 )
				{
					c = char.ToUpper( c );
				}

				sb.Append( c );
			}
		}

		return sb.ToString();
	}

	[Benchmark( Baseline = true )]
	public int GetHashCodeBuiltIn()
	{
		return _testString.GetHashCode();
	}

	[Benchmark]
	public int FastHash2009()
	{
		return FastHashLegacy( _testString );
	}

	// Moved from Sandbox.Utility
	// Preserved for future reference/comparison
	public static int FastHashLegacy( string str )
	{
		// FNV-1a hash
		uint hash = 0x811C9DC5;
		byte[] data = Encoding.Unicode.GetBytes( str );

		foreach ( byte b in data )
		{
			hash ^= b;
			hash *= 0x1000193;
		}

		return unchecked((int)hash);
	}

	[Benchmark]
	public int XxHash3DotNet10()
	{
		return (int)XxHash3.HashToUInt64( GetUtf16Bytes( _testString ) );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static ReadOnlySpan<byte> GetUtf16Bytes( string s )
	{
		ref char firstChar = ref MemoryMarshal.GetReference( s.AsSpan() );
		return MemoryMarshal.CreateReadOnlySpan(
			ref Unsafe.As<char, byte>( ref firstChar ),
			s.Length * sizeof( char ) );
	}
}
