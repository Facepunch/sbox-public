using Sandbox.Rendering;

namespace Sandbox.UI;

public partial class Panel
{
	internal string PanelLayerRTName => field ??= $"PanelLayer.{GetHashCode()}";

	Vector2? _panelLayerSize;

	/// <summary>
	/// How many mips the layer's blurs need, 1 for none. ui/blur.hlsl reads the deepest level whose own blur stays
	/// within GAUSSIAN_BLUR_CHAIN_SHARE of the sigma, so the chain stops there.
	/// </summary>
	internal int LayerMipCount { get; private set; } = 1;

	int WantedLayerMipCount( Styles styles )
	{
		var sigma = styles.FilterBlur?.GetPixels( 1.0f ) ?? 0.0f;

		foreach ( var shadow in styles.FilterDropShadow )
			sigma = MathF.Max( sigma, shadow.Blur );

		if ( sigma <= 0.05f ) return 1;

		var mip = 0;
		while ( LayerChainSigma( mip + 1 ) <= 0.9f * sigma )
			mip++;

		return mip + 1;
	}

	/// <summary>
	/// The blur mip <paramref name="mip"/> of the GaussianBlurAlpha chain carries, in texels of mip 0 - the same sum
	/// as blur.hlsl's GaussianBlurChainSigma.
	/// </summary>
	static float LayerChainSigma( int mip )
	{
		var texels = MathF.Pow( 4.0f, mip );
		return MathF.Sqrt( 2.75f * (texels - 1.0f) + texels / 6.0f );
	}

	// Cached layer state computed during Build for use during Gather
	internal Matrix? CachedLayerMatrix;

	bool NeedsLayer( Styles styles )
	{
		if ( HasFilter ) return true;
		if ( styles.FilterDropShadow.Count > 0 ) return true;
		if ( styles.MaskImage != null ) return true;
		if ( styles.Isolation == UI.Isolation.Isolate ) return true;

		return false;
	}

	internal void UpdateLayer( Styles styles )
	{
		if ( NeedsLayer( styles ) )
		{
			var size = Box.RectOuter.Size;

			if ( size.x <= 1 ) return;
			if ( size.y <= 1 ) return;

			_panelLayerSize = size;

			// GenerateMipMaps stops once the short side runs out - a longer chain leaves mips nothing writes
			var shortSideMips = (int)MathF.Log2( MathF.Min( (int)size.x, (int)size.y ) ) + 1;
			LayerMipCount = Math.Min( WantedLayerMipCount( styles ), shortSideMips );
		}
		else
		{
			_panelLayerSize = null;
			LayerMipCount = 1;
		}
	}

	/// <summary>
	/// Called before rendering this panel
	/// </summary>
	internal void PushLayer( PanelRenderer render )
	{
		if ( _panelLayerSize is null ) return;
		if ( ComputedStyle is null ) return;
		if ( !IsVisible ) return;

		var mat = render.Matrix.Inverted;
		mat *= Matrix.CreateTranslation( Box.RectOuter.Position * -1.0f );

		// Store state for gather phase
		CachedLayerMatrix = mat;
		_lastLayerMatrix = GlobalMatrix;
		_lastLayerMatrixInverted = GlobalMatrixInverted;
		GlobalMatrix = null;
	}

	/// <summary>
	/// Called during Gather phase — push RT on the global command list.
	/// </summary>
	internal void PushLayerGather( PanelRenderer render, CommandList globalCL )
	{
		if ( _panelLayerSize is null ) return;
		if ( CachedLayerMatrix is null ) return;

		// Set identity transform for layer content
		globalCL.Attributes.Set( "TransformMat", Matrix.Identity );

		// 8-bit sRGB so the layer round-trips exactly through the UI shaders' sRGB read/write in both gamma and linear mode. HDR color clamps inside it.
		var handle = globalCL.GetRenderTarget( PanelLayerRTName, (int)_panelLayerSize.Value.x, (int)_panelLayerSize.Value.y, ImageFormat.RGBA8888, ImageFormat.None, numMips: LayerMipCount );

		render.PushLayer( this, globalCL, handle, CachedLayerMatrix.Value );
	}

