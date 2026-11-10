namespace Editor;

public enum SceneCompileSkipReason
{
	None,

	[Title( "not active" )]
	NotActive,
	[Title( "no mesh" )]
	NoMesh,
	[Title( "object doesn't load in game" )]
	NotInGame,
	[Title( "object isn't static" )]
	NotStatic,
	[Title( "driven by a rigidbody" )]
	Rigidbody,
	[Title( "shadows aren't on" )]
	ShadowsDisabled,
	[Title( "animated" )]
	Animated,
	[Title( "has model deformers" )]
	ModelDeformers,
	[Title( "doesn't draw in the world" )]
	NotInWorld,
	[Title( "no model" )]
	NoModel,
	[Title( "model is procedural" )]
	ProceduralModel,
	[Title( "model has no render meshes" )]
	NoRenderMeshes,
	[Title( "material overrides" )]
	MaterialOverrides,
	[Title( "collision isn't active" )]
	CollisionNotActive,
	[Title( "collision has no shapes" )]
	CollisionNoShapes,
	[Title( "collision is driven by a rigidbody" )]
	CollisionRigidbody,
	[Title( "collision isn't static" )]
	CollisionNotStatic,
	[Title( "is a trigger" )]
	Trigger,
	[Title( "has collider flags" )]
	ColliderFlags,
	[Title( "has surface velocity" )]
	SurfaceVelocity,
	[Title( "has physics overrides" )]
	PhysicsOverrides,
	[Title( "collision is a trigger" )]
	CollisionTrigger,
	[Title( "collision has collider flags" )]
	CollisionFlags,
	[Title( "collision has surface velocity" )]
	CollisionSurfaceVelocity,
	[Title( "collision has physics overrides" )]
	CollisionPhysicsOverrides
}
