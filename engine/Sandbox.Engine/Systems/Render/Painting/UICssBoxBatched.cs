using Sandbox.UI;
using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Managed buffer layouts and constants for ui_cssbox_batched.shader.
/// </summary>
internal static class UICssBoxBatched
{
	/// <summary>
	/// Per-box data uploaded to a StructuredBuffer for the batched UI box shader.
	/// Must match BoxInstanceData in ui_cssbox_batched.shader.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct BoxInstance
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
		public float BackgroundAngle;
		public Vector4 BackgroundRect;
		public Color BackgroundTint;
		public int BorderImageIndex;
		public int BorderImageSamplerIndex;
		public Vector4 BorderImageSlice;
		public Color BorderImageTint;
		public uint Flags;
		public int ScissorIndex;
		public int TransformIndex;
		public int InverseScissorIndex;
		public int TextMaskIndex;
		public int TextMaskSamplerIndex;
		public Vector4 BackgroundClipRect;

		/// <summary>
		/// Index into the border shape table, or -1 for a plain rounded rect.
		/// </summary>
		public int ShapeIndex;

		// Packed settings, matching BoxInstanceData's accessors in ui_cssbox_batched.shader:
		// Mode 0..1, BorderImageMode 2..3, BorderStyle 4..7, BorderImageFill 8,
		// BackgroundRepeat 9..11, BackgroundClip 12..13. Bits 14..31 are reserved.
		public int Mode
		{
			readonly get => (int)((Flags >> 0) & 0x3u);
			set => SetFlags( value, 0, 0x3u );
		}

		public int BorderImageMode
		{
			readonly get => (int)((Flags >> 2) & 0x3u);
			set => SetFlags( value, 2, 0x3u );
		}

		public int BorderStyle
		{
			readonly get => (int)((Flags >> 4) & 0xFu);
			set => SetFlags( value, 4, 0xFu );
		}

		public int BorderImageFill
		{
			readonly get => (int)((Flags >> 8) & 0x1u);
			set => SetFlags( value, 8, 0x1u );
		}

		public int BackgroundRepeat
		{
			readonly get => (int)((Flags >> 9) & 0x7u);
			set => SetFlags( value, 9, 0x7u );
		}

		public int BackgroundClip
		{
			readonly get => (int)((Flags >> 12) & 0x3u);
			set => SetFlags( value, 12, 0x3u );
		}

		void SetFlags( int value, int shift, uint mask )
		{
			Flags = (Flags & ~(mask << shift)) | (((uint)value & mask) << shift);
		}

		// Mode 1/2 (shadow): BackgroundRect = the blurred shape as (x, y, w, h) relative to Rect, BackgroundAngle = blur,
		//                    BorderRadius/V = the shape's corners
		// Mode 3 (outline):  BackgroundRect = (panel w, panel h, width, offset), BackgroundAngle = how far Rect is grown past the panel
		//
		// BackgroundClipRect is the box clip's inset (left, top, right, bottom), or for a text clip the mask's
		// (x, y, w, h) relative to Rect - a box is never clipped to both.

		internal static BoxInstance FromShadow( in Painter.ShadowDescriptor desc )
		{
			// Outset: the border box, offset and grown by the spread, drawn outside the box.
			// Inset: the padding box, offset and shrunk by the spread, drawn inside the box.
			var spread = desc.Inset ? -desc.Spread : desc.Spread;
			var shape = (desc.Rect + desc.Offset).Grow( spread );
			var radii = desc.Radii.Grow( spread );

			// A gaussian with sigma = blur / 2 is gone by three sigma
			var quad = desc.Inset ? desc.Rect : shape.Grow( MathF.Ceiling( desc.Blur * 1.5f ) );

			return new BoxInstance
			{
				Rect = new Vector4( quad.Left, quad.Top, quad.Width, quad.Height ),
				Color = desc.Color,
				BorderRadius = radii.Horizontal,
				BorderRadiusV = radii.Vertical,
				BackgroundAngle = desc.Blur,
				BackgroundRect = new Vector4( shape.Left - quad.Left, shape.Top - quad.Top, shape.Width, shape.Height ),
				Mode = desc.Inset ? 2 : 1,
				InverseScissorIndex = -1,
				ShapeIndex = -1,
			};
		}

