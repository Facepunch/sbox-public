using System.Numerics;
using Nav.Build;

namespace Nav;

/// <summary>
/// Runs tile jobs: triangles, or the field the mesh kept, to the arrays of a tile. Owns all the memory
/// that takes, so use one per worker thread and reuse it for every job.
/// </summary>
public sealed class NavBuilder
{
	// The stages and what they leave behind, readable for tests and measurements until the next Build
	internal readonly SolidField Solid = new();
	internal readonly CompactField Field = new();
	internal readonly FieldBuilder FieldBuilder = new();
	internal readonly OpenBlocks Open = new();
	internal readonly RegionBuilder Regions = new();
	internal readonly ContourBuilder Contours = new();
	internal readonly PolyBuilder Polys = new();
	internal readonly TileAssembler Assembler = new();

	/// <summary>
	/// Triangles touching the tile that were rasterized as ground, and those that were not because they
	/// face down, are too steep or have no area.
	/// </summary>
	internal int TrianglesUsed, TrianglesRejected;

	/// <summary>
	/// Where the field of the last build sits. MinZ is the world height of floor 0.
	/// </summary>
	internal FieldFrame Frame;

	readonly CachedField unpacked = new();
	byte[] raw = [];
	byte[] packed = [];

	/// <summary>
	/// Reads the job, writes the job. Bad geometry and exceeded limits end the job Failed with Error
	/// set, as does a job that needs geometry and was not given any. A job given no triangles builds to
	/// an empty tile. A codec that throws while compressing ends the job Failed too, with its message.
	/// A kept field the codec does not hand back, or throws on, ends the job NeedsGeometry: commit it and
	/// the tile comes back as a job that asks for triangles.
	/// Once warm it allocates only the arrays of the tile it produces and, for a tile that volumes
	/// overlap, of the field the mesh keeps.
	/// </summary>
	public void Build( TileJob job ) => Build( job, job.Vertices.Span, job.Indices.Span, job.HasGeometry );

	internal void Build( TileJob job, ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices ) => Build( job, vertices, indices, true );

	void Build( TileJob job, ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, bool hasGeometry )
	{
		var status = Run( job, vertices, indices, hasGeometry, out var tile, out string? error );

		// A field that came back from the codec is used only when its checksum says it is the one that went
		// in. Should a build from it fail all the same, the tile is built from geometry instead: if the field
		// was not the reason, that build fails as well and says why.
		if ( status == TileJobStatus.Failed && job.Cached?.Packed != null )
		{
			status = TileJobStatus.NeedsGeometry;
			error = null;
		}
		job.Complete( status, tile, error );
	}

