using System.Collections.Concurrent;

namespace Sandbox;

/// <summary>
/// Renders a skinned model in the world. A skinned model is any model with bones/animations.
/// </summary>
[Title( "Model Renderer (skinned)" )]
[Category( "Rendering" )]
[Icon( "sports_martial_arts" )]
[Alias( "AnimatedModelComponent" )]
public sealed partial class SkinnedModelRenderer : ModelRenderer, Component.ExecuteInEditor
{
	bool _createBones = false;

	[Property, Group( "Bones", StartFolded = true )]
	public bool CreateBoneObjects
	{
		get => _createBones;
		set
		{
			if ( _createBones == value ) return;
			_createBones = value;

			UpdateObject();
		}
	}

	SkinnedModelRenderer _boneMergeTarget;

	[Property, Group( "Bones" )]
	public SkinnedModelRenderer BoneMergeTarget
	{
		get => _boneMergeTarget;
		set
		{
			if ( value == this ) return;
			if ( _boneMergeTarget == value ) return;

			_boneMergeTarget?.RemoveBoneMergeChild( this );

			_boneMergeTarget = value;

			_boneMergeTarget?.AddBoneMergeChild( this );
		}
	}

	bool _useAnimGraph = true;

	/// <summary>
	/// Usually used for turning off animation on ragdolls.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Use Animation Graph" )]
	public bool UseAnimGraph
	{
		get => _useAnimGraph;
		set
		{
			if ( _useAnimGraph == value ) return;

			_useAnimGraph = value;

			if ( SceneModel.IsValid() )
			{
				SceneModel.UseAnimGraph = value;
			}
		}
	}

	AnimationGraph _animationGraph;

	/// <summary>
	/// Override animgraph, otherwise uses animgraph of the model.
	/// </summary>
	[Property, Group( "Animation" ), ShowIf( nameof( UseAnimGraph ), true )]
	public AnimationGraph AnimationGraph
	{
		get => _animationGraph;
		set
		{
			if ( _animationGraph == value ) return;

			_animationGraph = value;

			if ( SceneModel.IsValid() )
			{
				SceneModel.AnimationGraph = value;
			}
		}
	}

	/// <summary>
	/// Allows playback of sequences directly, rather than using an animation graph.
	/// Requires <see cref="UseAnimGraph"/> disabled if the scene model has one.
	/// </summary>
	[Property, Group( "Animation" ), ShowIf( nameof( ShouldShowSequenceEditor ), true ), InlineEditor( Label = false )]
	public SequenceAccessor Sequence
	{
		get
		{
			_sequence ??= new SequenceAccessor( this );
			return _sequence;
		}
	}

	float _playbackRate = 1.0f;

	/// <summary>
	/// Control playback rate of animgraph or current sequence.
	/// </summary>
	[Property, Range( 0.0f, 4.0f ), Group( "Animation" )]
	public float PlaybackRate
	{
		get => _playbackRate;
		set
		{
			if ( _playbackRate == value ) return;

			_playbackRate = value;

			if ( SceneModel.IsValid() )
			{
				SceneModel.PlaybackRate = _playbackRate;
			}
		}
	}

	public SceneModel SceneModel => _sceneObject as SceneModel;

	public Transform RootMotion => SceneModel.IsValid() ? SceneModel.RootMotion : default;

	readonly HashSet<SkinnedModelRenderer> _mergeChildren = new();

	internal bool HasBoneMergeChildren => _mergeChildren.Count > 0;

	/// <summary>
	/// Does our model have collision and joints.
	/// </summary>
	bool HasBonePhysics()
	{
		return Model.IsValid() && Model.Physics is { Parts.Count: > 0, Joints.Count: > 0 };
	}

