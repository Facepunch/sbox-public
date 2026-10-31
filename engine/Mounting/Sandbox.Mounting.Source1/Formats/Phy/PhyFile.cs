using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Source1;

/// <summary>
/// PC IVP compact-surface PHY data. Each terminal ledge is one authored convex,
/// and each solid is one compound body. Positions are in Source inches.
/// </summary>
internal sealed partial class PhyFile
{
	public sealed class Solid
	{
		public int Index { get; internal set; }
		public string Name { get; internal set; }
		public string SurfaceProperty { get; internal set; } = "default";
		public float Mass { get; internal set; } = 1;
		public float Damping { get; internal set; } = 0.1f;
		public float RotationalDamping { get; internal set; } = 0.1f;
		public float Inertia { get; internal set; } = 1;
		public IReadOnlyList<Vector3[]> Convexes { get; internal set; }
	}

	public readonly record struct AxisLimit( float Min, float Max, float Friction );
	public sealed record Constraint( int Parent, int Child, AxisLimit X, AxisLimit Y, AxisLimit Z );
	public readonly record struct CollisionPair( int First, int Second );

	public string SourceName { get; }
	public int Checksum { get; }
	public IReadOnlyList<Solid> Solids { get; }
	public IReadOnlyList<Constraint> Constraints { get; private set; }
	public IReadOnlyList<CollisionPair> CollisionPairs { get; private set; }
	public bool? SelfCollisions { get; private set; }
	public IReadOnlyList<VmtNode> Text { get; private set; }

	private readonly byte[] data;
	private int rangeStart;
	private int rangeEnd;

	public PhyFile( Stream stream, string sourceName, int expectedChecksum )
	{
		ArgumentNullException.ThrowIfNull( stream );
		SourceName = sourceName;
		if ( !stream.CanRead || !stream.CanSeek )
			throw new ArgumentException( "PHY requires a readable, seekable stream.", nameof( stream ) );
		var length = stream.Length - stream.Position;
		if ( length < 16 || length > 256 * 1024 * 1024 )
			throw Error( "invalid file size (expected 16 bytes to 256 MiB)." );
		data = new byte[(int)length];
		try
		{
			stream.ReadExactly( data );
		}
		catch ( EndOfStreamException exception )
		{
			throw new InvalidDataException( $"{SourceName}: truncated PHY stream.", exception );
		}
		rangeEnd = data.Length;
		var headerSize = Int( 0 );
		if ( headerSize < 16 )
			throw Error( "header is smaller than 16 bytes." );
		Range( 0, headerSize );
		if ( Int( 4 ) != 0 )
			throw new NotSupportedException( $"{SourceName}: unsupported PHY file ID {Int( 4 )}." );
		Checksum = Int( 12 );
		if ( Checksum != expectedChecksum )
			throw Error( $"checksum {Checksum:X8} does not match MDL {expectedChecksum:X8}." );
		var count = Int( 8 );
		if ( count < 0 || count > ushort.MaxValue || count > (data.Length - headerSize) / 52 )
			throw Error( "invalid solid count." );

		var solids = new Solid[count];
		var offset = headerSize;
		for ( var i = 0; i < count; i++ )
		{
			rangeStart = 0;
			rangeEnd = data.Length;
			var size = Int( offset );
			if ( size < 48 )
				throw Error( $"solid {i} is smaller than its header." );
			var end = Range( (long)offset + 4, size ) + size;
			var compact = offset + 4;
			if ( Int( compact ) == 0x59485056 ) // VPHY
			{
				Range( offset, 32 );
				var version = UShort( offset + 8 );
				var type = UShort( offset + 10 );
				if ( version != 0x100 || type != 0 )
					throw new NotSupportedException( $"{SourceName}: PHY solid {i} has unsupported version {version}, type {type} (expected PC compact surface 256/0)." );
				var surfaceSize = Int( offset + 12 );
				var axisSize = Int( offset + 28 );
				if ( surfaceSize < 48 || axisSize < 0 || 28L + surfaceSize + axisSize != size )
					throw Error( $"solid {i} has inconsistent surface/axis-map sizes." );
				Vector( offset + 16 );
				compact = offset + 32;
				rangeEnd = compact + surfaceSize;
			}
			else
			{
				rangeEnd = end;
			}
			rangeStart = compact;
			Range( compact, 48 );
			var signature = Int( compact + 44 );
			if ( signature != 0 && signature != 0x53505649 ) // legacy zero or IVPS
				throw new NotSupportedException( $"{SourceName}: unsupported compact surface signature {signature:X8}." );
			var compactSize = (uint)Int( compact + 28 ) >> 8;
			if ( compactSize != rangeEnd - compact )
				throw Error( $"solid {i} has inconsistent compact surface size." );
			Position( compact );
			var inertia = Vector( compact + 12 );
			if ( inertia.x < 0 || inertia.y < 0 || inertia.z < 0 || Float( compact + 24 ) < 0 )
				throw Error( $"solid {i} has invalid mass properties." );
			var rootOffset = Int( compact + 32 );
			rangeStart = compact + 48;
			var convexes = ReadTree( Relative( compact, rootOffset, 28 ) );
			solids[i] = new Solid { Index = i, Convexes = convexes.AsReadOnly() };
			offset = end;
		}
		Solids = Array.AsReadOnly( solids );
		ReadText( offset );
	}

