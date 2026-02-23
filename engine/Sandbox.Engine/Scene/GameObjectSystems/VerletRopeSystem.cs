namespace Sandbox;

/// <summary>
/// Simulates VerletRope components in parallel during PrePhysicsStep
/// </summary>
internal sealed class VerletRopeGameSystem : GameObjectSystem
{
	private readonly List<VerletRope> _ropes = new();

	public VerletRopeGameSystem( Scene scene ) : base( scene )
	{
		// Listen to StartFixedUpdate to run before physics
		Listen( Stage.StartFixedUpdate, -100, UpdateRopes, "UpdateRopes" );
	}

	void UpdateRopes()
	{
		_ropes.Clear();
		Scene.GetAll<VerletRope>( _ropes );
		if ( _ropes.Count == 0 ) return;

		var timeDelta = Time.Delta;
		Sandbox.Utility.Parallel.ForEach( _ropes, rope => rope.Simulate( timeDelta ) );
	}
}
