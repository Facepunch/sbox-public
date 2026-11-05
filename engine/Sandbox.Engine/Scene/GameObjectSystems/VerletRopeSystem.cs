namespace Sandbox;

/// <summary>
/// Simulates VerletRope components in parallel during PrePhysicsStep
/// </summary>
internal sealed class VerletRopeGameSystem : GameObjectSystem
{
	private readonly List<VerletRope> _ropes = new();
	private float _timeDelta;

	public VerletRopeGameSystem( Scene scene ) : base( scene )
	{
		// Listen to StartFixedUpdate to run before physics
		Listen( Stage.StartFixedUpdate, -100, UpdateRopes, "UpdateRopes" );
	}

	void UpdateRopes()
	{
		using var _ = PerformanceStats.Timings.Physics.Scope();

		_ropes.Clear();
		Scene.GetAll<VerletRope>( _ropes );
		if ( _ropes.Count == 0 ) return;

		_timeDelta = Time.Delta;
		JobDispatcher.For( _ropes.Count, this, static ( system, i ) => system._ropes[i].Simulate( system._timeDelta ) );
	}
}
