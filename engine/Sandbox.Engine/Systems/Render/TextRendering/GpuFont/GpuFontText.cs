using Sandbox.UI;
using SkiaSharp;
using System.Runtime.InteropServices;
using Topten.RichTextKit;

namespace Sandbox;

/// <summary>
/// Turns a laid-out RichTextKit block into UI box instances that draw text straight from the glyph outlines
/// (modes 4 and 5 of ui_cssbox_batched.shader, evaluated by ui/text.hlsl). The walk follows the order Skia
/// painted in: run backgrounds and effects first, then selection, fills and decoration lines. The UI batches
/// these like any other box, so text draws live every frame; <see cref="Render"/> composites the same
/// instances into a texture for the consumers that need one.
/// </summary>
internal static class GpuFontText
{
	const int ModeGlyph = 4;
	const int ModeLine = 5;
	internal const int FlagAliased = 1; // TEXT_ALIASED in ui/text.hlsl
	const int FlagGradient = 2;
	const int TileSize = 16; // TEXT_TILE_SIZE in ui/text.hlsl

	internal struct Options
	{
		public bool Aliased;
		public int SelectionStart;
		public int SelectionEnd;
		public Color SelectionColor;
		public bool HasGradient;
		public float Opacity;

		public static Options Default => new() { SelectionStart = -1, SelectionEnd = -1, Opacity = 1 };

		/// <summary>The options a scene text scope draws with.</summary>
		public static Options For( in TextRendering.Scope scope )
		{
			var options = Default;
			options.Aliased = scope.FontSmooth == FontSmooth.Never;
			return options;
		}
	}

	/// <summary>A text instance wants the block's gradient resolved into its TextureIndex.</summary>
	internal static bool WantsGradient( in GPUBoxInstance inst ) => inst.Mode >= ModeGlyph && (inst.BorderImageMode & FlagGradient) != 0;

	/// <summary>
	/// Emit the block's instances. <paramref name="origin"/> is where the block's (0,0) lands in layout space.
	/// </summary>
	public static void Build( Topten.RichTextKit.TextBlock block, Vector2 origin, in Options options, List<GPUBoxInstance> instances )
	{
		var b = new Builder
		{
			Instances = instances,
			Options = options,
			Origin = origin,
			GradientRect = new Vector4( origin.x + block.MeasuredPadding.Left, origin.y, block.MeasuredWidth, block.MeasuredHeight ),
			AliasedFlag = options.Aliased ? FlagAliased : 0,
			GradientFlag = options.HasGradient ? FlagGradient : 0,
		};

		b.Build( block );
	}

