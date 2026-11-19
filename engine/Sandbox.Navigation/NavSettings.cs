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
	/// Longest outline edge, in cells.
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

	public static NavSettings Create( float agentHeight, float agentRadius, float agentClimb, float agentMaxSlope, float cellSize, float cellHeight, int tileSize = 256 )
	{
		return new NavSettings
		{
			CellSize = cellSize,
			CellHeight = cellHeight,
			TileSize = tileSize,
			AgentHeight = agentHeight,
			AgentRadius = agentRadius,
			AgentClimb = agentClimb,
			AgentMaxSlope = agentMaxSlope,
			MaxEdgeCells = 64,
			MinRegionCells = 64,
			MaxEdgeError = 2
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
		else if ( MaxEdgeCells < 0 ) reason = "MaxEdgeCells must not be negative";
		else if ( MinRegionCells < 0 ) reason = "MinRegionCells must not be negative";
		else if ( !(MaxEdgeError >= 0) || !float.IsFinite( MaxEdgeError ) ) reason = "MaxEdgeError must be finite and not negative";
		else if ( BuildConfig.RadiusCells( this ) > BuildConfig.MaxRadiusCells ) reason = "AgentRadius is more than 127 cells";
		else if ( BuildConfig.HeightCells( this ) > BuildConfig.MaxHeightCells ) reason = "AgentHeight is more than 32767 cells";
		else if ( BuildConfig.ClimbCells( this ) > BuildConfig.MaxHeightCells ) reason = "AgentClimb is more than 32767 cells";
		return reason == null;
	}
}
