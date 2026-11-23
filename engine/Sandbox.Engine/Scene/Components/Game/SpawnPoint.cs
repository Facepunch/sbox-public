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

	/// <summary>
	/// Order of preference. Higher priority spawn points are tried first.
	/// </summary>
	[Property] public int Priority { get; set; }

	/// <summary>
	/// The enabled spawn points in <paramref name="scene"/>, highest <see cref="Priority"/> first, shuffled within each priority.
	/// </summary>
	public static List<SpawnPoint> GetAllByPriority( Scene scene )
	{
		if ( !scene.IsValid() ) return new();

		return scene.GetAllComponents<SpawnPoint>()
			.OrderByDescending( x => x.Priority )
			.ThenBy( _ => Random.Shared.Next() )
			.ToList();
	}

	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		var spawnpointModel = GizmoModel;

		Gizmo.Hitbox.Model( spawnpointModel );
		Gizmo.Draw.Color = Color.WithAlpha( (Gizmo.IsHovered || Gizmo.IsSelected) ? 0.7f : 0.5f );
		Gizmo.Draw.Model( spawnpointModel, global::Transform.Zero, castShadows: true );
	}

}
