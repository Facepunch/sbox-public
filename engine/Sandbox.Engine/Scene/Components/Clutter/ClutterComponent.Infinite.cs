namespace Sandbox.Clutter;

/// <summary>
/// Infinite/streaming clutter mode
/// </summary>
public sealed partial class ClutterComponent
{
	/// <summary>
	/// Returns true if in infinite streaming mode.
	/// </summary>
	[Hide]
	public bool Infinite => Mode == ClutterMode.Infinite;

	/// <summary>
	/// Gets the current clutter settings for the grid system.
	/// </summary>
	internal ClutterSettings GetCurrentSettings()
	{
		if ( Clutter == null )
			return default;

		return new ClutterSettings( Seed, Clutter );
	}

	/// <summary>
	/// Clears all infinite mode tiles for this component.
	/// </summary>
	public void ClearInfinite()
	{
		var gridSystem = Scene.GetSystem<ClutterGridSystem>();
		gridSystem?.ClearComponent( this );
	}

	/// <summary>
	/// Invalidates the tile at the given world position, causing it to regenerate.
	/// </summary>
	public void InvalidateTileAt( Vector3 worldPosition )
	{
		if ( Mode != ClutterMode.Infinite ) return;

		var gridSystem = Scene.GetSystem<ClutterGridSystem>();
		gridSystem.InvalidateTileAt( this, worldPosition );
	}

	/// <summary>
	/// Invalidates all tiles within the given bounds, causing them to regenerate.
	/// </summary>
	public void InvalidateTilesInBounds( BBox bounds )
	{
		if ( Mode != ClutterMode.Infinite ) return;

		var gridSystem = Scene.GetSystem<ClutterGridSystem>();
		gridSystem.InvalidateTilesInBounds( this, bounds );
	}
}
