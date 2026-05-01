using Sandbox.Rendering;
using static Sandbox.IBatchedParticleSpriteRenderer;

namespace Sandbox;

/// <summary>
/// Renders particles as 2D sprites
/// </summary>
[Expose]
[Title( "Particle Text Renderer" )]
[Category( "Effects" )]
[Icon( "text_fields" )]
public sealed class ParticleTextRenderer : ParticleRenderer, Component.ExecuteInEditor, IBatchedParticleSpriteRenderer
{

	[Group( "Text" ), Order( 0 )]
	[Property] public TextRendering.Scope Text { get; set; } = TextRendering.Scope.Default;

	[Group( "Sprite" )]
	[Property, Range( 0, 1 )] public Vector2 Pivot { get; set; } = 0.5f;

	[Group( "Sprite" )]
	[Property, Range( 0, 2 )] public float Scale { get; set; } = 1.0f;

	[Group( "Rendering" ), Order( 1 )]
	[Property, Range( 0, 50 )] public float DepthFeather { get; set; } = 0.0f;

	[Group( "Rendering" )]
	[Property, Range( 0, 1 )] public float FogStrength { get; set; } = 1.0f;

	[Group( "Rendering" )]
	[Property] public bool Additive { get; set; }

	[Group( "Rendering" )]
	[Property] public bool Shadows { get; set; }

	[Group( "Rendering" )]
	[Property] public bool Lighting { get; set; }

	/// <summary>
	/// Indicates whether the sprite is opaque, optimizing rendering by skipping sorting.
	/// </summary>
	[Group( "Rendering" )]
	[Property] public bool Opaque { get; set; }

	[Group( "Rendering" )]
	[Property] public FilterMode TextureFilter { get; set; } = FilterMode.Bilinear;

	/// <summary>
	/// Aligns the sprite to face its velocity direction.
	/// </summary>
	[Property, ToggleGroup( "FaceVelocity" ), Order( 2 )]
	public bool FaceVelocity { get; set; }

	/// <summary>
	/// Offset applied to the rotation when facing velocity.
	/// </summary>
	[Property, ToggleGroup( "FaceVelocity" )]
	[Range( 0, 360 )] public float RotationOffset { get; set; }

	/// <summary>
	/// Enables motion blur effects for the sprite.
	/// </summary>
	[Property, ToggleGroup( "MotionBlur" ), Order( 3 )]
	public bool MotionBlur { get; set; }

	/// <summary>
	/// Determines whether the motion blur effect includes a leading trail.
	/// </summary>
	[Property, ToggleGroup( "MotionBlur" )]
	[InfoBox( "Creates a blur of sprites along the velocity of the particle, giving the impression of motion blur" )]
	public bool LeadingTrail { get; set; } = true;

	/// <summary>
	/// Amount of blur applied to the sprite during motion blur.
	/// </summary>
	[Property, ToggleGroup( "MotionBlur" ), Range( 0, 1 )]
	public float BlurAmount { get; set; } = 0.5f;

	/// <summary>
	/// Spacing between blur samples in the motion blur effect.
	/// </summary>
	[Property, ToggleGroup( "MotionBlur" ), Range( 0, 1 )]
	public float BlurSpacing { get; set; } = 0.5f;

	/// <summary>
	/// Opacity of the blur effect applied to the sprite.
	/// </summary>
	[Property, ToggleGroup( "MotionBlur" ), Range( 0, 1 )]
	public float BlurOpacity { get; set; } = 0.5f;

	/// <summary>
	/// Alignment mode for the sprite's billboard behavior.
	/// </summary>
	[Property]
	[Group( "Sprite" )]
	public ParticleSpriteRenderer.BillboardAlignment Alignment { get; set; } = ParticleSpriteRenderer.BillboardAlignment.LookAtCamera;

	public enum ParticleSortMode
	{
		Unsorted,
		ByDistance
	}

	/// <summary>
	/// Sorting mode used for rendering particles.
	/// </summary>
	[Group( "Sprite" )]
	[Property] public ParticleSortMode SortMode { get; set; }

	/// <summary>
	/// Interface property to determine if particles should be sorted
	/// </summary>
	public bool IsSorted => SortMode != ParticleSortMode.Unsorted;

	/// <summary>
	/// Provides texture for rendering the sprite
	/// </summary>
	public Texture RenderTexture => TextRendering.GetOrCreateTexture( Text, 4096 ) ?? Texture.White;

	ParticleType IBatchedParticleSpriteRenderer.Type => ParticleType.Text;

	protected override void OnAwake()
	{
		Tags.Add( "particles" );

		base.OnAwake();
	}

}