	private void AddBoneMergeChild( SkinnedModelRenderer newChild )
	{
		ArgumentNullException.ThrowIfNull( newChild );

		_mergeChildren.Add( newChild );

		// Merge immediately if we can. This prevents a problem where components
		// are added after the animation has been worked out, so you get a one frame
		// flicker of the default pose.
		if ( SceneModel is not null && newChild.SceneModel is not null )
		{
			newChild.SceneModel.Transform = SceneModel.Transform;
			newChild.SceneModel.MergeBones( SceneModel );

			// Updated bones, transform is no longer dirty.
			newChild._transformDirty = false;

			// Create bone physics on child if they exist.
			newChild.Physics?.Destroy();
			newChild.Physics = newChild.HasBonePhysics() ? new BonePhysics( newChild, this ) : null;

			if ( !newChild.UpdateGameObjectsFromBones() )
				return;

			if ( ThreadSafe.IsMainThread )
			{
				newChild.Transform.TransformChanged();
			}
		}
	}

	private void RemoveBoneMergeChild( SkinnedModelRenderer oldChild )
	{
		ArgumentNullException.ThrowIfNull( oldChild );

		_mergeChildren.Remove( oldChild );

		oldChild.Physics?.Destroy();
		oldChild.Physics = null;
	}

	private HashSet<SkinnedModelRenderer> _skinnedChildren = new();
	private SkinnedModelRenderer _skinnedParent = null;

	internal bool IsRootRenderer => _skinnedParent == null;

	internal IEnumerable<SkinnedModelRenderer> SkinnedChildren => _skinnedChildren;

	private void UpdateSkinnedRendererParent()
	{
		// Get the first ancestor skinned model renderer
		var potentialNewParent = GameObject.Parent?.Components.GetInAncestors<SkinnedModelRenderer>( true );

		// Check if there are any other skinned renderers in the parent
		// This is an edge case, generally you should only have one skinned model renderer per GO
		var otherPotentialParents = potentialNewParent?.GetComponents<SkinnedModelRenderer>( true );
		if ( otherPotentialParents != null && otherPotentialParents.Count() > 1 )
		{
			// Make sure only one of the skinned renderers contains children
			foreach ( var parentSibling in otherPotentialParents.Where( x => x != potentialNewParent ) )
			{
				potentialNewParent._skinnedChildren.UnionWith( parentSibling._skinnedChildren );
				parentSibling._skinnedChildren.Clear();
			}
		}

		_skinnedParent?._skinnedChildren.Remove( this );

		if ( potentialNewParent != null )
		{
			_skinnedParent = potentialNewParent;
			_skinnedParent._skinnedChildren.Add( this );
		}
	}

	protected override void OnParentChanged( GameObject oldParent, GameObject newParent )
	{
		UpdateSkinnedRendererParent();
	}

	protected override void OnEnabled()
	{
		Assert.True( _sceneObject == null, "_sceneObject should be null - disable wasn't called" );
		Assert.NotNull( Scene, "Scene should not be null" );

		UpdateSkinnedRendererParent();
		Scene.GetSystem<SceneAnimationSystem>().AddRenderer( this );

		var model = Model ?? Model.Load( "models/dev/box.vmdl" );

		var so = new SceneModel( Scene.SceneWorld, model, WorldTransform );
		_sceneObject = so;

		if ( AnimationGraph is not null )
		{
			so.AnimationGraph = AnimationGraph;
		}

		if ( so.UseAnimGraph != UseAnimGraph )
		{
			so.UseAnimGraph = UseAnimGraph;
		}

		so.PlaybackRate = PlaybackRate;

		OnSceneObjectCreated( _sceneObject );

		Transform.OnTransformChanged += OnTransformChanged;
	}

	protected override void OnDisabled()
	{
		UpdateSkinnedRendererParent();
		Scene.GetSystem<SceneAnimationSystem>().RemoveRenderer( this );
	}

	internal override void OnSceneObjectCreated( SceneObject o )
	{
		base.OnSceneObjectCreated( o );

		ApplyStoredAnimParameters();

		Morphs.Apply();
		Sequence.Apply();
	}

