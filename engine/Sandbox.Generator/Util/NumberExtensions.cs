using System;
using System.Runtime.CompilerServices;

namespace Sandbox
{
	internal static class NumberExtensions
	{
		/// <summary>
		/// Given a number, will format as a memory value, ie 10gb, 4mb
		/// </summary>
		public static string FormatBytes<T>( this T input, bool shortFormat = false ) where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable
		{
			ulong i = (ulong)Convert.ChangeType( input, typeof( ulong ) );

			double readable = (double)i;
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


		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public static bool Contains<T>( this T value, T flag ) where T : unmanaged, Enum
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
	}
}
