namespace Sandbox;

/// <summary>
/// Dictates where players will spawn when they join the game when using a NetworkHelper.
/// </summary>
[Expose]
[Title( "Spawn Point" )]
[Category( "Game" )]
[Icon( "accessibility_new" )]
[EditorHandle( "materials/gizmo/spawnpoint.png" )]
public sealed class SpawnPoint : Component
{
	static Model GizmoModel => field ??= Model.Load( "models/dev/playerstart_tint.vmdl" );

	[Property] public Color Color { get; set; } = "#E3510D";
	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		var spawnpointModel = GizmoModel;

		Gizmo.Hitbox.Model( spawnpointModel );
		Gizmo.Draw.Color = Color.WithAlpha( (Gizmo.IsHovered || Gizmo.IsSelected) ? 0.7f : 0.5f );
		Gizmo.Draw.Model( spawnpointModel, global::Transform.Zero, castShadows: true );
	}

}