	/// <summary>
	/// Build commands for post-children layer drawing (filters, masks, etc.)
	/// Called during Build phase after children are processed.
	/// LayerStack is maintained during Build so PopLayer can set correct state.
	/// </summary>
	internal void BuildLayerPopCommands( PanelRenderer render, RenderTarget defaultRenderTarget )
	{
		// Restore layer transform
		if ( _lastLayerMatrix.HasValue )
		{
			SetGlobalMatrix( _lastLayerMatrix, _lastLayerMatrixInverted );
			_lastLayerMatrix = null;
			_lastLayerMatrixInverted = null;
		}

		if ( _panelLayerSize is null ) return;
		if ( ComputedStyle is null ) return;
		if ( !IsVisible ) return;

		LayerCommandList.Reset();

		// Find parent layer's matrix by walking up the tree.
		// If no parent layer, use Identity.
		// RT is set during Draw via PopLayer on the global CL — don't set it here,
		// or it would override the parent layer's RT for nested layers.
		var parentLayerMat = Matrix.Identity;
		var isWorld = false;
		var ancestor = VisualParent;
		while ( ancestor != null )
		{
			if ( ancestor.HasPanelLayer && ancestor.CachedLayerMatrix.HasValue )
			{
				parentLayerMat = ancestor.CachedLayerMatrix.Value;
				break;
			}
			ancestor = ancestor.VisualParent;
		}
		isWorld = render.IsWorldPanel( this );

		LayerCommandList.Attributes.Set( "LayerMat", parentLayerMat );
		LayerCommandList.Attributes.SetCombo( "D_WORLDPANEL", isWorld ? 1 : 0 );

		// Apply transform/scissor from cached descriptor state
		LayerCommandList.Attributes.Set( "TransformMat", CachedDescriptors.TransformMat );
		PanelRenderer.SetScissorAttributes( LayerCommandList, CachedDescriptors.Scissor );

		BuildLayerPopCommandsInto( render, LayerCommandList );
	}

	private void BuildLayerPopCommandsInto( PanelRenderer render, CommandList commandList )
	{
		var attributes = commandList.Attributes;

		//
		// Shared attributes
		//
		attributes.Set( "Texture", new RenderTargetHandle { Name = PanelLayerRTName }.ColorTexture );
		attributes.Set( "BoxPosition", Box.RectOuter.Position );
		attributes.Set( "BoxSize", Box.RectOuter.Size );

		//
		// Pre-filter: draw shadows and border before everything else as separate layers
		//
		DrawPreFilterShadows( commandList );
		DrawPreFilterBorder( commandList );
		ResetPrefilterAttributes( commandList );

		float blurSize = ComputedStyle.FilterBlur.Value.GetPixels( 1.0f );

		attributes.Set( "FilterBlur", blurSize );
		attributes.Set( "FilterSaturate", ComputedStyle.FilterSaturate.Value.GetFraction( 1.0f ) );
		attributes.Set( "FilterSepia", ComputedStyle.FilterSepia.Value.GetFraction( 1.0f ) );
		attributes.Set( "FilterBrightness", ComputedStyle.FilterBrightness.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterContrast", ComputedStyle.FilterContrast.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterInvert", ComputedStyle.FilterInvert.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterHueRotate", ComputedStyle.FilterHueRotate.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterTint", ComputedStyle.FilterTint ?? Vector4.One );

		// filter: blur( r ) is a gaussian with sigma r, which reaches three sigma out. ui_filter.shader assumes this
		float growSize = MathF.Ceiling( blurSize * 3.0f );

		//
		// Handle masks
		//
		bool hasMask = ComputedStyle.MaskImage != null;
		attributes.SetCombo( "D_MASK_IMAGE", hasMask ? 1 : 0 );

		if ( hasMask )
		{
			var imageRectInput = new ImageRect.Input
			{
				ScaleToScreen = ScaleToScreen,
				Image = ComputedStyle?.MaskImage,
				PanelRect = Box.RectOuter,
				DefaultSize = Length.Auto,
				ImagePositionX = ComputedStyle.MaskPositionX,
				ImagePositionY = ComputedStyle.MaskPositionY,
				ImageSizeX = ComputedStyle.MaskSizeX,
				ImageSizeY = ComputedStyle.MaskSizeY,
			};

			var maskCalc = ImageRect.Calculate( imageRectInput );

			attributes.Set( "MaskPos", maskCalc.Rect );
			attributes.Set( "MaskTexture", ComputedStyle?.MaskImage );
			attributes.Set( "MaskMode", (int)(ComputedStyle?.MaskMode ?? MaskMode.MatchSource) );
			attributes.Set( "MaskAngle", ComputedStyle?.MaskAngle?.GetPixels( 1.0f ) ?? 0.0f );
			attributes.Set( "MaskScope", (int)(ComputedStyle?.MaskScope ?? MaskScope.Default) );

			var filter = (ComputedStyle?.ImageRendering ?? ImageRendering.Anisotropic) switch
			{
				ImageRendering.Point => FilterMode.Point,
				ImageRendering.Bilinear => FilterMode.Bilinear,
				ImageRendering.Trilinear => FilterMode.Trilinear,
				_ => FilterMode.Anisotropic
			};

			var sampler = (ComputedStyle?.MaskRepeat ?? BackgroundRepeat.Repeat) switch
			{
				BackgroundRepeat.RepeatX => new SamplerState { AddressModeV = TextureAddressMode.Clamp, Filter = filter },
				BackgroundRepeat.RepeatY => new SamplerState { AddressModeU = TextureAddressMode.Clamp, Filter = filter },
				BackgroundRepeat.NoRepeat => new SamplerState
				{
					AddressModeU = TextureAddressMode.Border,
					AddressModeV = TextureAddressMode.Border,
					Filter = filter
				},
				BackgroundRepeat.Clamp => new SamplerState
				{
					AddressModeU = TextureAddressMode.Clamp,
					AddressModeV = TextureAddressMode.Clamp,
					Filter = filter
				},
				_ => new SamplerState { Filter = filter }
			};

			attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( sampler ) );
			attributes.Set( "BorderSamplerIndex", SamplerState.GetBindlessIndex( new SamplerState
			{
				AddressModeU = TextureAddressMode.Border,
				AddressModeV = TextureAddressMode.Border,
				Filter = filter
			} ) );
		}

