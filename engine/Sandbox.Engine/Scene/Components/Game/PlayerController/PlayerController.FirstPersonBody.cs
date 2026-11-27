using Sandbox.Utility;

namespace Sandbox;

public sealed partial class PlayerController
{
	Model _firstPersonBodyModel;
	BoneCollection.Bone _firstPersonChest;
	BoneCollection.Bone _firstPersonNeck;
	bool _firstPersonShowArms;
	// Whether each of the body model's bones is collapsed by the first person view, by bone index
	bool[] _firstPersonUpperBody = [];
	readonly Dictionary<ModelRenderer, FirstPersonBodyCopy> _firstPersonBodyCopies = new();
	readonly Dictionary<ModelRenderer, FirstPersonBodyCopy> _firstPersonShadowCopies = new();
	readonly HashSet<GameObject> _firstPersonHiddenAttachments = new();
	readonly List<ModelRenderer> _firstPersonStaleCopies = new();

	static readonly Superluminal _firstPersonBodyZone = new( "PlayerController.FirstPersonBody", Color.Cyan );
	static readonly Superluminal _firstPersonPreviewZone = new( "PlayerController.FirstPersonPreview", Color.Cyan );

	bool PrepareFirstPersonBody()
	{
		if ( HideBodyInFirstPerson || !Renderer.IsValid() || !Renderer.Model.IsValid() ) return false;

		if ( _firstPersonBodyModel != Renderer.Model || _firstPersonShowArms != ShowArmsInFirstPerson
			|| _firstPersonUpperBody.Length != Renderer.Model.Bones.AllBones.Count )
		{
			// Copies cache which bones are hidden, so rebuild them when the model or arm visibility changes.
			ClearFirstPersonBodyCopies();
			_firstPersonBodyModel = Renderer.Model;
			_firstPersonShowArms = ShowArmsInFirstPerson;
			// Citizen Human's upper chest is the common parent of the neck, head and both arms.
			var bones = _firstPersonBodyModel.Bones;
			_firstPersonChest = bones.GetBone( "spine_2" );
			_firstPersonNeck = bones.GetBone( "neck_0" ) ?? bones.GetBone( "neck_1" ) ?? bones.GetBone( "head" );

			var allBones = bones.AllBones;
			_firstPersonUpperBody = new bool[allBones.Count];
			if ( FirstPersonHiddenRoot is not null )
			{
				for ( int i = 0; i < allBones.Count; i++ )
					_firstPersonUpperBody[i] = IsFirstPersonUpperBodyBone( allBones[i] );
			}
		}

		return FirstPersonHiddenRoot is not null;
	}

	// The bone collapsed by the first person view
	BoneCollection.Bone FirstPersonHiddenRoot => _firstPersonShowArms && _firstPersonNeck is not null ? _firstPersonNeck : _firstPersonChest;

	void ClearFirstPersonBodyCopies()
	{
		StopFirstPersonPreview();

		foreach ( var copy in _firstPersonBodyCopies.Values )
			copy.Object.Delete();
		_firstPersonBodyCopies.Clear();
		_firstPersonHiddenAttachments.Clear();
	}

	void ClearFirstPersonShadowCopies()
	{
		foreach ( var copy in _firstPersonShadowCopies.Values )
			copy.Object.Delete();
		_firstPersonShadowCopies.Clear();
	}

	bool CastsFirstPersonShadow( ModelRenderer renderer, GameObject body )
	{
		return renderer.IsValid() && renderer.Active && renderer.GameObject.IsAncestor( body )
			&& renderer.SceneObject.IsValid() && renderer.SceneObject.Model.IsValid()
			&& renderer.RenderType != ModelRenderer.ShadowRenderType.Off
			// Something the game hid itself keeps its shadow hidden too.
			&& !HasGameViewerTag( renderer.GameObject, body );
	}