	ref struct Builder
	{
		public List<GPUBoxInstance> Instances;
		public Options Options;
		public Vector2 Origin;
		public Vector4 GradientRect;

		// Instance flags, worked out once: effect passes take AliasedFlag alone, the fill pass both
		public int AliasedFlag;
		public int GradientFlag;

		public void Build( Topten.RichTextKit.TextBlock block )
		{
			foreach ( var line in block.Lines )
			{
				foreach ( var run in line.Runs )
				{
					if ( run.RunKind == FontRunKind.TrailingWhitespace ) continue;

					var style = run.Style;

					if ( style.BackgroundColor.Alpha > 0 && run.RunKind == FontRunKind.Normal )
						AddRect( Origin + new Vector2( run.XCoord, line.YCoord ), Origin + new Vector2( run.XCoord + run.Width, line.YCoord + line.Height ), ToColor( style.BackgroundColor ) );

					if ( run.RunKind == FontRunKind.Tabs || run.Glyphs.Length == 0 || style.TextEffects == null ) continue;

					foreach ( var effect in style.TextEffects )
					{
						var color = ToColor( effect.Color );
						float dilate = effect.Width * 0.5f;

						// Each effect is one copy at its offset, including a sharp shadow with zero blur.
						AddEffectPass( run, line, Origin + new Vector2( effect.Offset.X, effect.Offset.Y ), dilate, effect.BlurSize, color );
					}
				}
			}

			bool selection = Options.SelectionStart >= 0 && Options.SelectionEnd >= 0 && Options.SelectionStart != Options.SelectionEnd;
			int selStart = Math.Min( Options.SelectionStart, Options.SelectionEnd );
			int selEnd = Math.Max( Options.SelectionStart, Options.SelectionEnd );

			foreach ( var line in block.Lines )
			{
				foreach ( var run in line.Runs )
				{
					if ( selection && run.RunKind != FontRunKind.Ellipsis )
						AddSelection( run, line, selStart, selEnd );

					if ( run.RunKind == FontRunKind.Tabs || run.RunKind == FontRunKind.TrailingWhitespace ) continue;

					AddGlyphs( run, Origin, 0, 0, ToColor( run.Style.TextColor ), AliasedFlag | GradientFlag, layerColors: true );
					AddDecorations( run, line, Origin, 0, ToColor( run.Style.UnderlineColor ?? run.Style.TextColor ), GradientFlag );
				}
			}
		}

		/// <summary>One pass of a text effect - its glyphs and the decoration lines that go with them.</summary>
		void AddEffectPass( FontRun run, TextLine line, Vector2 origin, float dilate, float sigma, Color color )
		{
			AddGlyphs( run, origin, dilate, sigma, color, AliasedFlag );
			AddDecorations( run, line, origin, sigma, color, 0 );
		}

		GPUBoxInstance Instance( Vector2 min, Vector2 max, Color color, int mode, int flags )
		{
			color.a *= Options.Opacity;

			return new GPUBoxInstance
			{
				Rect = new Vector4( min.x, min.y, max.x - min.x, max.y - min.y ),
				Color = color,
				Mode = mode,
				BorderImageMode = flags,
				BorderImageSlice = GradientRect,
				InverseScissorIndex = -1,
				ShapeIndex = -1,
			};
		}

		/// <summary>
		/// One instance per glyph, or per colour layer of an emoji. Layers keep their palette colours for the
		/// fill and take the effect colour for shadows and outlines, like Skia drew them.
		/// </summary>
		void AddGlyphs( FontRun run, Vector2 origin, float dilate, float sigma, Color color, int flags, bool layerColors = false )
		{
			float fontSize = run.Style.FontSize;
			var glyphs = run.Glyphs.AsSpan();
			var positions = run.GlyphPositions.AsSpan();

			for ( int i = 0; i < glyphs.Length; i++ )
			{
				var pos = origin + new Vector2( positions[i].X, positions[i].Y );
				var layers = GpuFontGlyphCache.GetLayers( run.Typeface, glyphs[i] );

				if ( layers == null )
				{
					AddGlyph( GpuFontGlyphCache.Get( run.Typeface, glyphs[i] ), pos, fontSize, dilate, sigma, color, flags );
					continue;
				}

				foreach ( var (layer, layerColor) in layers )
				{
					var glyphColor = color;
					if ( layerColors && layerColor.HasValue )
					{
						glyphColor = layerColor.Value;
						glyphColor.a *= color.a;
					}

					AddGlyph( GpuFontGlyphCache.Get( run.Typeface, layer ), pos, fontSize, dilate, sigma, glyphColor, flags & ~FlagGradient );
				}
			}
		}

		void AddGlyph( in GpuFontGlyphCache.Glyph glyph, Vector2 pos, float fontSize, float dilate, float sigma, Color color, int flags )
		{
			if ( glyph.IsEmpty ) return;

			// The vertex shader adds the edge AA bloat; matches GlyphRect in ui_cssbox_batched.shader
			float grow = dilate + sigma * 3;
			var min = pos + new Vector2( glyph.Bounds.x, glyph.Bounds.y ) * fontSize - grow;
			var max = pos + new Vector2( glyph.Bounds.z, glyph.Bounds.w ) * fontSize + grow;

			var inst = Instance( min, max, color, ModeGlyph, flags );
			inst.BackgroundRect = new Vector4( pos.x, pos.y, fontSize, dilate );
			inst.BorderRadiusV = new Vector4( sigma, 0, 0, 0 );
			inst.Flags = glyph.Index;
			Instances.Add( inst );
		}

		/// <summary>
		/// Underline, overline and strike-through for a run, following FontRun.PaintUnderline / PaintStrikeThrough.
		/// </summary>
		void AddDecorations( FontRun run, TextLine line, Vector2 origin, float sigma, Color color, int flags )
		{
			var style = run.Style;
			if ( run.RunKind != FontRunKind.Normal ) return;
			if ( style.Underline == UnderlineStyle.None && style.StrikeThrough == StrikeThroughStyle.None ) return;

			using var font = new SKFont( run.Typeface, style.FontSize );
			var metrics = font.Metrics;
			float x0 = run.XCoord, x1 = run.XCoord + run.Width;

			var underlineWidth = style.StrokeThickness ?? metrics.UnderlineThickness ?? 0;
			if ( underlineWidth > 0 && style.Underline != UnderlineStyle.None )
			{
				float t = MathF.Max( underlineWidth, 1 );
				float underlineY = line.YCoord + line.BaseLine + (metrics.UnderlinePosition ?? 0);
				bool hasUnderline = false;

				if ( (style.Underline & UnderlineStyle.Gapped) != 0 )
				{
					AddGappedLine( run, x0, x1, underlineY + style.UnderlineOffset, t, style, origin, sigma, color, flags, false );
					hasUnderline = true;
				}

				if ( (style.Underline & UnderlineStyle.Overline) != 0 )
				{
					AddGappedLine( run, x0, x1, line.YCoord + style.OverlineOffset, t, style, origin, sigma, color, flags, true );
					hasUnderline = true;
				}

				if ( !hasUnderline || (style.Underline & UnderlineStyle.Solid) != 0 )
					AddLine( x0, x1, underlineY + style.UnderlineOffset, t, style.UnderlineStrokeType, false, origin, sigma, color, flags );
			}

			var strikeWidth = style.StrokeThickness ?? metrics.StrikeoutThickness ?? 0;
			if ( strikeWidth > 0 && style.StrikeThrough != StrikeThroughStyle.None )
			{
				float t = MathF.Max( strikeWidth, 1 );
				float y = line.YCoord + line.BaseLine + (metrics.StrikeoutPosition ?? 0) + style.StrikeThroughOffset;
				AddLine( x0, x1, y, t, style.UnderlineStrokeType, false, origin, sigma, color, flags );
			}
		}

		/// <summary>A line broken around the glyph ink, when the style skips ink.</summary>
		void AddGappedLine( FontRun run, float x0, float x1, float y, float t, IStyle style, Vector2 origin, float sigma, Color color, int flags, bool overline )
		{
			float x = x0;

			if ( style.StrokeInkSkip )
			{
				float fontSize = style.FontSize;
				var glyphs = run.Glyphs.AsSpan();
				var positions = run.GlyphPositions.AsSpan();
				var spans = new List<Vector2>();

				for ( int i = 0; i < glyphs.Length; i++ )
				{
					var glyph = GpuFontGlyphCache.Get( run.Typeface, glyphs[i] );
					if ( glyph.IsEmpty ) continue;

					float baseline = positions[i].Y;
					int start = spans.Count;
					GpuFontGlyphCache.Intercepts( glyph, (y - t / 2 - baseline) / fontSize, (y + t - baseline) / fontSize, spans );
					for ( int s = start; s < spans.Count; s++ )
						spans[s] = spans[s] * fontSize + positions[i].X;
				}

				spans.Sort( ( a, b ) => a.x.CompareTo( b.x ) );

				foreach ( var span in spans )
				{
					float b = span.x - t;
					if ( x < b )
						AddLine( x, b, y, t, style.UnderlineStrokeType, overline, origin, sigma, color, flags );

					x = MathF.Max( x, span.y + t );
				}
			}

			if ( x < x1 )
				AddLine( x, x1, y, t, style.UnderlineStrokeType, overline, origin, sigma, color, flags );
		}

		void AddLine( float x0, float x1, float y, float t, UnderlineType type, bool overline, Vector2 origin, float sigma, Color color, int flags )
		{
			x0 += origin.x;
			x1 += origin.x;
			y += origin.y;

			// Skia drew these without antialiasing, so the solid ones sit on whole pixel rows
			float y0 = MathF.Round( y - t / 2 );
			float y1 = MathF.Max( MathF.Round( y + t / 2 ), y0 + 1 );

			float doubleOffset = type == UnderlineType.Double ? (overline ? -2 * t : 2 * t) : 0;
			float grow = t + sigma * 3 + 1;
			var min = new Vector2( x0 - grow, MathF.Min( y0, y0 + doubleOffset ) - grow );
			var max = new Vector2( x1 + grow, MathF.Max( y1, y1 + doubleOffset ) + grow );

			var inst = Instance( min, max, color, ModeLine, flags );
			inst.BackgroundRect = new Vector4( x0, x1, y0, y1 );
			inst.BorderRadius = new Vector4( y, t, doubleOffset, (int)type );
			inst.BorderRadiusV = new Vector4( sigma, 0, 0, 0 );
			Instances.Add( inst );
		}

		/// <summary>A pixel-snapped filled rectangle - Skia painted these without antialiasing too.</summary>
		void AddRect( Vector2 min, Vector2 max, Color color )
		{
			min = new Vector2( MathF.Round( min.x ), MathF.Round( min.y ) );
			max = new Vector2( MathF.Round( max.x ), MathF.Round( max.y ) );
			if ( max.x <= min.x || max.y <= min.y ) return;

			Instances.Add( Instance( min, max, color, 0, 0 ) );
		}

		void AddSelection( FontRun run, TextLine line, int selStart, int selEnd )
		{
			bool ltr = run.Direction == TextDirection.LTR;

			float startX;
			if ( selStart < run.Start ) startX = ltr ? 0 : run.Width;
			else if ( selStart >= run.End ) startX = ltr ? run.Width : 0;
			else startX = run.RelativeCodePointXCoords[selStart - run.Start];

			float endX;
			if ( selEnd < run.Start ) endX = ltr ? 0 : run.Width;
			else if ( selEnd >= run.End ) endX = ltr ? run.Width : 0;
			else endX = run.RelativeCodePointXCoords[selEnd - run.Start];

			if ( startX == endX ) return;

			var a = Origin + new Vector2( run.XCoord + startX, line.YCoord );
			var b = Origin + new Vector2( run.XCoord + endX, line.YCoord + line.Height );
			AddRect( Vector2.Min( a, b ), Vector2.Max( a, b ), Options.SelectionColor );
		}

		static Color ToColor( SKColorF c ) => new( c.Red, c.Green, c.Blue, c.Alpha );
	}

