using System.Runtime.InteropServices;
using NativeEngine;
using ObjectFlags = Sandbox.Rendering.SceneObjectFlags;

namespace Sandbox;

/// <summary>
/// Overrides the default behaviour of a single <see cref="AggregateFragment"/>.
/// </summary>
[Flags]
internal enum AggregateFragmentFlags
{
	None = 0,

	/// <summary>
	/// Draw this fragment when rendering cubemaps.
	/// </summary>
	RenderToCubemaps = 1,

	/// <summary>
	/// Skip this fragment entirely on low quality settings.
	/// </summary>
	DisabledInLowQuality = 2,

	/// <summary>
	/// Don't cast shadows from this fragment, even if the aggregate does.
	/// </summary>
	DoNotCastShadows = 4
}

/// <summary>
/// A single fragment of a <see cref="SceneAggregateObject"/>. Draws one draw call of the aggregate's
/// model with its own transform, tint and bounds, and is culled on its own.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct AggregateFragment
{
	/// <summary>
	/// Where to draw this fragment.
	/// </summary>
	public Transform Transform;

	/// <summary>
	/// World space bounds, used to cull this fragment.
	/// </summary>
	public Vector3 BoundsMin;

	/// <summary>
	/// World space bounds, used to cull this fragment.
	/// </summary>
	public Vector3 BoundsMax;

	/// <summary>
	/// Colour tint, multiplied with the material.
	/// </summary>
	public Vector4 Tint;

	/// <summary>
	/// Which draw call of the aggregate's model this fragment draws. Has to be the fragment's own
	/// position in the list - the mapping lives on the model, which aggregates share.
	/// </summary>
	public int DrawDescriptorIndex;

	public AggregateFragmentFlags Flags;
}

/// <summary>
/// Draws many pieces of static geometry as one scene object, submitted as a single indirect draw
/// and culled per fragment. The model must have a single mesh whose draw calls all share one
/// material - each fragment then draws one of those draw calls.
/// </summary>
internal sealed class SceneAggregateObject : SceneObject
{
	/// <summary>
	/// The most fragments a single aggregate can draw.
	/// </summary>
	public const int MaxFragments = 512;

	internal override bool OwnsRenderObject => true;

	/// <summary>Its model and fragments as Borealis draws them; null while its model loads, and it's made again once it has.</summary>
	internal override Borealis.RenderObject MakeRenderObject( Rendering.ISceneWorldRender render ) => render.MakeAggregate( this, out _ );

	/// <summary>The model whose one mesh's draw calls the fragments draw.</summary>
	internal Model AggregateModel { get; }

	/// <summary>The fragments, fragment i drawing draw call i.</summary>
	internal AggregateFragment[] Fragments { get; }

	public new void Delete()
	{
		if ( native.IsNull )
			return;

		RenderingEnabled = false;
		base.Delete();
	}

	public SceneAggregateObject( SceneWorld sceneWorld, Model model, ReadOnlySpan<AggregateFragment> fragments, bool castShadows )
	{
		Assert.IsValid( sceneWorld );
		ArgumentNullException.ThrowIfNull( model );

		if ( fragments.IsEmpty )
			throw new ArgumentException( "An aggregate needs at least one fragment", nameof( fragments ) );

		if ( fragments.Length > MaxFragments )
			throw new ArgumentException( $"An aggregate can draw at most {MaxFragments} fragments, got {fragments.Length}", nameof( fragments ) );

		var material = CheckModel( model, fragments );

		// Its native twin holds only what every scene object has (world, transform, flags, tags); Borealis draws the fragments
		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CManagedSceneObject.Create( sceneWorld );
		}

		AggregateModel = model;
		Fragments = fragments.ToArray();

		// Opaque static world geometry, casting shadows unless its material says not to
		var flags = ObjectFlags.IsOpaque | ObjectFlags.Reflects | ObjectFlags.StaticObject;
		if ( castShadows && (material is null || !material.native.GetBoolAttributeOrDefault( "DoNotCastShadows", false )) ) flags |= ObjectFlags.CastShadows;
		ChangeFlags( flags, ~InternalFlags );

		// No transform of its own: every fragment is in world space already, and its bounds are all of theirs
		var bounds = new BBox( fragments[0].BoundsMin, fragments[0].BoundsMax );
		for ( int i = 1; i < fragments.Length; i++ ) bounds = bounds.AddBBox( new BBox( fragments[i].BoundsMin, fragments[i].BoundsMax ) );
		Transform = Transform.Zero;
		LocalBounds = bounds;
		RenderingEnabled = true;
	}

	/// <summary>
	/// Whether an aggregate can draw <paramref name="fragments"/> of <paramref name="model"/>: one mesh with one scene object,
	/// its draw calls all with one material, one fragment per draw call, each drawing its own. Throws if not; the material.
	/// </summary>
	static Material CheckModel( Model model, ReadOnlySpan<AggregateFragment> fragments )
	{
		var native = model.native;
		if ( native.IsNull )
			throw new ArgumentException( "Couldn't build an aggregate from this model - it isn't loaded", nameof( model ) );

		if ( native.GetNumMeshes() != 1 )
			throw new ArgumentException( $"Couldn't build an aggregate from {model.ResourcePath} - it has {native.GetNumMeshes()} meshes, needs exactly 1", nameof( model ) );

		if ( native.GetNumSceneObjects( 0 ) != 1 )
			throw new ArgumentException( $"Couldn't build an aggregate from {model.ResourcePath} - its mesh has {native.GetNumSceneObjects( 0 )} scene objects, needs exactly 1", nameof( model ) );

		var draws = native.GetNumDrawCalls( 0, 0 );
		if ( draws <= 0 )
			throw new ArgumentException( $"Couldn't build an aggregate from {model.ResourcePath} - it has no draw calls", nameof( model ) );

		// Every fragment is drawn with the same material, so the model can only carry the one
		// (each read is a handle of its own: compared as the materials they name)
		var material = Material.FromNative( native.GetMaterial( 0, 0, 0 ) );
		for ( int i = 1; i < draws; i++ )
		{
			if ( Material.FromNative( native.GetMaterial( 0, 0, i ) ) != material )
				throw new ArgumentException( $"Couldn't build an aggregate from {model.ResourcePath} - draw call {i} uses a different material to the rest", nameof( model ) );
		}

		if ( fragments.Length != draws )
			throw new ArgumentException( $"{fragments.Length} fragments for {model.ResourcePath} with {draws} draw calls, needs one each", nameof( fragments ) );

		for ( int i = 0; i < fragments.Length; i++ )
		{
			if ( fragments[i].DrawDescriptorIndex != i )
				throw new ArgumentException( $"Fragment {i} of {model.ResourcePath} draws call {fragments[i].DrawDescriptorIndex}, has to draw its own", nameof( fragments ) );
		}

		return material;
	}
}
