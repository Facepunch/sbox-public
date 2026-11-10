namespace Sandbox;

/// <summary>
/// Uploads changed volumes before animation and render traversal consume their snapshots.
/// </summary>
internal sealed class SceneDeformationSystem : GameObjectSystem<SceneDeformationSystem>
{
	private readonly HashSet<ModelDeformer> _volumes = new();
	private readonly HashSet<ModelRenderer> _renderers = new();

	/// <summary>
	/// Registers deformation updates ahead of animation evaluation.
	/// </summary>
	public SceneDeformationSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.UpdateBones, -100, Update, "UpdateDeformations" );
	}

	internal void Add( ModelDeformer volume ) => _volumes.Add( volume );
	internal void Remove( ModelDeformer volume ) => _volumes.Remove( volume );
	internal void Track( ModelRenderer renderer )
	{
		if ( renderer is not SkinnedModelRenderer )
		{
			_renderers.Add( renderer );
		}
	}

	private void Update()
	{
		foreach ( var volume in _volumes )
		{
			volume.UpdateVolume();
		}

		foreach ( var renderer in _renderers )
		{
			if ( renderer.IsValid() )
			{
				renderer.UpdateDeformations();
			}
		}

		_renderers.RemoveWhere( x => !x.IsValid() || x.ModelDeformers.Count == 0 );
	}
}
