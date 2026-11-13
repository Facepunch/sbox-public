namespace Nav.Build;

/// <summary>
/// The settings of one navmesh in cells, as the build stages use them.
/// </summary>
internal struct BuildConfig
{
	public const int MaxSpanHeight = 0xFFFF;
	public const int NotConnected = 63;
	public const int VertsPerPoly = Poly.MaxVerts;
	public const int MaxPolys = PolyRef.MaxPolys;

	// The builder's polygons use the tile's encoding: NoIndex for no vertex and for a wall, BorderEdge
	// plus the side for an edge on the tile border, sides as in Poly.
	public const ushort NoIndex = Poly.Wall;
	public const ushort BorderEdge = Poly.Border;

	public float CellSize, CellHeight;
	public int TileSize;
	public int Border;
	public int FieldSize;
	public int WalkableHeight, WalkableClimb, WalkableRadius;
	public float WalkableCos;
	public int MaxEdgeLength;
	public float MaxEdgeError;
	public int MinRegionCells;

	public static bool TryCreate( in NavSettings settings, out BuildConfig config, out string? error )
	{
		config = default;
		if ( !settings.IsValid( out error ) ) return false;

		config.CellSize = settings.CellSize;
		config.CellHeight = settings.CellHeight;
		config.TileSize = settings.TileSize;
		config.WalkableHeight = HeightCells( settings );
		config.WalkableClimb = ClimbCells( settings );
		config.WalkableRadius = RadiusCells( settings );
		config.WalkableCos = MathF.Cos( settings.AgentMaxSlope / 180.0f * MathF.PI );
		config.MaxEdgeLength = settings.MaxEdgeCells;
		config.MaxEdgeError = settings.MaxEdgeError;
		config.MinRegionCells = settings.MinRegionCells;
		config.Border = BorderCells( settings );
		config.FieldSize = config.TileSize + config.Border * 2;
		return true;
	}

	// The agent in cells. NavSettings.IsValid holds them to the limits: erosion distances are bytes
	// counting half cells, and connections compare 16-bit heights.
	public const int MaxRadiusCells = 127, MaxHeightCells = 0x7FFF;

	public static int RadiusCells( in NavSettings settings ) => CeilToCells( settings.AgentRadius / settings.CellSize );
	public static int HeightCells( in NavSettings settings ) => Math.Max( CeilToCells( settings.AgentHeight / settings.CellHeight ), 3 );
	public static int ClimbCells( in NavSettings settings ) => FloorToCells( settings.AgentClimb / settings.CellHeight );

	/// <summary>
	/// Where the field of a tile sits in the world: the tile and its border. MinZ is for the build to set.
	/// </summary>
	public readonly FieldFrame Frame( int tileX, int tileY ) => new()
	{
		MinX = (float)(((long)tileX * TileSize - Border) * (double)CellSize),
		MinY = (float)(((long)tileY * TileSize - Border) * (double)CellSize),
		MaxX = (float)((((long)tileX + 1) * TileSize + Border) * (double)CellSize),
		MaxY = (float)((((long)tileY + 1) * TileSize + Border) * (double)CellSize),
		CellSize = CellSize,
		InverseCellSize = 1.0f / CellSize,
		InverseCellHeight = 1.0f / CellHeight
	};

	// The cells a tile build reads around the tile: what erosion by the agent radius eats, and three more.
	public static int BorderCells( in NavSettings settings ) => RadiusCells( settings ) + 3;

	static int CeilToCells( float value ) => (int)MathF.Min( MathF.Ceiling( value ), 1 << 20 );
	static int FloorToCells( float value ) => (int)MathF.Min( MathF.Floor( value ), 1 << 20 );
}