	TileJobStatus Run( TileJob job, ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, bool hasGeometry, out Tile? tile, out string? error )
	{
		tile = null;
		var mesh = job.Mesh;
		if ( mesh == null )
		{
			error = "The job was not taken from a mesh";
			return TileJobStatus.Failed;
		}

		var settings = mesh.Settings;
		if ( !BuildConfig.TryCreate( settings, out var config, out error ) ) return TileJobStatus.Failed;
		ref var frame = ref Frame;
		frame = config.Frame( job.X, job.Y );

		// Either way the field ends up eroded with the area in each span's connections. Without anything
		// walkable in it the tile is empty.
		bool walkable;
		if ( job.Cached != null )
		{
			// A field that does not come back from the codec as it went in is rebuilt from geometry
			var field = Unpack( job.Cached, mesh.Codec, config.FieldSize );
			if ( field == null ) return TileJobStatus.NeedsGeometry;
			OpenField( field );
			frame.MinZ = field.OriginZ;
			walkable = true;
		}
		else
		{
			if ( !hasGeometry )
			{
				error = "The job needs geometry and SetGeometry was not called";
				return TileJobStatus.Failed;
			}
			if ( !Prepare( vertices, indices, job.MinZ, job.MaxZ, config, ref frame, out walkable, out error ) ) return TileJobStatus.Failed;
			if ( walkable )
			{
				Rasterize( vertices, indices, job.MinZ, job.MaxZ, config, frame );
				walkable = Solid.SpanCount > 0;
			}
			if ( walkable )
			{
				if ( !FieldBuilder.Compact( Solid, Field, config.WalkableHeight, config.WalkableClimb, out error ) ) return TileJobStatus.Failed;
				walkable = Field.SpanCount > 0 && FieldBuilder.Erode( Field, config.WalkableRadius ) > 0;
			}
		}

		ulong areaHash = 0;
		if ( walkable )
		{
			areaHash = MarkAreas( job.Volumes, frame, config.CellHeight );
			if ( job.Cached != null && areaHash == job.LastAreaHash ) return TileJobStatus.Unchanged;

			Open.Find( Field, config.Border );
			if ( !Regions.Build( Field, Open, config.Border, config.MinRegionCells, out error ) ) return TileJobStatus.Failed;
			if ( !Contours.Build( Field, Open, config.Border, config.MaxEdgeError, config.MaxEdgeLength, out error ) ) return TileJobStatus.Failed;
			if ( Contours.Count > 0 )
			{
				if ( !Polys.Build( Contours, config.TileSize, out error ) ) return TileJobStatus.Failed;
				if ( Polys.PolyCount > 0 )
				{
					tile = Assemble( job.X, job.Y, frame.MinZ, settings, out error );
					if ( tile == null ) return TileJobStatus.Failed;
				}
			}
		}

		job.AreaHash = areaHash;
		if ( job.Cached == null )
		{
			job.TrianglesUsed = TrianglesUsed;
			job.TrianglesRejected = TrianglesRejected;
			if ( job.KeepField && walkable )
			{
				job.Field = Pack( mesh.Codec, frame.MinZ, out error );
				if ( job.Field == null )
				{
					tile = null;
					return TileJobStatus.Failed;
				}
			}
		}
		return TileJobStatus.Built;
	}

	/// <summary>
	/// Checks the triangles and finds the height range of those that touch the tile, which decides where
	/// floor 0 of the field is. any is false when none touches. False with an error for data that cannot
	/// be built from.
	/// </summary>
	internal bool Prepare( ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, float minZ, float maxZ, in BuildConfig config, ref FieldFrame frame, out bool any, out string? error )
	{
		TrianglesUsed = 0;
		TrianglesRejected = 0;
		any = false;
		error = null;
		if ( indices.Length % 3 != 0 )
		{
			error = "The index count is not a multiple of three";
			return false;
		}

		float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
		for ( int t = 0; t < indices.Length; t += 3 )
		{
			int i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
			if ( (uint)i0 >= (uint)vertices.Length || (uint)i1 >= (uint)vertices.Length || (uint)i2 >= (uint)vertices.Length )
			{
				error = "A triangle index is out of range";
				return false;
			}

			Vector3 v0 = vertices[i0], v1 = vertices[i1], v2 = vertices[i2];

			// x - x is zero unless x is infinite or NaN
			if ( (v0 - v0) + (v1 - v1) + (v2 - v2) != Vector3.Zero )
			{
				error = "A vertex is not finite";
				return false;
			}

			var min = Vector3.Min( Vector3.Min( v0, v1 ), v2 );
			var max = Vector3.Max( Vector3.Max( v0, v1 ), v2 );
			if ( !Touches( frame, min, max, minZ, maxZ ) ) continue;
			lowest = MathF.Min( lowest, min.Z );
			highest = MathF.Max( highest, max.Z );
		}
		if ( lowest > highest ) return true;

		// Floor 0 sits two cells below the lowest vertex, on a height grid shared by all tiles
		frame.MinZ = (MathF.Floor( lowest / config.CellHeight ) - 2) * config.CellHeight;
		if ( !((highest - frame.MinZ) * frame.InverseCellHeight < BuildConfig.MaxSpanHeight - 2) )
		{
			error = "The geometry in this tile is more than 65533 cells tall";
			return false;
		}
		any = true;
		return true;
	}

