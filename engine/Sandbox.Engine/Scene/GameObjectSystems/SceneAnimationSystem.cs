using Sandbox.Utility;
using System.Collections.Concurrent;

namespace Sandbox;

[Expose]
public sealed class SceneAnimationSystem : GameObjectSystem<SceneAnimationSystem>
{
	private HashSetEx<SkinnedModelRenderer> SkinnedRenderers { get; } = new();

	internal void AddRenderer( SkinnedModelRenderer renderer )
	{
		SkinnedRenderers.Add( renderer );
	}

	internal void RemoveRenderer( SkinnedModelRenderer renderer )
	{
		SkinnedRenderers.Remove( renderer );
	}

	private ConcurrentQueue<GameTransform> ChangedTransforms { get; } = new();

	// Reusable lists to avoid per-frame allocations from Parallel.ForEach with IEnumerable<T>.
	// Parallel.ForEach on IEnumerable uses a dynamic partitioner that allocates KeyValuePair<long, T>[]
	// chunks internally; passing an IList<T> uses the static range partitioner instead.
	private readonly List<SkinnedModelRenderer> _rootRenderers = new();
	private readonly List<SkinnedModelRenderer> _boneMergeRoots = new();
	private readonly List<SkinnedModelRenderer> _physRenderers = new();

	private static int _animThreadCount = Math.Max( 1, Environment.ProcessorCount - 1 );

	private static ParallelOptions _animParallelOptions = new()
	{
		MaxDegreeOfParallelism = _animThreadCount
	};

	public SceneAnimationSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.UpdateBones, 0, UpdateAnimation, "UpdateAnimation" );
		Listen( Stage.FinishUpdate, 0, FinishUpdate, "FinishUpdate" );
		Listen( Stage.PhysicsStep, 0, PhysicsStep, "PhysicsStep" );
	}

	void UpdateAnimation()
	{
		using ( PerformanceStats.Timings.Animation.Scope() )
		{
			_rootRenderers.Clear();
			_boneMergeRoots.Clear();

			foreach ( var renderer in SkinnedRenderers.EnumerateLocked() )
			{
				if ( renderer.IsRootRenderer )
					_rootRenderers.Add( renderer );

				if ( !renderer.BoneMergeTarget.IsValid() && renderer.HasBoneMergeChildren )
					_boneMergeRoots.Add( renderer );
			}

			// Skip out if we have a parent that is a skinned model, because we need to move relative to that
			// and their bones haven't been worked out yet. They will get worked out after our parent is.
			System.Threading.Tasks.Parallel.ForEach( _rootRenderers, _animParallelOptions, ProcessRenderer );

			// This is a good time to maintain decode caches
			// Will copy local caches to the global cache and handle LRU eviction
			// Can do this in a background task as nothing is touching these caches until next frame
			Task.Run( g_pAnimationSystemUtils.MaintainDecodeCaches );

			// Now merge any descendants without allocating per-merge delegates
			System.Threading.Tasks.Parallel.ForEach( _boneMergeRoots, _animParallelOptions, renderer => renderer.MergeDescendants( ChangedTransforms ) );

			while ( ChangedTransforms.TryDequeue( out var tx ) )
			{
				tx.TransformChanged( true );
			}

			//
			// Run events in the main thread
			//
			foreach ( var x in SkinnedRenderers.EnumerateLocked() )
			{
				x.DispatchEvents();
			}
		}
	}

	void ProcessRenderer( SkinnedModelRenderer renderer )
	{
		if ( !renderer.IsValid() || !renderer.Enabled )
			return;

		if ( renderer.AnimationUpdate() )
		{
			ChangedTransforms.Enqueue( renderer.Transform );
		}

		foreach ( var child in renderer.SkinnedChildren )
		{
			ProcessRenderer( child );
		}
	}

	void FinishUpdate()
	{
		foreach ( var renderer in SkinnedRenderers.EnumerateLocked() )
		{
			renderer.FinishUpdate();
		}
	}

	void PhysicsStep()
	{
		_physRenderers.Clear();

		foreach ( var renderer in SkinnedRenderers.EnumerateLocked() )
		{
			if ( renderer.Physics != null )
				_physRenderers.Add( renderer );
		}

		System.Threading.Tasks.Parallel.ForEach( _physRenderers, _animParallelOptions, renderer => renderer.Physics.Step() );
	}
}
