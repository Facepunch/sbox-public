using System.Runtime.CompilerServices;

namespace Sandbox;

public static partial class SandboxSystemExtensions
{
	/// <summary>
	/// Given a number, will format as a memory value, ie 10gb, 4mb
	/// </summary>
	public static string FormatBytes<T>( this T input, bool shortFormat = false ) where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable
	{
		ulong i = 0;

		if ( input is float f ) i = (ulong)Math.Max( 0, f );
		else if ( input is int ii ) i = (ulong)Math.Max( 0, ii );
		else if ( input is long l ) i = (ulong)Math.Max( 0, l );
		else if ( input is double d ) i = (ulong)Math.Max( 0, d );
		else i = (ulong)Convert.ChangeType( input, typeof( ulong ) );

		double readable;
		string suffix;
		if ( i >= 0x1000000000000000 ) // Exabyte
		{
			suffix = "eb";
			readable = (double)(i >> 50);
		}
		else if ( i >= 0x4000000000000 ) // Petabyte
		{
			suffix = "pb";
			readable = (double)(i >> 40);
		}
		else if ( i >= 0x10000000000 ) // Terabyte
		{
			suffix = "tb";
			readable = (double)(i >> 30);
		}
		else if ( i >= 0x40000000 ) // Gigabyte
		{
			suffix = "gb";
			readable = (double)(i >> 20);
		}
		else if ( i >= 0x100000 ) // Megabyte
		{
			suffix = "mb";
			readable = (double)(i >> 10);
		}
		else if ( i >= 0x400 ) // Kilobyte
		{
			suffix = "kb";
			readable = (double)i;
		}
		else
		{
			return i.ToString( "0b" ); // Byte
		}
		readable /= 1024;

		return readable.ToString( shortFormat ? "0" : "0.00" ) + suffix;
	}

	/// <summary>
	/// Clamp a number between two values.
	/// </summary>
	public static T Clamp<T>( this T input, T min, T max ) where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable
	{
		if ( input.CompareTo( min ) < 0 ) return min;
		if ( input.CompareTo( max ) > 0 ) return max;

		return input;
	}

	/// <summary>
	/// Formats the given value in format "1w2d3h4m5s". Will not display 0 values.
	/// </summary>
	/// <param name="secs">Time to format, in seconds.</param>
	public static string FormatSeconds( this long secs )
	{
		var m = System.Math.Floor( (float)secs / 60.0f );
		var h = System.Math.Floor( (float)m / 60.0f );
		var d = System.Math.Floor( (float)h / 24.0f );
		var w = System.Math.Floor( (float)d / 7.0f );

		if ( secs < 60 ) return string.Format( "{0}s", secs ); // 1s
		if ( m < 60 ) return string.Format( "{1}m{0}s", secs % 60, m ); // 5m3s
		if ( h < 48 ) return string.Format( "{2}h{1}m{0}s", secs % 60, m % 60, h ); // 6h40m34h
		if ( d < 7 ) return string.Format( "{3}d{2}h{1}m{0}s", secs % 60, m % 60, h % 24, d ); // 5d15h15m10s

		return string.Format( "{4}w{3}d{2}h{1}m{0}s", secs % 60, m % 60, h % 24, d % 7, w );
	}
	/// <inheritdoc cref=" FormatSeconds(long)"/>
	public static string FormatSeconds( this ulong secs ) { return FormatSeconds( (long)secs ); }

	/// <summary>
	/// Formats the given value in format "4 weeks, 3 days, 2 hours and 1 minutes".
	/// Will not display 0 values. Will not display seconds if value is more than 1 hour.
	/// </summary>
	/// <param name="secs">Time to format, in seconds.</param>
	public static string FormatSecondsLong( this long secs )
	{
		var m = System.Math.Floor( (float)secs / 60.0f );
		var h = System.Math.Floor( (float)m / 60.0f );
		var d = System.Math.Floor( (float)h / 24.0f );
		var w = System.Math.Floor( (float)d / 7.0f );

		if ( secs < 60 ) return string.Format( "{0} seconds", secs );
		if ( m < 60 ) return string.Format( "{1} minutes, {0} seconds", secs % 60, m );
		if ( h < 48 ) return string.Format( "{1} hours and {0} minutes", m % 60, h );
		if ( d < 7 ) return string.Format( "{2} days, {1} hours and {0} minutes", m % 60, h % 24, d );

		return string.Format( "{3} weeks, {2} days, {1} hours and {0} minutes", m % 60, h % 24, d % 7, w );
	}
	/// <inheritdoc cref=" FormatSecondsLong(long)"/>
	public static string FormatSecondsLong( this ulong secs ) { return FormatSecondsLong( (long)secs ); }

