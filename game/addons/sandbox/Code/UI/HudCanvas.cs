using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// HUD layer with glow & scanline effects
/// </summary>
public sealed class HudCanvas : Panel, IPanelDraw
{
	/// <summary>
	/// The component that owns the effect settings
	/// </summary>
	public HudLayer Layer { get; set; }

	readonly List<HudSection> _sections = new();

	Texture _target;
	RenderTarget _renderTarget;
	CommandList _paintCommands;
	CameraComponent _camera;
	Material _material;

	// half resolution ping-pong targets for the glow blur
	Texture _glowA, _glowB;
	RenderTarget _glowATarget, _glowBTarget;
	Material _glowMaterial;

	/// <summary>
	/// Screen rects the sections draw into this frame. Empty means there is nothing to paint
	/// </summary>
	readonly List<Rect> _regions = new();

	bool _paintRecorded;

	/// <summary>
	/// Canvas size in whole pixels, which is what the paint target is created with
	/// </summary>
	public Vector2 PixelSize => new( (int)Box.Rect.Width, (int)Box.Rect.Height );

	public HudCanvas()
	{
		Style.Position = PositionMode.Absolute;
		Style.Width = Length.Percent( 100 );
		Style.Height = Length.Percent( 100 );
		Style.PointerEvents = PointerEvents.None;
	}

	/// <summary>
	/// The section of type <typeparamref name="T"/>, added on first use unless <paramref name="create"/> is false (then null if there is none)
	/// </summary>
	public T Section<T>( bool create = true ) where T : HudSection, new()
	{
		foreach ( var section in _sections )
		{
			if ( section is T existing ) return existing;
		}

		if ( !create ) return null;

		var added = new T { Canvas = this };
		_sections.Add( added );
		_sections.Sort( ( a, b ) => a.Order.CompareTo( b.Order ) );
		return added;
	}

	public override void Tick()
	{
		base.Tick();

		if ( !Layer.IsValid() ) return;

		foreach ( var section in _sections )
			section.Tick();

		_regions.Clear();
		foreach ( var section in _sections )
			section.GetCompositeRegions( _regions );

		if ( _regions.Count == 0 )
		{
			if ( _paintRecorded ) _paintCommands.Reset();
			_paintRecorded = false;
			return;
		}

		PaintTarget();
	}

	void PaintTarget()
	{
		var width = (int)PixelSize.x;
		var height = (int)PixelSize.y;
		if ( width <= 0 || height <= 0 ) return;

		if ( _target is null || _target.Width != width || _target.Height != height )
		{
			_target?.Dispose();
			_target = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RGBA16161616F ).Create();
			_renderTarget = RenderTarget.From( _target );
		}

		_paintCommands ??= new CommandList( GetType().Name );

		var camera = Game.ActiveScene?.Camera;
		if ( camera != _camera )
		{
			_camera?.RemoveCommandList( _paintCommands );
			camera?.AddCommandList( _paintCommands, Stage.AfterViewmodel );
			_camera = camera;
		}

		_paintCommands.Reset();
		_paintRecorded = true;
		_paintCommands.Attributes.Set( "UIGammaOutput", true );
		_paintCommands.Attributes.Set( "UIFrameGrabEncoded", true );
		_paintCommands.SetRenderTarget( _renderTarget );

		using ( var painter = Painter.Begin( _paintCommands, new Rect( 0, 0, width, height ) ) )
		{
			painter.Clear( Color.Transparent );

			foreach ( var section in _sections )
			{
				using var _ = painter.Scope();
				section.PaintTint = Layer.CurrentTint;
				section.Paint( painter );
			}
		}

		BlurGlow( width, height );

		_paintCommands.ClearRenderTarget();
	}

	void BlurGlow( int width, int height )
	{
		var glowWidth = Math.Max( 1, width / 2 );
		var glowHeight = Math.Max( 1, height / 2 );

		if ( _glowA is null || _glowA.Width != glowWidth || _glowA.Height != glowHeight )
		{
			_glowA?.Dispose();
			_glowB?.Dispose();
			_glowA = Texture.CreateRenderTarget().WithSize( glowWidth, glowHeight ).WithFormat( ImageFormat.RGBA16161616F ).Create();
			_glowB = Texture.CreateRenderTarget().WithSize( glowWidth, glowHeight ).WithFormat( ImageFormat.RGBA16161616F ).Create();
			_glowATarget = RenderTarget.From( _glowA );
			_glowBTarget = RenderTarget.From( _glowB );
		}

		_glowMaterial ??= Material.FromShader( "shaders/hud_glow.shader" );

		var reach = Layer.GlowRadius * ScaleToScreen * GamePreferences.HudScale * 0.5f;
		_paintCommands.Attributes.Set( "GlowReach", reach );

		_paintCommands.SetRenderTarget( _glowATarget );
		_paintCommands.Attributes.Set( "GlowSource", _target );
		_paintCommands.Attributes.Set( "GlowExtract", 1 );
		_paintCommands.Attributes.Set( "GlowStep", new Vector2( 1f / glowWidth, 0 ) );
		_paintCommands.Blit( _glowMaterial );

		_paintCommands.SetRenderTarget( _glowBTarget );
		_paintCommands.Attributes.Set( "GlowSource", _glowA );
		_paintCommands.Attributes.Set( "GlowExtract", 0 );
		_paintCommands.Attributes.Set( "GlowStep", new Vector2( 0, 1f / glowHeight ) );
		_paintCommands.Blit( _glowMaterial );
	}

	void IPanelDraw.Draw( CommandList commands )
	{
		if ( !Layer.IsValid() || _regions.Count == 0 || _target is null || _glowB is null ) return;

		_material ??= Material.FromShader( "shaders/hud_composite.shader" );

		var scale = ScaleToScreen;
		var attributes = commands.Attributes;
		attributes.Set( "HudTexture", _target );
		attributes.Set( "HudInvSize", new Vector2( 1f / _target.Width, 1f / _target.Height ) );
		attributes.Set( "GlowTexture", _glowB );
		attributes.Set( "GlowStrength", Layer.GlowStrength * GamePreferences.HudGlow );
		attributes.Set( "HudOpacity", Layer.Opacity );
		attributes.Set( "ScanlineIntensity", Layer.ScanlineIntensity );
		attributes.Set( "ScanlinePeriod", MathF.Max( 2, MathF.Round( Layer.ScanlinePeriod * scale ) ) );
		attributes.Set( "ScanlineThickness", Layer.ScanlineThickness );
		attributes.Set( "ScanlineSoftness", Layer.ScanlineSoftness );
		attributes.SetCombo( "D_BLENDMODE", BlendMode.Normal );

		// The glow is added onto what's behind the HUD
		attributes.GrabFrameTexture( "FrameBufferCopyTexture", Graphics.DownsampleMethod.None );

		foreach ( var region in _regions )
			commands.DrawQuad( region, _material, Color.White );
	}

	public override void OnDeleted()
	{
		if ( _paintCommands is not null ) _camera?.RemoveCommandList( _paintCommands );
		_camera = null;
		_target?.Dispose();
		_glowA?.Dispose();
		_glowB?.Dispose();
		_target = _glowA = _glowB = null;

		base.OnDeleted();
	}
}
