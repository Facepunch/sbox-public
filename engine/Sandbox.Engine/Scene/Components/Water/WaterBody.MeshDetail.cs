namespace Sandbox;

public sealed partial class WaterBody
{
	const int MaxClipmapLevels = 20;

	/// <summary>
	/// The fewest cells per level. With fewer, a coarser level's inner edge can fall inside its own
	/// morph range, so it would no longer match the finer level's edge.
	/// </summary>
	const int MinClipmapCells = 16;

	/// <summary>
	/// Hysteresis on the base cell's power-of-two shift, in octaves of camera distance.
	/// </summary>
	const float ClipmapShiftHysteresis = 0.2f;

	/// <summary>
	/// Wave mesh density: 0 is low, 1 is good, and 2 is great. Every level renders waves.
	/// This client setting only changes rendering quality.
	/// It does not change buoyancy, swimming, or CPU wave queries.
	/// </summary>
	[ConVar( "r.water.detail", Saved = true, Min = 0, Max = 2,
		Help = "Water mesh density: 0 = low, 1 = good, 2 = great." )]
	internal static int Detail { get; set; } = 2;

	/// <summary>
	/// Clamp direct assignments as well as console input at the quality policy boundary.
	/// </summary>
	internal static int DetailLevel => Math.Clamp( Detail, 0, 2 );

	bool RenderWaves => HasWaves;

	/// <summary>
	/// Signed depth below the rendered surface in world inches.
	/// </summary>
	internal float ViewSurfaceDepth( Vector3 worldPosition )
	{
		var point = ToSurface( worldPosition );
		return (RenderWaves ? SampleHeight( point ) : 0) - point.z;
	}

	/// <summary>
	/// The shape of the surface mesh: square levels of <see cref="Cells"/> cells around the camera, with the
	/// cell size doubling at each level outward from <see cref="BaseCell"/>.
	/// <see cref="Shift"/> is the power of two that coarsened the base cell for a distant camera.
	/// </summary>
	internal readonly record struct ClipmapLayout( int Cells, float BaseCell, int Levels, int Shift )
	{
		/// <summary>
		/// Cells from a level's centre to the edge of its mesh. The extra four cells cover the coarser level's
		/// hole wherever the two levels' snapped centres sit; the shader clamps the excess onto the hole's edge.
		/// </summary>
		public int Extent => Cells / 2 + 4;

		/// <summary>
		/// Cells from a level's centre to the edge of the hole left for the next finer level.
		/// </summary>
		public int Hole => Cells / 4 + 1;
	}

	/// <summary>
	/// Choose the surface mesh for the viewing camera. Its base cell follows the camera's distance to the
	/// nearest water, a small body gets smaller levels, and enough levels are added to reach the far side
	/// of a finite body or the view's edge. Water without waves uses sparse levels: it only needs triangles
	/// small enough near the camera that interpolating the surface position across them stays precise.
	/// </summary>
	ClipmapLayout GetClipmap( CameraComponent viewer )
	{
		var half = new Vector2( MathF.Abs( Size.x * WorldScale.x ) * 0.5f, MathF.Abs( Size.y * WorldScale.y ) * 0.5f );
		var eye = viewer.IsValid() ? ToSurface( viewer.WorldPosition ) : Vector3.Zero;
		float nearest = Ocean ? MathF.Abs( eye.z )
			: new Vector3( MathF.Max( 0, MathF.Abs( eye.x ) - half.x ), MathF.Max( 0, MathF.Abs( eye.y ) - half.y ), eye.z ).Length;

		// Orthographic zoom controls apparent detail; moving that camera backwards does not.
		if ( viewer.IsValid() && viewer.Orthographic )
			nearest = viewer.OrthographicHeight * 0.5f;

		float reach = Ocean ? ViewReach( viewer )
			: MathF.Max( MathF.Abs( eye.x ) + half.x, MathF.Abs( eye.y ) + half.y );

		float size = Ocean ? float.PositiveInfinity : MathF.Max( half.x, half.y ) * 2;
		return SelectClipmap( DetailLevel, RenderWaves, nearest, reach, size, _builtLayout );
	}

