using static Sandbox.TerrainClipmap;

namespace Sandbox;

/// <summary>
/// Frustum culls a clipmap layout. Meshlets are sorted into groups of blocks inside each LOD ring, so a whole ring or
/// group that is fully outside or fully inside costs one test, and only blocks on the frustum's edge are tested alone.
/// </summary>
internal sealed class TerrainClipmapCuller
{
	// Blocks per side of a group
	private const int GroupBlocks = 4;

	private enum Containment { Outside, Partial, Inside }

	private struct Range
	{
		public int Start, Count;
		public int Level;
		public Vector2 CellMin, CellMax; // cells from the ring centre, covering every block in the range
	}

	private readonly int _blockSize;
	private readonly Range[] _rings;
	private readonly Range[] _groups; // each ring's groups are contiguous, Start/Count index meshlets
	private readonly int[] _ringGroupStart; // first group of each ring, one past the end at [rings]

	/// <summary>
	/// The layout reordered by ring, then by group. Draw order doesn't matter, so this is the order to upload.
	/// </summary>
	public Meshlet[] Meshlets { get; }

	public TerrainClipmapCuller( Meshlet[] meshlets, int blockSize )
	{
		_blockSize = blockSize;
		int groupCells = blockSize * GroupBlocks;

		Meshlets = meshlets
			.OrderBy( m => m.Level )
			.ThenBy( m => FloorDiv( (int)m.BlockOffset.y, groupCells ) )
			.ThenBy( m => FloorDiv( (int)m.BlockOffset.x, groupCells ) )
			.ToArray();

		var groups = new List<Range>();
		var rings = new List<Range>();
		var ringGroupStart = new List<int>();

		for ( int i = 0; i < Meshlets.Length; )
		{
			ref readonly var first = ref Meshlets[i];
			int gx = FloorDiv( (int)first.BlockOffset.x, groupCells );
			int gy = FloorDiv( (int)first.BlockOffset.y, groupCells );

			var group = new Range { Start = i, Level = first.Level, CellMin = first.BlockOffset, CellMax = first.BlockOffset };

			for ( ; i < Meshlets.Length; i++ )
			{
				ref readonly var m = ref Meshlets[i];
				if ( m.Level != group.Level ) break;
				if ( FloorDiv( (int)m.BlockOffset.x, groupCells ) != gx || FloorDiv( (int)m.BlockOffset.y, groupCells ) != gy ) break;

				group.CellMin = Vector2.Min( group.CellMin, m.BlockOffset );
				group.CellMax = Vector2.Max( group.CellMax, m.BlockOffset );
			}

			group.Count = i - group.Start;
			group.CellMax += new Vector2( blockSize, blockSize );

			if ( rings.Count == 0 || rings[^1].Level != group.Level )
			{
				ringGroupStart.Add( groups.Count );
				rings.Add( group with { Count = 0 } );
			}

			var ring = rings[^1];
			ring.Count += group.Count;
			ring.CellMin = Vector2.Min( ring.CellMin, group.CellMin );
			ring.CellMax = Vector2.Max( ring.CellMax, group.CellMax );
			rings[^1] = ring;

			groups.Add( group );
		}

		ringGroupStart.Add( groups.Count );

		_rings = [.. rings];
		_groups = [.. groups];
		_ringGroupStart = [.. ringGroupStart];
	}

	/// <summary>
	/// Write the frustum's planes in the terrain's local space, so meshlet bounds can be tested without transforming each one.
	/// The normals aren't renormalized; only which side of a plane a point lies on is used.
	/// </summary>
	public static void ToLocalPlanes( in Frustum frustum, in Transform transform, Span<Plane> planes )
	{
		var inverse = transform.Rotation.Inverse;
		var scale = transform.Scale;
		var position = transform.Position;

		planes[0] = ToLocal( frustum.LeftPlane, inverse, scale, position );
		planes[1] = ToLocal( frustum.RightPlane, inverse, scale, position );
		planes[2] = ToLocal( frustum.TopPlane, inverse, scale, position );
		planes[3] = ToLocal( frustum.BottomPlane, inverse, scale, position );
		planes[4] = ToLocal( frustum.NearPlane, inverse, scale, position );
		planes[5] = ToLocal( frustum.FarPlane, inverse, scale, position );

		// World = Position + Rotation * (local * Scale), so n.world - d = (Scale * (Rotation^-1 * n)).local - (d - n.Position)
		static Plane ToLocal( in Plane p, Rotation inverse, Vector3 scale, Vector3 position ) => new()
		{
			Normal = (inverse * p.Normal) * scale,
			Distance = p.Distance - p.Normal.Dot( position ),
		};
	}

