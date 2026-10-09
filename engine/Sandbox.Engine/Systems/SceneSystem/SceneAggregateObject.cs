using System.Runtime.InteropServices;
using NativeEngine;

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
	public static int MaxFragments => AggregateGlue.GetMaxFragmentCount();

	public new void Delete()
	{
		if ( native.IsNull )
			return;

		RenderingEnabled = false;
		base.Delete();
	}

	public unsafe SceneAggregateObject( SceneWorld sceneWorld, Model model, ReadOnlySpan<AggregateFragment> fragments, bool castShadows )
	{
		Assert.IsValid( sceneWorld );
		ArgumentNullException.ThrowIfNull( model );

		if ( fragments.IsEmpty )
			throw new ArgumentException( "An aggregate needs at least one fragment", nameof( fragments ) );

		if ( fragments.Length > MaxFragments )
			throw new ArgumentException( $"An aggregate can draw at most {MaxFragments} fragments, got {fragments.Length}", nameof( fragments ) );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			fixed ( AggregateFragment* ptr = fragments )
			{
				AggregateGlue.CreateAggregateSceneObject( sceneWorld, model.native, (IntPtr)ptr, fragments.Length, castShadows );
			}
		}

		if ( native.IsNull )
			throw new ArgumentException( "Couldn't build an aggregate from this model - it needs a single mesh whose draw calls all share one material", nameof( model ) );
	}
}
