using Sandbox.UI;
using System.Runtime.InteropServices;

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
		if ( glyphs.Count == 0 ) return;

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

		// Every row has to be final before it's hashed and before the batch can flush, which can upload the table
		int first = _textTable.Count;
		var text = _textTable.Append( glyphs.Count );
		CollectionsMarshal.AsSpan( glyphs ).CopyTo( text );
		if ( gradientIndex >= 0 )
		{
			foreach ( ref var glyph in text )
				if ( GpuFontText.WantsGradient( glyph ) ) glyph.TextureIndex = -gradientIndex - 1;
		}
		_textTable.Fold( text );

		// One label shares a clip and transform, so resolve them once
		var box = new UICssBoxBatched.BoxInstance
		{
			Flags = 1u << 14,
			ShapeIndex = -1,
			InverseScissorIndex = -1,
			TextMaskIndex = -1
		};
		ResolveSpatial( ref box, clipIndex, Destination.ResolveSpatial( this, transform ) );

		SetBlendMode( blendMode );
		var boxes = _boxTable.Append( text.Length );
		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );
		for ( int i = 0; i < text.Length; i++ )
		{
			var rect = text[i].Rect;
			box.Rect = rect;
			box.Color = text[i].Color.WithAlphaMultiplied( opacity );
			box.TextureIndex = first + i;
			boxes[i] = box;

			min = Vector2.Min( min, new Vector2( rect.x, rect.y ) );
			max = Vector2.Max( max, new Vector2( rect.x + rect.z, rect.y + rect.w ) );
		}
		_boxTable.Fold( boxes );

		// The glyphs share a transform, so their bounds count as one write
		TrackBackdropWrite( new Rect( min, max - min ), box.TransformIndex );
	}
}
