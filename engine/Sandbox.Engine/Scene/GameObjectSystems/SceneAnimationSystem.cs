using Editor;
using Sandbox.Utility;
using System.Collections.Concurrent;

namespace Sandbox;

[Expose]
public sealed class SceneAnimationSystem : GameObjectSystem<SceneAnimationSystem>
{
	private HashSetEx<SkinnedModelRenderer> SkinnedRenderers { get; } = new();

	/// <summary>
	/// Incremented once per frame when bones are updated. Lets per-frame bone caches - like
	/// <see cref="SkinnedModelRenderer.BoneWorldTransforms"/> - know when they're stale without re-reading
	/// the skeleton more than once per frame.
	/// </summary>
	public int BoneFrame { get; private set; }

	internal void AddRenderer( SkinnedModelRenderer renderer )
	{
		SkinnedRenderers.Add( renderer );
	}

	internal void RemoveRenderer( SkinnedModelRenderer renderer )
	{
		SkinnedRenderers.Remove( renderer );
	}

	private readonly HashSet<SkinnedModelRenderer> _editorForceBindPose = [];
	private readonly HashSet<SkinnedModelRenderer> _editorBindPoseTargets = [];

	private ConcurrentQueue<GameTransform> ChangedTransforms { get; } = new();

	// Reusable lists, so the parallel passes don't allocate every frame.
	private readonly List<SkinnedModelRenderer> _rootRenderers = new();
	private readonly List<SkinnedModelRenderer> _boneMergeRoots = new();
	private readonly List<SkinnedModelRenderer> _units = new();
	private readonly List<SkinnedModelRenderer> _physRenderers = new();

	// The passes use one thread fewer than there are cores.
	private static readonly int AnimationThreads = Math.Max( 1, Environment.ProcessorCount - 1 );

	// Decode cache upkeep runs in the background until the next update, which joins it before decoding again
	private JobHandle _maintainDecodeCaches;