	/// <summary>
	/// "1500" becomes "1,500", "15 000" becomes "15K", "15 000 000" becomes "15KK", etc.
	/// </summary>
	public static string FormatNumberShort( this long num )
	{
		if ( num >= 100000 )
		{
			return FormatNumberShort( num / 1000 ) + "K";
		}

		if ( num >= 10000 )
		{
			return (num / 1000D).ToString( "0.#" ) + "K";
		}

		return num.ToString( "#,0" );
	}
	/// <inheritdoc cref=" FormatNumberShort(long)"/>
	public static string FormatNumberShort( this ulong num ) { return FormatNumberShort( (long)num ); }

	/// <summary>
	/// Does what you expected to happen when you did "a % b", that is, handles negative <paramref name="a"/> values by returning a positive number from the end.
	/// </summary>
	public static int UnsignedMod( this int a, int b )
	{
		// pasted from https://stackoverflow.com/questions/2691025/mathematical-modulus-in-c-sharp
		return (Math.Abs( a * b ) + a) % b;
	}

	/// <summary>
	/// Returns the number of bits set in an integer. This us usually used for flags to count
	/// the amount of flags set.
	/// </summary>
	public static int BitsSet( this int i )
	{
		i = i - ((i >> 1) & 0x55555555);        // add pairs of bits
		i = (i & 0x33333333) + ((i >> 2) & 0x33333333);  // quads
		i = (i + (i >> 4)) & 0x0F0F0F0F;        // groups of 8
		return (i * 0x01010101) >> 24;          // horizontal sum of bytes
	}

	/// <summary>
	/// Return single if 1 else plural
	/// </summary>
	public static string Plural( this int a, string single, string plural )
	{
		if ( a == 1 ) return single;
		if ( a == -1 ) return single;
		return plural;
	}

	/// <summary>
	/// Change 1 to 1st, 2 to 2nd etc
	/// </summary>
	public static string FormatWithSuffix( this int num )
	{
		string number = num.ToString();
		if ( number.EndsWith( "11" ) ) return number + "th";
		if ( number.EndsWith( "12" ) ) return number + "th";
		if ( number.EndsWith( "13" ) ) return number + "th";
		if ( number.EndsWith( "1" ) ) return number + "st";
		if ( number.EndsWith( "2" ) ) return number + "nd";
		if ( number.EndsWith( "3" ) ) return number + "rd";
		return number + "th";
	}

	private enum SizeUnit { B, KB, MB, GB, TB, PB, EB, ZB, YB }

	public static string SizeFormat( this long bytes )
	{
		if ( bytes < 0 )
			return "-" + SizeFormat( -bytes );

		int unit = 0;
		double value = bytes;
		while ( unit < (int)SizeUnit.YB && value >= 1024 )
		{
			value /= 1024;
			unit++;
		}

		return $"{value:F2} {(SizeUnit)unit}";
	}

	public static string SizeFormat( this int bytes ) => SizeFormat( (long)bytes );

	/// <summary>
	/// Format a large number into "1045M", "56K"
	/// </summary>
	public static string KiloFormat( this int num )
	{
		if ( num >= 10000000 ) return (num / 1000000).ToString( "#,0M" );
		if ( num >= 1000000 ) return (num / 1000000).ToString( "0.#" ) + "M";
		if ( num >= 100000 ) return (num / 1000).ToString( "#,0K" );
		if ( num >= 1000 ) return (num / 1000).ToString( "0.#" ) + "K";

		return num.ToString( "#,0" );
	}

	/// <summary>
	/// Format a large number into "1045M", "56K"
	/// </summary>
	public static string KiloFormat( this long num )
	{
		if ( num >= 10000000 ) return (num / 1000000).ToString( "#,0M" );
		if ( num >= 1000000 ) return (num / 1000000).ToString( "0.#" ) + "M";
		if ( num >= 100000 ) return (num / 1000).ToString( "#,0K" );
		if ( num >= 1000 ) return (num / 1000).ToString( "0.#" ) + "K";

		return num.ToString( "#,0" );
	}

