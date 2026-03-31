using System.Collections.Immutable;
using System.Numerics;
using System.IO;

namespace Sandbox.MovieMaker;

#nullable enable

internal static class ByteStreamExtensions
{
	[Flags]
	private enum Flags : uint;

	private readonly record struct Header( int Magic, uint Version, int Count, Flags Flags );

	private const int MaxSampleCount = 0x10_0000;

	private static void ValidateHeader( Header header )
	{
		if ( header.Magic != -1 || header.Version != 1u )
		{
			throw new InvalidDataException( "Invalid compressed movie header (magic/version mismatch)." );
		}

		if ( header.Count < 0 || header.Count > MaxSampleCount )
		{
			throw new InvalidDataException( $"Invalid compressed movie header (Count={header.Count} is out of range)." );
		}
	}

	public static void WriteCompressed( this ref ByteStream stream, ReadOnlySpan<Transform> samples )
	{
		// New format always starts with a -1, to distinguish from old uncompressed transforms.

		stream.Write( new Header( -1, 1, samples.Length, default ) );

		if ( samples.Length == 0 ) return;

		// We delta-encode because it'll get compressed slightly better by GZip later on

		stream.WriteDeltaRangeEncoded( samples, static x => x.Position );
		stream.WriteDeltaEncoded( samples, static x => (Quat32)x.Rotation );
		stream.WriteDeltaRangeEncoded( samples, static x => x.Scale );
	}

	public static void WriteCompressed( this ref ByteStream stream, ReadOnlySpan<Rotation> samples )
	{
		// New format always starts with a -1, to distinguish from old uncompressed transforms.

		stream.Write( new Header( -1, 1, samples.Length, default ) );

		if ( samples.Length == 0 ) return;

		// We delta-encode because it'll get compressed slightly better by GZip later on

		stream.WriteDeltaEncoded( samples, static x => (Quat32)x );
	}

	private static Header? ReadHeader( this ref ByteStream stream )
	{
		var lookahead = stream;
		var legacyLength = lookahead.Read<int>();

		if ( legacyLength != -1 )
		{
			return null;
		}

		return stream.Read<Header>();
	}

	public static ImmutableArray<Transform> ReadCompressedTransforms( this ref ByteStream stream )
	{
		if ( stream.ReadHeader() is not { } header )
		{
			return [.. stream.ReadArraySpan<Transform>( MaxSampleCount )];
		}

		ValidateHeader( header );

		var positions = new Vector3[header.Count];
		var rotations = new Rotation[header.Count];
		var scales = new Vector3[header.Count];

		stream.ReadDeltaRangeEncoded( positions );
		stream.ReadDeltaEncoded<Quat32, Rotation>( rotations, static x => x );
		stream.ReadDeltaRangeEncoded( scales );

		return [
			..Enumerable.Range( 0, header.Count )
				.Select( i => new Transform( positions[i], rotations[i], scales[i] ) )
		];
	}

	public static ImmutableArray<Rotation> ReadCompressedRotations( this ref ByteStream stream )
	{
		if ( stream.ReadHeader() is not { } header )
		{
			return [.. stream.ReadArraySpan<Rotation>( MaxSampleCount )];
		}

		ValidateHeader( header );

		var rotations = new Rotation[header.Count];

		stream.ReadDeltaEncoded<Quat32, Rotation>( rotations, static x => x );

		return [.. rotations];
	}

	public static void WriteDeltaEncoded<TSrc, TDst>( this ref ByteStream stream, ReadOnlySpan<TSrc> samples, Func<TSrc, TDst> encode )
		where TDst : unmanaged, ISubtractionOperators<TDst, TDst, TDst>
	{
		var prev = encode( samples[0] );
		stream.Write( prev );

		foreach ( var sample in samples[1..] )
		{
			var next = encode( sample );
			stream.Write( next - prev );
			prev = next;
		}
	}