	/// <summary>
	/// The viewer tag hides the body's shadow from the viewer along with the body (shadow views use the main view's tags)
	/// Cast it from local shadow-only copies instead: normal pose, no viewer tag, out of the game layer.
	/// Other cameras still draw the original, whose shadow lands in exactly the same place.
	/// </summary>
	void UpdateFirstPersonShadowCopies( GameObject body )
	{
		_firstPersonStaleCopies.Clear();
		foreach ( var (renderer, copy) in _firstPersonShadowCopies )
		{
			if ( CastsFirstPersonShadow( renderer, body ) && renderer.SceneObject.Model == copy.Object.Model ) continue;
			copy.Object.Delete();
			_firstPersonStaleCopies.Add( renderer );
		}
		foreach ( var renderer in _firstPersonStaleCopies )
			_firstPersonShadowCopies.Remove( renderer );
		_firstPersonStaleCopies.Clear();

		foreach ( var renderer in _bodyRenderers )
		{
			if ( !CastsFirstPersonShadow( renderer, body ) ) continue;

			var source = renderer.SceneObject;
			if ( !_firstPersonShadowCopies.TryGetValue( renderer, out var copy ) )
			{
				copy = new FirstPersonBodyCopy( source, Scene.SceneWorld );
				copy.Object.Flags.CastShadows = true;
				copy.Object.Flags.ExcludeGameLayer = true;
				_firstPersonShadowCopies.Add( renderer, copy );
			}

			var target = copy.Object;
			target.Transform = source.Transform;
			target.Flags.CastShadowsTwoSided = source.Flags.CastShadowsTwoSided;
			CopyRenderState( source, target );
			CopyTags( copy, renderer.GameObject.Tags, dropViewer: true, firstPerson: false );

			// Materials matter for alpha-tested shadows (hair, cloth). No RenderOptions: they'd re-include the game layer.
			var materialState = HashCode.Combine( renderer.MaterialOverride, renderer.MaterialGroup, renderer.MaterialOverrideVersion );
			if ( copy.MaterialState != materialState )
			{
				copy.MaterialState = materialState;
				renderer.CopyMaterialsTo( target );
				copy.TagsCopied = false;
			}

			if ( source is SceneModel sourceModel && target is SceneModel targetModel )
			{
				sourceModel.GetBoneWorldTransforms( copy.Bones );
				targetModel.SetBoneWorldTransforms( copy.Bones );

				CopyMorphs( sourceModel, targetModel );
			}

			target.Bounds = source.Bounds;
		}
	}

	/// <summary>
	/// Visibility and mesh groups, written only when they changed: each write is a native call and re-collects the copy's draws.
	/// </summary>
	static void CopyRenderState( SceneObject source, SceneObject target, bool renderingEnabled = true )
	{
		var enabled = renderingEnabled && source.RenderingEnabled;
		if ( target.RenderingEnabled != enabled ) target.RenderingEnabled = enabled;

		var meshGroups = source.MeshGroupMask;
		if ( target.MeshGroupMask != meshGroups ) target.MeshGroupMask = meshGroups;
	}

	static void CopyMorphs( SceneModel source, SceneModel target )
	{
		var morphs = source.Morphs;
		var targetMorphs = target.Morphs;
		var count = source.Model.MorphCount;
		for ( int i = 0; i < count; i++ )
			targetMorphs.Set( i, morphs.Get( i ), 0 );
	}

	/// <summary>
	/// The renderer's tags, less the viewer tag and plus "firstperson" if asked. Written only when the renderer's tags or
	/// what's asked changed: rewriting them every frame would remove and add tags, each a native call and a render update.
	/// </summary>
	static void CopyTags( FirstPersonBodyCopy copy, GameTags tags, bool dropViewer, bool firstPerson )
	{
		var tokens = tags.GetTokens();
		if ( copy.TagsCopied && copy.TagsDropViewer == dropViewer && copy.TagsFirstPerson == firstPerson && copy.Tags.SetEquals( tokens ) )
			return;

		var target = copy.Object.Tags;
		target.SetFrom( tags );
		if ( dropViewer ) target.Remove( "viewer" );
		if ( firstPerson ) target.Add( "firstperson" );

		copy.Tags.Clear();
		copy.Tags.UnionWith( tokens );
		copy.TagsCopied = true;
		copy.TagsDropViewer = dropViewer;
		copy.TagsFirstPerson = firstPerson;
	}

	bool IsFirstPersonUpperBodyBone( BoneCollection.Bone bone )
	{
		var root = FirstPersonHiddenRoot;
		for ( ; bone is not null; bone = bone.Parent )
		{
			if ( bone == root || bone.IsNamed( "neck_clothing" ) ) return true;
			// Hold bones can be constrained to the hands without being parented to the chest.
			// Held items stay visible with the hands when arms are shown.
			if ( !_firstPersonShowArms && (bone.IsNamed( "hold_R" ) || bone.IsNamed( "hold_L" )) ) return true;
		}
		return false;
	}

