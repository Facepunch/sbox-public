using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Renders a sprite in the world
/// </summary>
[Expose]
[Title( "Sprite Renderer" )]
[Category( "Rendering" )]
[Icon( "favorite" )]
public sealed partial class SpriteRenderer : Renderer, Component.ExecuteInEditor, ISpriteRenderGroup
{
	public enum BillboardMode
	{
		Always,
		YOnly,
		Particle,
		None
	}

	/// <summary>
	/// The sprite resource to render. This can be completely static or contain animation(s).
	/// </summary>
	[Property]
	public Sprite Sprite
	{
		get => _instance.Sprite;
		set
		{
			if ( Sprite == value ) return;
			_instance.Sprite = value;
			_currentAnimationIndex = 0;
		}
	}

	/// <summary>
	/// The animation that this sprite should start playing when the scene starts.
	/// </summary>
	[Property, Title( "Current Animation" ), Editor( "sprite_animation_name" )]
	[ShowIf( nameof( IsAnimated ), true )]
	public string StartingAnimationName
	{
		get => CurrentAnimation?.Name ?? (Sprite?.Animations?.FirstOrDefault()?.Name ?? "");
		set
		{
			if ( Sprite == null ) return;
			PlayAnimation( value );
		}
	}

	/// <summary>
	/// The playback speed of the animation. 0 is paused, and negative values will play the animation in reverse.
	/// </summary>
	[Property]
	[ShowIf( nameof( IsAnimated ), true )]
	public float PlaybackSpeed
	{
		get => _instance.PlaybackSpeed;
		set => _instance.PlaybackSpeed = value;
	}

	/// <summary>
	/// The width and height of the sprite in world units.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public Vector2 Size { get; set; } = 10.0f;

	/// <summary>
	/// The color of the sprite. This is multiplied with the texture color.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public Color Color { get; set; } = Color.White;

	[Property, Category( "Visuals" ), Order( -200 )]
	public Color OverlayColor { get; set; } = Color.White.WithAlpha( 0 );

	/// <summary>
	/// Whether or not the sprite should be rendered additively.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public bool Additive { get; set; }

	/// <summary>
	/// Whether or not the sprite should cast shadows.
	/// </summary>
	[Property, Title( "Cast Shadows" ), Category( "Visuals" ), Order( -200 )]
	public bool Shadows { get; set; }

	/// <summary>
	/// Whether or not the sprite should be rendered opaque. If true, any semi-transparent pixels will be dithered.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public bool Opaque { get; set; }

	/// <summary>
	/// Alpha threshold for discarding pixels. Pixels with alpha below this value will be discarded. 
	/// Only used when Opaque is true. Range: 0.0 (transparent) to 1.0 (opaque). Default is 0.5.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 ), Range( 0f, 1f )]
	public float AlphaCutoff { get; set; } = 0.5f;

	/// <summary>
	/// Whether or not the sprite should be lit by the scene's lighting system. Otherwise it will be unlit/fullbright.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public bool Lighting { get; set; }

	/// <summary>
	/// Amount of feathering applied to the depth, softening its intersection with geometry.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public float DepthFeather { get; set; }

	/// <summary>
	/// Sprites closer to the camera than this are completely invisible.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public float CameraFadeNear { get; set; }

	/// <summary>
	/// Sprites further from the camera than this are fully opaque. Between this and
	/// <see cref="CameraFadeNear"/> they fade out. Leave at zero to disable the fade.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public float CameraFadeFar { get; set; }

	/// <summary>
	/// The strength of the fog effect applied to the sprite. This determines how much the sprite blends with any fog in the scene.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public float FogStrength { get; set; } = 1.0f;

	/// <summary>
	/// Whether or not the sprite should be flipped horizontally.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public bool FlipHorizontal { get; set; }

	/// <summary>
	/// Whether or not the sprite should be flipped vertically.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public bool FlipVertical { get; set; }

