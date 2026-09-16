namespace Sandbox;

/// <summary>
/// Support's Source Engine's vpcf particles
/// </summary>
[Expose]
[Title( "Legacy Particle System" )]
[Category( "Effects" )]
[Icon( "shower" )]
[EditorHandle( "materials/gizmo/particles.png" )]
[Alias( "ParticleSystem" )]
[Obsolete]
public class LegacyParticleSystem : Component, Component.ExecuteInEditor
{
	Sandbox.ParticleSystem _particles;

	[Property] public bool Looped { get; set; } = false;

	[Range( 0, 2.0f )]
	[Property] public float PlaybackSpeed { get; set; } = 1.0f;

	[Property]
	public Sandbox.ParticleSystem Particles
	{
		get => _particles;
		set
		{
			if ( _particles == value ) return;
			_particles = value;

			RecreateSceneObject();
		}
	}

	[Property] public List<ParticleControlPoint> ControlPoints { get; set; }

	SceneParticles _sceneObject;
	public SceneParticles SceneObject => _sceneObject;

	protected override void DrawGizmos()
	{

	}

	protected override void OnAwake()
	{
		Tags.Add( "particles" );

		base.OnAwake();
	}

	protected override void OnEnabled()
	{
		Assert.NotNull( Scene );

		if ( !_sceneObject.IsValid() )
		{
			RecreateSceneObject();
		}

		OnTransformChanged();
		Transform.OnTransformChanged += OnTransformChanged;
	}

	void RecreateSceneObject()
	{
		// Native particle effects require an available renderer.
		if ( !Graphics.IsAvailable )
			return;

		if ( Particles is null )
			return;

		if ( !Enabled )
			return;

		_sceneObject?.Delete();

		_sceneObject = new SceneParticles( Scene.SceneWorld, _particles );
		_sceneObject.PhysicsWorld = Scene.PhysicsWorld;
		_sceneObject.Transform = WorldTransform;
		_sceneObject.Tags.SetFrom( Tags );
	}

	protected override void OnUpdate()
	{
		if ( !_sceneObject.IsValid() )
		{
			if ( Scene.IsEditor || Looped )
			{
				RecreateSceneObject();
			}

			if ( !_sceneObject.IsValid() )
				return;
		}

		if ( ControlPoints != null && ControlPoints.Count > 0 )
		{
			_sceneObject.SetControlPoint( 0, WorldTransform );

			foreach ( var cp in ControlPoints )
			{
				// Check if StringCP is a number and use it as an index for SetControlPoint
				if ( int.TryParse( cp.StringCP, out int cpIndex ) )
				{
					var outputValue = cp.OutputValue();
					if ( outputValue is Vector3 vectorValue )
					{
						_sceneObject.SetControlPoint( cpIndex, vectorValue );
					}
					else if ( outputValue is Transform transformValue )
					{
						_sceneObject.SetControlPoint( cpIndex, transformValue.Position );
					}
				}
				else
				{
					var outputValue = cp.OutputValue();
					if ( outputValue is Vector3 vectorValue )
					{
						_sceneObject.SetNamedValue( cp.StringCP, vectorValue );
					}
					else if ( outputValue is Transform transformValue )
					{
						_sceneObject.SetNamedValue( cp.StringCP, transformValue.Position );
					}
				}
			}
		}
		else
		{
			_sceneObject.SetControlPoint( 0, WorldPosition );
		}

		_sceneObject.Simulate( Time.Delta * PlaybackSpeed );

		if ( _sceneObject.Finished )
		{
			_sceneObject?.Delete();
			_sceneObject = null;
		}
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTransformChanged;

		_sceneObject?.Delete();
		_sceneObject = null;
	}

	private void OnTransformChanged()
	{
		if ( _sceneObject.IsValid() )
			_sceneObject.Transform = WorldTransform;
	}

	protected override void OnTagsChanged()
	{
		_sceneObject?.Tags.SetFrom( Tags );
	}
}
