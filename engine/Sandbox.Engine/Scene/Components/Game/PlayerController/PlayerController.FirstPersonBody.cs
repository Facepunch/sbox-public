namespace Sandbox;

public sealed partial class PlayerController
{
	Model _firstPersonBodyModel;
	BoneCollection.Bone _firstPersonChest;
	BoneCollection.Bone _firstPersonNeck;
	bool _firstPersonShowArms;
	readonly Dictionary<ModelRenderer, FirstPersonBodyCopy> _firstPersonBodyCopies = new();
	readonly Dictionary<ModelRenderer, FirstPersonBodyCopy> _firstPersonShadowCopies = new();
	readonly HashSet<GameObject> _firstPersonHiddenAttachments = new();

	bool PrepareFirstPersonBody()
	{
		if ( HideBodyInFirstPerson || !Renderer.IsValid() || !Renderer.Model.IsValid() ) return false;

		if ( _firstPersonBodyModel != Renderer.Model || _firstPersonShowArms != ShowArmsInFirstPerson )
		{
			// Copies cache which bones are hidden, so rebuild them when the model or arm visibility changes.
			ClearFirstPersonBodyCopies();
			_firstPersonBodyModel = Renderer.Model;
			_firstPersonShowArms = ShowArmsInFirstPerson;
			// Citizen Human's upper chest is the common parent of the neck, head and both arms.
			var bones = _firstPersonBodyModel.Bones;
			_firstPersonChest = bones.GetBone( "spine_2" );
			_firstPersonNeck = bones.GetBone( "neck_0" ) ?? bones.GetBone( "neck_1" ) ?? bones.GetBone( "head" );
		}

		return FirstPersonHiddenRoot is not null;
	}

	// The bone collapsed by the first person view
	BoneCollection.Bone FirstPersonHiddenRoot => _firstPersonShowArms && _firstPersonNeck is not null ? _firstPersonNeck : _firstPersonChest;

