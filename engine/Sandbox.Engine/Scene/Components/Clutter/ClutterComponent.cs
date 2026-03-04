namespace Sandbox.Clutter;

/// <summary>
/// Clutter scattering component supporting both infinite and volumes.
/// </summary>
[Icon( "forest" )]
[EditorHandle( Icon = "forest" )]
public sealed partial class ClutterComponent : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Clutter generation mode.
	/// </summary>
	public enum ClutterMode
	{
		[Icon( "inventory_2" ), Description( "Scatter clutter within a defined volume" )]
		Volume,

		[Icon( "all_inclusive" ), Description( "Stream clutter infinitely around the camera" )]
		Infinite
	}

	/// <summary>
	/// The clutter containing objects to scatter and scatter settings.
	/// </summary>
	[Property]
	public ClutterDefinition Clutter { get; set; }

	/// <summary>
	/// Seed for deterministic generation. Change to get different variations.
	/// </summary>
	[Property]
	public int Seed { get; set; }

	/// <summary>
	/// Clutter generation mode - Volume or Infinite streaming.
	/// </summary>
	[Property]
	public ClutterMode Mode
	{
		get => field;
		set
		{
			if ( field == value ) return;
			Clear();
			field = value;
		}
	}

	protected override void OnEnabled()
	{
		if ( Mode == ClutterMode.Volume )
		{
			RebuildVolumeLayer();
		}
	}

	protected override void OnDisabled()
	{
		Clear();
	}

	protected override void OnUpdate()
	{
		if ( Mode == ClutterMode.Volume )
		{
			UpdateVolumeProgress();
		}
	}

	protected override void DrawGizmos()
	{
		if ( Mode == ClutterMode.Volume )
		{
			DrawVolumeGizmos();
		}
	}
}
