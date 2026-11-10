namespace Editor.AnimGraph;

/// <summary>
/// Ground plane that scrolls opposite to the preview model's root motion, so
/// the character walks in place on a treadmill instead of drifting away.
/// </summary>
internal sealed class TreadmillGround
{
	// dev measure tiles every 100 units - scale down for a denser pattern, wrap to match
	private const float Scale = 0.5f;
	private const float Tile = 100f * Scale;

	private readonly GameObject _ground;
	private Rotation _rotation = Rotation.Identity;
	private Vector3 _wrapped;

	public bool Visible { get => _ground.Enabled; set => _ground.Enabled = value; }

	/// <summary>Create inside the preview scene's scope.</summary>
	public TreadmillGround()
	{
		_ground = new GameObject( true, "ground" );
		var renderer = _ground.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/plane_large.vmdl" );
		renderer.MaterialOverride = Material.Load( "materials/dev/dev_measuregeneric01b.vmat" );
		_ground.WorldScale = Scale;
	}

	public void Update( SkinnedModelRenderer model )
	{
		if ( !model.IsValid() )
			return;

		var motion = model.RootMotion;

		// The character doesn't turn - the ground rotates the reverse way under it
		_rotation = motion.Rotation.Inverse * _rotation;

		var slide = _rotation.Inverse * -motion.Position;
		_wrapped.x = Wrap( _wrapped.x + slide.x );
		_wrapped.y = Wrap( _wrapped.y + slide.y );

		Apply();
	}

	/// <summary>
	/// Place the ground from a clip's total root motion at a point in time, for previews that
	/// know exactly where they are. Playback only ever reports how far the root moved since the
	/// last frame, and a graph reads any step backwards as having looped, so scrubbing can't be
	/// driven by <see cref="Update"/>.
	/// </summary>
	public void SetFromRootMotion( Transform rootMotion )
	{
		_rotation = rootMotion.Rotation.Inverse;

		var slide = rootMotion.Rotation * -rootMotion.Position;
		_wrapped.x = Wrap( slide.x );
		_wrapped.y = Wrap( slide.y );

		Apply();
	}

	public void Reset()
	{
		_rotation = Rotation.Identity;
		_wrapped = default;
		Apply();
	}

	private void Apply()
	{
		_ground.WorldRotation = _rotation;
		_ground.WorldPosition = _rotation * _wrapped;
	}

	private static float Wrap( float v ) => v - MathF.Round( v / Tile ) * Tile;
}
