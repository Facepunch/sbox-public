namespace Borealis;

/// <summary>
/// Clustered, box-projected reflection probe (<c>CEnvMapSceneObject</c>).
/// Overlaps prefer higher priority, then smaller bounds, with feathered edges.
/// Also supplies ambient light according to <see cref="SceneLighting.AmbientColor"/> alpha.
/// </summary>
public sealed class EnvMapObject : RenderObject
{
	BBox projectionBounds = BBox.FromPositionAndSize( 0, 1000 );
	float feathering = 0.25f;
	LightBinnerFeature.GpuEnvMap packed;
	int packedTransformVersion;
	bool packedValid;

	/// <summary>The probe's size for blend ordering; independent of the camera and feathering.</summary>
	internal float ProjectionSize { get; private set; }

	/// <summary>
	/// Prepare only: reuse the probe's CPU data across views. Texture indices are resolved into the view's own copy in Setup.
	/// </summary>
	internal ref readonly LightBinnerFeature.GpuEnvMap Packed
	{
		get
		{
			if ( !packedValid || packedTransformVersion != TransformVersion )
			{
				packed = LightBinnerFeature.Pack( this );
				packedTransformVersion = TransformVersion;
				packedValid = true;
			}

			return ref packed;
		}
	}

	/// <summary>
	/// Create a transformed, box-projected cubemap probe.
	/// </summary>
	public EnvMapObject( GpuTexture cubemap, BBox projectionBounds, Transform transform )
	{
		Cubemap = cubemap;
		this.projectionBounds = projectionBounds;
		ProjectionSize = projectionBounds.Size.Length;
		Transform = transform;
		UpdateBounds();
	}

	/// <summary>
	/// The captured cubemap. Without one it reflects black, as native's does.
	/// </summary>
	public GpuTexture Cubemap { get; set; }

	/// <summary>
	/// Local-space influence and projection bounds.
	/// </summary>
	public BBox ProjectionBounds
	{
		get => projectionBounds;
		set
		{
			projectionBounds = value;
			ProjectionSize = value.Size.Length;
			packedValid = false;
			UpdateBounds();
		}
	}

	/// <summary>
	/// How far past its bounds the probe fades out, in world units. <c>SceneCubemap</c>'s default is 0.25.
	/// </summary>
	public float Feathering
	{
		get => feathering;
		set
		{
			feathering = value;
			packedValid = false;
			UpdateBounds();
		}
	}

	/// <summary>
	/// Multiplied into what it reflects.
	/// </summary>
	public Color Tint
	{
		get;
		set
		{
			field = value;
			packedValid = false;
		}
	} = Color.White;

	/// <summary>
	/// Wins over lower priority probes where they overlap.
	/// </summary>
	public int Priority
	{
		get;
		set
		{
			field = value;
			packedValid = false;
		}
	}

	/// <summary>
	/// Small projected bounds can still affect visible reflections.
	/// </summary>
	internal override bool SizeCulled => false;

	void UpdateBounds()
	{
		var feather = MathF.Max( feathering, 0 );
		LocalBounds = new BBox( projectionBounds.Mins - feather, projectionBounds.Maxs + feather );
	}
}