	/// <summary>
	/// The triangles Prepare accepted, as solid spans. Every triangle is an obstacle; one that faces up
	/// within the slope limit is ground as well.
	/// </summary>
	internal void Rasterize( ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, float minZ, float maxZ, in BuildConfig config, in FieldFrame frame )
	{
		Solid.Reset( config.FieldSize, config.FieldSize );
		for ( int t = 0; t < indices.Length; t += 3 )
		{
			Vector3 v0 = vertices[indices[t]], v1 = vertices[indices[t + 1]], v2 = vertices[indices[t + 2]];
			var min = Vector3.Min( Vector3.Min( v0, v1 ), v2 );
			var max = Vector3.Max( Vector3.Max( v0, v1 ), v2 );
			if ( !Touches( frame, min, max, minZ, maxZ ) ) continue;

			// Ground when the unit normal's z is above the cosine of the slope limit. A triangle thinner
			// than a hundred-thousandth of its own size has no direction worth trusting and is not ground.
			// Two things here are as they are only to match today's engine bit for bit, and can go once
			// that comparison is no longer needed: the normal's components are summed in the order x, z, y
			// (its Y-up code), and the triangle is clipped as v0, v2, v1, which decides the cut points.
			Vector3 e1 = v1 - v0, e2 = v2 - v0;
			var normal = Vector3.Cross( e1, e2 );
			var ordered = new Vector3( normal.X, normal.Z, normal.Y );
			bool ground = ordered.LengthSquared() > 1e-10f * e1.LengthSquared() * e2.LengthSquared() && Vector3.Normalize( ordered ).Y > config.WalkableCos;
			if ( ground ) TrianglesUsed++;
			else TrianglesRejected++;

			Rasterizer.Triangle( Solid, frame, v0, v2, v1, ground ? NavFilter.DefaultArea : NavFilter.NoArea, config.WalkableClimb );
		}
	}

	static bool Touches( in FieldFrame frame, Vector3 min, Vector3 max, float minZ, float maxZ )
	{
		return min.X <= frame.MaxX && max.X >= frame.MinX && min.Y <= frame.MaxY && max.Y >= frame.MinY && min.Z <= maxZ && max.Z >= minZ;
	}

	/// <summary>
	/// The area of every span: what erosion left, then the volumes. Returns a hash that stands for all
	/// of them and is never 0.
	/// </summary>
	internal ulong MarkAreas( ReadOnlySpan<NavVolume> volumes, in FieldFrame frame, float cellHeight )
	{
		if ( Field.Area.Length < Field.SpanCount ) Field.Area = new byte[Field.SpanCount + Field.SpanCount / 2];
		var con = Field.Con;
		var area = Field.Area;
		for ( int i = 0, n = Field.SpanCount; i < n; i++ ) area[i] = (byte)(con[i] >> 24);
		foreach ( ref readonly var volume in volumes ) VolumeMarker.Mark( Field, frame, cellHeight, volume );
		return Fnv.Hash( area.AsSpan( 0, Field.SpanCount ) );
	}

	/// <summary>
	/// The polygons of the last stage as a tile: vertices from cells to world, then the tile's arrays.
	/// </summary>
	internal Tile? Assemble( int tileX, int tileY, float originZ, in NavSettings settings, out string? error )
	{
		var cells = Polys.Verts;
		var verts = new Vector3[Polys.VertCount];
		long cellX = (long)tileX * settings.TileSize, cellY = (long)tileY * settings.TileSize;
		// The floor of the field is a whole number of cell heights, so heights too are counted from zero
		long floorRow = (long)Math.Round( originZ / (double)settings.CellHeight );
		for ( int i = 0; i < verts.Length; i++ )
		{
			// From the cell's place in the world, so both tiles at a border get the same number
			verts[i] = new Vector3(
				(float)((cellX + cells[i * 3]) * (double)settings.CellSize),
				(float)((cellY + cells[i * 3 + 1]) * (double)settings.CellSize),
				(float)((floorRow + cells[i * 3 + 2]) * (double)settings.CellHeight) );
		}

		int count = Polys.PolyCount;
		return Assembler.Create( tileX, tileY, verts, Polys.Polys.AsSpan( 0, count * Poly.MaxVerts ), Polys.Neis.AsSpan( 0, count * Poly.MaxVerts ), Polys.Areas.AsSpan( 0, count ), settings, out error );
	}