	/// <summary>
	/// Humanize a timespan into "x hours", "x seconds"
	/// </summary>
	public static string Humanize( this TimeSpan timespan, bool shortVersion = false, bool minutes = true, bool hours = true, bool days = true )
	{
		if ( shortVersion )
		{
			if ( timespan.TotalSeconds < 1 )
			{
				return "0s";
			}
			else if ( timespan.TotalSeconds < 60 || !minutes )
			{
				return $"{timespan.TotalSeconds:n0}s";
			}
			else if ( timespan.TotalMinutes < 60 || !hours )
			{
				return $"{timespan.TotalMinutes:n0}m";
			}
			else if ( timespan.TotalHours < 24 || !days )
			{
				return $"{timespan.TotalHours:n0}h";
			}
			else
			{
				return $"{timespan.TotalDays:n0}d";
			}
		}
		else
		{
			if ( timespan.TotalSeconds < 1 )
			{
				return "0 seconds";
			}
			else if ( timespan.TotalSeconds < 60 || !minutes )
			{
				return $"{timespan.TotalSeconds:n0} seconds";
			}
			else if ( timespan.TotalMinutes < 60 || !hours )
			{
				return $"{timespan.TotalMinutes:n0} minutes";
			}
			else if ( timespan.TotalHours < 24 || !days )
			{
				return $"{timespan.TotalHours:n0} hours";
			}
			else
			{
				return $"{timespan.TotalDays:n0} days";
			}
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static unsafe bool Contains<T>( this T value, T flag ) where T : unmanaged, Enum
	{
		if ( Unsafe.SizeOf<T>() == sizeof( int ) )
		{
			return (Unsafe.As<T, int>( ref value ) & Unsafe.As<T, int>( ref flag )) == Unsafe.As<T, int>( ref flag );
		}
		else if ( Unsafe.SizeOf<T>() == sizeof( long ) )
		{
			return (Unsafe.As<T, long>( ref value ) & Unsafe.As<T, long>( ref flag )) == Unsafe.As<T, long>( ref flag );
		}
		else
		{
			throw new ArgumentException( "Unsupported enum type" );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static unsafe T WithFlag<T>( this T value, T flag, bool set ) where T : unmanaged, Enum
	{
		switch ( sizeof( T ) )
		{
			case 1:
				{
					byte* valPtr = (byte*)&value;
					byte* flagPtr = (byte*)&flag;
					byte result = set ? (byte)(*valPtr | *flagPtr) : (byte)(*valPtr & ~(*flagPtr));
					return *(T*)&result;
				}
			case 2:
				{
					ushort* valPtr = (ushort*)&value;
					ushort* flagPtr = (ushort*)&flag;
					ushort result = set ? (ushort)(*valPtr | *flagPtr) : (ushort)(*valPtr & ~(*flagPtr));
					return *(T*)&result;
				}
			case 4:
				{
					uint* valPtr = (uint*)&value;
					uint* flagPtr = (uint*)&flag;
					uint result = set ? *valPtr | *flagPtr : *valPtr & ~(*flagPtr);
					return *(T*)&result;
				}
			case 8:
				{
					ulong* valPtr = (ulong*)&value;
					ulong* flagPtr = (ulong*)&flag;
					ulong result = set ? *valPtr | *flagPtr : *valPtr & ~(*flagPtr);
					return *(T*)&result;
				}
			default:
				throw new NotSupportedException( $"Unsupported enum underlying type size {sizeof( T )}" );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static int AsInt<T>( this T value ) where T : unmanaged, Enum
	{
		if ( Unsafe.SizeOf<T>() == sizeof( int ) ) return Unsafe.As<T, int>( ref value );
		if ( Unsafe.SizeOf<T>() == sizeof( byte ) ) return Unsafe.As<T, byte>( ref value );
		if ( Unsafe.SizeOf<T>() == sizeof( short ) ) return Unsafe.As<T, short>( ref value );
		if ( Unsafe.SizeOf<T>() == sizeof( long ) ) return (int)Unsafe.As<T, long>( ref value );

		return 0;
	}

	/// <summary>
	/// Convert 1100 to 1.1k
	/// </summary>
	public static string ToMetric( this int input, int decimals = 2 )
	{
		return Humanizer.MetricNumeralExtensions.ToMetric( input, decimals: decimals );
	}


	/// <summary>
	/// Return true if the number is a power of two (2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, etc)
	/// </summary>
	public static bool IsPowerOfTwo( this int x )
	{
		return (x & (x - 1)) == 0;
	}

	/// <summary>
	/// Convert 1100 to 1.1k
	/// </summary>
	public static string ToMetric( this long input, int decimals = 2 )
	{
		return Humanizer.MetricNumeralExtensions.ToMetric( (int)input, decimals: decimals );
	}

	/// <summary>
	/// Convert 1100 to 1.1k
	/// </summary>
	public static string ToMetric( this double input, int decimals = 2 )
	{
		return Humanizer.MetricNumeralExtensions.ToMetric( input, decimals: decimals );
	}

	/// <summary>
	/// Convert 1100 to 1.1k
	/// </summary>
	public static string ToMetric( this float input, int decimals = 2 )
	{
		return Humanizer.MetricNumeralExtensions.ToMetric( input, decimals: decimals );
	}
}