	//
	// Rendering into a texture, for particles, HUD text, command lists and background-clip masks
	//

	// Per-frame append buffers, like UIBatcher: a dispatch recorded inside a render block reads them later
	class FrameSlot
	{
		public ulong Frame;
		public List<GPUBoxInstance> Instances = new();
		public List<uint> Tiles = new();
		public GpuBuffer<GPUBoxInstance> InstanceBuffer;
		public GpuBuffer<uint> TileBuffer;
		public int InstancesUploaded;
		public int TilesUploaded;
	}

	static readonly object _lock = new();
	static readonly FrameSlot[] _slots = { new(), new(), new() };
	static int _slot;
	static ComputeShader _shader;
	static readonly List<GPUBoxInstance> _instances = new();

	/// <summary>Creating a ComputeShader asserts the main thread, so text drawn from the render thread needs it made here first.</summary>
	internal static void PreloadShader()
	{
		if ( _shader is not null ) return;
		lock ( _lock ) _shader ??= new ComputeShader( "text_gpufont_cs" );
	}

	/// <summary>Where a block's instances landed in a frame's buffers and the pixel area they cover, for the shader compositing it.</summary>
	internal readonly record struct Placement( ulong Frame, int InstanceOffset, int TileOffset, int TilesX, int Width, int Height );

