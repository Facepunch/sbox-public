using Sandbox.Rendering;

namespace Sandbox;

internal partial class PainterBatcher
{
	[ConVar( "ui_painter_share_backdrops", Help = "Share CSS backdrop captures between separate panel surfaces" )]
	internal static bool ShareBackdrops { get; set; } = true;

	[ConVar( "ui_painter_defer_batches", Help = "Keep independent panel instances batched across shared CSS backdrops" )]
	internal static bool DeferBatches { get; set; } = true;

	[ConVar( "ui_painter_backdrop_sample_guard", Help = "Include blur sampling outside the panel when deciding whether to share a backdrop" )]
	internal static bool BackdropSampleGuard { get; set; }

	// Writes since the last shareable capture, including that backdrop's own quad.
	// An empty list means there is no capture to reuse.
	readonly List<Rect> _backdropWrites = [];

	// An explicit flush is a barrier: callers may append arbitrary native commands or change targets.
	void InvalidateBackdrop()
	{
		_backdropWrites.Clear();
	}

	// Explicit Painter backdrops always see all preceding drawing.
	internal void AddBackdrop( in Painter.BackdropData data, Matrix localTransform, int localClip, BlendMode blendMode )
	{
		Flush();
		var target = Destination;
		var transform = localTransform * target.Transform;
		var clipIndex = GetOrAddDrawClip( localClip, target.Transform, GetOrAddScissor( target.Scissor ) );
		var attributes = _commands.BeginDrawAttributes();
		attributes.Set( "LayerMat", target.LayerMatrix );
		attributes.SetCombo( "D_WORLDPANEL", target.WorldPanelCombo );
		attributes.Set( "UIInPanelLayer", target.Layered );
		// Native quads apply the scene object's transform in ui/vertex.hlsl.
		if ( target.WorldMatrix.HasValue ) attributes.Set( "WorldMat", ScenePanelObject.BuildPanelToObjectMatrix() );
		if ( target.GammaOutput.HasValue )
		{
			attributes.Set( "UIGammaOutput", target.GammaOutput.Value );
			attributes.Set( "UIFrameGrabEncoded", target.GammaOutput.Value );
		}
		BindScissor( attributes, clipIndex );
		attributes.Set( "TransformMat", transform );
		attributes.Set( "HasScissor", 0 );
		DrawBackdrop( data, blendMode, attributes, reuseGrab: false );
	}

	// CSS backdrops can reuse a capture while the intervening drawing is independent.
	internal void AddBackdrop( in Painter.BackdropData data, BlendMode blendMode )
	{
		var reuseGrab = PrepareBackdrop( data );
		ApplyDestinationAttributes( objectSpace: true );
		var attributes = _commands.BeginDrawAttributes();
		attributes.Set( "PainterScissorIndex", -1 );
		DrawBackdrop( data, blendMode, attributes, reuseGrab );
		ApplyDestinationAttributes();
	}

	void DrawBackdrop( in Painter.BackdropData data, BlendMode blendMode, CommandList.AttributeAccess attributes, bool reuseGrab )
	{
		var radii = data.Radii.Clamped( data.Rect.Width, data.Rect.Height );
		attributes.SetCombo( "D_BLENDMODE", (int)blendMode );
		attributes.Set( "BoxPosition", data.Rect.Position );
		attributes.Set( "BoxSize", data.Rect.Size );
		attributes.Set( "BoxBloat", 1f );
		attributes.Set( "BorderRadius", radii.Horizontal );
		attributes.Set( "BorderRadiusV", radii.Vertical );
		attributes.Set( "Brightness", data.Filter.Brightness );
		attributes.Set( "Contrast", data.Filter.Contrast );
		attributes.Set( "Saturate", data.Filter.Saturation );
		attributes.Set( "Sepia", data.Filter.Sepia );
		attributes.Set( "Invert", data.Filter.Invert );
		attributes.Set( "HueRotate", data.Filter.HueRotation );
		attributes.Set( "BlurScale", data.Filter.Blur );

		if ( reuseGrab )
		{
			var frame = new RenderTargetHandle { Name = "FrameBufferCopyTexture" };
			attributes.Set( "FrameBufferCopyTexture", frame.ColorTexture );
		}
		else
		{
			attributes.GrabFrameTexture( "FrameBufferCopyTexture", Graphics.DownsampleMethod.GaussianBlur );
		}
		_commands.DrawQuad( data.Rect.Grow( 1 ), Material.UI.BackdropFilter, data.Filter.Tint.WithAlphaMultiplied( data.Opacity ), attributes );
		CountDraw( grabs: reuseGrab ? 0 : 1 );
	}

	bool PrepareBackdrop( in Painter.BackdropData data )
	{
		var viewport = Destination.Viewport;
		if ( !ShareBackdrops || viewport.Position != Vector2.Zero || viewport.Width <= 0 || viewport.Height <= 0
			|| Destination.Layered || Destination.WorldPanelCombo != 0
			|| Destination.LayerMatrix != Matrix.Identity
			|| !TryScreenBounds( data.Rect.Grow( 1 ), Destination.Transform, out var bounds ) )
		{
			Flush();
			return false;
		}

		// By default, separate surfaces share their backdrop as in the previous panel
		// renderer. The optional strict policy also preserves blur bleed from neighboring
		// surfaces: LOD sqrt(blur / 2), both trilinear levels, and the Gaussian footprint.
		var samples = bounds;
		if ( BackdropSampleGuard )
		{
			var level = MathF.Ceiling( MathF.Sqrt( MathF.Max( 0, data.Filter.Blur ) * 0.5f ) );
			var apron = level == 0 ? 2 : 12 * MathF.Pow( 2, MathF.Min( level, 20 ) );
			samples = bounds.Grow( apron );
		}
		bool reuse = _backdropWrites.Count > 0;
		foreach ( var write in _backdropWrites )
		{
			if ( samples.Overlaps( write ) )
			{
				reuse = false;
				break;
			}
		}

		if ( !reuse )
		{
			Flush();
		}
		else if ( !DeferBatches )
		{
			FlushBatch();
		}

		// Include pending instances in the dependency check before moving them after a
		// shared backdrop. Direct overlap is a boundary under either sampling policy.
		_backdropWrites.Add( bounds );
		return reuse;
	}

	void TrackBackdropWrite( in UICssBoxBatched.BoxInstance instance )
	{
		if ( _backdropWrites.Count == 0 ) return;
		var rect = new Rect( instance.Rect.x, instance.Rect.y, instance.Rect.z, instance.Rect.w ).Grow( 1 );
		if ( !TryScreenBounds( rect, _transformTable[instance.TransformIndex].Mat, out var bounds ) )
		{
			InvalidateBackdrop();
			return;
		}
		_backdropWrites.Add( bounds );
	}

	static bool TryScreenBounds( Rect rect, Matrix transform, out Rect bounds )
	{
		bounds = default;
		if ( transform.M14 != 0 || transform.M24 != 0 || transform.M44 != 1 ) return false;
		var screen = transform.Transform( rect );
		if ( !float.IsFinite( screen.Left ) || !float.IsFinite( screen.Top ) || !float.IsFinite( screen.Right ) || !float.IsFinite( screen.Bottom ) ) return false;
		bounds = screen;
		return true;
	}
}