	/// <summary>
	/// Pick a clipmap for a quality level, the camera's distance to the nearest water and how far the
	/// mesh must reach from the camera. Waves start at four-inch cells at great quality, eight at good and
	/// sixteen at low, halving the cells per level each step down. Water without waves uses sixty-four-inch
	/// cells and the fewest cells per level. The rings
	/// keep cells a steady size on screen, so only a camera further away than the finest level reaches gets
	/// coarser base cells. A body smaller than a level gets the fewest cells per level that cover it, so a
	/// small pool doesn't pay for a level of vertices squashed onto its walls. The base cell and level count
	/// keep their previous values near a boundary, so hovering doesn't rebuild the mesh.
	/// </summary>
	internal static ClipmapLayout SelectClipmap( int quality, bool waves, float nearest, float reach, float size, ClipmapLayout previous )
	{
		quality = Math.Clamp( quality, 0, 2 );
		int maxCells = waves ? MinClipmapCells << quality : MinClipmapCells;
		float detailCell = waves ? 16 >> quality : 64;
		if ( previous.Levels > 0 && previous.BaseCell != detailCell * MathF.Pow( 2, previous.Shift ) )
			previous = default;

		float octave = MathF.Log2( MathF.Max( nearest, 1 ) / (maxCells / 2 * detailCell) );
		int shift = Math.Clamp( (int)MathF.Ceiling( octave ), 0, 30 );
		if ( previous.Levels > 0 && octave > previous.Shift - 1 - ClipmapShiftHysteresis && octave <= previous.Shift + ClipmapShiftHysteresis )
			shift = previous.Shift;

		float baseCell = detailCell * MathF.Pow( 2, shift );

		int cells = MinClipmapCells;
		while ( cells < maxCells && cells * baseCell < size )
			cells *= 2;

		// A level's snapped centre is within one of its cells of the camera, so it reaches Extent - 1 cells.
		float span = (cells / 2 + 3) * baseCell;
		int levels = 1 + Math.Max( 0, (int)MathF.Ceiling( MathF.Log2( MathF.Max( reach, 1 ) / span ) ) );
		levels = Math.Min( levels, MaxClipmapLevels );

		// Keep one spare level rather than rebuilding as the camera crosses a level's reach.
		if ( previous.Levels > 0 && shift == previous.Shift && previous.Levels == levels + 1 )
			levels = previous.Levels;

		return new ClipmapLayout( cells, baseCell, levels, shift );
	}

	/// <summary>
	/// Distance from the camera that an ocean must cover: its far plane, or a wider frustum corner.
	/// </summary>
	static float ViewReach( CameraComponent viewer )
	{
		float radius = 1024;
		if ( !viewer.IsValid() )
			return radius;

		radius = MathF.Max( radius, viewer.ZFar );
		var frustum = viewer.GetFrustum();
		for ( int i = 0; i < 8; i++ )
		{
			if ( frustum.GetCorner( i ) is { } corner )
				radius = MathF.Max( radius, (corner - viewer.WorldPosition).Length );
		}
		return radius;
	}

	/// <summary>
	/// Vertices and indices for every clipmap level. Each vertex stores its cell coordinates from the level's
	/// centre in TexCoord0 and its level in TexCoord1; the shader places it around the camera. Positions are a
	/// rough placement for editor picking. Every level but the finest leaves a hole for the next finer level.
	/// </summary>
	internal static (Vertex[] Vertices, int[] Indices) BuildClipmap( ClipmapLayout layout, Vector2 half )
	{
		int extent = layout.Extent;
		int side = extent * 2 + 1;
		int hole = layout.Hole;
		int holeCells = hole * 2;
		int cellsPerLevel = extent * 2 * extent * 2;

		var vertices = new Vertex[side * side * layout.Levels];
		var indices = new int[(cellsPerLevel * layout.Levels - holeCells * holeCells * (layout.Levels - 1)) * 6];
		int index = 0;
		for ( int level = 0; level < layout.Levels; level++ )
		{
			int first = level * side * side;
			float cell = layout.BaseCell * MathF.Pow( 2, level );
			for ( int y = 0; y < side; y++ )
			{
				for ( int x = 0; x < side; x++ )
				{
					var grid = new Vector2( x - extent, y - extent );
					var position = Vector2.Min( Vector2.Max( grid * cell, -half ), half );
					vertices[first + y * side + x] = new Vertex( new Vector3( position.x, position.y, 0 ), Vector3.Up, Vector3.Forward,
						new Vector4( grid.x, grid.y, 0, 0 ) )
					{ TexCoord1 = new Vector4( level, 0, 0, 0 ) };
				}
			}

			for ( int y = 0; y < side - 1; y++ )
			{
				for ( int x = 0; x < side - 1; x++ )
				{
					if ( level > 0 && x - extent >= -hole && x - extent < hole && y - extent >= -hole && y - extent < hole )
						continue;

					int a = first + y * side + x;
					int b = a + side;
					indices[index++] = a;
					indices[index++] = a + 1;
					indices[index++] = b;
					indices[index++] = a + 1;
					indices[index++] = b + 1;
					indices[index++] = b;
				}
			}
		}

		return (vertices, indices);
	}

	/// <summary>
	/// The same surface facing down: flipped winding, a downward normal, and TexCoord1.y set so the shader shades
	/// it as the underside.
	/// </summary>
	internal static (Vertex[] Vertices, int[] Indices) BuildUnderside( Vertex[] vertices, int[] indices )
	{
		var underVertices = new Vertex[vertices.Length];
		for ( int i = 0; i < vertices.Length; i++ )
		{
			underVertices[i] = vertices[i];
			underVertices[i].Normal = Vector3.Down;
			underVertices[i].TexCoord1.y = 1;
		}

		var underIndices = new int[indices.Length];
		for ( int i = 0; i < indices.Length; i += 3 )
		{
			underIndices[i] = indices[i];
			underIndices[i + 1] = indices[i + 2];
			underIndices[i + 2] = indices[i + 1];
		}

		return (underVertices, underIndices);
	}
}