	/// <summary>
	/// Points the working field at a kept one. Only the column index is rebuilt.
	/// </summary>
	internal void OpenField( CachedField cached )
	{
		int size = cached.Size;
		int columns = size * size;
		if ( Field.Index.Length < columns + 1 ) Field.Index = new int[columns + 1];

		var index = Field.Index;
		var counts = cached.Counts;
		int span = 0;
		for ( int c = 0; c < columns; c++ )
		{
			index[c] = span;
			span += counts[c];
		}
		index[columns] = span;

		Field.Width = size;
		Field.Height = size;
		Field.SpanCount = span;
		Field.Floor = cached.Floors;
		Field.Con = cached.Cons;
	}

	/// <summary>
	/// Copies the field of a build from triangles into target, growing its arrays when they are too small.
	/// </summary>
	internal void CopyFieldTo( CachedField target, float originZ )
	{
		int columns = Field.Width * Field.Height;
		int spanCount = Field.SpanCount;
		if ( target.Counts.Length < columns ) target.Counts = new byte[columns];
		if ( target.Floors.Length < spanCount ) target.Floors = new ushort[spanCount];
		if ( target.Cons.Length < spanCount ) target.Cons = new uint[spanCount];

		var index = Field.Index;
		var counts = target.Counts;
		for ( int c = 0; c < columns; c++ ) counts[c] = (byte)(index[c + 1] - index[c]);
		Array.Copy( Field.Floor, target.Floors, spanCount );
		Array.Copy( Field.Con, target.Cons, spanCount );

		target.Size = Field.Width;
		target.OriginZ = originZ;
		target.SpanCount = spanCount;
	}

	// The field of the build that just ran, in the form the mesh keeps: what the codec makes of it when
	// that is smaller, the arrays otherwise. Null with an error when the codec throws: it is the caller's
	// code, and a job that never ends would be handed out for ever.
	CachedField? Pack( in NavCodec codec, float originZ, out string? error )
	{
		error = null;
		if ( !codec.IsNone )
		{
			CopyFieldTo( unpacked, originZ );
			int size = unpacked.ByteSize;
			if ( raw.Length < size ) raw = new byte[size + size / 2];
			if ( packed.Length < size ) packed = new byte[size + size / 2];
			unpacked.WriteTo( raw );
			int written;
			try
			{
				written = codec.Compress!( raw.AsSpan( 0, size ), packed.AsSpan( 0, size ) );
			}
			catch ( Exception e )
			{
				error = $"The codec threw while compressing: {e.Message}";
				return null;
			}
			if ( written > 0 && written < size ) return new CachedField { Size = unpacked.Size, OriginZ = originZ, SpanCount = unpacked.SpanCount, Packed = packed.AsSpan( 0, written ).ToArray(), Checksum = Fnv.Checksum( raw.AsSpan( 0, size ) ) };
		}

		var plain = new CachedField();
		CopyFieldTo( plain, originZ );
		return plain;
	}

	// The arrays of a kept field. Null when the codec does not hand back the field it was given, or throws.
	CachedField? Unpack( CachedField cached, in NavCodec codec, int fieldSize )
	{
		if ( cached.Packed == null ) return cached;
		if ( codec.IsNone ) return null;

		int size = cached.ByteSize;
		if ( raw.Length < size ) raw = new byte[size + size / 2];
		try
		{
			if ( codec.Decompress!( cached.Packed, raw.AsSpan( 0, size ) ) != size ) return null;
		}
		catch ( Exception )
		{
			return null;
		}

		// The stages index by what the field says. They get it only when it is, byte for byte, what was packed.
		var bytes = raw.AsSpan( 0, size );
		if ( Fnv.Checksum( bytes ) != cached.Checksum ) return null;
		return unpacked.Parse( bytes ) && unpacked.Size == fieldSize && unpacked.SpanCount == cached.SpanCount ? unpacked : null;
	}
}