	/// <summary>
	/// Write the meshlets whose bounds touch every plane's front side to <paramref name="visible"/>, returning how many.
	/// The bounds match the vertex shader's placement: each ring snaps to twice its cell size around the camera.
	/// </summary>
	public int Cull( ReadOnlySpan<Plane> planes, Vector2 cameraLocal, float unitsPerTexel, float heightScale, Span<Meshlet> visible )
	{
		int count = 0;

		for ( int r = 0; r < _rings.Length; r++ )
		{
			ref readonly var ring = ref _rings[r];

			float vertexStep = unitsPerTexel * (1 << ring.Level);
			float increment = vertexStep * 2.0f;
			var center = cameraLocal.SnapToGrid( increment );

			var containment = Test( planes, ring, center, vertexStep, increment, heightScale );
			if ( containment == Containment.Outside ) continue;

			if ( containment == Containment.Inside )
			{
				Meshlets.AsSpan( ring.Start, ring.Count ).CopyTo( visible[count..] );
				count += ring.Count;
				continue;
			}

			for ( int g = _ringGroupStart[r]; g < _ringGroupStart[r + 1]; g++ )
			{
				ref readonly var group = ref _groups[g];

				containment = Test( planes, group, center, vertexStep, increment, heightScale );
				if ( containment == Containment.Outside ) continue;

				var meshlets = Meshlets.AsSpan( group.Start, group.Count );

				if ( containment == Containment.Inside )
				{
					meshlets.CopyTo( visible[count..] );
					count += meshlets.Length;
					continue;
				}

				foreach ( ref readonly var m in meshlets )
				{
					var block = new Range { CellMin = m.BlockOffset, CellMax = m.BlockOffset + new Vector2( _blockSize, _blockSize ) };

					if ( Test( planes, block, center, vertexStep, increment, heightScale ) != Containment.Outside )
						visible[count++] = m;
				}
			}
		}

		return count;
	}

	private static Containment Test( ReadOnlySpan<Plane> planes, in Range range, Vector2 center, float vertexStep, float increment, float heightScale )
	{
		// Grow by one snap increment so sub-cell rounding and vertex displacement never cull an on-screen block
		var mins = new Vector3(
			center.x + range.CellMin.x * vertexStep - increment,
			center.y + range.CellMin.y * vertexStep - increment,
			-increment );

		var maxs = new Vector3(
			center.x + range.CellMax.x * vertexStep + increment,
			center.y + range.CellMax.y * vertexStep + increment,
			heightScale + increment );

		var containment = Containment.Inside;

		foreach ( ref readonly var p in planes )
		{
			var n = p.Normal;

			// The corner furthest along the normal: behind the plane means the whole box is
			float far = (n.x < 0 ? mins.x : maxs.x) * n.x + (n.y < 0 ? mins.y : maxs.y) * n.y + (n.z < 0 ? mins.z : maxs.z) * n.z;
			if ( far - p.Distance <= 0.0f ) return Containment.Outside;

			// The nearest corner: behind the plane means the box straddles it
			float near = (n.x < 0 ? maxs.x : mins.x) * n.x + (n.y < 0 ? maxs.y : mins.y) * n.y + (n.z < 0 ? maxs.z : mins.z) * n.z;
			if ( near - p.Distance <= 0.0f ) containment = Containment.Partial;
		}

		return containment;
	}

	private static int FloorDiv( int value, int divisor ) => value / divisor - (value % divisor < 0 ? 1 : 0);
}
