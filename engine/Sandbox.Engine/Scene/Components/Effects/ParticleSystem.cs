namespace Sandbox;

/// <summary>
/// Serialized compatibility for legacy Source particle effects. Use ParticleEffect and a particle emitter for new content.
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
	[Property] public bool Looped { get; set; } = false;

	[Range( 0, 2.0f )]
	[Property] public float PlaybackSpeed { get; set; } = 1.0f;

	[Property]
	public Sandbox.ParticleSystem Particles { get; set; }

	[Property] public List<ParticleControlPoint> ControlPoints { get; set; }

	/// <summary>Legacy Source particle scene objects are no longer created.</summary>
	public SceneParticles SceneObject => null;

	protected override void OnAwake()
	{
		Tags.Add( "particles" );

		base.OnAwake();
	}
}
