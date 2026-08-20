using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.UI;

/// <summary>
/// Per-box data uploaded to a StructuredBuffer for the batched UI box shader.
/// Must match BoxInstanceData in ui_cssbox_batched.shader.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
struct GPUBoxInstance
{
	public Vector4 Rect;
	public Color Color;
	public Vector4 BorderRadius;   // horizontal, (top-left, top-right, bottom-left, bottom-right)
	public Vector4 BorderRadiusV;  // vertical, same order
	public Vector4 BorderSize;
	public Color BorderColorL;
	public Color BorderColorT;
	public Color BorderColorR;
	public Color BorderColorB;
	public int TextureIndex;
	public int SamplerIndex;
	public int BackgroundRepeat;
	public float BackgroundAngle;
	public Vector4 BackgroundRect;
	public Color BackgroundTint;
	public int BorderImageIndex;
	public int BorderImageSamplerIndex;
	public int BorderImageMode;
	public int BorderImageFill;
	public Vector4 BorderImageSlice;
	public Color BorderImageTint;
	public int Flags; // unused, kept for layout
	public int ScissorIndex;
	public int Mode;
	public int TransformIndex;
	public int InverseScissorIndex;
	public int TextMaskIndex;
	public int TextMaskSamplerIndex;
	public int BackgroundClip;
	public Vector4 BackgroundClipRect;

	// Mode 1/2 (shadow): BackgroundRect = the blurred shape as (x, y, w, h) relative to Rect, BackgroundAngle = blur,
	//                    BorderRadius/V = the shape's corners
	// Mode 3 (outline):  BackgroundRect = (panel w, panel h, width, offset), BackgroundAngle = how far Rect is grown past the panel
	//
	// BackgroundClipRect is the box clip's inset (left, top, right, bottom), or for a text clip the mask's
	// (x, y, w, h) relative to Rect - a box is never clipped to both.

	internal static GPUBoxInstance FromShadow( in ShadowDrawDescriptor desc )
	{
		// Outset: the border box, offset and grown by the spread, drawn outside the box.
		// Inset: the padding box, offset and shrunk by the spread, drawn inside the box.
		var spread = desc.Inset ? -desc.Spread : desc.Spread;
		var shape = (desc.PanelRect + desc.Offset).Grow( spread );
		var radii = desc.Radii.Grow( spread );

		// A gaussian with sigma = blur / 2 is gone by three sigma
		var quad = desc.Inset ? desc.PanelRect : shape.Grow( MathF.Ceiling( desc.Blur * 1.5f ) );

		return new GPUBoxInstance
		{
			Rect = new Vector4( quad.Left, quad.Top, quad.Width, quad.Height ),
			Color = desc.Color,
			BorderRadius = radii.Horizontal,
			BorderRadiusV = radii.Vertical,
			BackgroundAngle = desc.Blur,
			BackgroundRect = new Vector4( shape.Left - quad.Left, shape.Top - quad.Top, shape.Width, shape.Height ),
			Mode = desc.Inset ? 2 : 1,
			InverseScissorIndex = -1,
		};
	}

	internal static GPUBoxInstance FromOutline( in OutlineDrawDescriptor desc )
	{
		var outwardExtent = MathF.Max( desc.Offset + desc.Width, 0f );
		var bloat = outwardExtent + 1.0f;
		var bloatedRect = desc.PanelRect.Grow( bloat );

		var radii = desc.Radii.Clamped( desc.PanelRect.Width, desc.PanelRect.Height );

		return new GPUBoxInstance
		{
			Rect = new Vector4( bloatedRect.Left, bloatedRect.Top, bloatedRect.Width, bloatedRect.Height ),
			Color = desc.Color,
			BorderRadius = radii.Horizontal,
			BorderRadiusV = radii.Vertical,
			BackgroundRect = new Vector4( desc.PanelRect.Width, desc.PanelRect.Height, desc.Width, desc.Offset ),
			BackgroundAngle = bloat,
			Mode = 3,
			InverseScissorIndex = -1,
		};
	}