	/// <summary>
	/// The texture filtering mode used when rendering the sprite. For pixelated sprites, use <see cref="Sandbox.UI.ImageRendering.Point"/>.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public FilterMode TextureFilter { get; set; } = FilterMode.Bilinear;

	/// <summary>
	/// Alignment mode for the sprite's billboard behavior.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public BillboardMode Billboard { get; set; } = BillboardMode.Always;

	/// <summary>
	/// Whether or not the sprite should be sorted by depth. If the sprite is opaque, this can be turned off for a performance boost if not needed.
	/// </summary>
	[Property, Category( "Visuals" ), Order( -200 )]
	public bool IsSorted { get; set; }

	/// <summary>
	/// This action is invoked when an animation starts playing. The string parameter is the name of the animation that started.
	/// </summary>
	[Property, Category( "Actions" )]
	public Action<string> OnAnimationStart { get; set; }

	/// <summary>
	/// This action is invoked when an animation finishes playing or has looped. The string parameter is the name of the animation.
	/// </summary>
	[Property, Category( "Actions" )]
	public Action<string> OnAnimationEnd { get; set; }

	/// <summary>
	/// This action is invoked when advancing to a new frame that has broadcast messages. The string parameter is the message being broadcast.
	/// </summary>
	[Property, Category( "Actions" )]
	public Action<string> OnBroadcastMessage { get; set; }

	/// <summary>
	/// The animation that is currently being played. Returns null if no sprite is set or the sprite has no animations.
	/// </summary>
	public Sprite.Animation CurrentAnimation => Sprite?.GetAnimation( _currentAnimationIndex );

	/// <summary>
	/// The index of the current frame being displayed. This will change over time if the sprite is animated, and can be set to go to a specific frame even during playback.
	/// </summary>
	public int CurrentFrameIndex
	{
		get => _instance.CurrentFrameIndex;
		set
		{
			_instance.SelectAnimation( CurrentAnimation );
			_instance.CurrentFrameIndex = value;
		}
	}

	/// <summary>
	/// Whether or not the sprite is animated. This is true if the sprite has more than one animation.
	/// </summary>
	public bool IsAnimated => (Sprite?.Animations?.Count ?? 0) > 1;

	/// <summary>
	/// The texture of the current frame being displayed. Returns a transparent texture when no valid frame is available.
	/// </summary>
	public Texture Texture
	{
		get
		{
			var _anim = CurrentAnimation;
			if ( _anim is null )
				return Texture.Transparent;
			if ( CurrentFrameIndex < 0 || CurrentFrameIndex >= _anim.Frames.Count )
				return Texture.Transparent;
			return _anim.Frames[CurrentFrameIndex]?.Texture;
		}
		[Obsolete]
		set { }
	}

	internal Vector2 Pivot
	{
		get
		{
			var _anim = CurrentAnimation;
			if ( _anim is null )
				return new Vector2( 0.5f, 0.5f );
			return _anim.Origin;
		}
	}

	readonly SpriteInstance _instance = new( null );
	int _currentAnimationIndex = 0;

	protected override void DrawGizmos()
	{
		base.DrawGizmos();
		if ( Game.IsPlaying ) return;

		Gizmo.Transform = Transform.World;

		bool isBillboard = Billboard == BillboardMode.Always || Billboard == BillboardMode.YOnly;
		if ( isBillboard )
			Gizmo.Transform = Gizmo.Transform.WithRotation( new Rotation() );

		Vector3 scale = new( Transform.World.Scale.x, Transform.World.Scale.x, Transform.World.Scale.z );
		Gizmo.Transform = Gizmo.Transform.WithScale( scale );

		Vector2 pivotScale = (Pivot - 0.5f) * Size;
		Vector2 spriteSize = new( Size.x, Size.y );

		if ( isBillboard )
		{
			spriteSize += Vector2.Abs( pivotScale * 2 );

			// Calculate the AABB of the rotated sprite around its pivot
			float angle = MathX.DegreeToRadian( Transform.World.Rotation.Roll() );
			float cos = MathF.Cos( angle );
			float sin = MathF.Sin( angle );

			spriteSize = new Vector2(
				MathF.Abs( spriteSize.x * cos ) + MathF.Abs( spriteSize.y * sin ),
				MathF.Abs( spriteSize.x * sin ) + MathF.Abs( spriteSize.y * cos )
			);
		}

		// Flatten it if not a billboard
		Vector3 bboxSize = new( isBillboard ? spriteSize.x : 0.5f, spriteSize.x, spriteSize.y );
		Vector3 bboxPos = isBillboard ? Vector3.Zero : new Vector3( 0, pivotScale.x, pivotScale.y );
		var bbox = BBox.FromPositionAndSize( bboxPos, bboxSize );

		Gizmo.Hitbox.BBox( bbox );

		if ( Gizmo.IsHovered || Gizmo.IsSelected )
		{
			Gizmo.Draw.Color = Gizmo.IsSelected ? Color.White : Color.Orange;
			Gizmo.Draw.LineBBox( bbox );
		}
	}