	void CollectFirstPersonHiddenAttachments()
	{
		_firstPersonHiddenAttachments.Clear();
		var bones = _firstPersonBodyModel.Bones.AllBones;
		for ( int i = 0; i < bones.Count; i++ )
		{
			if ( !_firstPersonUpperBody[i] ) continue;
			var go = Renderer.GetBoneObject( bones[i] );
			if ( go.IsValid() ) _firstPersonHiddenAttachments.Add( go );
		}
		foreach ( var (attachment, go) in Renderer.attachmentToGameObject )
		{
			if ( !go.IsValid() ) continue;
			var bone = attachment.Bone;
			var upper = bone is not null && bone.Index < bones.Count && bones[bone.Index] == bone ? _firstPersonUpperBody[bone.Index] : IsFirstPersonUpperBodyBone( bone );
			if ( upper ) _firstPersonHiddenAttachments.Add( go );
		}
	}

	/// <summary>
	/// The top-most collapsed bone/attachment objects. Tagging these covers everything parented below them.
	/// </summary>
	void CollectFirstPersonViewerTagTargets( GameObject body )
	{
		foreach ( var go in _firstPersonHiddenAttachments )
		{
			var covered = false;
			for ( var parent = go.Parent; parent.IsValid() && parent != body; parent = parent.Parent )
			{
				if ( !_firstPersonHiddenAttachments.Contains( parent ) ) continue;
				covered = true;
				break;
			}
			if ( !covered ) _viewerTagTargets.Add( go );
		}
	}

	bool IsFirstPersonHiddenAttachment( GameObject go )
	{
		var body = Renderer.IsValid() ? Renderer.GameObject : null;
		for ( ; go.IsValid() && go != body; go = go.Parent )
		{
			if ( go.Tags.Has( "firstperson-hidden" ) ) return true;
			if ( _firstPersonHiddenAttachments.Contains( go ) ) return true;
		}
		return false;
	}

	FirstPersonBodyCopy GetFirstPersonBodyCopy( ModelRenderer renderer )
	{
		if ( _firstPersonBodyCopies.TryGetValue( renderer, out var copy ) ) return copy;

		var source = renderer.SceneObject;
		copy = new FirstPersonBodyCopy( source, Scene.SceneWorld );
		for ( int i = 0; i < copy.BoneMap.Length; i++ )
		{
			var bone = _firstPersonBodyModel.Bones.GetBone( source.Model.Bones.AllBones[i].Name );
			copy.BoneMap[i] = (bone?.Index ?? -1, bone is not null && _firstPersonUpperBody[bone.Index]);
		}
		_firstPersonBodyCopies.Add( renderer, copy );
		return copy;
	}

	IDisposable _firstPersonPreviewHook;
	JobHandle _firstPersonPreviewJob;
	SceneModel _firstPersonPreviewModel;
	Transform _firstPersonPreviewTransform;
	float _firstPersonPreviewDelta;
	// When the hook last ran, and whether pre-render has run since: between the two the bones stage is running its hooks
	float _firstPersonPreviewHookTime = float.NaN;
	bool _firstPersonPreviewHookPassed;

	/// <summary>
	/// Evaluate the first person preview's graph alongside the body's own animation, off the main thread. Runs as bones
	/// update, before the animation pass: game code has set this frame's parameters, which the body reads in the same pass.
	/// Pre-render joins it and falls back to evaluating there when it didn't start (first frame shown, graph unavailable).
	/// </summary>
	void StartFirstPersonPreview()
	{
		_firstPersonPreviewHookTime = Time.Now;
		_firstPersonPreviewHookPassed = false;
		JoinFirstPersonPreview();

		if ( !Renderer.IsValid() || !_firstPersonBodyCopies.TryGetValue( Renderer, out var preview ) ) return;
		var bodySource = Renderer.SceneModel;
		if ( !bodySource.IsValid() || preview.Object is not SceneModel previewModel || !previewModel.IsValid() ) return;
		if ( previewModel.Model != bodySource.Model ) return;

		// The body's scene model takes this transform as the animation pass starts.
		var transform = Renderer.WorldTransform;
		previewModel.Transform = transform;
		previewModel.PrepareFirstPersonAnimation( bodySource, UseFirstPersonAnimation );
		if ( !previewModel.UseAnimGraph ) return;

		previewModel.PlaybackRate = bodySource.PlaybackRate;
		_firstPersonPreviewModel = previewModel;
		_firstPersonPreviewTransform = transform;
		_firstPersonPreviewDelta = Time.Delta;
		_firstPersonPreviewJob = JobDispatcher.Begin( 1, this, static ( controller, _ ) => controller.EvaluateFirstPersonPreview() );
	}

