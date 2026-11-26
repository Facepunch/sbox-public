namespace Sandbox;

/// <summary>
/// Many placements of one model drawn as one object. With a GPU-driven device the GPU culls every instance, picks its LOD and
/// multi-draws them; otherwise they draw instanced, culled per view on the CPU.
/// </summary>
/// <remarks>
/// What the object has applies to every instance: <see cref="SceneObject.Transform"/>, <see cref="SceneObject.ColorTint"/>
/// (multiplying each instance's tint), <see cref="SceneObject.Tags"/>, <see cref="SceneObject.RenderingEnabled"/> and the
/// flags, shadow casting among them. It has no attributes or draw callbacks, which would keep it from drawing GPU-driven.
/// Only opaque models without a skeleton are drawn.
/// </remarks>
public sealed class SceneModelInstances : SceneObject
{
	Transform[] instances = [];
	Color[] tints = [];

	internal override bool OwnsRenderObject => true;

	internal override Borealis.RenderObject MakeRenderObject( Rendering.ISceneWorldRender render ) => new Borealis.InstancedMeshObject();

	/// <summary>
	/// Create an object that draws <paramref name="model"/> at no instances yet: give them with <see cref="SetInstances(ReadOnlySpan{Transform})"/>.
	/// </summary>
	public SceneModelInstances( SceneWorld world, Model model )
	{
		Assert.IsValid( world );
		ArgumentNullException.ThrowIfNull( model );

		if ( !model.HasRenderMeshes() ) model = Model.Error;

		var flags = Rendering.SceneObjectFlags.CastShadows | Rendering.SceneObjectFlags.IsLoaded;
		MakeManaged( world, model );
		_keptModel = model;
		StartResolvingDraws( model );
		FlagsAtCreation( model, flags );
		Tags.RemoveAll();
		KeepFixedBounds( false );
	}

	/// <summary>
	/// How many instances it draws.
	/// </summary>
	public int Count => instances.Length;

	/// <summary>
	/// Replace all instances, copying them. Each is in this object's space: its <see cref="SceneObject.Transform"/> applies on top.
	/// </summary>
	/// <param name="transforms">Where each instance is.</param>
	public void SetInstances( ReadOnlySpan<Transform> transforms ) => SetInstances( transforms, default );

	/// <summary>
	/// Replace all instances, copying them, each with an RGB tint (its alpha is ignored).
	/// </summary>
	/// <param name="transforms">Where each instance is, in this object's space.</param>
	/// <param name="tints">One colour per transform, or empty for none.</param>
	public void SetInstances( ReadOnlySpan<Transform> transforms, ReadOnlySpan<Color> tints )
	{
		if ( !tints.IsEmpty && tints.Length != transforms.Length ) throw new ArgumentException( "A tint for every instance, or none", nameof( tints ) );

		instances = transforms.ToArray();
		this.tints = tints.IsEmpty ? [] : tints.ToArray();
		UpdateBounds( Transform );
		NotifyChanged( Rendering.SceneObjectChange.Instances );
	}

	/// <summary>
	/// The bounds are the instances' union, which move with the transform and change with the model.
	/// </summary>
	void UpdateBounds( in Transform transform )
	{
		if ( instances.Length == 0 || Model is not { } model ) return;

		var local = model.Bounds;
		var bounds = default( BBox );
		for ( int i = 0; i < instances.Length; i++ )
		{
			var box = local.Transform( transform.ToWorld( instances[i] ) );
			bounds = i == 0 ? box : bounds.AddBBox( box );
		}

		KeepBounds( bounds );
	}

	internal override void OnTransformChanged( in Transform tx )
	{
		base.OnTransformChanged( tx );
		UpdateBounds( tx );
	}

	internal override void OnModelChanged()
	{
		base.OnModelChanged();
		UpdateBounds( Transform );
	}

	internal override void WriteRender( Borealis.RenderObject obj, Rendering.SceneObjectChange change, Rendering.ISceneWorldRender render )
	{
		if ( (change & Rendering.SceneObjectChange.Tags) != 0 ) WriteTags( obj );
		if ( obj is not Borealis.InstancedMeshObject drawn ) return;

		// The arrays are replaced whole by each SetInstances and never changed, so the render object takes them as they are
		if ( (change & Rendering.SceneObjectChange.Instances) != 0 ) drawn.Adopt( instances, tints );
		render.WriteInstances( this, drawn, change );
	}
}
