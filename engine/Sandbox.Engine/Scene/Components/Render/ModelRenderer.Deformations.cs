using System.Runtime.InteropServices;

namespace Sandbox;

partial class ModelRenderer
{
	internal List<ModelDeformer> ModelDeformers { get; } = new();
	private readonly List<SceneDeformationVolumeData> _effectiveVolumes = new();
	private readonly List<ModelDeformer> _orderedDeformers = new();
	internal virtual ModelRenderer DeformationSource => null;
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
		var source = this;
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
			_effectiveVolumes.Add( component.Data );
		}

		_hasActiveDeformations = _effectiveVolumes.Count > 0;
		if ( this is not SkinnedModelRenderer && (_sceneObject is SceneModel) != _hasActiveDeformations )
		{
			RecreateSceneObject();
		}

		if ( _sceneObject is SceneModel model )
		{
			model.SetDeformationVolumes( CollectionsMarshal.AsSpan( _effectiveVolumes ) );
			_sceneObject.Flags.IsStatic = GameObject.IsStatic && !_hasActiveDeformations;
			if ( this is not SkinnedModelRenderer )
			{
				// The same compute path also supports rigid meshes, without inventing bones.
				model.UpdateToBindPose();
			}
		}
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
}