	/// <summary>
	/// Put a block's instances in this frame's buffers, binned into 16px tiles over a width by height pixel area, for
	/// a shader that composites the whole block per pixel with ui/text.hlsl. A block that doesn't change keeps its
	/// list and only pays for the append.
	/// </summary>
	public static Placement Upload( List<GPUBoxInstance> instances, int width, int height )
	{
		lock ( _lock )
		{
			var slot = AcquireSlot();
			var placement = new Placement( slot.Frame, slot.Instances.Count, slot.Tiles.Count, (width + TileSize - 1) / TileSize, width, height );
			BinTiles( CollectionsMarshal.AsSpan( instances ), slot.Tiles, placement.TilesX, (height + TileSize - 1) / TileSize );
			slot.Instances.AddRange( instances );
			GpuFontGlyphCache.Append( ref slot.InstanceBuffer, slot.Instances, ref slot.InstancesUploaded, 256 );
			GpuFontGlyphCache.Append( ref slot.TileBuffer, slot.Tiles, ref slot.TilesUploaded, 256 );
			return placement;
		}
	}

	/// <summary>
	/// Bind the block buffers a frame's placements point into, and the glyph outlines, for a shader that composites
	/// them. The render thread can be a frame or two behind, so the caller says which frame it recorded.
	/// </summary>
	public static void Bind( RenderAttributes attributes, ulong frame )
	{
		foreach ( var slot in _slots )
		{
			if ( slot.Frame != frame || slot.InstanceBuffer is null ) continue;

			GpuFontGlyphCache.Bind( attributes );
			attributes.Set( "TextInstances", slot.InstanceBuffer );
			attributes.Set( "TextTiles", slot.TileBuffer );
			return;
		}
	}