	protected override void UpdateObject()
	{
		BuildBoneHierarchy();

		base.UpdateObject();

		if ( !SceneModel.IsValid() )
			return;

		SceneModel.OnFootstepEvent = InternalOnFootstep;
		SceneModel.OnSoundEvent = InternalOnSoundEvent;
		SceneModel.OnGenericEvent = InternalOnGenericEvent;
		SceneModel.OnAnimTagEvent = InternalOnAnimTagEvent;

		//
		// If we have a bone merge target then just set up the bone merge
		// which will read the bones and set the game object positions.
		//
		// If we're not bone merge, then do a first frame update to set
		// the bone positions before anything else happens.
		//
		if ( _boneMergeTarget is not null )
		{
			_boneMergeTarget.AddBoneMergeChild( this );
		}
		else
		{
			if ( Scene.IsEditor && !CanUpdateInEditor() )
			{
				SceneModel.UpdateToBindPose( ReadBonesFromGameObjects );
			}
			else
			{
				UpdateTransform( WorldTransform );
			}

			// Updated bones, transform is no longer dirty.
			_transformDirty = false;

			UpdateGameObjectsFromBones();
		}
	}

	internal override void OnDisabledInternal()
	{
		try
		{
			ClearBoneProxies();
		}
		finally
		{
			Transform.OnTransformChanged -= OnTransformChanged;

			base.OnDisabledInternal();
		}

		Physics?.Destroy();
	}

	[Obsolete]
	public void PostAnimationUpdate()
	{
		ThreadSafe.AssertIsMainThread();

		if ( !SceneModel.IsValid() )
			return;

		SceneModel.RunPendingEvents();
		SceneModel.DispatchTagEvents();

		// Skip if we're bone merged, the target will handle the merge.
		if ( _boneMergeTarget.IsValid() )
			return;

		// Bone merge all children in hierarchy in order.
		MergeDescendants();
	}

	internal void DispatchEvents()
	{
		ThreadSafe.AssertIsMainThread();

		if ( !SceneModel.IsValid() )
			return;
		SceneModel.RunPendingEvents();
		SceneModel.DispatchTagEvents();
	}

	/// <summary>
	/// If true then animations will play while in an editor scene.
	/// </summary>
	public bool PlayAnimationsInEditorScene { get; set; }

	internal bool CanUpdateInEditor()
	{
		if ( PlayAnimationsInEditorScene ) return true;

		// Do we have any modified animgraph parameters?
		if ( parameters.Count > 0 )
			return true;

		// If we're not using animgraph, do we have a sequence selected?
		if ( !UseAnimGraph && !string.IsNullOrWhiteSpace( Sequence.Name ) )
			return true;

		// Have we procedurally moved any bones?
		return SceneModel.IsValid() && SceneModel.HasBoneOverrides();
	}

	internal bool AnimationUpdate()
	{
		if ( !SceneModel.IsValid() )
			return false;

		SceneModel.Transform = WorldTransform;

		lock ( this )
		{
			// Update physics bones if they exist.
			Physics?.Update();

			if ( Scene.IsEditor && !CanUpdateInEditor() )
			{
				SceneModel.UpdateToBindPose( ReadBonesFromGameObjects );
			}
			else
			{
				SceneModel.Update( Time.Delta, ReadBonesFromGameObjects );
			}
		}

		// Updated bones, transform is no longer dirty.
		_transformDirty = false;

		// Skip if we're bone merged, the target will handle the merge.
		return !_boneMergeTarget.IsValid() && UpdateGameObjectsFromBones();
	}

	bool _transformDirty;

	void OnTransformChanged()
	{
		// Check transform because we could get a false positive.
		_transformDirty = SceneModel.IsValid() && SceneModel.Transform != WorldTransform;
	}

	internal void FinishUpdate()
	{
		// Debug draw physics world if it exists.
		Physics?.DebugDraw();

		if ( !_transformDirty )
			return;

		// Skip if we're bone merged, the target will handle the merge.
		if ( _boneMergeTarget.IsValid() )
			return;

		// Transform changed, make sure bones are updated.
		UpdateTransform( WorldTransform );

		// Updated bones, transform is no longer dirty.
		_transformDirty = false;

		// Update all bone merge children to new transform.
		MergeDescendants();
	}

