using System.Runtime.InteropServices;

namespace Borealis;

/// <summary>
/// Many pieces of static geometry drawn as one object: every fragment is one draw of <see cref="Mesh"/>'s single material,
/// with its own world transform and bounds, culled on its own and drawn together in one multi-draw. A map's world geometry
/// is made of these. The managed counterpart of native's <c>CAggregateSceneObject</c>.
/// </summary>
internal sealed class AggregateObject : RenderObject
{
	/// <summary>
	/// One fragment as native draws it (<c>RenderTools::AggregateFragmentDraw_t</c>): its world transform and bounds, its draw's
	/// tint and UV density, and its draw's range with the fragment's index as its base instance.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct FragmentDraw
	{
		public Matrix3x4 Transform;
		public Vector3 BoundsMin;
		public FragmentFlags Flags;
		public Vector3 BoundsMax;
		public float UvDensity;
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

	// Its fragments are one draw at one LOD: it animates whole or not at all
	internal override RenderMesh.LodAnimation MaterialAnimation => Mesh.Animation.Whole();

	readonly FragmentDraw[] fragments;
	readonly Vector3[] centers;
	readonly Vector3[] extents;

	/// <summary>
	/// Every fragment, by index - the base instance its draw names.
	/// </summary>
	public ReadOnlySpan<FragmentDraw> Fragments => fragments;

	/// <summary>
	/// Each fragment's world bounds as a center and half extents, for culling.
	/// </summary>
	public ReadOnlySpan<Vector3> Centers => centers;
	public ReadOnlySpan<Vector3> Extents => extents;

	/// <summary>The most indices any fragment's draw has: what bounds the meshlets a fragment can have.</summary>
	public uint MaxIndexCount { get; }

	public AggregateObject( RenderMesh mesh, ReadOnlySpan<FragmentDraw> fragments )
	{
		ArgumentNullException.ThrowIfNull( mesh );
		Mesh = mesh;

		// Fragments are already in world space; the object's bounds are all of theirs
		Transform = Transform.Zero;
		this.fragments = fragments.ToArray();
		centers = new Vector3[fragments.Length];
		extents = new Vector3[fragments.Length];

		var bounds = fragments.Length > 0 ? new BBox( fragments[0].BoundsMin, fragments[0].BoundsMax ) : default;
		var maxIndexCount = 0u;
		for ( int i = 0; i < fragments.Length; i++ )
		{
			ref readonly var fragment = ref fragments[i];
			centers[i] = (fragment.BoundsMin + fragment.BoundsMax) * 0.5f;
			extents[i] = (fragment.BoundsMax - fragment.BoundsMin) * 0.5f;
			bounds = bounds.AddPoint( fragment.BoundsMin ).AddPoint( fragment.BoundsMax );
			maxIndexCount = Math.Max( maxIndexCount, fragment.Args.IndexCount );
		}

		MaxIndexCount = maxIndexCount;

		LocalBounds = bounds;
	}

	/// <summary>
	/// Draws at all (<c>SceneObject.RenderingEnabled</c>).
	/// </summary>
	public bool Visible
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ShadowChanged();
		}
	} = true;

	/// <summary>
	/// Multiplies every fragment's draw tint (<c>CAggregateSceneObjectDesc::FindOrAllocateFragmentTransforms</c>).
	/// </summary>
	public Color Tint
	{
		get;
		set
		{
			// Only alpha reaches a shadow: a faded caster dithers. Its fragments' kept entries hold it
			var faded = field.a != value.a;
			var changed = !field.Equals( value );
			field = value;
			if ( faded ) ShadowChanged();
			if ( changed ) Changed( WorldNote.Entry );
		}
	} = Color.White;

	/// <summary>
	/// Draws in shadow maps, apart from fragments flagged <see cref="FragmentFlags.DoNotCastShadows"/>.
	/// </summary>
	public bool CastShadows
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ShadowChanged();
		}
	} = true;

	/// <summary>
	/// GPU occlusion culled, fragment by fragment (<c>SCENEOBJECTFLAG_NO_OCCLUSION_CULLING</c> off).
	/// </summary>
	public bool OcclusionCulling { get; set; } = true;

	/// <summary>
	/// Lit from lightmaps in forward passes; the bridge supplies <see cref="LightmapAttributes"/>.
	/// </summary>
	public bool Lightmapped
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			Changed( WorldNote.Object );
		}
	}

	/// <summary>
	/// Lightmap textures and constants (<c>CAggregateSceneObjectDesc::SetBakedLightingAttributes</c>).
	/// </summary>
	internal ShaderParameters LightmapAttributes
	{
		get;
		set
		{
			if ( ReferenceEquals( field, value ) ) return;
			field = value;
			Changed( WorldNote.Object );
		}
	}
}

/// <summary>
/// An aggregate fragment's flags, as native's aggregate stores them (<c>AggregateFragmentFlags</c>).
/// </summary>
[Flags]
internal enum FragmentFlags
{
	None = 0,
	RenderToCubemaps = 1,
	DisabledInLowQuality = 2,
	DoNotCastShadows = 4,
}