		internal static BoxInstance FromOutline( in Painter.OutlineDescriptor desc )
		{
			var outwardExtent = MathF.Max( desc.Offset + desc.Width, 0f );
			var bloat = outwardExtent + 1.0f;
			var bloatedRect = desc.Rect.Grow( bloat );

			var radii = desc.Radii.Clamped( desc.Rect.Width, desc.Rect.Height );

			return new BoxInstance
			{
				Rect = new Vector4( bloatedRect.Left, bloatedRect.Top, bloatedRect.Width, bloatedRect.Height ),
				Color = desc.Color,
				BorderRadius = radii.Horizontal,
				BorderRadiusV = radii.Vertical,
				BackgroundRect = new Vector4( desc.Rect.Width, desc.Rect.Height, desc.Width, desc.Offset ),
				BackgroundAngle = bloat,
				Mode = 3,
				InverseScissorIndex = -1,
				ShapeIndex = -1,
			};
		}

		internal static void From( in Painter.BoxDescriptor desc, out BoxInstance gpu )
		{
			var hasImage = desc.BackgroundImage != null && desc.BackgroundImage != Texture.Invalid;
			var hasBorderImage = desc.HasBorderImage;
			ref readonly var border = ref desc.Stroke;
			ref readonly var image = ref desc.BorderImage;

			var bgRect = hasImage || desc.HasGradient
				? (desc.BackgroundRect.z > 0 || desc.BackgroundRect.w > 0
					? desc.BackgroundRect
					: new Vector4( 0, 0, desc.Rect.Width, desc.Rect.Height ))
				: Vector4.Zero;

			var bgTint = hasImage || desc.HasGradient
				? desc.BackgroundTint
				: new Color( 0, 0, 0, 0 );

			// Style radii are already clamped, user radii aren't
			var radii = desc.Radii.Clamped( desc.Rect.Width, desc.Rect.Height );

			gpu = default;
			gpu.Rect = new Vector4( desc.Rect.Left, desc.Rect.Top, desc.Rect.Width, desc.Rect.Height );
			gpu.Color = desc.Color;
			gpu.BorderRadius = radii.Horizontal;
			gpu.BorderRadiusV = radii.Vertical;
			gpu.BorderSize = border.Size;
			gpu.BorderColorL = border.ColorL;
			gpu.BorderColorT = border.ColorT;
			gpu.BorderColorR = border.ColorR;
			gpu.BorderColorB = border.ColorB;
			gpu.TextureIndex = hasImage ? desc.BackgroundImage.Index : 0;
			gpu.SamplerIndex = hasImage ? GetSamplerIndex( desc.BackgroundRepeat, desc.FilterMode ) : 0;
			gpu.BackgroundRepeat = (int)desc.BackgroundRepeat;
			gpu.BackgroundAngle = desc.BackgroundAngle;
			gpu.BackgroundRect = bgRect;
			gpu.BackgroundTint = bgTint;
			gpu.BorderImageIndex = hasBorderImage ? image.Texture.Index : 0;
			gpu.BorderImageSamplerIndex = hasBorderImage ? GetClampSamplerIndex( desc.FilterMode ) : 0;
			gpu.BorderImageMode = hasBorderImage ? (image.Repeat == UI.BorderImageRepeat.Stretch ? 2 : 1) : 0;
			gpu.BorderImageFill = hasBorderImage && image.Fill == UI.BorderImageFill.Filled ? 1 : 0;
			gpu.BorderImageSlice = image.Slices;
			gpu.BorderImageTint = hasBorderImage ? image.Tint : default;
			gpu.BorderStyle = (int)border.Style;
			gpu.InverseScissorIndex = -1;
			gpu.BackgroundClip = (int)desc.BackgroundClip;
			gpu.BackgroundClipRect = desc.BackgroundClip == UI.BackgroundClip.Text ? desc.TextMaskRect : desc.BackgroundClipInset;
			gpu.TextMaskIndex = desc.HasTextMask ? desc.TextMask.Index : 0;
			gpu.TextMaskSamplerIndex = desc.HasTextMask ? GetClampSamplerIndex( FilterMode.Bilinear ) : 0;
			// The caller resolves this against the batcher's table, like ScissorIndex and TransformIndex
			gpu.ShapeIndex = -1;
		}

