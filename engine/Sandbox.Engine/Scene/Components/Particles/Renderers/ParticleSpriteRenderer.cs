using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Renders particles as 2D sprites - can be static or animated
/// </summary>
[Expose]
[Title( "Particle Sprite Renderer" )]
[Category( "Effects" )]
[Icon( "favorite" )]
public sealed partial class ParticleSpriteRenderer : ParticleRenderer, Component.ExecuteInEditor, IBatchedParticleSpriteRenderer
{
	/// <summary>
	/// The sprite resource to render. This can be completely static or contain animation(s).
	/// </summary>
	[Property]
	public Sprite Sprite
	{
		get => _sprite;
		set
		{
			if ( _sprite == value ) return;
			_sprite = value;
		}
	}

	/// <summary>
	/// The animation that this sprite should start playing when the scene starts.
	/// </summary>
	[Property, Title( "Animation" ), Category( "Animation" ), Order( -400 ), Editor( "sprite_animation_name" )]
	[ShowIf( nameof( IsAnimated ), true )]
	public string StartingAnimationName
	{
		get => CurrentAnimation?.Name ?? (_sprite?.Animations?.FirstOrDefault()?.Name ?? "");
		set
		{
			if ( _sprite == null ) return;
			SetAnimation( value );
		}
	}

	[Property, Title( "Animation" ), Category( "Animation" ), Order( -399 )]
	[ShowIf( nameof( IsAnimated ), true )]
	public float PlaybackSpeed
	{
		get => _animationState.PlaybackSpeed;
		set => _animationState.PlaybackSpeed = value;
	}

	/// <summary>
	/// The scale of the sprite when rendered.
	/// </summary>
	[Group( "Rendering" )]
	[Property, Range( 0, 2 )] public float Scale { get; set; } = 1.0f;

	/// <summary>
	/// Whether or not the sprite should be rendered additively.
	/// </summary>
	[Group( "Rendering" )]
	[Property] public bool Additive { get; set; }

	/// <summary>
	/// Whether or not the sprite should cast shadows in the scene.
	/// </summary>
	[Group( "Rendering" ), Title( "Cast Shadows" )]
	[Property] public bool Shadows { get; set; }

	/// <summary>
	/// Whether or not the sprite should be lit by the scene lighting.
	/// </summary>
	[Group( "Rendering" )]
	[Property] public bool Lighting { get; set; }

	/// <summary>
	/// Indicates whether the sprite is opaque, optimizing rendering by skipping sorting.
	/// </summary>
	[Group( "Rendering" )]
	[Property] public bool Opaque { get; set; }

	/// <summary>
	/// The texture filtering mode used when rendering the sprite. For pixelated sprites use <see cref="FilterMode.Point"/>.
	/// </summary>
	[Group( "Rendering" )]
	[Property] public FilterMode TextureFilter { get; set; } = FilterMode.Bilinear;

	/// <summary>
	/// Alignment mode for the sprite's billboard behavior.
	/// </summary>
	[Group( "Rendering" )]
	[Property] public BillboardAlignment Alignment { get; set; } = BillboardAlignment.LookAtCamera;

	/// <summary>
	/// Sorting mode used for rendering particles.
	/// </summary>
	[Group( "Rendering" )]
	[Property] public ParticleSortMode SortMode { get; set; }

	/// <summary>
	/// Amount of feathering applied to the depth, softening its intersection with geometry.
	/// </summary>
	[Group( "Rendering" )]
	[Property, Range( 0, 50 )] public float DepthFeather { get; set; } = 0.0f;

	/// <summary>
	/// Sprites closer to the camera than this are completely invisible.
	/// </summary>
	[Group( "Rendering" )]
	[Property, Range( 0, 64 )] public float CameraFadeNear { get; set; } = 0.0f;

	/// <summary>
	/// Sprites further from the camera than this are fully opaque. Between this and
	/// <see cref="CameraFadeNear"/> they fade out. Leave at zero to disable the fade.
	/// </summary>
	[Group( "Rendering" )]
	[Property, Range( 0, 256 )] public float CameraFadeFar { get; set; } = 0.0f;

	/// <summary>
	/// The strength of the fog effect applied to the sprite. This determines how much the sprite blends with any fog in the scene.
	/// </summary>
	[Group( "Rendering" )]
	[Property, Range( 0, 1 )] public float FogStrength { get; set; } = 1.0f;

	/// <summary>
	/// Draw the sprite artwork over a refracted background in one pass. Alpha controls the artwork only.
	/// Requires a normal texture and translucent, non-additive rendering. The captured scene is already lit and fogged.
	/// </summary>
	[Property, ToggleGroup( "Refraction" )]
	public bool Refraction { get; set; }

