using Sandbox.Engine.Resources;
using Sandbox.Volumes;

namespace Sandbox.Navigation;

internal abstract class NavMeshSpatialAuxiliaryData
{
	// Either scale, position, rotation or something else changed
	public bool HasChanged;

	public bool IsPendingRemoval = false;

	public bool IsBlocked = false;

	public NavMeshAreaDefinition AreaDefinition;

	public IEnumerable<Vector2Int> CurrentOverlappingTiles => currentOverlappingTiles;

	private HashSet<Vector2Int> currentOverlappingTiles = new();

	public IEnumerable<Vector2Int> PreviousOverlappingTiles => previousOverlappingTiles;

	private HashSet<Vector2Int> previousOverlappingTiles = new();

	protected abstract RectInt CalculateCurrentOverlappingTiles( NavMesh navMesh );

	internal void UpdateOverlappingTiles( NavMesh navMesh )
	{
		previousOverlappingTiles.Clear();
		foreach ( var tile in currentOverlappingTiles )
		{
			previousOverlappingTiles.Add( tile );
		}
		currentOverlappingTiles.Clear();

		var minMaxTileCoord = CalculateCurrentOverlappingTiles( navMesh );

		for ( int x = minMaxTileCoord.Left; x <= minMaxTileCoord.Right; x++ )
		{
			for ( int y = minMaxTileCoord.Top; y <= minMaxTileCoord.Bottom; y++ )
			{
				currentOverlappingTiles.Add( new Vector2Int( x, y ) );
			}
		}
	}
}

internal class NavMeshAreaData : NavMeshSpatialAuxiliaryData
{
	public BBox WorldBounds;

	public BBox LocalBounds;

	public Transform Transform;

	public SceneVolume Volume;

	protected override RectInt CalculateCurrentOverlappingTiles( NavMesh navMesh )
	{
		if ( Volume.Type == SceneVolume.VolumeTypes.Infinite ) return navMesh.CalculateMinMaxTileCoords( navMesh.Bounds );

		return navMesh.CalculateMinMaxTileCoords( WorldBounds );
	}
}