	void EvaluateFirstPersonPreview()
	{
		using ( _firstPersonPreviewZone.Start() )
		{
			_firstPersonPreviewModel.Update( _firstPersonPreviewDelta );
		}
	}

	/// <summary>
	/// Wait for this frame's preview evaluation. Returns the preview it evaluated, or null when none ran.
	/// </summary>
	SceneModel JoinFirstPersonPreview()
	{
		// A handle joins once. The job reads the model, so it's cleared after the join.
		var job = _firstPersonPreviewJob;
		_firstPersonPreviewJob = default;
		var model = _firstPersonPreviewModel;
		try
		{
			job.Join();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"First person preview evaluation failed: {e.Message}" );
			model = null;
		}
		_firstPersonPreviewModel = null;
		return model;
	}

	void StopFirstPersonPreview()
	{
		JoinFirstPersonPreview();

		// Removing a hook while its stage runs them would skip the next one, so one disabled from inside the bones stage
		// (an animation event) stays registered with nothing to evaluate, and is reused if this is enabled again.
		if ( _firstPersonPreviewHookTime == Time.Now && !_firstPersonPreviewHookPassed ) return;
		_firstPersonPreviewHook?.Dispose();
		_firstPersonPreviewHook = null;
	}

	protected override void OnPreRender()
	{
		// Run after animation and bone merging, so the body and all clothing share this frame's pose,
		// and after game code has touched the renderers this frame.
		using var _ = _firstPersonBodyZone.Start();
		_firstPersonPreviewHookPassed = true;
		var evaluated = JoinFirstPersonPreview();

		if ( !UpdateBodyVisibility() || !Renderer.SceneModel.IsValid() )
		{
			ClearFirstPersonBodyCopies();
			return;
		}

		var body = Renderer.GameObject;

		_firstPersonStaleCopies.Clear();
		foreach ( var (renderer, copy) in _firstPersonBodyCopies )
		{
			if ( renderer.IsValid() && renderer.Active && renderer.GameObject.IsAncestor( body )
				&& renderer.SceneObject.IsValid() && renderer.SceneObject.Model == copy.Object.Model ) continue;
			copy.Object.Delete();
			_firstPersonStaleCopies.Add( renderer );
		}
		foreach ( var renderer in _firstPersonStaleCopies )
			_firstPersonBodyCopies.Remove( renderer );
		_firstPersonStaleCopies.Clear();

		// An independent graph instance, evaluated as bones updated or here. Never set b_firstperson on the shadow source.
		var preview = GetFirstPersonBodyCopy( Renderer );
		var previewModel = (SceneModel)preview.Object;
		var bodySource = Renderer.SceneModel;
		if ( evaluated == previewModel && previewModel.UseAnimGraph )
		{
			previewModel.GetBoneWorldTransforms( preview.Bones );
			// The body moved after the pass started: carry the pose along, as evaluating at its new transform would.
			if ( bodySource.Transform != _firstPersonPreviewTransform )
			{
				for ( int i = 0; i < preview.Bones.Length; i++ )
					preview.Bones[i] = bodySource.Transform.ToWorld( _firstPersonPreviewTransform.ToLocal( preview.Bones[i] ) );
			}
			previewModel.Transform = bodySource.Transform;
		}
		else
		{
			previewModel.Transform = bodySource.Transform;
			previewModel.PrepareFirstPersonAnimation( bodySource, UseFirstPersonAnimation );
			if ( previewModel.UseAnimGraph )
			{
				previewModel.PlaybackRate = bodySource.PlaybackRate;
				previewModel.Update( Time.Delta );
				previewModel.GetBoneWorldTransforms( preview.Bones );
			}
			else
			{
				// Direct sequence/procedural bodies have no graph to reshape, or the first person pose is
				// disabled (e.g. seated, where it pushes the back into the seat); retain the current pose.
				bodySource.GetBoneWorldTransforms( preview.Bones );
			}
		}

		// From the next frame on, the preview evaluates with the animation pass.
		_firstPersonPreviewHook ??= Scene.AddHook( GameObjectSystem.Stage.UpdateBones, -1, StartFirstPersonPreview, nameof( PlayerController ), "FirstPersonPreview" );

		var collapsed = preview.Bones[FirstPersonHiddenRoot.Index].WithScale( 0 );
		var previewBounds = previewModel.UseAnimGraph ? previewModel.Bounds : bodySource.Bounds;

		foreach ( var renderer in _bodyRenderers )
		{
			var source = renderer.SceneObject;
			if ( !source.IsValid() || !source.Model.IsValid() ) continue;

			var copy = GetFirstPersonBodyCopy( renderer );

			var target = copy.Object;
			target.Transform = source.Transform;
			// Hide the entire held object, including skinned weapons with their own skeleton.
			// Collapsing its parent transform isn't sufficient when copying world-space bones.
			// ShadowsOnly renderers are shadow proxies, the original still casts, and the copy (which never casts) has nothing to show.
			CopyRenderState( source, target, renderer.RenderType != ModelRenderer.ShadowRenderType.ShadowsOnly && !IsFirstPersonHiddenAttachment( renderer.GameObject ) );
			// The copy is what the viewer sees, so it drops the viewer tag inherited from the hidden body.
			// It's tagged "firstperson" like view models, so other cameras (mirrors, render targets) can exclude the headless copy.
			CopyTags( copy, renderer.GameObject.Tags, dropViewer: !HasGameViewerTag( renderer.GameObject, body ), firstPerson: true );
			var tint = source.ColorTint;
			if ( target.ColorTint != tint ) target.ColorTint = tint;

			// Re-copying materials clears and rebuilds the overrides, so only do it when the copy is created or the renderer's materials changed
			var materialState = HashCode.Combine( renderer.MaterialOverride, renderer.MaterialGroup, renderer.MaterialOverrideVersion, renderer.RenderOptions );
			if ( copy.MaterialState != materialState )
			{
				copy.MaterialState = materialState;
				renderer.RenderOptions.Apply( target );
				renderer.CopyMaterialsTo( target );
				copy.TagsCopied = false;
			}

			// Attributes have no change tracking and game code commonly drives them every frame
			// (hit flashes, dissolves). Merging is a single native copy.
			source.Attributes.MergeTo( target.Attributes );

			if ( source is SceneModel sourceModel && target is SceneModel targetModel )
			{
				if ( copy != preview ) sourceModel.GetBoneWorldTransforms( copy.Bones );
				// Bone-merged clothing follows the preview's pose, not the normally animated shadow body.
				bool mergePreview = renderer == Renderer || renderer is SkinnedModelRenderer skin && skin.RootBoneMergeTarget == Renderer;
				var pose = copy.Pose;
				for ( int i = 0; i < pose.Length; i++ )
				{
					var (bodyBone, hidden) = copy.BoneMap[i];
					var bone = mergePreview && bodyBone >= 0 ? preview.Bones[bodyBone] : copy.Bones[i];
					pose[i] = hidden ? collapsed : bone;
				}
				targetModel.SetBoneWorldTransforms( pose );

				CopyMorphs( sourceModel, targetModel );
			}

			// Clothing copies don't evaluate animations; include both poses in their culling bounds.
			target.Bounds = source.Bounds.AddPoint( previewBounds.Mins ).AddPoint( previewBounds.Maxs ).AddPoint( collapsed.Position );
		}
	}

	sealed class FirstPersonBodyCopy
	{
		public SceneObject Object { get; }
		public Transform[] Bones { get; }
		public (int Index, bool Hidden)[] BoneMap { get; }
		// The pose written to the copy
		public Transform[] Pose { get; }
		public int? MaterialState { get; set; }
		// The renderer's tag tokens and the asks last written to the copy's tags
		public HashSet<uint> Tags { get; } = new();
		public bool TagsCopied { get; set; }
		public bool TagsDropViewer { get; set; }
		public bool TagsFirstPerson { get; set; }

		public FirstPersonBodyCopy( SceneObject source, SceneWorld world )
		{
			Object = source is SceneModel
				? new SceneModel( world, source.Model, source.Transform ) { UseAnimGraph = false }
				: new SceneObject( world, source.Model, source.Transform );
			Object.Flags.CastShadows = false;
			Object.Component = source.Component;
			Bones = new Transform[source.Model.BoneCount];
			BoneMap = new (int, bool)[Bones.Length];
			Pose = new Transform[Bones.Length];
		}
	}
}
