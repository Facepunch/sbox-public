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
	[Flags]
	public enum FlipFlags
	{
		None = 0,

		[Icon( "align_horizontal_center" )]
		[Title( "Horizontal Flip" )]
		[Description( "Flip the sprite horizontally around the origin." )]
		FlipX = 2,
		[Icon( "align_vertical_center" )]
		[Title( "Vertical Flip" )]
		[Description( "Flip the sprite vertically around the origin." )]
		FlipY = 4
	}

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
		get => _sprite;
		set
		{
			if ( _sprite == value ) return;
			_sprite = value;
			_currentAnimationIndex = 0;
			_animationState.ResetState();
		}
	}

	/// <summary>
	/// The animation that this sprite should start playing when the scene starts.
	/// </summary>
	[Property, Title( "Current Animation" ), Editor( "sprite_animation_name" )]
	[ShowIf( nameof( IsAnimated ), true )]
	public string StartingAnimationName
	{
		get => CurrentAnimation?.Name ?? (_sprite?.Animations?.FirstOrDefault()?.Name ?? "");
		set
		{
			if ( _sprite == null ) return;
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
		get => _animationState.PlaybackSpeed;
		set => _animationState.PlaybackSpeed = value;
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
	[ShowIf( nameof( Opaque ), true )]
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
	public Sprite.Animation CurrentAnimation => _sprite?.GetAnimation( _currentAnimationIndex );

	/// <summary>
	/// The index of the current frame being displayed. This will change over time if the sprite is animated, and can be set to go to a specific frame even during playback.
	/// </summary>
	public int CurrentFrameIndex
	{
		get => _animationState.CurrentFrameIndex;
		set
		{
			_animationState.CurrentFrameIndex = value;
			_animationState.TimeSinceLastFrame = 0;
		}
	}

	/// <summary>
	/// Whether or not the sprite is animated. This is true if the sprite has more than one animation or if the current animation has more than one frame.
	/// </summary>
	public bool IsAnimated => (_sprite?.Animations?.Count ?? 0) > 1;

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

	Sprite.AnimationState _animationState = new();
	HashSet<(MessageType Type, Sprite.BroadcastEvent Content)> _messageQueue = new();
	int _currentAnimationIndex = 0;
	Sprite _sprite;

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
		if ( _sprite is null )
			return;
		if ( index < 0 || index >= (_sprite.Animations?.Count ?? 0) )
		{
			Log.Warning( $"Sprite '{_sprite.ResourceName}' does not have an animation at index {index}." );
			return;
		}
		if ( _currentAnimationIndex == index )
			return;

		_currentAnimationIndex = index;
		_animationState.ResetState();
		OnAnimationStart?.Invoke( CurrentAnimation?.Name );
	}

	/// <summary>
	/// Play an animation by name.
	/// </summary>
	public void PlayAnimation( string name )
	{
		if ( _sprite is null ) return;
		int index = _sprite.GetAnimationIndex( name );
		if ( index < 0 )
		{
			Log.Warning( $"Sprite '{_sprite.ResourceName}' does not have an animation named '{name}'." );
			return;
		}

		PlayAnimation( index );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		ProcessMessageQueue();
	}

	internal void AdvanceFrame()
	{
		var result = _animationState.TryAdvanceFrame( CurrentAnimation, Game.IsPlaying ? Time.Delta : RealTime.Delta );
		if ( !result )
		{
			return;
		}

		if ( _animationState.JustFinished )
		{
			QueueMessage( MessageType.AnimationEnd, new Sprite.BroadcastEvent() { Message = CurrentAnimation?.Name ?? "" } );
		}

		var newFrameIndex = _animationState.CurrentFrameIndex;
		var frame = CurrentAnimation?.Frames?[newFrameIndex];
		if ( frame is not null )
		{
			foreach ( var message in frame.BroadcastMessages )
			{
				QueueMessage( MessageType.BroadcastMessage, message );
			}
		}
	}

	void QueueMessage( MessageType messageType, Sprite.BroadcastEvent message )
	{
		_messageQueue.Add( (messageType, message) );
	}

	// Process any actions that were queued up during frame advancement
	void ProcessMessageQueue()
	{
		// Do this so the actions end up getting invoked on the main thread
		if ( _messageQueue.Count == 0 )
			return;

		foreach ( var ev in _messageQueue )
		{
			switch ( ev.Type )
			{
				case MessageType.BroadcastMessage:
					RunBroadcastEvent( ev.Content );
					break;
				case MessageType.AnimationStart:
					OnAnimationStart?.Invoke( ev.Content.Message );
					break;
				case MessageType.AnimationEnd:
					OnAnimationEnd?.Invoke( ev.Content.Message );
					break;
			}
		}
		_messageQueue.Clear();
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

	enum MessageType
	{
		BroadcastMessage,
		AnimationStart,
		AnimationEnd
	}
}
