namespace Nav;

public sealed partial class NavCrowd
{
	// The mesh state the current Step works on, kept to notice the next change.
	MeshState? pinned;

	uint step;
	float dt;
	Action<int, Action<int>>? run;

	// The stages that run through the run hook. Sync, the snapshot and binning run on the calling thread.
	enum Stage { Plan, Move, Separate, Constrain }

	// The fewest agents a body is worth starting for in the stages that look at neighbours. Planning
	// and constraining do less per agent and take two shares. Results do not depend on it; tests set
	// it to 1 so that a small crowd uses every body.
	internal int Share = 64;

	Stage stage;
	int cursor;
	readonly Action<int> work;

	/// <summary>
	/// Advances every agent by delta. run( count, body ), when given, must call body( i ) once for each
	/// i below count, at the same time on different threads if it can, and return when all have
	/// returned; count is never more than the crowd's workers. run is called several times in a Step,
	/// and not at all for a crowd too small to be worth it. Without run the step stays on the
	/// calling thread. The result is the same either way and for any number of workers. A mesh Commit
	/// during the step is harmless: the step works on the state it started with.
	/// </summary>
	public void Step( float delta, Action<int, Action<int>>? run = null )
	{
		if ( !(delta > 0) || !float.IsFinite( delta ) || high == 0 ) return;
		step++;
		this.run = run;
		Sync( delta );

		// Bound the movement of one substep, and of a step after a long pause.
		int substeps = 1;
		dt = delta;
		if ( SubstepTime > 0 )
		{
			int most = Math.Max( 1, MaxSubsteps );
			substeps = (int)Math.Clamp( MathF.Ceiling( delta / SubstepTime ), 1f, most );
			dt = MathF.Min( delta, most * SubstepTime ) / substeps;
		}

		for ( int n = 0; n < substeps; n++ )
		{
			tallies.AsSpan().Clear();
			Run( Stage.Plan );
			Snapshot();
			Run( Stage.Move );
			Separate();
		}
		this.run = null;
	}

	// Sync: pin the mesh state. When it changed, every corridor and wall cache is checked again. Island
	// maps are not asked for here: a search gets its own when it needs one. An External agent's
	// velocity is how far the caller has moved it since the last Step, unless the caller tells it.
	void Sync( float delta )
	{
		var s = mesh.State;
		bool changed = s != pinned;
		pinned = s;
		for ( int i = 0; i < high; i++ )
		{
			ref var a = ref agents[i];
			if ( !a.Has( AgentFlags.Active ) ) continue;
			if ( changed )
			{
				a.Flags |= AgentFlags.Validate;
				a.WallCount = -1;
			}
			if ( !parms[i].External ) continue;
			if ( !a.Has( AgentFlags.OwnVelocity ) ) states[i].Velocity = (states[i].Position - a.Seen) / delta;
			a.Seen = states[i].Position;
		}
	}

	// No more bodies than the stage has work for: handing a body out costs about what a body does
	// for a share of agents, so a stage with less than two shares stays on the calling thread.
	void Run( Stage next )
	{
		stage = next;
		cursor = 0;
		int bodies = Math.Min( workers, high / (next is Stage.Move or Stage.Separate ? Share : Share * 2) );
		if ( run == null || bodies <= 1 ) Work( 0 );
		else run( bodies, work );
	}

	// One body of a stage: claims batches of agent ids from one cursor until none are left, and does
	// the stage's work for each agent. Every stage reads the pinned state and the frame and writes
	// only the agent's own slots, so it does not matter which body gets which batch.
	void Work( int worker )
	{
		var query = queries[worker];
		var s = pinned!;
		bool pushed = false;
		float reach = Reach;
		Tally tally = default;
		while ( true )
		{
			int start = Interlocked.Add( ref cursor, Batch ) - Batch;
			if ( start >= high || start < 0 ) break;
			for ( int i = start, end = Math.Min( start + Batch, high ); i < end; i++ )
			{
				ref readonly var a = ref agents[i];
				if ( !a.Has( AgentFlags.Active ) )
				{
					if ( stage == Stage.Plan ) shown.Id[i] = -1;
					continue;
				}
				bool onLink = states[i].Link >= 0, external = parms[i].External;
				switch ( stage )
				{
					// Agents on a link have nothing to plan: the move stage takes them across.
					case Stage.Plan:
						if ( !onLink )
						{
							Plan( i, s, query );
							Publish( i );
						}
						Show( i, ref tally, reach );
						break;
					case Stage.Move when onLink:
						WalkLink( i );
						break;
					case Stage.Move when !external && a.Stride >= 0:
						Walk( i, s, query );
						break;
					case Stage.Separate when !external && slots[i] >= 0:
						pushed |= Separate( i );
						break;
					case Stage.Constrain when slots[i] >= 0 && a.Push != default:
						MoveOnMesh( i, s, query, a.Push );
						break;
				}
			}
		}
		if ( stage == Stage.Plan ) tallies[worker] = tally;
		if ( pushed ) Volatile.Write( ref overlapped, 1 );
	}
}