	void ClearFirstPersonBodyCopies()
	{
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
		_bodyRenderers.Clear();
		foreach ( var (renderer, copy) in _firstPersonShadowCopies )
		{
			if ( CastsFirstPersonShadow( renderer, body ) && renderer.SceneObject.Model == copy.Object.Model ) continue;
			copy.Object.Delete();
			_bodyRenderers.Add( renderer );
		}
		foreach ( var renderer in _bodyRenderers )
			_firstPersonShadowCopies.Remove( renderer );

		_bodyRenderers.Clear();
		body.Components.GetAll( _bodyRenderers, FindMode.InSelf | FindMode.InDescendants | FindMode.Enabled );
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
			target.RenderingEnabled = source.RenderingEnabled;
			target.MeshGroupMask = source.MeshGroupMask;
			target.Tags.SetFrom( renderer.GameObject.Tags );
			target.Tags.Remove( "viewer" );

			// Materials matter for alpha-tested shadows (hair, cloth). No RenderOptions: they'd re-include the game layer.
			var materialState = HashCode.Combine( renderer.MaterialOverride, renderer.MaterialGroup, renderer.MaterialOverrideVersion );
			if ( copy.MaterialState != materialState )
			{
				copy.MaterialState = materialState;
				renderer.CopyMaterialsTo( target );
			}

			if ( source is SceneModel sourceModel && target is SceneModel targetModel )
			{
				sourceModel.GetBoneWorldTransforms( copy.Bones );
				for ( int i = 0; i < copy.Bones.Length; i++ )
					targetModel.SetBoneWorldTransform( i, copy.Bones[i] );

				for ( int i = 0; i < source.Model.MorphCount; i++ )
					targetModel.Morphs.Set( i, sourceModel.Morphs.Get( i ), 0 );
			}

			target.Bounds = source.Bounds;
		}
		_bodyRenderers.Clear();
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
		foreach ( var bone in _firstPersonBodyModel.Bones.AllBones )
		{
			if ( !IsFirstPersonUpperBodyBone( bone ) ) continue;
			var go = Renderer.GetBoneObject( bone );
			if ( go.IsValid() ) _firstPersonHiddenAttachments.Add( go );
		}
		foreach ( var (attachment, go) in Renderer.attachmentToGameObject )
		{
			if ( go.IsValid() && IsFirstPersonUpperBodyBone( attachment.Bone ) )
				_firstPersonHiddenAttachments.Add( go );
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
			copy.BoneMap[i] = (bone?.Index ?? -1, IsFirstPersonUpperBodyBone( bone ));
		}
		_firstPersonBodyCopies.Add( renderer, copy );
		return copy;
	}

	protected override void OnPreRender()
	{
		// Run after animation and bone merging, so the body and all clothing share this frame's pose,
		// and after game code has touched the renderers this frame.
		if ( !UpdateBodyVisibility() || !Renderer.SceneModel.IsValid() )
		{
			ClearFirstPersonBodyCopies();
			return;
		}

		var body = Renderer.GameObject;

		_bodyRenderers.Clear();
		foreach ( var (renderer, copy) in _firstPersonBodyCopies )
		{
			if ( renderer.IsValid() && renderer.Active && renderer.GameObject.IsAncestor( body )
				&& renderer.SceneObject.IsValid() && renderer.SceneObject.Model == copy.Object.Model ) continue;
			copy.Object.Delete();
			_bodyRenderers.Add( renderer );
		}
		foreach ( var renderer in _bodyRenderers )
			_firstPersonBodyCopies.Remove( renderer );

		_bodyRenderers.Clear();
		body.Components.GetAll( _bodyRenderers, FindMode.InSelf | FindMode.InDescendants | FindMode.Enabled );
		// Evaluate an independent graph instance. Never set b_firstperson on the shadow source.
		var preview = GetFirstPersonBodyCopy( Renderer );
		var previewModel = (SceneModel)preview.Object;
		var bodySource = Renderer.SceneModel;
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
			target.RenderingEnabled = source.RenderingEnabled && renderer.RenderType != ModelRenderer.ShadowRenderType.ShadowsOnly && !IsFirstPersonHiddenAttachment( renderer.GameObject );
			target.Tags.SetFrom( renderer.GameObject.Tags );
			// The copy is what the viewer sees, so it drops the viewer tag inherited from the hidden body.
			// It's tagged "firstperson" like view models, so other cameras (mirrors, render targets) can exclude the headless copy.
			if ( !HasGameViewerTag( renderer.GameObject, body ) ) target.Tags.Remove( "viewer" );
			target.Tags.Add( "firstperson" );
			target.ColorTint = source.ColorTint;
			target.MeshGroupMask = source.MeshGroupMask;

			// Re-copying materials clears and rebuilds the overrides, so only do it when the copy is created or the renderer's materials changed
			var materialState = HashCode.Combine( renderer.MaterialOverride, renderer.MaterialGroup, renderer.MaterialOverrideVersion, renderer.RenderOptions );
			if ( copy.MaterialState != materialState )
			{
				copy.MaterialState = materialState;
				renderer.RenderOptions.Apply( target );
				renderer.CopyMaterialsTo( target );
			}

			// Attributes have no change tracking and game code commonly drives them every frame
			// (hit flashes, dissolves). Merging is a single native copy.
			source.Attributes.MergeTo( target.Attributes );

			if ( source is SceneModel sourceModel && target is SceneModel targetModel )
			{
				if ( copy != preview ) sourceModel.GetBoneWorldTransforms( copy.Bones );
				// Bone-merged clothing follows the preview's pose, not the normally animated shadow body.
				bool mergePreview = renderer == Renderer || renderer is SkinnedModelRenderer skin && skin.RootBoneMergeTarget == Renderer;
				for ( int i = 0; i < copy.Bones.Length; i++ )
				{
					var (bodyBone, hidden) = copy.BoneMap[i];
					var bone = mergePreview && bodyBone >= 0 ? preview.Bones[bodyBone] : copy.Bones[i];
					targetModel.SetBoneWorldTransform( i, hidden ? collapsed : bone );
				}

				for ( int i = 0; i < source.Model.MorphCount; i++ )
					targetModel.Morphs.Set( i, sourceModel.Morphs.Get( i ), 0 );
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
		public int? MaterialState { get; set; }

		public FirstPersonBodyCopy( SceneObject source, SceneWorld world )
		{
			Object = source is SceneModel
				? new SceneModel( world, source.Model, source.Transform ) { UseAnimGraph = false }
				: new SceneObject( world, source.Model, source.Transform );
			Object.Flags.CastShadows = false;
			Object.Component = source.Component;
			Bones = new Transform[source.Model.BoneCount];
			BoneMap = new (int, bool)[Bones.Length];
		}
	}
}