	/// <summary>Bind the buffers and one block's placement, for a shader compositing that block over a quad or texture.</summary>
	public static void Bind( RenderAttributes attributes, in Placement placement )
	{
		Bind( attributes, placement.Frame );
		attributes.Set( "TextInstanceOffset", placement.InstanceOffset );
		attributes.Set( "TextTileOffset", placement.TileOffset );
		attributes.Set( "TextTilesX", placement.TilesX );
		attributes.Set( "TextWidth", placement.Width );
		attributes.Set( "TextHeight", placement.Height );
	}

	/// <summary>
	/// Render the block into a texture, for the consumers that need one. <paramref name="reuse"/> is rendered
	/// into again when it matches, saving the allocation. Compute dispatches run immediately outside a render
	/// block, so this works from layout as well as from a command list. Text gradients are UI only and not drawn here.
	/// </summary>
	public static Texture Render( Topten.RichTextKit.TextBlock block, Vector2 origin, int width, int height, bool hdr, int mips, in Options options, Texture reuse = null )
	{
		using var perfScope = Performance.Scope( "GpuFontText.Render" );

		lock ( _lock )
		{
			// PreloadShader has run by now on any live frame; a render thread caller that beats it waits one frame
			if ( _shader is null && !ThreadSafe.IsMainThread ) return reuse;
			PreloadShader();

			var format = hdr ? ImageFormat.RGBA16161616F : ImageFormat.RGBA8888;
			mips = Math.Clamp( mips, 1, (int)Math.Log2( Math.Min( width, height ) ) + 1 );

			var texture = reuse;
			if ( texture is null || texture.Width != width || texture.Height != height || texture.ImageFormat != format || texture.Mips != mips )
			{
				reuse?.Dispose();
				texture = Texture.Create( width, height, format )
					.WithName( "gpufonttext" )
					.WithMips( mips )
					.WithUAVBinding()
					.WithGPUOnlyUsage()
					.Finish();
			}

			_instances.Clear();
			Build( block, origin, options, _instances );

			var attributes = _shader.Attributes;
			Bind( attributes, Upload( _instances, width, height ) );
			attributes.Set( "BaseColor", BaseColor( block ) );

			// Every mip is rendered from the outlines, no downsampling
			for ( int mip = 0; mip < mips; mip++ )
			{
				attributes.Set( "MipLevel", mip );
				attributes.Set( "TextOutput", texture, mip );
				_shader.Dispatch( Math.Max( width >> mip, 1 ), Math.Max( height >> mip, 1 ), 1 );
			}

			return texture;
		}
	}