		internal void ApplyOpacity( float opacity )
		{
			if ( opacity == 1 ) return;
			Color = Color.WithAlphaMultiplied( opacity );
			BackgroundTint = BackgroundTint.WithAlphaMultiplied( opacity );
			BorderColorL = BorderColorL.WithAlphaMultiplied( opacity );
			BorderColorT = BorderColorT.WithAlphaMultiplied( opacity );
			BorderColorR = BorderColorR.WithAlphaMultiplied( opacity );
			BorderColorB = BorderColorB.WithAlphaMultiplied( opacity );
			BorderImageTint = BorderImageTint.WithAlphaMultiplied( opacity );
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
	internal struct GradientInstance
	{
		/// <summary>
		/// Bit per axis, set when that centre component is a fraction of the box rather than pixels.
		/// </summary>
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

		/// <summary>
		/// Bit per stop, set when that stop's offset is a pixel length rather than a fraction.
		/// </summary>
		public int StopUnits;

		/// <summary>
		/// Linear only - which corner the gradient runs to, 0 when it's an angle instead.
		/// </summary>
		public int Corner;

		internal static GradientInstance From( in GradientInfo gradient )
		{
			var stops = gradient.ColorOffsets;
			var count = Math.Min( stops.Length, GradientInfo.MaxStops );

			var inst = new GradientInstance
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
	/// Shape kinds shared with the batched UI shader.
	/// </summary>
	internal static class ShapeKind
	{
		internal const int None = (int)BorderShapeKind.None;
		internal const int Polygon = (int)BorderShapeKind.Polygon;
		internal const int Circle = (int)BorderShapeKind.Circle;
		internal const int PolygonPath = 3;
		internal const int StrokePath = 4;
		internal const int Capsule = 5;
		internal const int Crescent = 6;
		internal const int Heart = 7;

		/// <summary>
		/// Two-point stroke with analytic caps and patterns, without path buffers.
		/// Circle stores the start, width and cap; Polygon01 stores the end, length and pattern kind.
		/// Polygon23 stores dash length, period, first run start and last run index (pattern: 0 solid, 1 dashed, 2 dotted).
		/// Polygon45.xy stores the bounds origin; endpoints use drawing coordinates, like general stroke paths.
		/// </summary>
		internal const int SimpleLine = 8;

		/// <summary>
		/// Round polygon stroke referencing its contour shape through PolygonCount (zero-based).
		/// Circle stores the stroke-to-fill origin offset, effective width and alignment mask sign.
		/// The referenced polygon owns the vertices and optional path hierarchy.
		/// </summary>
		internal const int PolygonStroke = 9;

		/// <summary>
		/// Kinds from here up are analytic shapes with no path nodes. Keep node-based kinds below it
		/// and mirror UI_SHAPE_FIRST_ANALYTIC in ui_cssbox_batched.shader.
		/// </summary>
		internal const int FirstAnalytic = Capsule;
	}

	/// <summary>
	/// One border shape, uploaded to a StructuredBuffer and pointed at by <see cref="BoxInstance.ShapeIndex"/>.
	/// Must match BorderShapeData in ui_cssbox_batched.shader. Vertices are relative to the box's
	/// top-left, which keeps a shape identical wherever its panel sits so the table can dedupe it;
	/// unused slots are zero and the shader only reads the first <see cref="PolygonCount"/> of them.
	/// Stroke paths instead use PolygonCount for a one-based alignment-mask shape index, and Circle.w
	/// for its sign (inside +1, outside -1). Circle masks use Circle.w as an optional inner ring radius.
	/// Simple lines use drawing-space endpoints and pattern parameters as described by <see cref="ShapeKind.SimpleLine"/>.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct BorderShape : IEquatable<BorderShape>
	{
		public Vector4 Polygon01, Polygon23, Polygon45, Polygon67;
		public int PolygonCount;
		public Vector4 Circle;
		public int PathOffset;
		public int PathCount;
		public int PathNodeOffset;

		/// <summary>
		/// Hierarchy node count. For polygon paths, -1 instead addresses raw float2 points
		/// through PathOffset and PathCount, without changing the shape record layout.
		/// </summary>
		public int PathNodeCount;

		public int Kind;

		/// <summary>
		/// Creates a round outline referencing a polygon's existing contour. Effective width
		/// includes the doubled width used by inside/outside strokes before alignment clipping.
		/// These aliases use existing packed fields without changing the GPU buffer layout.
		/// </summary>
		internal static BorderShape CreatePolygonStroke( int polygonIndex, Vector2 originOffset, float effectiveWidth, Stroke.StrokeAlignment alignment ) => new()
		{
			Kind = ShapeKind.PolygonStroke,
			PolygonCount = polygonIndex,
			Circle = new Vector4( originOffset.x, originOffset.y, effectiveWidth,
				alignment == Stroke.StrokeAlignment.Center ? 0 : alignment == Stroke.StrokeAlignment.Inside ? 1 : -1 )
		};

		/// <summary>
		/// Zero-based contour shape index for a polygon outline.
		/// </summary>
		internal readonly int PolygonStrokeShapeIndex => PolygonCount;

		/// <summary>
		/// Offset from the outline's local origin to the fill's coordinate system.
		/// </summary>
		internal readonly Vector2 PolygonStrokeOriginOffset => new( Circle.x, Circle.y );

		/// <summary>
		/// Effective outline width, before inside/outside alignment clipping.
		/// </summary>
		internal readonly float PolygonStrokeWidth => Circle.z;

		/// <summary>
		/// Alignment mask sign: zero for centered, positive for inside, negative for outside.
		/// </summary>
		internal readonly float PolygonStrokeAlignmentSign => Circle.w;


		/// <summary>
		/// Compares shape data, including path ranges.
		/// </summary>
		public readonly bool Equals( BorderShape other ) => Polygon01 == other.Polygon01 && Polygon23 == other.Polygon23
			&& Polygon45 == other.Polygon45 && Polygon67 == other.Polygon67 && PolygonCount == other.PolygonCount
			&& Circle == other.Circle && PathOffset == other.PathOffset && PathCount == other.PathCount
			&& PathNodeOffset == other.PathNodeOffset && PathNodeCount == other.PathNodeCount && Kind == other.Kind;

		/// <summary>
		/// Compares shape data.
		/// </summary>
		public override readonly bool Equals( object obj ) => obj is BorderShape other && Equals( other );

		/// <summary>
		/// Hashes shape data.
		/// </summary>
		public override readonly int GetHashCode() => HashCode.Combine( HashCode.Combine( Polygon01, Polygon23, Polygon45, Polygon67, PolygonCount, Circle, Kind ), PathOffset, PathCount, PathNodeOffset, PathNodeCount );
	}

	/// <summary>
	/// A stackless path hierarchy node. Next and Primitive are relative to the owning path.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct PathNode
	{
		public Vector4 Bounds;
		public int Next;
		public int Primitive;
	}

	/// <summary>
	/// Path primitive kinds shared with the batched UI shader.
	/// </summary>
	internal static class PathPrimitiveKind
	{
		internal const int Segment = 0;
		internal const int Disc = 1;
		internal const int Join = 2;
		internal const int Arc = 3;
		internal const int RoundJoin = 4;
	}

	/// <summary>
	/// Cap flags shared with the batched UI shader. Cap styles use <see cref="Stroke.LineCap"/> values.
	/// </summary>
	internal static class PathCap
	{
		internal const int Ring = -1;
		internal const int SquareStart = 1;
		internal const int SquareEnd = 2;
	}

	/// <summary>
	/// One path primitive. Matches PathPrimitiveData in ui_cssbox_batched.shader.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct PathPrimitive
	{
		// Segment: A = endpoints, Count = square-cap bits, B.xy = start/end pointed Stroke.LineCap styles (otherwise Butt).
		// Disc: A.xy = center.
		// Bevel/miter join: A.xy = center; A.zw/B/C pack vertices. Outer vertices scale by half-width,
		// the last two are layout offsets. Round join: A.xy = center, B.xy = lengths, C = adjacent directions.
		// Arc: A = center/radius/start radians, B.x = signed sweep, Count = cap (-1 for a ring).
		public Vector4 A;
		public Vector4 B;
		public Vector4 C;
		public int Kind;
		public int Count;
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
		public int Next;
		public int Pad1;
		public ClipShapes Clips;

		internal static ScissorInstance From( in Painter.Scissoring scissor, int next = -1 )
		{
			var s = new ScissorInstance { Count = scissor.Count, Invert = scissor.Invert ? 1 : 0, Next = next };

			for ( int i = 0; i < scissor.Count; i++ )
			{
				var c = scissor.Clips[i].ForShader();
				s.Clips[i] = new ClipShape
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
	internal struct ClipShape
	{
		public Vector4 Rect;
		public Vector4 RadiiH;
		public Vector4 RadiiV;
		public Matrix TransformMat;
	}

	[System.Runtime.CompilerServices.InlineArray( Painter.Scissoring.MaxClips )]
	internal struct ClipShapes
	{
		ClipShape _element;
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
}
