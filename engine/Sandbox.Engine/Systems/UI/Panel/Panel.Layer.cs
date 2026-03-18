using Sandbox.Rendering;

namespace Sandbox.UI;

public partial class Panel
{
	string PanelLayerRTName => field ??= $"PanelLayer.{GetHashCode()}";

	Vector2? _panelLayerSize;

	bool NeedsLayer( Styles styles )
	{
		if ( HasFilter ) return true;
		if ( styles.FilterDropShadow.Count > 0 ) return true;
		if ( styles.MaskImage != null ) return true;

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
		}
		else
		{
			_panelLayerSize = null;
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

		// we need to push a matrix to offset the panel position,
		// so we're drawing in the correct place
		var mat = render.Matrix.Inverted;
		mat *= Matrix.CreateTranslation( Box.RectOuter.Position * -1.0f );

		// filter applies before transform, so we need to store it temporarily
		CommandList.Attributes.Set( "TransformMat", Matrix.Identity );
		_lastLayerMatrix = GlobalMatrix;
		GlobalMatrix = null;

		// get an RT handle for this panel layer
		var handle = CommandList.GetRenderTarget( PanelLayerRTName, (int)_panelLayerSize?.x, (int)_panelLayerSize?.y, depthFormat: ImageFormat.None );

		render.PushLayer( this, handle, mat );
	}

	/// <summary>
	/// Build commands for post-children layer drawing (filters, masks, etc.)
	/// These commands go into LayerCommandList and are executed after children.
	/// </summary>
	internal void BuildLayerPopCommands( PanelRenderer render, RenderTarget defaultRenderTarget )
	{
		// Restore layer transform
		if ( _lastLayerMatrix.HasValue )
		{
			GlobalMatrix = _lastLayerMatrix;
			_lastLayerMatrix = null;
		}

		if ( _panelLayerSize is null ) return;
		if ( ComputedStyle is null ) return;
		if ( !IsVisible ) return;

		LayerCommandList.Reset();

		// Restore render target (pop from layer stack)
		render.PopLayer( this, LayerCommandList, defaultRenderTarget );

		LayerCommandList.InsertList( TransformCommandList );
		LayerCommandList.InsertList( ClipCommandList );

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

		//
		// Draw this panel, with filters
		//
		// Todo, awesome, smooth, multipass blur
		float blurSize = ComputedStyle.FilterBlur.Value.GetPixels( 1.0f );
		attributes.Set( "FilterBlur", blurSize );

		attributes.Set( "FilterSaturate", ComputedStyle.FilterSaturate.Value.GetFraction( 1.0f ) );
		attributes.Set( "FilterSepia", ComputedStyle.FilterSepia.Value.GetFraction( 1.0f ) );
		attributes.Set( "FilterBrightness", ComputedStyle.FilterBrightness.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterContrast", ComputedStyle.FilterContrast.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterInvert", ComputedStyle.FilterInvert.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterHueRotate", ComputedStyle.FilterHueRotate.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterTint", ComputedStyle.FilterTint ?? Vector4.One );

		float growSize = blurSize;

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

			// Grow outerRect so that it can fit the shadow
			float growSize = MathF.Max( shadowSize.x, shadowSize.y );
			growSize = MathF.Max( 1.0f, growSize );
			growSize *= MathF.Max( 1.0f, shadow.Blur * 2.0f );
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