	public static void ReadDeltaEncoded<TSrc, TDst>( this ref ByteStream stream, Span<TDst> samples, Func<TSrc, TDst> decode )
		where TSrc : unmanaged, IAdditionOperators<TSrc, TSrc, TSrc>
	{
		var prev = stream.Read<TSrc>();

		samples[0] = decode( prev );

		for ( var i = 1; i < samples.Length; i++ )
		{
			var next = prev + stream.Read<TSrc>();
			samples[i] = decode( next );
			prev = next;
		}
	}

	public static void WriteDeltaRangeEncoded<TSrc>( this ref ByteStream stream, ReadOnlySpan<TSrc> samples, Func<TSrc, Vector3> getValue )
	{
		Vector3 min = float.PositiveInfinity;
		Vector3 max = float.NegativeInfinity;

		foreach ( var sample in samples )
		{
			var value = getValue( sample );

			if ( value.IsInfinity || value.IsNaN )
			{
				value = default;
			}

			min = Vector3.Min( min, value );
			max = Vector3.Max( max, value );
		}

		stream.Write( min );

		if ( max.AlmostEqual( min ) )
		{
			stream.Write( min );
			return;
		}

		stream.Write( max );

		var scale = 1f / (max - min);

		stream.WriteDeltaEncoded( samples, x =>
		{
			var value = getValue( x );

			if ( value.IsInfinity || value.IsNaN )
			{
				value = default;
			}

			return (Vector3B)((value - min) * scale);
		} );
	}

	public static void ReadDeltaRangeEncoded( this ref ByteStream stream, Span<Vector3> samples )
	{
		var min = stream.Read<Vector3>();
		var max = stream.Read<Vector3>();

		if ( min.Equals( max ) )
		{
			samples.Fill( min );
			return;
		}

		var scale = max - min;

		stream.ReadDeltaEncoded<Vector3B, Vector3>( samples, x => min + x * scale );
	}
}

file readonly record struct Vector3B( byte X, byte Y, byte Z ) :
	IAdditionOperators<Vector3B, Vector3B, Vector3B>,
	ISubtractionOperators<Vector3B, Vector3B, Vector3B>
{
	public static explicit operator Vector3B( Vector3 vector )
	{
		return new Vector3B(
			(byte)(vector.x.Clamp( 0f, 1f ) * 255f),
			(byte)(vector.y.Clamp( 0f, 1f ) * 255f),
			(byte)(vector.z.Clamp( 0f, 1f ) * 255f) );
	}

	public static implicit operator Vector3( Vector3B vector )
	{
		return new Vector3( vector.X / 255f, vector.Y / 255f, vector.Z / 255f );
	}

	public static Vector3B operator +( Vector3B left, Vector3B right )
	{
		unchecked
		{
			return new Vector3B( (byte)(left.X + right.X), (byte)(left.Y + right.Y), (byte)(left.Z + right.Z) );
		}
	}

	public static Vector3B operator -( Vector3B left, Vector3B right )
	{
		unchecked
		{
			return new Vector3B( (byte)(left.X - right.X), (byte)(left.Y - right.Y), (byte)(left.Z - right.Z) );
		}
	}
}

