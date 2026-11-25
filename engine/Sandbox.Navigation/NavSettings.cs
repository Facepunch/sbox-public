using System.Numerics;
using Nav.Build;

namespace Nav;

/// <summary>
/// The build parameters of one navmesh. Z is up. Distances are in the caller's units.
/// </summary>
public struct NavSettings
{
	/// <summary>
	/// Horizontal and vertical edge of one cell.
	/// </summary>
	public float CellSize, CellHeight;

	/// <summary>
	/// Cells per tile edge. A multiple of 16, from 32 to 1024.
	/// </summary>
	public int TileSize;

	/// <summary>
	/// The radius is at most 127 cells wide, the height and the climb at most 32,767 cells tall.
	/// </summary>
	public float AgentHeight, AgentRadius, AgentClimb;

	/// <summary>
	/// Steepest walkable slope in degrees.
	/// </summary>
	public float AgentMaxSlope;

	/// <summary>
	/// Longest outline edge, in cells, at least 1. int.MaxValue for no limit.
	/// </summary>
	public int MaxEdgeCells;

	/// <summary>
	/// Isolated regions with fewer cells are dropped.
	/// </summary>
	public int MinRegionCells;

	/// <summary>
	/// How far a simplified outline may stray from the cell outline, in cells.
	/// </summary>
	public float MaxEdgeError;

	/// <summary>
	/// The most polygons a tile may hold, from 1 to 65,520. Create allows one for every four cells of the
	/// tile: 16,383 at 256 cells. A tile over the limit fails to build. The limit decides
	/// how many tiles a mesh can hold at once: a polygon id has 32 bits, the polygons of a tile take as
	/// many as the limit needs and the rest number tiles, less the top 65,536 ids, which name links.
	/// 16,383 polygons leave room for 262,140 tiles, 1,023 for 4,194,240. Lower it for a large world
	/// of small or simple tiles, raise it for tiles with many floors.
	/// </summary>
	public int MaxTilePolygons;

	/// <summary>
	/// The fields of desc, MaxEdgeCells 64, MinRegionCells 64, MaxEdgeError 2, and MaxTilePolygons one for
	/// every four cells of a tile, at most 65,520.
	/// </summary>
	public static NavSettings Create( in NavSettingsDesc desc )
	{
		return new NavSettings
		{
			CellSize = desc.CellSize,
			CellHeight = desc.CellHeight,
			TileSize = desc.TileSize,
			AgentHeight = desc.AgentHeight,
			AgentRadius = desc.AgentRadius,
			AgentClimb = desc.AgentClimb,
			AgentMaxSlope = desc.AgentMaxSlope,
			MaxEdgeCells = 64,
			MinRegionCells = 64,
			MaxEdgeError = 2,
			MaxTilePolygons = Math.Min( desc.TileSize * desc.TileSize / 4 - 1, Poly.MaxPerTile )
		};
	}

	/// <summary>
	/// False, with the reason, for settings that make no sense and for those no tile could be built with.
	/// </summary>
	public readonly bool IsValid( out string? reason )
	{
		reason = null;
		if ( !(CellSize > 0) || !float.IsFinite( CellSize ) ) reason = "CellSize must be positive";
		else if ( !(CellHeight > 0) || !float.IsFinite( CellHeight ) ) reason = "CellHeight must be positive";
		// A tile is up to 1024 cells wide and 65,535 tall, and the build multiplies by the inverses
		else if ( !float.IsFinite( CellSize * 1024 ) || !float.IsFinite( 1 / CellSize ) ) reason = "CellSize is too small or too large for a tile";
		else if ( !float.IsFinite( CellHeight * 65535 ) || !float.IsFinite( 1 / CellHeight ) ) reason = "CellHeight is too small or too large for a tile";
		else if ( TileSize < 32 || TileSize > 1024 || TileSize % 16 != 0 ) reason = "TileSize must be a multiple of 16 from 32 to 1024";
		else if ( !(AgentHeight > 0) || !float.IsFinite( AgentHeight ) ) reason = "AgentHeight must be positive";
		else if ( !(AgentRadius >= 0) || !float.IsFinite( AgentRadius ) ) reason = "AgentRadius must not be negative";
		else if ( !(AgentClimb >= 0) || !float.IsFinite( AgentClimb ) ) reason = "AgentClimb must not be negative";
		else if ( !(AgentMaxSlope >= 0) || AgentMaxSlope > 90 ) reason = "AgentMaxSlope must be from 0 to 90 degrees";
		else if ( MaxEdgeCells < 1 ) reason = "MaxEdgeCells must be at least 1";
		else if ( MinRegionCells < 0 ) reason = "MinRegionCells must not be negative";
		else if ( !(MaxEdgeError >= 0) || !float.IsFinite( MaxEdgeError ) ) reason = "MaxEdgeError must be finite and not negative";
		else if ( MaxTilePolygons < 1 || MaxTilePolygons > Poly.MaxPerTile ) reason = "MaxTilePolygons must be from 1 to 65,520";
		else if ( BuildConfig.RadiusCells( this ) > BuildConfig.MaxRadiusCells ) reason = "AgentRadius is more than 127 cells";
		else if ( BuildConfig.HeightCells( this ) > BuildConfig.MaxHeightCells ) reason = "AgentHeight is more than 32767 cells";
		else if ( BuildConfig.ClimbCells( this ) > BuildConfig.MaxHeightCells ) reason = "AgentClimb is more than 32767 cells";
		return reason == null;
	}


	// The bits of a polygon id that number the polygon in its tile. See PolyRef.
	internal readonly int PolyBits => 32 - BitOperations.LeadingZeroCount( (uint)MaxTilePolygons );
}
