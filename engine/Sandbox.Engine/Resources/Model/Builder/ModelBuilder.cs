namespace Sandbox;

/// <summary>
/// Provides the ability to generate <see cref="Model"/>s at runtime.
/// A new instance is available from <see cref="Model.Builder"/>.
/// </summary>
public sealed partial class ModelBuilder
{
	private string modelName;
	private float mass = 1000;
	private string surfaceProperty = "";
	private readonly float[] lodSwitchDistance = Enumerable.Range( 0, 8 )
		.Select( i => i * 50.0f )
		.ToArray();

	/// <summary>
	/// Sets the mass, in kilograms, used by collision shapes added directly to this builder.
	/// The default is 1000.
	/// </summary>
	/// <param name="mass">The total mass of the collision body.</param>
	public ModelBuilder WithMass( float mass )
	{
		this.mass = mass;
		return this;
	}

	/// <summary>
	/// Sets the surface used by collision shapes added directly to this builder.
	/// </summary>
	/// <param name="name">The surface name.</param>
	public ModelBuilder WithSurface( string name )
	{
		surfaceProperty = name;
		return this;
	}

	/// <summary>
	/// Sets the distance at which an LOD level becomes active.
	/// By default, each level switches at <c>lod * 50</c>.
	/// </summary>
	/// <param name="lod">The LOD level, from 0 to 7.</param>
	/// <param name="distance">The switch distance.</param>
	/// <exception cref="ArgumentOutOfRangeException">The LOD level is outside the range 0 to 7.</exception>
	public ModelBuilder WithLodDistance( int lod, float distance )
	{
		if ( lod is < 0 or >= 8 )
			throw new ArgumentOutOfRangeException( nameof( lod ), "LOD level must be between 0 and 7" );

		lodSwitchDistance[lod] = distance;
		return this;
	}

	/// <summary>
	/// Sets the name used to identify the model.
	/// </summary>
	/// <param name="name">The model name.</param>
	public ModelBuilder WithName( string name )
	{
		modelName = name;
		return this;
	}

	/// <summary>
	/// Builds the model.
	/// </summary>
	/// <returns>The finished model.</returns>
	public unsafe Model Create()
	{
		ApplyMorphTargets();

		var modelBuilder = NativeEngine.ModelBuilder.Create();
		var animationBuilder = default( CAnimationGroupBuilder );
		var physicsBodies = default( CPhysBodyDescArray );

		try
		{
			ApplyMetadata( modelBuilder );
			modelBuilder.SetMass( mass );
			for ( var lod = 0; lod < lodSwitchDistance.Length; ++lod )
			{
				modelBuilder.SetLODSwitchDistance( lod, lodSwitchDistance[lod] );
			}

			ApplyMeshes( modelBuilder );
			AddMaterialGroups( modelBuilder );
			ApplyBones( modelBuilder );
			ApplyHitboxes( modelBuilder );

			physicsBodies = CreatePhysicsBodies();
			if ( physicsBodies.IsValid )
				modelBuilder.SetPhysics_Runtime( physicsBodies );

			modelBuilder.AppendSkeletonsFromChildResources( false );
			ApplyTraceMesh( modelBuilder );

			animationBuilder = CreateAnimationGroup();
			if ( animationBuilder.IsValid )
				modelBuilder.AddAnimationGroupRuntime( animationBuilder );

			var model = modelBuilder.CreateRuntimeModel( "sbox_procedural_model", true );
			return Model.FromNative( model, true, modelName );
		}
		finally
		{
			if ( animationBuilder.IsValid )
				animationBuilder.DeleteThis();

			if ( physicsBodies.IsValid )
				physicsBodies.DeleteThis();

			if ( modelBuilder.IsValid )
				modelBuilder.DeleteThis();
		}
	}
}