		attributes.SetCombo( "D_BLENDMODE", render.OverrideBlendMode );
		commandList.DrawQuad( Box.RectOuter.Grow( growSize ).Ceiling(), Material.UI.Filter, Color.White );
	}

	/// <summary>
	/// Draws shadows for the current layer into the specified command list.
	/// </summary>
	private void DrawPreFilterShadows( CommandList commandList )
	{
		foreach ( var shadow in ComputedStyle.FilterDropShadow )
		{
			var outerRect = Box.RectOuter;

			var shadowSize = new Vector2( shadow.OffsetX, shadow.OffsetY );

			// Grow outerRect to fit the shadow: its offset plus three sigma of gaussian. For drop-shadow the blur value is the sigma
			float growSize = MathF.Max( MathF.Abs( shadowSize.x ), MathF.Abs( shadowSize.y ) ) + MathF.Ceiling( shadow.Blur * 3.0f ) + 1.0f;
			outerRect = outerRect.Grow( growSize );

			ResetPrefilterAttributes( commandList );

			commandList.Attributes.Set( "FilterDropShadowScale", Box.RectOuter.Size / outerRect.Size );
			commandList.Attributes.Set( "FilterDropShadowOffset", shadowSize );
			commandList.Attributes.Set( "FilterDropShadowBlur", shadow.Blur );
			commandList.Attributes.Set( "FilterDropShadowColor", shadow.Color );

			commandList.DrawQuad( outerRect, Material.UI.DropShadow, Color.White );
		}
	}

	/// <summary>
	/// Draws borders for the current layer into the specified command list.
	/// </summary>
	private void DrawPreFilterBorder( CommandList commandList )
	{
		float filterBorderWidth = ComputedStyle.FilterBorderWidth.Value.GetPixels( 1.0f );
		filterBorderWidth *= ScaleToScreen;

		if ( filterBorderWidth > 0.0f )
		{
			var outerRect = Box.RectOuter;

			// Grow outerRect so that it can fit the border
			outerRect = outerRect.Grow( filterBorderWidth );

			ResetPrefilterAttributes( commandList );

			commandList.Attributes.Set( "FilterBorderWrapColorScale", Box.RectOuter.Size / outerRect.Size );
			commandList.Attributes.Set( "FilterBorderWrapColor", ComputedStyle.FilterBorderColor.Value );
			commandList.Attributes.Set( "FilterBorderWrapWidth", filterBorderWidth );

			commandList.DrawQuad( outerRect, Material.UI.BorderWrap, Color.White );
		}
	}

	private void ResetPrefilterAttributes( CommandList commandList )
	{
		commandList.Attributes.Set( "FilterDropShadowScale", 0 );
		commandList.Attributes.Set( "FilterDropShadowOffset", 0 );
		commandList.Attributes.Set( "FilterDropShadowBlur", 0 );
		commandList.Attributes.Set( "FilterDropShadowColor", 0 );

		commandList.Attributes.Set( "FilterBorderWrapColor", 0 );
		commandList.Attributes.Set( "FilterBorderWrapWidth", 0 );
	}

	/// <summary>
	/// Returns true if this panel has a layer that needs post-children rendering.
	/// </summary>
	internal bool HasPanelLayer => _panelLayerSize != null;
}
