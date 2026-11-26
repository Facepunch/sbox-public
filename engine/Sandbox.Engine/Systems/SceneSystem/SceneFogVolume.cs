namespace Sandbox;

/// <summary>
/// Represents a volume of fog in a scene, contributing to volumetric fog effects set on <see cref="SceneCamera.VolumetricFog"/>.
/// </summary>
public sealed class SceneFogVolume : IValid
{
	readonly SceneWorld world;
	bool registered;

	Transform transform;

	/// <summary>
	/// The position and rotation of the fog volume in the scene.
	/// </summary>
	public Transform Transform
	{
		get => transform;
		set
		{
			if ( transform == value ) return;
			transform = value;
			Update();
		}
	}

	BBox boundingBox;

	/// <summary>
	/// Defines the spatial boundaries of the fog volume.
	/// </summary>
	public BBox BoundingBox
	{
		get => boundingBox;
		set
		{
			if ( boundingBox == value ) return;

			boundingBox = value;
			Update();
		}
	}

	float fogStrength;

	/// <summary>
	/// The intensity of the fog. Higher values indicate denser fog.
	/// </summary>
	public float FogStrength
	{
		get => fogStrength;
		set
		{
			if ( fogStrength == value )
				return;

			fogStrength = value;
			Update();
		}
	}

	float falloffExponent;

	/// <summary>
	/// Controls how quickly the fog fades at the edges of the volume. Higher values give sharper transitions.
	/// </summary>
	public float FalloffExponent
	{
		get => falloffExponent;
		set
		{
			if ( registered && falloffExponent == value ) return;
			falloffExponent = value;
			Update();
		}
	}

	Color color = Color.White;

	/// <summary>
	/// Tint applied to the in-scattered light inside this fog volume.
	/// </summary>
	public Color Color
	{
		get => color;
		set
		{
			if ( registered && color.Equals( value ) ) return;
			color = value;
			Update();
		}
	}

	public SceneFogVolume( SceneWorld world, Transform transform, BBox boundingBox, float fogStrength = 1.0f, float falloffExponent = 1.0f )
	{
		this.world = world;
		this.transform = transform;
		this.boundingBox = boundingBox;
		this.fogStrength = fogStrength;
		this.falloffExponent = falloffExponent;

		Update();
	}

	public bool IsValid => registered && world.IsValid();

	/// <summary>
	/// Remove this fog volume from its world. Updating it later adds it back if the world is still valid.
	/// </summary>
	public void Delete()
	{
		if ( !registered ) return;
		registered = false;
		if ( world.IsValid() ) world.RemoveFogVolume( this );
	}

	void Update()
	{
		if ( !world.IsValid() ) return;

		if ( registered )
		{
			world.FogVolumeChanged();
		}
		else
		{
			world.AddFogVolume( this );
			registered = true;
		}
	}
}
