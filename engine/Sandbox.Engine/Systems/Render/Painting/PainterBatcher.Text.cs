using Sandbox.UI;

namespace Sandbox;

internal partial class PainterBatcher
{
	/// <summary>
	/// Records GPU glyphs in the same spatial and clipping tables as Painter shapes.
	/// The box is a proxy into the upstream text instance buffer, evaluated from outlines in the shader.
	/// </summary>
	internal void AddText( List<GPUBoxInstance> glyphs, BlendMode blendMode, in TextGradientInfo gradient,
		Matrix transform, int clipIndex, float opacity = 1 )
	{
		var spatial = Destination.ResolveSpatial( this, transform );
		int gradientIndex = -1;
		if ( !gradient.ColorOffsets.IsDefaultOrEmpty )
		{
			var stops = new GradientStops();
			foreach ( var stop in gradient.ColorOffsets.Take( GradientInfo.MaxStops ) ) stops = stops.Add( stop );
			gradientIndex = GetOrAddGradient( new GradientInfo
			{
				Angle = gradient.Angle,
				OffsetX = gradient.OffsetX,
				OffsetY = gradient.OffsetY,
				SizeMode = gradient.SizeMode,
				GradientType = gradient.GradientType,
				ColorOffsets = stops
			} );
		}
		foreach ( var glyph in glyphs )
		{
			var text = glyph;
			if ( GpuFontText.WantsGradient( text ) && gradientIndex >= 0 ) text.TextureIndex = -gradientIndex - 1;
			var box = new UICssBoxBatched.BoxInstance
			{
				Rect = text.Rect,
				Color = text.Color.WithAlphaMultiplied( opacity ),
				TextureIndex = _textTable.Add( text ),
				Flags = 1u << 14,
				ShapeIndex = -1,
				InverseScissorIndex = -1,
				TextMaskIndex = -1
			};
			ResolveSpatial( ref box, clipIndex, spatial );
			Append( box, blendMode );
		}
	}
}