	internal static GPUBoxInstance From( in BoxDrawDescriptor desc )
	{
		var hasImage = desc.BackgroundImage != null && desc.BackgroundImage != Texture.Invalid;
		var hasBorderImage = desc.BorderImageTexture != null;

		var bgRect = hasImage || desc.HasGradient
			? (desc.BackgroundRect.z > 0 || desc.BackgroundRect.w > 0
				? desc.BackgroundRect
				: new Vector4( 0, 0, desc.PanelRect.Width, desc.PanelRect.Height ))
			: Vector4.Zero;

		var bgTint = hasImage || desc.HasGradient
			? desc.BackgroundTint
			: new Color( 0, 0, 0, 0 );

		// Style radii are already clamped, user radii aren't
		var radii = desc.Radii.Clamped( desc.PanelRect.Width, desc.PanelRect.Height );

		return new GPUBoxInstance
		{
			Rect = new Vector4( desc.PanelRect.Left, desc.PanelRect.Top, desc.PanelRect.Width, desc.PanelRect.Height ),
			Color = desc.Color,
			BorderRadius = radii.Horizontal,
			BorderRadiusV = radii.Vertical,
			BorderSize = desc.BorderSize,
			BorderColorL = desc.BorderColorL,
			BorderColorT = desc.BorderColorT,
			BorderColorR = desc.BorderColorR,
			BorderColorB = desc.BorderColorB,
			TextureIndex = hasImage ? desc.BackgroundImage.Index : 0,
			SamplerIndex = GetSamplerIndex( desc.BackgroundRepeat, desc.FilterMode ),
			BackgroundRepeat = (int)desc.BackgroundRepeat,
			BackgroundAngle = desc.BackgroundAngle,
			BackgroundRect = bgRect,
			BackgroundTint = bgTint,
			BorderImageIndex = hasBorderImage ? desc.BorderImageTexture.Index : 0,
			BorderImageSamplerIndex = GetClampSamplerIndex( desc.FilterMode ),
			BorderImageMode = hasBorderImage ? (desc.BorderImageRepeat == UI.BorderImageRepeat.Stretch ? 2 : 1) : 0,
			BorderImageFill = hasBorderImage && desc.BorderImageFill == UI.BorderImageFill.Filled ? 1 : 0,
			BorderImageSlice = desc.BorderImageSlice,
			BorderImageTint = hasBorderImage ? desc.BorderImageTint : default,
			Flags = 0,
			InverseScissorIndex = -1,
			BackgroundClip = (int)desc.BackgroundClip,
			BackgroundClipRect = desc.BackgroundClip == UI.BackgroundClip.Text ? desc.TextMaskRect : desc.BackgroundClipInset,
			TextMaskIndex = desc.HasTextMask ? desc.TextMask.Index : 0,
			TextMaskSamplerIndex = desc.HasTextMask ? GetClampSamplerIndex( FilterMode.Bilinear ) : 0,
		};
	}

	static int GetSamplerIndex( UI.BackgroundRepeat repeat, FilterMode filter )
	{
		var sampler = repeat switch
		{
			UI.BackgroundRepeat.RepeatX => new SamplerState { AddressModeV = TextureAddressMode.Clamp, Filter = filter },
			UI.BackgroundRepeat.RepeatY => new SamplerState { AddressModeU = TextureAddressMode.Clamp, Filter = filter },
			UI.BackgroundRepeat.NoRepeat => new SamplerState { AddressModeU = TextureAddressMode.Border, AddressModeV = TextureAddressMode.Border, Filter = filter },
			UI.BackgroundRepeat.Clamp => new SamplerState { AddressModeU = TextureAddressMode.Clamp, AddressModeV = TextureAddressMode.Clamp, Filter = filter },
			_ => new SamplerState { Filter = filter }
		};

		return SamplerState.GetBindlessIndex( sampler );
	}

	static int GetClampSamplerIndex( FilterMode filter )
	{
		return SamplerState.GetBindlessIndex( new SamplerState
		{
			AddressModeU = TextureAddressMode.Clamp,
			AddressModeV = TextureAddressMode.Clamp,
			Filter = filter
		} );
	}
}

[System.Runtime.CompilerServices.InlineArray( GradientInfo.MaxStops )]
internal struct GradientStopColors
{
	Color _element;
}

[System.Runtime.CompilerServices.InlineArray( GradientInfo.MaxStops )]
internal struct GradientStopOffsets
{
	float _element;
}

