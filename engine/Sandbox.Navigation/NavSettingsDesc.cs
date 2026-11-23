namespace Nav;

/// <summary>
/// What NavSettings.Create needs: the agent and the cells. Every field is chosen by the caller.
/// Distances are in the caller's units, see NavSettings.
/// </summary>
public struct NavSettingsDesc
{
	public required float AgentHeight, AgentRadius, AgentClimb;

	/// <summary>
	/// Steepest walkable slope in degrees.
	/// </summary>
	public required float AgentMaxSlope;

	public required float CellSize, CellHeight;

	/// <summary>
	/// Cells per tile edge, a multiple of 16 from 32 to 1024. It also sets MaxTilePolygons and with it how
	/// many tiles the mesh can hold.
	/// </summary>
	public required int TileSize;
}