	/// <summary>
	/// Tangent-space normal map sampled in linear space. Use the same UV layout as the sprite texture:
	/// particle sheet sequences, frame blending and rotation are shared with the alpha mask.
	/// For sprites that change texture between frames, this map is shared by every frame.
	/// </summary>
	[Property, ToggleGroup( "Refraction" )]
	public Texture NormalTexture { get; set; }

	/// <summary>
	/// Maximum refraction offset in render pixels before the normal is applied. Alpha, depth feathering
	/// and camera fading affect artwork opacity only, never the refraction.
	/// Supports constants, random ranges and lifetime curves, like particle alpha.
	/// Zero leaves the captured background undistorted. Refraction must be disabled to return to ordinary sprite rendering.
	/// </summary>
	[Property, ToggleGroup( "Refraction" ), Range( 0, 64 )]
	public ParticleFloat RefractionStrength { get; set; } = 8.0f;

	/// <summary>
	/// Whether this renderer can use scene-color refraction with its current blend settings and normal map.
	/// </summary>
	internal bool IsRefractive => Refraction && NormalTexture is not null && !Opaque && !Additive;

	/// <summary>
	/// Supply the normal map to the batch only when this renderer uses refraction.
	/// </summary>
	Texture IBatchedParticleSpriteRenderer.RefractionNormalTexture => IsRefractive ? NormalTexture : null;

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
	/// The animation that is currently being played. Returns null if no sprite is set or the sprite has no animations.
	/// </summary>
	public Sprite.Animation CurrentAnimation => _sprite?.GetAnimation( _currentAnimationIndex );

	/// <summary>
	/// Whether or not the sprite is animated. This is true if the sprite has more than one animation or if the current animation has more than one frame.
	/// </summary>
	public bool IsAnimated => (_sprite?.Animations?.Count ?? 0) > 1;

	/// <summary>
	/// Interface property to determine if particles should be sorted
	/// </summary>
	public bool IsSorted => SortMode != ParticleSortMode.Unsorted;

	/// <summary>
	/// The pivot point of the sprite, used for rotation and scaling. This is in normalized coordinates (0 to 1).
	/// </summary>
	public Vector2 Pivot => CurrentAnimation?.Origin ?? new Vector2( 0.5f, 0.5f );

	/// <summary>
	/// The texture being displayed from the sprite given the current frame/animation.
	/// </summary>
	public Texture Texture
	{
		get => RenderTexture;
		[Obsolete]
		set { }
	}

	/// <summary>
	/// Provides texture for rendering - implementation for IBatchedParticleSpriteRenderer
	/// </summary>
	public Texture RenderTexture
	{
		get
		{
			var _anim = CurrentAnimation;
			if ( _anim is null )
				return Texture.Transparent;
			var currentFrameIndex = _animationState.CurrentFrameIndex;
			if ( currentFrameIndex < 0 || currentFrameIndex >= _anim.Frames.Count )
				return Texture.Transparent;
			return _anim.Frames[currentFrameIndex]?.Texture;
		}
	}

	Sprite.AnimationState _animationState = new();
	int _currentAnimationIndex = 0;
	Sprite _sprite;

	public enum ParticleSortMode
	{
		Unsorted,
		ByDistance
	}

	public enum BillboardAlignment
	{
		/// <summary>
		/// Look directly at the camera, apply roll
		/// </summary>
		LookAtCamera,

		/// <summary>
		/// Look at the camera but don't pitch up and down, up is always up, can roll
		/// </summary>
		RotateToCamera,

		/// <summary>
		/// Use rotation provided by the particle, pitch yaw and roll
		/// </summary>
		Particle,

		/// <summary>
		/// Align to game object rotation, apply pitch yaw and roll
		/// </summary>
		Object,
	}

	protected override void OnAwake()
	{
		Tags.Add( "particles" );

		base.OnAwake();
	}

	/// <summary>
	/// Set the animation by index (the first animation is index 0).
	/// </summary>
	public void SetAnimation( int index )
	{
		if ( _sprite is null ) return;
		if ( index < 0 || index >= (_sprite.Animations?.Count ?? 0) )
		{
			Log.Warning( $"Sprite '{_sprite.ResourceName}' does not have an animation at index {index}." );
			return;
		}

		_currentAnimationIndex = index;
		_animationState.CurrentFrameIndex = 0;
	}

	/// <summary>
	/// Set the animation by name.
	/// </summary>
	public void SetAnimation( string name )
	{
		if ( _sprite is null ) return;
		int index = _sprite.GetAnimationIndex( name );
		if ( index < 0 )
		{
			Log.Warning( $"Sprite '{_sprite.ResourceName}' does not have an animation named '{name}'." );
			return;
		}

		SetAnimation( index );
	}

	internal void AdvanceFrame()
	{
		_animationState.TryAdvanceFrame( CurrentAnimation, Game.IsPlaying ? Time.Delta : RealTime.Delta );
	}

}