	/// <summary>
	/// Play an animation by index (the first animation is index 0).
	/// </summary>
	public void PlayAnimation( int index )
	{
		if ( Sprite is null )
			return;
		if ( index < 0 || index >= (Sprite.Animations?.Count ?? 0) )
		{
			Log.Warning( $"Sprite '{Sprite.ResourceName}' does not have an animation at index {index}." );
			return;
		}
		if ( _currentAnimationIndex == index )
			return;

		_currentAnimationIndex = index;
		_instance.SelectAnimation( CurrentAnimation, restart: true );
		OnAnimationStart?.Invoke( CurrentAnimation?.Name );
	}

	/// <summary>
	/// Play an animation by name.
	/// </summary>
	public void PlayAnimation( string name )
	{
		if ( Sprite is null ) return;
		int index = Sprite.GetAnimationIndex( name );
		if ( index < 0 )
		{
			Log.Warning( $"Sprite '{Sprite.ResourceName}' does not have an animation named '{name}'." );
			return;
		}

		PlayAnimation( index );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		var animation = CurrentAnimation;
		// Resources can be edited in place, including replacing the selected animation.
		_instance.SelectAnimation( animation );
		if ( !_instance.Update( Time.Delta, singleFrame: true ) ) return;

		var frame = animation.Frames.ElementAtOrDefault( CurrentFrameIndex );
		// Capture before any callbacks: playback changes don't cancel reached-frame events.
		var messages = frame?.BroadcastMessages is { Count: > 0 } events ? events.ToArray() : null;

		if ( _instance.JustFinished )
		{
			OnAnimationEnd?.Invoke( animation.Name );
			if ( !IsValid || GameObject.IsDestroyed ) return;
		}

		if ( messages is null ) return;
		foreach ( var message in messages )
		{
			if ( message is not null ) RunBroadcastEvent( message );
			if ( !IsValid || GameObject.IsDestroyed ) return;
		}
	}

	// Run any user-defined broadcast events
	void RunBroadcastEvent( Sprite.BroadcastEvent broadcastEvent )
	{
		var isEditorOnly = Scene.IsEditor && GameObject.Flags.HasFlag( GameObjectFlags.EditorOnly );
		var shouldBroadcast = Game.IsPlaying || isEditorOnly;
		switch ( broadcastEvent.Type )
		{
			case Sprite.BroadcastEventType.CustomMessage:
				if ( shouldBroadcast )
				{
					OnBroadcastMessage?.Invoke( broadcastEvent.Message );
				}
				break;
			case Sprite.BroadcastEventType.PlaySound:
				if ( shouldBroadcast && broadcastEvent.Sound is not null )
				{
					Sound.Play( broadcastEvent.Sound, WorldPosition );
				}
				break;
			case Sprite.BroadcastEventType.SpawnPrefab:
				// Only spawn prefabs during gameplay, not in editor
				if ( Game.IsPlaying )
				{
					broadcastEvent.Prefab?.Clone( WorldPosition );
				}
				break;
		}
	}

}