	public SceneAnimationSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.UpdateBones, 0, UpdateAnimation, "UpdateAnimation" );
		Listen( Stage.FinishUpdate, 0, FinishUpdate, "FinishUpdate" );
		Listen( Stage.PhysicsStep, 0, PhysicsStep, "PhysicsStep" );
	}

	internal void UpdateAnimation()
	{
		// Bump once per frame, before any FinishUpdate-stage consumer reads bone snapshots.
		BoneFrame++;

		using ( PerformanceStats.Timings.Animation.Scope() )
		{
			JoinDecodeCacheUpkeep();

			_rootRenderers.Clear();
			_boneMergeRoots.Clear();

			// Nothing animating - no bones to update, no decode caches worth maintaining
			if ( SkinnedRenderers.Count == 0 )
				return;

			CollectEditorBindForcePoses();

			foreach ( var renderer in SkinnedRenderers.EnumerateLocked() )
			{
				// Drive viseme morphs from any speaking voice before bones/morphs update
				if ( renderer.VoiceActive )
					renderer.UpdateVoice();

				if ( renderer.IsRootRenderer )
					_rootRenderers.Add( renderer );

				if ( !renderer.BoneMergeTarget.IsValid() && renderer.HasBoneMergeChildren )
					_boneMergeRoots.Add( renderer );
			}

			GroupUnits();

			// Skip out if we have a parent that is a skinned model, because we need to move relative to that
			// and their bones haven't been worked out yet. They will get worked out after our parent is.
			// Units are claimed one at a time: work per unit is highly uneven (clothed characters have many
			// bone-merged children), so static range partitioning would cause severe thread idle time.
			JobDispatcher.For( _units.Count, this, static ( system, i ) => system.ProcessUnit( system._units[i] ), itemsPerBatch: 1, maxDegreeOfParallelism: AnimationThreads );

			_editorForceBindPose.Clear();

			// This is a good time to maintain decode caches
			// Will copy local caches to the global cache and handle LRU eviction
			// Runs in the background, joined before this scene's next animation pass. Native locking serializes it with
			// any other animation evaluation meanwhile.
			_maintainDecodeCaches = JobDispatcher.Begin( 1, 0, static ( _, _ ) => g_pAnimationSystemUtils.MaintainDecodeCaches() );

			// Now merge what the units didn't, without allocating per-merge delegates
			if ( _boneMergeRoots.Count > 0 )
				JobDispatcher.For( _boneMergeRoots.Count, this, static ( system, i ) => system._boneMergeRoots[i].MergeDescendants( system.ChangedTransforms ), itemsPerBatch: 1, maxDegreeOfParallelism: AnimationThreads );

			while ( ChangedTransforms.TryDequeue( out var tx ) )
			{
				tx.TransformChanged( true );
			}

			//
			// Run events in the main thread
			//
			foreach ( var x in SkinnedRenderers.EnumerateLocked() )
			{
				// A renderer whose own update was skipped this frame has no graph and no animations: nothing to raise
				if ( x.AnimatedFrame == BoneFrame && x.OwnUpdateSkipped )
					continue;

				x.DispatchEvents();
			}
		}
	}

	void JoinDecodeCacheUpkeep()
	{
		// Cleared first, a handle can only be joined once even if the upkeep threw
		var pending = _maintainDecodeCaches;
		_maintainDecodeCaches = default;
		pending.Join();
	}

	public override void Dispose()
	{
		try
		{
			JoinDecodeCacheUpkeep();
		}
		finally
		{
			base.Dispose();
		}
	}

	/// <summary>
	/// Collects all the bind pose previews from the editor selection and adds them to the _editorForceBindPose set.
	/// </summary>
	private void CollectEditorBindForcePoses()
	{
		_editorForceBindPose.Clear();
		_editorBindPoseTargets.Clear();

		// Important! editor only!
		if ( !Scene.IsEditor ) return;

		// And active editor only!
		if ( Scene.Editor is not { IsActive: true } editor )
			return;

		foreach ( var selected in editor.Selection )
		{
			if ( selected is GameObject gameObject && gameObject.IsValid() )
			{
				foreach ( var component in gameObject.Components.GetAll() )
				{
					TryAddEditorForceBindPose( component );
				}
			}
			else if ( selected is Component component )
			{
				TryAddEditorForceBindPose( component );
			}
		}

		foreach ( var target in _editorBindPoseTargets )
		{
			if ( !target.IsValid() || target.Scene != Scene || !target.Active )
			{
				continue;
			}

			_editorForceBindPose.Add( target.RootBoneMergeTarget );
		}

		_editorBindPoseTargets.Clear();
	}

	void TryAddEditorForceBindPose( Component component )
	{
		if ( !component.IsValid() ) return;
		if ( component.Scene != Scene ) return;
		if ( component is not ISkinnedModelEditor preview ) return;

		preview.AddBindPosePreviewTargets( _editorBindPoseTargets );
	}

	/// <summary>
	/// Group this frame's roots into units, each animated on one thread in one go: a root, the roots bone-merged onto
	/// something in its hierarchy (clothing), then the merges whose every renderer is in the unit. A merge reaching
	/// outside its unit waits for the pass to finish, as every merge used to, so it never reads a pose mid-update.
	/// What a unit's merges write is only read inside the unit, so the poses come out as if every update ran first.
	/// </summary>
	void GroupUnits()
	{
		var frame = BoneFrame;
		_units.Clear();

		foreach ( var root in _rootRenderers )
		{
			root.AnimationUnitFrame = frame;
			root.AnimationUnitHead = null;
			root.FirstAttachedRoot = root.NextAttachedRoot = null;
			root.FirstUnitMerge = root.NextUnitMerge = null;
		}

		// A root merged onto another root's hierarchy joins that root's unit
		foreach ( var root in _rootRenderers )
		{
			var head = AttachHead( root, frame );
			if ( head is null )
			{
				_units.Add( root );
				continue;
			}

			root.AnimationUnitHead = head;
			root.NextAttachedRoot = head.FirstAttachedRoot;
			head.FirstAttachedRoot = root;
		}

		// Merges kept inside one unit run at its end; the rest stay in the list for afterwards
		var kept = 0;
		for ( int i = 0; i < _boneMergeRoots.Count; i++ )
		{
			var mergeRoot = _boneMergeRoots[i];
			var head = UnitOf( mergeRoot, frame, requireUpdated: true );
			if ( head is not null && MergesWithin( mergeRoot, head, frame ) )
			{
				mergeRoot.NextUnitMerge = head.FirstUnitMerge;
				head.FirstUnitMerge = mergeRoot;
				continue;
			}

			_boneMergeRoots[kept++] = mergeRoot;
		}

		_boneMergeRoots.RemoveRange( kept, _boneMergeRoots.Count - kept );
	}

	/// <summary>
	/// The unit head a root joins: the root of the hierarchy its bone merge target's chain ends in, when that's another
	/// root animated this frame that isn't merged onto anything itself.
	/// </summary>
	static SkinnedModelRenderer AttachHead( SkinnedModelRenderer root, int frame )
	{
		if ( !root.BoneMergeTarget.IsValid() ) return null;

		var target = HierarchyRoot( root.RootBoneMergeTarget );
		if ( target is null || target == root || target.AnimationUnitFrame != frame || target.BoneMergeTarget.IsValid() )
			return null;

		return target;
	}

	/// <summary>
	/// The root at the top of a renderer's hierarchy, or null if the chain is implausibly deep.
	/// </summary>
	static SkinnedModelRenderer HierarchyRoot( SkinnedModelRenderer renderer )
	{
		for ( int depth = 0; renderer is not null && depth < 256; depth++ )
		{
			var parent = renderer.SkinnedParent;
			if ( parent is null ) return renderer;
			renderer = parent;
		}

		return null;
	}

	/// <summary>
	/// The unit a renderer is animated in this frame, or null when it isn't in one. With <paramref name="requireUpdated"/>,
	/// null too when the unit won't reach it (it or something above it in its hierarchy is off), so it isn't updated.
	/// </summary>
	static SkinnedModelRenderer UnitOf( SkinnedModelRenderer renderer, int frame, bool requireUpdated )
	{
		var root = renderer;
		for ( int depth = 0; ; depth++ )
		{
			if ( depth >= 256 ) return null;
			if ( requireUpdated && (!root.IsValid() || !root.Enabled) ) return null;

			var parent = root.SkinnedParent;
			if ( parent is null ) break;
			root = parent;
		}

		if ( root.AnimationUnitFrame != frame ) return null;
		return root.AnimationUnitHead ?? root;
	}

	/// <summary>
	/// Whether every renderer merged onto <paramref name="target"/>, all the way down, is animated in the unit.
	/// </summary>
	static bool MergesWithin( SkinnedModelRenderer target, SkinnedModelRenderer head, int frame )
	{
		foreach ( var child in target.BoneMergeChildren )
		{
			if ( !child.IsValid() ) continue;
			if ( UnitOf( child, frame, requireUpdated: false ) != head ) return false;
			if ( !MergesWithin( child, head, frame ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// A unit: its root's hierarchy, the roots merged onto it, then its merges. The links go as they're walked, so
	/// nothing keeps a renderer that's gone.
	/// </summary>
	void ProcessUnit( SkinnedModelRenderer head )
	{
		ProcessRenderer( head );

		var root = head.FirstAttachedRoot;
		head.FirstAttachedRoot = null;
		while ( root is not null )
		{
			ProcessRenderer( root );

			var next = root.NextAttachedRoot;
			root.NextAttachedRoot = null;
			root.AnimationUnitHead = null;
			root = next;
		}

		var mergeRoot = head.FirstUnitMerge;
		head.FirstUnitMerge = null;
		while ( mergeRoot is not null )
		{
			mergeRoot.MergeDescendants( ChangedTransforms );

			var next = mergeRoot.NextUnitMerge;
			mergeRoot.NextUnitMerge = null;
			mergeRoot = next;
		}
	}

	void ProcessRenderer( SkinnedModelRenderer renderer )
	{
		if ( !renderer.IsValid() || !renderer.Enabled )
			return;

		bool forceBindPose = _editorForceBindPose.Count > 0 && _editorForceBindPose.Contains( renderer.RootBoneMergeTarget );

		if ( renderer.AnimationUpdate( forceBindPose ) )
		{
			ChangedTransforms.Enqueue( renderer.Transform );
		}

		renderer.AnimatedFrame = BoneFrame;

		foreach ( var child in renderer.SkinnedChildren )
		{
			ProcessRenderer( child );
		}
	}

	void FinishUpdate()
	{
		using var _ = PerformanceStats.Timings.Animation.Scope();

		foreach ( var renderer in SkinnedRenderers.EnumerateLocked() )
		{
			renderer.FinishUpdate();
		}
	}

	void PhysicsStep()
	{
		using var _ = PerformanceStats.Timings.Animation.Scope();

		_physRenderers.Clear();

		foreach ( var renderer in SkinnedRenderers.EnumerateLocked() )
		{
			if ( renderer.Physics != null )
				_physRenderers.Add( renderer );
		}

		JobDispatcher.For( _physRenderers.Count, _physRenderers, static ( renderers, i ) => renderers[i].Physics.Step(), itemsPerBatch: 1, maxDegreeOfParallelism: AnimationThreads );
	}
}
