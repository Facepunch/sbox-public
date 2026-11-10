namespace Sandbox.SceneRenderer;

/// <summary>
/// Clustered, box-projected reflection probe (<c>CEnvMapSceneObject</c>).
/// Overlaps prefer higher priority, then smaller bounds, with feathered edges.
/// Also supplies ambient light according to <see cref="SceneLighting.AmbientColor"/> alpha.
/// </summary>
public sealed class EnvMapObject : RenderObject
{
	BBox projectionBounds = BBox.FromPositionAndSize( 0, 1000 );
	float feathering = 0.25f;

	/// <summary>
	/// Create a transformed, box-projected cubemap probe.
	/// </summary>
	public EnvMapObject( Texture cubemap, BBox projectionBounds, Transform transform )
	{
		Cubemap = cubemap;
		this.projectionBounds = projectionBounds;
		Transform = transform;
		UpdateBounds();
	}

	/// <summary>
	/// The captured cubemap. Without one it reflects black, as native's does.
	/// </summary>
	public Texture Cubemap { get; set; }

	/// <summary>
	/// Local-space influence and projection bounds.
	/// </summary>
	public BBox ProjectionBounds
	{
		get => projectionBounds;
		set
		{
			projectionBounds = value;
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
			UpdateBounds();
		}
	}

	/// <summary>
	/// Multiplied into what it reflects.
	/// </summary>
	public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// Wins over lower priority probes where they overlap.
	/// </summary>
	public int Priority { get; set; }

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
