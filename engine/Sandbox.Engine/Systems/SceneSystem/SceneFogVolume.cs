namespace Sandbox;

/// <summary>
/// Represents a volume of fog in a scene, contributing to volumetric fog effects set on <see cref="SceneCamera.VolumetricFog"/>.
/// </summary>
public sealed class SceneFogVolume : IValid
{
	uint? ID { get; set; }
	SceneWorld World { get; set; }

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
		set { falloffExponent = value; Update(); }
	}

	Color color = Color.White;

	/// <summary>
	/// Tint applied to the in-scattered light inside this fog volume.
	/// </summary>
	public Color Color
	{
		get => color;
		set { color = value; Update(); }
	}

	public SceneFogVolume( SceneWorld world, Transform transform, BBox boundingBox, float fogStrength = 1.0f, float falloffExponent = 1.0f )
	{
		ID = default;
		World = world;

		// set the backing fields to not trigger Update() for each
		this.transform = transform;
		this.boundingBox = boundingBox;
		this.fogStrength = fogStrength;
		this.falloffExponent = falloffExponent;

		Update();
	}

	public bool IsValid => ID.HasValue && World.IsValid();

	/// <summary>
	/// Delete this fog volume. You shouldn't access it anymore.
	/// </summary>
	public void Delete()
	{
		if ( !IsValid )
			return;

		if ( ID.HasValue )
		{
			NativeEngine.CSceneSystem.RemoveVolumetricFogVolume( World, ID.Value );
			ID = default;
		}
	}

	void Update()
	{
		if ( !World.IsValid() )
			return;

		if ( ID.HasValue )
		{
			NativeEngine.CSceneSystem.RemoveVolumetricFogVolume( World, ID.Value );
			ID = default;
		}

		var mat = Matrix.CreateRotation( Transform.Rotation ) * Matrix.CreateTranslation( Transform.Position );

		NativeEngine.SceneVolumetricFogVolume volume = new()
		{
			m_vMin = boundingBox.Mins,
			m_vMax = boundingBox.Maxs,
			m_fStrength = fogStrength,
			m_vColor = color,
			m_bSpherical = false,
			m_fExponent = falloffExponent,
			m_matWorldToVolume = mat.Inverted.Transpose(),
			m_uID = 0,
		};

		ID = NativeEngine.CSceneSystem.AddVolumetricFogVolume( World, volume );
	}
}
