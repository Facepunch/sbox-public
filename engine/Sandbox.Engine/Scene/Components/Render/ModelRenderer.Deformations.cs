using System.Runtime.InteropServices;

namespace Sandbox;

partial class ModelRenderer
{
	internal List<ModelDeformer> ModelDeformers { get; } = new();
	private readonly List<SceneDeformationVolumeData> _effectiveVolumes = new();
	private readonly List<ModelDeformer> _orderedDeformers = new();
	internal virtual ModelRenderer DeformationSource => null;

	/// <summary>
	/// A model-space point per model bone that deformations move each bone's part of the model by, without reshaping it -
	/// or none, to deform it as usual.
	/// </summary>
	internal virtual ReadOnlySpan<Vector4> DeformationAnchors => [];
	private readonly HashSet<ModelRenderer> _deformationSources = new();
	private bool _hasActiveDeformations;

	private void ResetDeformations()
	{
		_effectiveVolumes.Clear();
		_orderedDeformers.Clear();
		_deformationSources.Clear();
		_hasActiveDeformations = false;
	}

	internal void UpdateDeformations()
	{
		if ( !Active || !_sceneObject.IsValid() )
		{
			return;
		}

		_effectiveVolumes.Clear();
		_orderedDeformers.Clear();
		_deformationSources.Clear();
		var source = this is SkinnedModelRenderer { DeformationMode: SkinnedModelRenderer.DeformationModeType.None } ? null : this;
		while ( source.IsValid() && source.Active && _deformationSources.Add( source ) )
		{
			foreach ( var component in source.ModelDeformers )
			{
				if ( component.Active && component.IsActive &&
					(source == this || component.ApplyToBoneMergedChildren) )
				{
					_orderedDeformers.Add( component );
				}
			}

			source = source.DeformationSource;
		}

		_orderedDeformers.Sort( static ( a, b ) => a.Priority.CompareTo( b.Priority ) );
		foreach ( var component in _orderedDeformers )
		{
			_effectiveVolumes.Add( VolumeFor( component ) );
		}

		_hasActiveDeformations = _effectiveVolumes.Count > 0;
		if ( this is not SkinnedModelRenderer && (_sceneObject is SceneModel) != _hasActiveDeformations )
		{
			RecreateSceneObject();
		}

		if ( _sceneObject is SceneModel model )
		{
			model.SetDeformationVolumes( CollectionsMarshal.AsSpan( _effectiveVolumes ) );
			model.SetDeformationAnchors( _hasActiveDeformations ? DeformationAnchors : [] );
			_sceneObject.Flags.IsStatic = GameObject.IsStatic && !_hasActiveDeformations;
			if ( this is not SkinnedModelRenderer )
			{
				// The same compute path also supports rigid meshes, without inventing bones.
				model.UpdateToBindPose();
			}
		}
	}

	/// <summary>
	/// A deformer's volume in this model's space. One on the model this is bone merged to is in that model's, which is this
	/// one's too unless this was made turned or moved from it - exported facing another way - when it's taken across.
	/// </summary>
	private SceneDeformationVolumeData VolumeFor( ModelDeformer deformer )
	{
		var owner = deformer.Owner;
		if ( owner == this || !owner.IsValid() || owner.Model is null || Model is null )
			return deformer.Data;

		if ( _restOffsetModels != (Model, owner.Model) )
		{
			_restOffset = RestOffset( Model, owner.Model );
			_restOffsetModels = (Model, owner.Model);
		}

		return _restOffset is { } o ? deformer.Pack( o.ToLocal( deformer.Placement ) ) : deformer.Data;
	}

	private Transform? _restOffset;
	private (Model Model, Model Target) _restOffsetModels;

	/// <summary>
	/// Where a model's rest pose sits in another's, through the first bone they share, or null when it's the same - as a
	/// bone merged model's usually is.
	/// </summary>
	private static Transform? RestOffset( Model model, Model target )
	{
		foreach ( var bone in model.Bones.AllBones )
		{
			if ( target.Bones.GetBone( bone.Name ) is not { } shared )
				continue;

			var offset = shared.LocalTransform.ToWorld( bone.LocalTransform.ToLocal( global::Transform.Zero ) );
			return offset.Position.Length < 0.01f && offset.Rotation.Distance( Rotation.Identity ) < 0.01f ? null : offset;
		}

		return null;
	}

	/// <summary>
	/// Creates the scene representation needed by this renderer's current deformation state.
	/// </summary>
	protected virtual SceneObject CreateSceneObject( Model model )
	{
		if ( _hasActiveDeformations )
		{
			return new SceneModel( Scene.SceneWorld, model, WorldTransform ) { UseAnimGraph = false };
		}

		return new SceneObject( Scene.SceneWorld, model, WorldTransform );
	}

	private void RecreateSceneObject()
	{
		BackupRenderAttributes( _sceneObject.Attributes );
		_sceneObject.Delete();
		_sceneObject = CreateSceneObject( Model ?? Model.Load( "models/dev/box.vmdl" ) );
		OnSceneObjectCreated( _sceneObject );
	}
}

partial class SkinnedModelRenderer
{
	internal override ModelRenderer DeformationSource => _boneMergeTarget;

	/// <summary>
	/// How this model responds to model deformers. Normal reshapes it, None ignores them, and Rigid moves it without
	/// reshaping it, for hard items like earrings or a sword.
	/// </summary>
	[Property, Advanced]
	public DeformationModeType DeformationMode { get; set; } = DeformationModeType.Normal;

	/// <summary>
	/// How model deformers affect a skinned model.
	/// </summary>
	public enum DeformationModeType
	{
		/// <summary>
		/// Reshape the model with model deformers.
		/// </summary>
		Normal,

		/// <summary>
		/// Ignore model deformers, including those inherited through bone merging.
		/// </summary>
		None,

		/// <summary>
		/// Move each part as the deformation moves the bone it hangs from without reshaping it. When bone merged,
		/// use the nearest bone up its chain that the target has, so jiggle bones move as one piece with it.
		/// </summary>
		Rigid
	}

	private Vector4[] _anchors = [];
	private (Model Model, Model Target) _anchorModels;

	/// <summary>
	/// Each bone's anchor in model space: the rest position of the bone it hangs from.
	/// </summary>
	internal override ReadOnlySpan<Vector4> DeformationAnchors
	{
		get
		{
			if ( DeformationMode != DeformationModeType.Rigid || Model is null ) return [];

			var target = _boneMergeTarget?.Model;
			if ( _anchorModels != (Model, target) )
			{
				var bones = Model.Bones.AllBones;
				_anchors = new Vector4[bones.Count];
				for ( int i = 0; i < bones.Count; i++ )
					_anchors[i] = new Vector4( HangsFrom( bones[i], target ).LocalTransform.Position, 0 );
				_anchorModels = (Model, target);
			}

			return _anchors;
		}
	}

	/// <summary>
	/// The nearest bone up a bone's chain that the target has, or the bone itself when none does or there's no target.
	/// </summary>
	private static BoneCollection.Bone HangsFrom( BoneCollection.Bone bone, Model target )
	{
		for ( var b = bone; b is not null && target is not null; b = b.Parent )
		{
			if ( target.Bones.HasBone( b.Name ) ) return b;
		}

		return bone;
	}
}
