using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Many pieces of static geometry drawn as one object: every fragment is one draw of <see cref="Mesh"/>'s single material,
/// with its own world transform and bounds, culled on its own and drawn together in one multi-draw. A map's world geometry
/// is made of these. The managed counterpart of native's <c>CAggregateSceneObject</c>.
/// </summary>
internal sealed class AggregateObject : RenderObject
{
	/// <summary>
	/// One fragment as native draws it (<c>RenderTools::AggregateFragmentDraw_t</c>): its world transform and bounds, its draw's
	/// tint, and its draw's range with the fragment's index as its base instance.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct FragmentDraw
	{
		public Matrix3x4 Transform;
		public Vector3 BoundsMin;
		public AggregateFragmentFlags Flags;
		public Vector3 BoundsMax;
		uint unused;
		public Vector3 DrawTint;
		uint unused2;
		public GpuBuffer.IndirectDrawIndexedArguments Args;

		internal static void ValidateLayout()
		{
			const int nativeSize = 116;
			var size = Marshal.SizeOf<FragmentDraw>();
			if ( size != nativeSize ) throw new InvalidOperationException( $"AggregateObject.FragmentDraw is {size} bytes, native's AggregateFragmentDraw_t is {nativeSize}" );
		}
	}

	/// <summary>
	/// The model's draws: the material the fragments share, and the buffers they're all drawn from.
	/// </summary>
	public RenderMesh Mesh { get; }

	FragmentDraw[] fragments;
	Vector3[] centers;
	Vector3[] extents;

	/// <summary>
	/// Every fragment, by index - the base instance its draw names.
	/// </summary>
	public ReadOnlySpan<FragmentDraw> Fragments => fragments;

	/// <summary>
	/// Each fragment's world bounds as a center and half extents, for culling.
	/// </summary>
	public ReadOnlySpan<Vector3> Centers => centers;
	public ReadOnlySpan<Vector3> Extents => extents;

	public AggregateObject( RenderMesh mesh, ReadOnlySpan<FragmentDraw> fragments )
	{
		ArgumentNullException.ThrowIfNull( mesh );
		Mesh = mesh;

		// Fragments are already in world space; the object's bounds are all of theirs (AggregateGlue::CreateAggregateSceneObject)
		Transform = Transform.Zero;
		SetFragments( fragments );
	}

	/// <summary>
	/// Replace the fragments, and the bounds they make.
	/// </summary>
	public void SetFragments( ReadOnlySpan<FragmentDraw> value )
	{
		fragments = value.ToArray();
		centers = new Vector3[fragments.Length];
		extents = new Vector3[fragments.Length];

		var bounds = fragments.Length > 0 ? new BBox( fragments[0].BoundsMin, fragments[0].BoundsMax ) : default;
		for ( int i = 0; i < fragments.Length; i++ )
		{
			ref readonly var fragment = ref fragments[i];
			centers[i] = (fragment.BoundsMin + fragment.BoundsMax) * 0.5f;
			extents[i] = (fragment.BoundsMax - fragment.BoundsMin) * 0.5f;
			bounds = bounds.AddPoint( fragment.BoundsMin ).AddPoint( fragment.BoundsMax );
		}

		SlotGeneration = -1;
		LocalBounds = bounds;
	}

	/// <summary>
	/// Draws at all (<c>SceneObject.RenderingEnabled</c>).
	/// </summary>
	public bool Visible { get; set; } = true;

	/// <summary>
	/// Multiplies every fragment's draw tint (<c>CAggregateSceneObjectDesc::FindOrAllocateFragmentTransforms</c>).
	/// </summary>
	public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// Draws in shadow maps, apart from fragments flagged <see cref="AggregateFragmentFlags.DoNotCastShadows"/>.
	/// </summary>
	public bool CastShadows { get; set; } = true;

	/// <summary>
	/// GPU occlusion culled, fragment by fragment (<c>SCENEOBJECTFLAG_NO_OCCLUSION_CULLING</c> off).
	/// </summary>
	public bool OcclusionCulling { get; set; } = true;

	/// <summary>
	/// Lit from lightmaps in forward passes; the bridge supplies <see cref="LightmapAttributes"/>.
	/// </summary>
	public bool Lightmapped { get; set; }

	/// <summary>
	/// Lightmap textures and constants (<c>CAggregateSceneObjectDesc::SetBakedLightingAttributes</c>).
	/// </summary>
	internal RenderAttributes LightmapAttributes { get; set; }

	/// <summary>
	/// Where this frame's instance ids for the fragments start: fragment i's transform is the id at this plus i.
	/// Valid while <see cref="RenderObject.SlotGeneration"/> is the frame's.
	/// </summary>
	internal int FirstInstance;
}
