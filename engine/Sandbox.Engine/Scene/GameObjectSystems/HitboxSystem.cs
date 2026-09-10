
namespace Sandbox;

/// <summary>
/// Ticks the physics in FrameStage.PhysicsStep
/// </summary>
[Expose]
sealed class HitboxSystem : GameObjectSystem<HitboxSystem>, GameObjectSystem.ITraceProvider
{
	[ConVar( "debug_hitbox", ConVarFlags.Protected | ConVarFlags.Cheat )]
	static bool Debug { get; set; }

	private PhysicsWorld _physicsWorld;

	public PhysicsWorld PhysicsWorld => _physicsWorld ??= new PhysicsWorld();

	public HitboxSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.UpdateBones, 100, InvalidateHitboxes, "InvalidateHitboxes" );
		Listen( Stage.FinishUpdate, -1, DrawDebug, "DrawDebug" );
	}

	public override void Dispose()
	{
		_physicsWorld?.Delete();
		_physicsWorld = null;
	}

	bool hitboxesDirty;

	void InvalidateHitboxes()
	{
		hitboxesDirty = true;
	}

	void UpdateHitboxPositions()
	{
		if ( !hitboxesDirty )
			return;

		lock ( this )
		{
			if ( !hitboxesDirty )
				return;

			hitboxesDirty = false;

			// these could be foreach parallel!!

			foreach ( var group in Scene.GetAllComponents<ModelHitboxes>() )
			{
				group.UpdatePositions();
			}

			foreach ( var group in Scene.GetAllComponents<ManualHitbox>() )
			{
				group.UpdatePositions();
			}
		}
	}

	public void DoTrace( in SceneTrace trace, List<SceneTraceResult> results )
	{
		if ( !trace.IncludeHitboxes )
			return;

		UpdateHitboxPositions();

		var traceResults = PhysicsWorld.RunTraceAll( trace.PhysicsTrace );

		foreach ( var traceResult in traceResults )
		{
			var sceneResult = SceneTraceResult.From( Scene, traceResult );
			sceneResult.Hitbox = traceResult.Body.Hitbox as Hitbox;
			sceneResult.Body = null;
			sceneResult.Shape = null;
			sceneResult.GameObject = sceneResult.Hitbox.GameObject;
			results.Add( sceneResult );
		}
	}

	public SceneTraceResult? DoTrace( in SceneTrace trace )
	{
		if ( !trace.IncludeHitboxes )
			return null;

		UpdateHitboxPositions();

		var tr = PhysicsWorld.RunTrace( trace.PhysicsTrace );

		if ( !tr.Hit )
			return null;

		var result = SceneTraceResult.From( Scene, tr );
		result.Hitbox = (Hitbox)tr.Body.Hitbox;
		result.Body = null;
		result.Shape = null;
		result.GameObject = result.Hitbox.GameObject;
		result.Bone = result.Hitbox?.Bone?.Index ?? tr.Bone;
		return result;
	}

	private void DrawDebug()
	{
		if ( !Debug ) return;

		foreach ( var group in Scene.GetAllComponents<ModelHitboxes>() )
		{
			DrawDebug( group );
		}

		foreach ( var group in Scene.GetAllComponents<ManualHitbox>() )
		{
			DrawDebug( group );
		}
	}

	private void DrawDebug( ModelHitboxes hitboxes )
	{
		using var _ = Gizmo.Scope();
		Gizmo.Draw.Color = Color.Orange;

		hitboxes.UpdatePositions();

		foreach ( var hitbox in hitboxes.Hitboxes )
		{
			DrawDebug( hitbox );
		}
	}

	private void DrawDebug( ManualHitbox hitbox )
	{
		using var _ = Gizmo.Scope();
		Gizmo.Draw.Color = Color.Orange;

		hitbox.UpdatePositions();

		if ( hitbox.Hitbox is null )
			return;

		DrawDebug( hitbox.Hitbox );
	}

	private void DrawDebug( Hitbox hitbox )
	{
		using ( Gizmo.ObjectScope( hitbox, hitbox.Body.Transform ) )
		{
			foreach ( var shape in hitbox.Body.Shapes )
			{
				if ( shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
				{
					Gizmo.Draw.LineSphere( shape.Sphere.Center, shape.Sphere.Radius );
				}
				else if ( shape.ShapeType == PhysicsShapeType.SHAPE_CAPSULE )
				{
					Gizmo.Draw.LineCapsule( new( shape.Capsule.CenterA, shape.Capsule.CenterB, shape.Capsule.Radius ) );
				}
				else if ( shape.ShapeType == PhysicsShapeType.SHAPE_HULL )
				{
					Gizmo.Draw.Lines( shape.GetOutline() );
				}
			}
		}
	}
}