/// <summary>
/// Per-gradient data uploaded to a StructuredBuffer for shader-evaluated background
/// gradients. Must match GradientData in ui_cssbox_batched.shader. Colors are straight
/// alpha in sRGB space, exactly as authored; Angle is radians, 0 pointing down the panel
/// for linear and straight up for conic.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct GPUGradientInstance
{
	/// <summary>Bit per axis, set when that centre component is a fraction of the box rather than pixels.</summary>
	const int CenterXIsFraction = 1;
	const int CenterYIsFraction = 2;

	public GradientStopColors StopColors;
	public GradientStopOffsets StopOffsets;
	public int Count;
	public float Angle;
	public int Type;
	public int SizeMode;
	public Vector2 Center;
	public int CenterUnits;
	public int Circle;

	/// <summary>Bit per stop, set when that stop's offset is a pixel length rather than a fraction.</summary>
	public int StopUnits;

	/// <summary>Linear only - which corner the gradient runs to, 0 when it's an angle instead.</summary>
	public int Corner;

	internal static GPUGradientInstance From( in GradientInfo gradient )
	{
		var stops = gradient.ColorOffsets;
		var count = Math.Min( stops.Length, GradientInfo.MaxStops );

		var inst = new GPUGradientInstance
		{
			Count = count,
			Angle = gradient.Angle,
			Type = (int)gradient.GradientType,
			SizeMode = (int)gradient.SizeMode,
			Circle = gradient.Circle ? 1 : 0,
			Corner = (int)gradient.Corner,
		};

		// The box size only exists in the shader, so percentages travel as a fraction
		// with a flag and get resolved there.
		inst.Center = new Vector2( CenterValue( gradient.OffsetX ), CenterValue( gradient.OffsetY ) );

		if ( IsFraction( gradient.OffsetX ) ) inst.CenterUnits |= CenterXIsFraction;
		if ( IsFraction( gradient.OffsetY ) ) inst.CenterUnits |= CenterYIsFraction;

		for ( int i = 0; i < count; i++ )
		{
			inst.StopColors[i] = stops[i].color;
			inst.StopOffsets[i] = stops[i].offset ?? 0f;

			// A pixel offset only becomes a fraction once the gradient's length is known, which
			// is in the shader - so it travels as it was written, like the centre does.
			if ( stops[i].offsetIsPixels ) inst.StopUnits |= 1 << i;
		}

		return inst;
	}

	static bool IsFraction( Length length ) => length.Unit != LengthUnit.Pixels;

	static float CenterValue( Length length )
	{
		// GetPixels against a parent of 1 turns a percentage into its fraction and
		// leaves a pixel length alone.
		return length.GetPixels( 1f );
	}
}

/// <summary>
/// Per-scissor data uploaded to a StructuredBuffer for per-instance clipping.
/// Must match ScissorData in ui_cssbox_batched.shader.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct ScissorInstance
{
	public int Count;
	public int Invert;
	public int Pad0;
	public int Pad1;
	public GPUClipShapes Clips;

	internal static ScissorInstance From( in PanelRenderer.GPUScissor scissor )
	{
		var s = new ScissorInstance { Count = scissor.Count, Invert = scissor.Invert ? 1 : 0 };

		for ( int i = 0; i < scissor.Count; i++ )
		{
			ref readonly var c = ref scissor.Clips[i];
			s.Clips[i] = new GPUClipShape
			{
				Rect = c.Rect.ToVector4(),
				RadiiH = c.Radii.Horizontal,
				RadiiV = c.Radii.Vertical,
				TransformMat = c.Matrix,
			};
		}

		return s;
	}
}

/// <summary>
/// One rounded rect of a clip stack. Rect is left, top, right, bottom in the clipping panel's layout space,
/// TransformMat takes screen space there.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct GPUClipShape
{
	public Vector4 Rect;
	public Vector4 RadiiH;
	public Vector4 RadiiV;
	public Matrix TransformMat;
}

[System.Runtime.CompilerServices.InlineArray( PanelRenderer.GPUScissor.MaxClips )]
internal struct GPUClipShapes
{
	GPUClipShape _element;
}

/// <summary>
/// Per-transform data uploaded to a StructuredBuffer for per-instance transforms.
/// Must match TransformData in ui_cssbox_batched.shader.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct TransformInstance
{
	public Matrix Mat;
}