	/// <summary>
	/// The colour fully transparent texels hold. Skia left them black, which bilinear filtering dragged into
	/// glyph edges, so they got repaired to the text colour afterwards - here they start out that way.
	/// </summary>
	static Vector4 BaseColor( Topten.RichTextKit.TextBlock block )
	{
		foreach ( var line in block.Lines )
		{
			foreach ( var run in line.Runs )
			{
				return new Vector4( run.Style.TextColor.Red, run.Style.TextColor.Green, run.Style.TextColor.Blue, 1 );
			}
		}

		return Vector4.Zero;
	}

	// Each 16px tile of the texture lists the instances touching it, so a pixel only visits those
	static void BinTiles( ReadOnlySpan<GPUBoxInstance> instances, List<uint> tiles, int tilesX, int tilesY )
	{
		int tileCount = tilesX * tilesY;
		int start = tiles.Count;

		Span<int> counts = tileCount <= 4096 ? stackalloc int[tileCount] : new int[tileCount];
		counts.Clear();

		for ( int i = 0; i < instances.Length; i++ )
		{
			GetTileRange( instances[i].Rect, tilesX, tilesY, out var x0, out var y0, out var x1, out var y1 );
			for ( int y = y0; y <= y1; y++ )
				for ( int x = x0; x <= x1; x++ )
					counts[y * tilesX + x]++;
		}

		// Offsets are absolute into the buffer; entry tileCount closes the last tile
		int running = start + tileCount + 1;
		for ( int t = 0; t < tileCount; t++ )
		{
			tiles.Add( (uint)running );
			running += counts[t];
		}
		tiles.Add( (uint)running );

		CollectionsMarshal.SetCount( tiles, running );
		var span = CollectionsMarshal.AsSpan( tiles );
		counts.Clear();

		for ( int i = 0; i < instances.Length; i++ )
		{
			GetTileRange( instances[i].Rect, tilesX, tilesY, out var x0, out var y0, out var x1, out var y1 );
			for ( int y = y0; y <= y1; y++ )
			{
				for ( int x = x0; x <= x1; x++ )
				{
					int t = y * tilesX + x;
					span[(int)span[start + t] + counts[t]] = (uint)i;
					counts[t]++;
				}
			}
		}
	}

	static void GetTileRange( Vector4 rect, int tilesX, int tilesY, out int x0, out int y0, out int x1, out int y1 )
	{
		x0 = Math.Clamp( (int)MathF.Floor( rect.x / TileSize ), 0, tilesX - 1 );
		y0 = Math.Clamp( (int)MathF.Floor( rect.y / TileSize ), 0, tilesY - 1 );
		x1 = Math.Clamp( (int)MathF.Floor( (rect.x + rect.z) / TileSize ), 0, tilesX - 1 );
		y1 = Math.Clamp( (int)MathF.Floor( (rect.y + rect.w) / TileSize ), 0, tilesY - 1 );
	}

	static FrameSlot AcquireSlot()
	{
		var slot = _slots[_slot];
		if ( slot.Frame != Application.FrameCount )
		{
			_slot = (_slot + 1) % _slots.Length;
			slot = _slots[_slot];
			slot.Frame = Application.FrameCount;
			slot.Instances.Clear();
			slot.Tiles.Clear();
			slot.InstancesUploaded = 0;
			slot.TilesUploaded = 0;
		}

		return slot;
	}

}
