using Sandbox.Utility;

namespace Sandbox;

/// <summary>
/// Updates interpolation for any <see cref="GameTransform"/> that needs it.
/// </summary>
[Expose]
sealed class InterpolationSystem : GameObjectSystem<InterpolationSystem>
{
	HashSetEx<GameObject> _list { get; set; } = new();

	[ConVar( "debug_interp", ConVarFlags.Protected | ConVarFlags.Cheat )]
	static bool Debug { get; set; }

	public InterpolationSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.Interpolation, -1, Update, "UpdateInterpolation" );
	}

	/// <summary>
	/// Add a <see cref="GameObject"/> to the interpolation list.
	/// </summary>
	internal void AddGameObject( GameObject go )
	{
		_list.Add( go );
	}

	/// <summary>
	/// Remove a <see cref="GameObject"/> from the interpolation list.
	/// </summary>
	internal void RemoveGameObject( GameObject go )
	{
		_list.Remove( go );
	}

	private void Update()
	{
		foreach ( var go in _list.EnumerateLocked( true ) )
		{
			if ( go.IsValid() )
			{
				go.Transform.Update();

				if ( Debug )
				{
					DrawDebug( go );
				}
			}
		}
	}

	private void DrawDebug( GameObject go )
	{
		using var _ = Gizmo.Scope();
		var targetWorld = go.WorldTransform;
		var world = go.Transform.InterpolatedWorld;

		Gizmo.Draw.Color = Color.White;
		Gizmo.Draw.LineSphere( world.Position, 16f );

		Gizmo.Draw.Color = Color.Cyan;
		Gizmo.Draw.LineSphere( targetWorld.Position, 16f );
	}
}