	private List<Vector3[]> ReadTree( int root )
	{
		var pending = new Stack<int>();
		var nodes = new HashSet<int>();
		var ledges = new HashSet<int>();
		var convexes = new List<Vector3[]>();
		long trianglesRead = 0;
		pending.Push( root );
		while ( pending.TryPop( out var node ) )
		{
			Range( node, 28 );
			if ( !nodes.Add( node ) || nodes.Count > (rangeEnd - rangeStart) / 28 )
				throw Error( "cyclic, shared or overlapping ledge tree." );
			Vector( node + 8 );
			if ( Float( node + 20 ) < 0 )
				throw Error( "negative ledge-tree radius." );
			var right = Int( node );
			if ( right != 0 )
			{
				var enclosing = Int( node + 4 );
				if ( enclosing != 0 ) Relative( node, enclosing, 16 );
				pending.Push( Relative( node, right, 28 ) );
				pending.Push( Range( (long)node + 28, 28 ) );
				continue;
			}
			var ledgeOffset = Int( node + 4 );
			if ( ledgeOffset == 0 )
				throw Error( "terminal ledge-tree node has no convex." );
			var ledge = Relative( node, ledgeOffset, 16 );
			if ( !ledges.Add( ledge ) )
				throw Error( "duplicate terminal convex." );
			var triangleCount = UShort( ledge + 12 );
			var flags = (uint)Int( ledge + 8 );
			var ledgeSize = (long)(flags >> 8) * 16;
			if ( triangleCount < 4 || triangleCount > short.MaxValue || ledgeSize < 16L + triangleCount * 16 )
				throw Error( "invalid convex triangle count or size." );
			Range( ledge, ledgeSize );
			trianglesRead += triangleCount;
			if ( trianglesRead > (rangeEnd - rangeStart) / 16 )
				throw Error( "overlapping or excessive convex triangle data." );
			var pointBase = Relative( ledge, Int( ledge ), 16 );
			var points = new List<Vector3>();
			var indices = new HashSet<ushort>();
			for ( var triangle = 0; triangle < triangleCount; triangle++ )
			{
				var start = ledge + 16 + triangle * 16;
				for ( var edge = 0; edge < 3; edge++ )
				{
					var vertex = UShort( start + 4 + edge * 4 );
					if ( indices.Add( vertex ) )
						points.Add( Position( Range( (long)pointBase + vertex * 16L, 16 ) ) );
				}
			}
			ValidateVolume( points );
			convexes.Add( points.ToArray() );
		}
		if ( convexes.Count == 0 )
			throw Error( "solid has no terminal convexes." );
		return convexes;
	}

	private void ValidateVolume( List<Vector3> points )
	{
		if ( points.Count < 4 )
			throw Error( "convex has fewer than four distinct vertices." );
		var origin = points[0];
		var longest = Vector3.Zero;
		foreach ( var point in points )
			if ( (point - origin).LengthSquared > longest.LengthSquared ) longest = point - origin;
		var normal = Vector3.Zero;
		foreach ( var point in points )
		{
			var cross = Vector3.Cross( longest, point - origin );
			if ( cross.LengthSquared > normal.LengthSquared ) normal = cross;
		}
		var volume = 0.0f;
		foreach ( var point in points )
			volume = Math.Max( volume, Math.Abs( Vector3.Dot( normal, point - origin ) ) );
		if ( !float.IsFinite( volume ) || volume <= 1e-10f )
			throw Error( "convex is degenerate or has invalid volume." );
	}

	private InvalidDataException Error( string message ) => new( $"{SourceName}: PHY {message}" );
	private int Range( long offset, long size )
	{
		if ( size < 0 || offset < rangeStart || offset > rangeEnd || size > rangeEnd - offset )
			throw Error( $"data range {offset}+{size} lies outside its owning structure ({rangeStart}..{rangeEnd})." );
		return (int)offset;
	}
	private int Relative( int start, int offset, int size ) => Range( (long)start + offset, size );
	private int Int( int offset ) => BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( Range( offset, 4 ), 4 ) );
	private ushort UShort( int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( Range( offset, 2 ), 2 ) );
	private float Float( int offset )
	{
		var value = BitConverter.Int32BitsToSingle( Int( offset ) );
		if ( !float.IsFinite( value ) )
			throw Error( $"non-finite float at {offset}." );
		return value;
	}
	private Vector3 Vector( int offset ) => new( Float( offset ), Float( offset + 4 ), Float( offset + 8 ) );
	private Vector3 Position( int offset )
	{
		var value = Vector( offset );
		// IVP stores meters with Y down and Z along Source Y. COM is metadata, not a vertex offset.
		var result = new Vector3( value.x, value.z, -value.y ) / 0.0254f;
		if ( !float.IsFinite( result.x ) || !float.IsFinite( result.y ) || !float.IsFinite( result.z ) )
			throw Error( "position overflows Source units." );
		return result;
	}
}