	void UpdateTransform( Transform transform )
	{
		if ( !SceneModel.IsValid() )
			return;

		SceneModel.Transform = transform;
		ReadBonesFromGameObjects();
		SceneModel.FinishBoneUpdate();
	}

	/// <summary>
	/// For Procedural Bones, copy the current value to the animation bone
	/// </summary>
	void ReadBonesFromGameObjects()
	{
		foreach ( var entry in boneToGameObject )
		{
			if ( !entry.Value.Flags.Contains( GameObjectFlags.ProceduralBone ) )
				continue;

			// Ignore absolute bones, they're probably physics bones
			if ( entry.Value.Flags.Contains( GameObjectFlags.Absolute ) )
				continue;

			var localTransform = entry.Value.LocalTransform;
			if ( localTransform.IsValid )
			{
				SceneModel.SetParentSpaceBone( entry.Key.Index, localTransform );
			}
		}
	}

	private SkinnedModelRenderer RootBoneMergeTarget => BoneMergeTarget.IsValid() ? BoneMergeTarget.RootBoneMergeTarget : this;

	/// <summary>
	/// For non procedural bones, copy the "parent space" bone from to the GameObject transform. Will
	/// return true if any transforms have changed.
	/// </summary>
	bool UpdateGameObjectsFromBones()
	{
		bool transformsChanged = false;

		var mergeTarget = RootBoneMergeTarget;

		// The offset between our transform and root target.
		Transform? mergeOffset = mergeTarget.IsValid() ? WorldTransform.ToLocal( mergeTarget.WorldTransform ) : default;

		foreach ( var entry in boneToGameObject )
		{
			// Ignore procedural bones, local transform is set manually.
			if ( entry.Value.Flags.Contains( GameObjectFlags.ProceduralBone ) )
				continue;

			// Ignore absolute bones, they're probably physics bones.
			if ( entry.Value.Flags.Contains( GameObjectFlags.Absolute ) )
				continue;

			var transform = SceneModel.GetParentSpaceBone( entry.Key.Index );
			if ( !transform.IsValid )
				continue;

			// Offset root bones to move us to the root target.
			if ( mergeOffset.HasValue && entry.Key.Parent is null )
			{
				transform = mergeOffset.Value.ToWorld( transform );
			}

			transformsChanged |= entry.Value.Transform.SetLocalTransformFast( transform );
		}

		foreach ( var entry in attachmentToGameObject )
		{
			var transform = SceneModel.GetAttachment( entry.Key.Name, false );
			if ( !transform.HasValue )
				continue;

			// Offset root attachments to move us to the root target.
			if ( mergeOffset.HasValue && entry.Key.Bone is null )
			{
				transform = mergeOffset.Value.ToWorld( transform.Value );
			}

			transformsChanged |= entry.Value.Transform.SetLocalTransformFast( transform.Value );
		}

		return transformsChanged;
	}

	internal void MergeDescendants( ConcurrentQueue<GameTransform> changedTransforms = null )
	{
		foreach ( var child in _mergeChildren )
		{
			if ( !child.IsValid() )
				continue;

			var so = child.SceneModel;
			if ( !so.IsValid() )
				continue;

			var target = child.BoneMergeTarget;
			if ( !target.IsValid() )
				continue;

			var parent = target.SceneModel;
			if ( !parent.IsValid() )
				continue;

			so.Transform = parent.Transform;
			so.MergeBones( parent );

			// Updated bones, transform is no longer dirty.
			child._transformDirty = false;

			if ( child.UpdateGameObjectsFromBones() )
			{
				if ( changedTransforms is not null )
				{
					changedTransforms.Enqueue( child.Transform );
				}
				else if ( ThreadSafe.IsMainThread )
				{
					child.Transform.TransformChanged();
				}
			}

			child.MergeDescendants( changedTransforms );
		}
	}

	public Transform? GetAttachment( string name, bool worldSpace = true )
	{
		return SceneModel?.GetAttachment( name, worldSpace );
	}
}
