namespace Sandbox.Npcs;

/// <summary>
/// Blocks a prop an NPC got stuck against out of the navmesh, so NPCs route round it. One per object; every NPC stuck on
/// it again keeps it a while longer. Gone once the prop is moved.
/// </summary>
internal sealed class NpcDetourBlocker : Component
{
	internal const float Lifetime = 5f;

	NavMeshArea _area;
	Transform _blockedAt;
	TimeSince _sinceRefresh;

	/// <summary>Block this object out of the navmesh, or keep its block for longer.</summary>
	public static void Refresh( GameObject go )
	{
		var blocker = go.GetComponent<NpcDetourBlocker>();
		if ( !blocker.IsValid() )
		{
			blocker = go.AddComponent<NpcDetourBlocker>( false );
			blocker.Flags |= ComponentFlags.Hidden | ComponentFlags.NotSaved | ComponentFlags.NotNetworked | ComponentFlags.NotCloned;
			blocker.Enabled = true;
		}

		blocker._sinceRefresh = 0;
	}

	protected override void OnEnabled()
	{
		_sinceRefresh = 0;
		_blockedAt = WorldTransform;

		// Its solid shapes, held by the body relative to this object
		BBox? bounds = null;
		foreach ( var shape in GetComponent<Rigidbody>()?.PhysicsBody?.Shapes ?? [] )
		{
			if ( shape.EnableSolidCollisions && !shape.IsTrigger )
				bounds = bounds?.AddBBox( shape.LocalBounds ) ?? shape.LocalBounds;
		}

		if ( bounds is not { } box ) return;

		_area = AddComponent<NavMeshArea>( false );
		_area.Flags |= ComponentFlags.Hidden | ComponentFlags.NotSaved | ComponentFlags.NotNetworked | ComponentFlags.NotCloned;
		_area.IsBlocker = true;

		// An agent radius off it, as from a wall
		_area.Margin = Scene.NavMesh.AgentRadius;
		_area.SceneVolume = new Volumes.SceneVolume { Type = Volumes.SceneVolume.VolumeTypes.Box, Box = box.Scale( 1f / _blockedAt.SafeScale ) };
		_area.Enabled = true;
	}

	protected override void OnDisabled()
	{
		if ( _area.IsValid() ) _area.Destroy();
		_area = null;
	}

	protected override void OnUpdate()
	{
		// Moved, it may be out of the way now, and its navmesh would be rebuilt as it goes
		if ( _sinceRefresh > Lifetime || WorldPosition.Distance( _blockedAt.Position ) > 4f || WorldRotation.Distance( _blockedAt.Rotation ) > 5f )
			Destroy();
	}
}