file readonly struct Quat32 : IEquatable<Quat32>,
	IAdditionOperators<Quat32, Quat32, Quat32>,
	ISubtractionOperators<Quat32, Quat32, Quat32>
{
	// Based on https://www.gdcvault.com/play/1022195/Physics-for-Game-Programmers-Networking
	// at around 18 minutes.

	// Rotation quaternions have length 1, so we only need to store
	// 3 components and can reconstruct the missing one.

	// We should omit the largest component (absolute value) to help with precision.
	// The second-largest component can be at most +-sqrt(0.5).

	// If the omitted component is negative, we can flip the sign of the
	// other components to get the same rotation when decoded.

	// Final encoded form             (32 bits)
	// * Omitted component index      ( 2 bits)
	// * Component A                  (10 bits)
	// * Component B                  (10 bits)
	// * Component C                  (10 bits)

	public static explicit operator Quat32( Rotation rotation )
	{
		rotation = rotation.Normal;

		// Survive a nonsense rotation

		if ( !float.IsFinite( rotation.x ) || !float.IsFinite( rotation.y ) || !float.IsFinite( rotation.z ) || !float.IsFinite( rotation.w ) )
		{
			return default;
		}

		var absX = MathF.Abs( rotation.x );
		var absY = MathF.Abs( rotation.y );
		var absZ = MathF.Abs( rotation.z );
		var absW = MathF.Abs( rotation.w );

		ComponentIndex omitted;
		float a, b, c;

		if ( absX >= absY && absX >= absZ && absX >= absW )
		{
			omitted = ComponentIndex.X;

			var sign = MathF.Sign( rotation.x );

			a = rotation.y * sign;
			b = rotation.z * sign;
			c = rotation.w * sign;
		}
		else if ( absY >= absZ && absY >= absW )
		{
			omitted = ComponentIndex.Y;

			var sign = MathF.Sign( rotation.y );

			a = rotation.x * sign;
			b = rotation.z * sign;
			c = rotation.w * sign;
		}
		else if ( absZ >= absW )
		{
			omitted = ComponentIndex.Z;

			var sign = MathF.Sign( rotation.z );

			a = rotation.x * sign;
			b = rotation.y * sign;
			c = rotation.w * sign;
		}
		else
		{
			omitted = ComponentIndex.W;

			var sign = MathF.Sign( rotation.w );

			a = rotation.x * sign;
			b = rotation.y * sign;
			c = rotation.z * sign;
		}

		return new Quat32( (((uint)omitted) << 30)
			| (PackComponent( a ) << 20)
			| (PackComponent( b ) << 10)
			| (PackComponent( c )) );
	}

	public static implicit operator Rotation( Quat32 compressed )
	{
		var omitted = (ComponentIndex)((compressed._encoded >> 30) & 3);
		var a = UnpackComponent( (compressed._encoded >> 20) & 0x3ff );
		var b = UnpackComponent( (compressed._encoded >> 10) & 0x3ff );
		var c = UnpackComponent( compressed._encoded & 0x3ff );
		var x = MathF.Sqrt( 1f - a * a - b * b - c * c );

		return omitted switch
		{
			ComponentIndex.X => new Rotation( x, a, b, c ).Normal,
			ComponentIndex.Y => new Rotation( a, x, b, c ).Normal,
			ComponentIndex.Z => new Rotation( a, b, x, c ).Normal,
			ComponentIndex.W => new Rotation( a, b, c, x ).Normal,
			_ => throw new Exception( "Unexpected component index." )
		};
	}

	private enum ComponentIndex : byte
	{
		X,
		Y,
		Z,
		W
	}

	private readonly uint _encoded;

	private Quat32( uint encoded )
	{
		_encoded = encoded;
	}

	private const float SqrtTwo = 1.41421356237f;
	private const float SqrtHalf = SqrtTwo / 2f;

	private static float UnpackComponent( uint value )
	{
		return value * SqrtTwo / 0x3ff - SqrtHalf;
	}

	private static uint PackComponent( float value )
	{
		return (uint)(((value + SqrtHalf) / SqrtTwo).Clamp( 0f, 1f ) * 0x3ff);
	}

	public bool Equals( Quat32 other )
	{
		return _encoded == other._encoded;
	}

	public override bool Equals( object? obj )
	{
		return obj is Quat32 other && Equals( other );
	}

	public override int GetHashCode()
	{
		return (int)_encoded;
	}

	public static Quat32 operator +( Quat32 left, Quat32 right )
	{
		unchecked
		{
			return new Quat32( left._encoded + right._encoded );
		}
	}

	public static Quat32 operator -( Quat32 left, Quat32 right )
	{
		unchecked
		{
			return new Quat32( left._encoded - right._encoded );
		}
	}
}
