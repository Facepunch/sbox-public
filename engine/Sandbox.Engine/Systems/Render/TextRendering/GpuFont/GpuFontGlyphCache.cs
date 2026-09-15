using SkiaSharp;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Glyph outlines as quadratic Béziers in em space (y down), each glyph with Lengyel's bands listing the curves a
/// horizontal or vertical ray can cross. Everything is appended into three growing GPU buffers that ui/text.hlsl
/// reads, so a glyph is encoded once per process and shared by every text block.
/// </summary>
internal static class GpuFontGlyphCache
{
	internal struct Glyph
	{
		public Vector4 Bounds; // em: min x, min y, max x, max y
		public int BandOffset;
		public int BandCount;
		public int CurveStart;
		public int CurveCount;
		public int Index; // into GlyphTable

		public readonly bool IsEmpty => CurveCount == 0;
	}

	const int MaxBands = 128;

	[ConVar( "text_band_density", ConVarFlags.None, Min = 0.25f, Max = 8f, Help = "Bands per glyph relative to its curve count. Higher is fewer curves walked per pixel for more memory. Only affects glyphs encoded after the change." )]
	static float BandDensity { get; set; } = 2f;

	static readonly object _lock = new();
	// Keyed by the typeface object, so a reloaded font never collides with a stale entry
	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SKTypeface, Dictionary<ushort, Glyph>> _glyphs = new();
	static readonly List<Vector2> _curves = new(); // 3 points per curve
	static readonly List<uint> _bands = new();
	static readonly List<Glyph> _table = new();
	static GpuBuffer<Vector2> _curveBuffer;
	static GpuBuffer<uint> _bandBuffer;
	static GpuBuffer<Glyph> _tableBuffer;
	static int _curvesUploaded;
	static int _bandsUploaded;
	static int _tableUploaded;

	/// <summary>
	/// A colour glyph's layers from COLR v0, which v1 fonts like Segoe UI Emoji still carry: outline glyphs
	/// stacked in order, each with a CPAL colour or null for the text colour. Null for a plain glyph.
	/// </summary>
	internal static (ushort Glyph, Color? Color)[] GetLayers( SKTypeface typeface, ushort glyphId )
	{
		var table = _colorTables.GetValue( typeface, ColorTable.Load );
		if ( table.IsEmpty ) return null;
		lock ( table ) return table.Get( glyphId );
	}

	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SKTypeface, ColorTable> _colorTables = new();

	class ColorTable
	{
		byte[] _colr, _cpal;
		int _baseCount, _baseOffset, _layerOffset, _colorOffset;
		readonly Dictionary<ushort, (ushort, Color?)[]> _layers = new();
		public bool IsEmpty => _baseCount == 0;

		static ushort U16( byte[] b, int o ) => (ushort)(b[o] << 8 | b[o + 1]);
		static int U32( byte[] b, int o ) => b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3];

		public static ColorTable Load( SKTypeface typeface )
		{
			var table = new ColorTable();
			if ( (!typeface.TryGetTableData( 0x434F4C52, out table._colr ) && !typeface.TryGetTableData( 0x434F4C58, out table._colr )) || !typeface.TryGetTableData( 0x4350414C, out table._cpal ) || table._colr.Length < 14 || table._cpal.Length < 14 )
				return table;

			table._baseCount = U16( table._colr, 2 );
			table._baseOffset = U32( table._colr, 4 );
			table._layerOffset = U32( table._colr, 8 );

			// Palette 0: the colour records start where its first index points
			table._colorOffset = U32( table._cpal, 8 ) + U16( table._cpal, 12 ) * 4;
			return table;
		}

		public (ushort, Color?)[] Get( ushort glyphId )
		{
			if ( _baseCount == 0 ) return null;
			if ( _layers.TryGetValue( glyphId, out var layers ) ) return layers;

			int lo = 0, hi = _baseCount - 1;
			while ( lo <= hi )
			{
				int mid = (lo + hi) / 2;
				int record = _baseOffset + mid * 6;
				int gid = U16( _colr, record );
				if ( gid < glyphId ) lo = mid + 1;
				else if ( gid > glyphId ) hi = mid - 1;
				else
				{
					int first = U16( _colr, record + 2 ), count = U16( _colr, record + 4 );
					layers = new (ushort, Color?)[count];
					for ( int i = 0; i < count; i++ )
					{
						int layer = _layerOffset + (first + i) * 4;
						int palette = U16( _colr, layer + 2 );
						Color? color = null;
						if ( palette != 0xFFFF )
						{
							int c = _colorOffset + palette * 4; // BGRA
							color = new Color( _cpal[c + 2] / 255f, _cpal[c + 1] / 255f, _cpal[c] / 255f, _cpal[c + 3] / 255f );
						}
						layers[i] = (U16( _colr, layer ), color);
					}
					break;
				}
			}

			_layers[glyphId] = layers;
			return layers;
		}
	}

	public static Glyph Get( SKTypeface typeface, ushort glyphId )
	{
		lock ( _lock )
		{
			var glyphs = _glyphs.GetOrCreateValue( typeface );
			if ( glyphs.TryGetValue( glyphId, out var glyph ) )
				return glyph;

			glyph = Encode( typeface, glyphId );
			glyphs[glyphId] = glyph;
			return glyph;
		}
	}

	/// <summary>
	/// Upload anything encoded since the last bind and point the shader at the buffers. Only ever appends,
	/// so a dispatch already recorded against the old contents still reads what it expects.
	/// </summary>
	public static void Bind( RenderAttributes attributes )
	{
		Upload();
		attributes.Set( "GlyphCurves", _curveBuffer );
		attributes.Set( "GlyphBands", _bandBuffer );
		attributes.Set( "GlyphTable", _tableBuffer );
	}

	public static void Bind( Sandbox.Rendering.CommandList.AttributeAccess attributes )
	{
		Upload();
		attributes.Set( "GlyphCurves", (GpuBuffer)_curveBuffer );
		attributes.Set( "GlyphBands", (GpuBuffer)_bandBuffer );
		attributes.Set( "GlyphTable", (GpuBuffer)_tableBuffer );
	}

	static void Upload()
	{
		lock ( _lock )
		{
			Append( ref _curveBuffer, _curves, ref _curvesUploaded, 4096 );
			Append( ref _bandBuffer, _bands, ref _bandsUploaded, 4096 );
			Append( ref _tableBuffer, _table, ref _tableUploaded, 4096 );
		}
	}

	/// <summary>
	/// Grow the buffer to hold the list and upload whatever was appended since last time. Growing re-uploads
	/// from the start, since the new buffer holds none of it.
	/// </summary>
	internal static void Append<T>( ref GpuBuffer<T> buffer, List<T> data, ref int uploaded, int minimum ) where T : unmanaged
	{
		if ( buffer == null || buffer.ElementCount < data.Count )
		{
			buffer = new GpuBuffer<T>( Math.Max( minimum, (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)data.Count ) ) );
			uploaded = 0;
		}

		if ( uploaded < data.Count )
		{
			buffer.SetData<T>( CollectionsMarshal.AsSpan( data ).Slice( uploaded ), uploaded );
			uploaded = data.Count;
		}
	}

	//
	// Encoding
	//

	static Glyph Encode( SKTypeface typeface, ushort glyphId )
	{
		var curves = new List<Vector2>();
		var upem = typeface.UnitsPerEm;
		if ( upem <= 0 ) upem = 1000;

		using ( var font = new SKFont( typeface, upem ) { Hinting = SKFontHinting.None } )
		using ( var path = font.GetGlyphPath( glyphId ) )
		{
			if ( path != null && !path.IsEmpty )
				ExtractCurves( path, 1.0f / upem, curves );
		}

		int curveCount = curves.Count / 3;
		if ( curveCount == 0 )
			return default;

		var lo = new Vector2[curveCount];
		var hi = new Vector2[curveCount];
		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );
		var extent = Vector2.Zero;

		for ( int c = 0; c < curveCount; c++ )
		{
			lo[c] = Vector2.Min( curves[c * 3], Vector2.Min( curves[c * 3 + 1], curves[c * 3 + 2] ) );
			hi[c] = Vector2.Max( curves[c * 3], Vector2.Max( curves[c * 3 + 1], curves[c * 3 + 2] ) );
			min = Vector2.Min( min, lo[c] );
			max = Vector2.Max( max, hi[c] );
			extent += hi[c] - lo[c];
		}

		var size = Vector2.Max( max - min, new Vector2( 1e-6f ) );

		// A ray across the glyph crosses this many curves on average; band finely enough that it visits only about half
		// as many again, which also puts each curve in about three bands per axis whatever the glyph.
		float crossings = MathF.Max( extent.x / size.x, extent.y / size.y );
		int bandCount = Math.Clamp( (int)MathF.Ceiling( curveCount / crossings * BandDensity ), 1, MaxBands );
		int Band( float t ) => Math.Clamp( (int)(t * bandCount), 0, bandCount - 1 );

		var lists = new List<int>[bandCount * 2];
		for ( int i = 0; i < lists.Length; i++ ) lists[i] = new List<int>();

		for ( int c = 0; c < curveCount; c++ )
		{
			var a = (lo[c] - min) / size;
			var b = (hi[c] - min) / size;
			for ( int i = Band( a.y ); i <= Band( b.y ); i++ ) lists[i].Add( c );
			for ( int i = Band( a.x ); i <= Band( b.x ); i++ ) lists[bandCount + i].Add( c );
		}

		// Header: (triplet offset, count) per horizontal band, then per vertical band. Each band's curves follow in
		// the curve buffer, the one reaching furthest along its ray first, so the walk can stop at the first one behind the pixel.
		int curveStart = _curves.Count / 3;
		_curves.AddRange( curves );
		int bandOffset = _bands.Count;

		for ( int i = 0; i < lists.Length; i++ )
		{
			bool vertical = i >= bandCount;
			float Far( int c ) => vertical ? hi[c].y : hi[c].x;

			lists[i].Sort( ( a, b ) => Far( b ).CompareTo( Far( a ) ) );
			_bands.Add( (uint)(_curves.Count / 3) );
			_bands.Add( (uint)lists[i].Count );
			foreach ( var c in lists[i] )
			{
				_curves.AddRange( CollectionsMarshal.AsSpan( curves ).Slice( c * 3, 3 ) );
			}
		}

		var glyph = new Glyph
		{
			Bounds = new Vector4( min.x, min.y, max.x, max.y ),
			BandOffset = bandOffset,
			BandCount = bandCount,
			CurveStart = curveStart,
			CurveCount = curveCount,
			Index = _table.Count,
		};
		_table.Add( glyph );
		return glyph;
	}

	/// <summary>The encoded buffers as the shader sees them, for tests that walk a band like GpuFontRay does.</summary>
	internal static (IReadOnlyList<Vector2> Curves, IReadOnlyList<uint> Bands) EncodedForTests => (_curves, _bands);

	static void ExtractCurves( SKPath path, float scale, List<Vector2> curves )
	{
		using var iter = path.CreateIterator( false );
		var pts = new SKPoint[4];
		Vector2 cur = default, start = default;

		while ( true )
		{
			var verb = iter.Next( pts );
			if ( verb == SKPathVerb.Done ) break;

			switch ( verb )
			{
				case SKPathVerb.Move:
					cur = start = Point( pts[0], scale );
					break;

				case SKPathVerb.Line:
					AddLine( curves, cur, cur = Point( pts[1], scale ) );
					break;

				case SKPathVerb.Quad:
				case SKPathVerb.Conic:
					AddQuad( curves, cur, Point( pts[1], scale ), cur = Point( pts[2], scale ) );
					break;

				case SKPathVerb.Cubic:
					AddCubic( curves, cur, Point( pts[1], scale ), Point( pts[2], scale ), cur = Point( pts[3], scale ), 0 );
					break;

				case SKPathVerb.Close:
					AddLine( curves, cur, start );
					cur = start;
					break;
			}
		}
	}

	static Vector2 Point( SKPoint p, float scale ) => new( p.X * scale, p.Y * scale );

	static void AddLine( List<Vector2> curves, Vector2 a, Vector2 b )
	{
		if ( a.AlmostEqual( b, 1e-7f ) ) return;
		AddQuad( curves, a, (a + b) * 0.5f, b );
	}

	static void AddQuad( List<Vector2> curves, Vector2 a, Vector2 c, Vector2 b )
	{
		if ( a.AlmostEqual( b, 1e-7f ) && a.AlmostEqual( c, 1e-7f ) ) return;
		curves.Add( a );
		curves.Add( c );
		curves.Add( b );
	}

	/// <summary>
	/// Split until a single quadratic approximates each piece to within a fifth of a pixel at 100px.
	/// </summary>
	static void AddCubic( List<Vector2> curves, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int depth )
	{
		var error = (p0 - p1 * 3 + p2 * 3 - p3).Length * 0.0481f;
		if ( error < 0.002f || depth >= 4 )
		{
			AddQuad( curves, p0, (p1 * 3 + p2 * 3 - p0 - p3) * 0.25f, p3 );
			return;
		}

		var p01 = (p0 + p1) * 0.5f;
		var p12 = (p1 + p2) * 0.5f;
		var p23 = (p2 + p3) * 0.5f;
		var p012 = (p01 + p12) * 0.5f;
		var p123 = (p12 + p23) * 0.5f;
		var mid = (p012 + p123) * 0.5f;

		AddCubic( curves, p0, p01, p012, mid, depth + 1 );
		AddCubic( curves, mid, p123, p23, p3, depth + 1 );
	}

	static readonly List<float> _crossings = new();     // scratch, only touched under _lock
	static readonly IComparer<Vector2> ByX = Comparer<Vector2>.Create( ( a, b ) => a.x.CompareTo( b.x ) );

	/// <summary>
	/// Where the glyph's ink crosses a horizontal band, as merged (min x, max x) spans in em, left to right.
	/// Decoration lines skipping ink break around these. Like Skia's intercepts: the outline's crossings of
	/// the band's two edges, paired up along each edge.
	/// </summary>
	internal static void Intercepts( in Glyph glyph, float top, float bottom, List<Vector2> spans )
	{
		const int segments = 8;
		int start = spans.Count;

		lock ( _lock )
		{
			for ( int edge = 0; edge < 2; edge++ )
			{
				float y = edge == 0 ? top : bottom;
				_crossings.Clear();

				for ( int c = 0; c < glyph.CurveCount; c++ )
				{
					int i = (glyph.CurveStart + c) * 3;
					var p1 = _curves[i];
					var p2 = _curves[i + 1];
					var p3 = _curves[i + 2];

					var prev = p1;
					for ( int k = 1; k <= segments; k++ )
					{
						float t = k / (float)segments;
						float u = 1 - t;
						var next = p1 * (u * u) + p2 * (2 * u * t) + p3 * (t * t);

						if ( (prev.y <= y) != (next.y <= y) )
							_crossings.Add( prev.x + (next.x - prev.x) * (y - prev.y) / (next.y - prev.y) );

						prev = next;
					}
				}

				_crossings.Sort();
				for ( int i = 0; i + 1 < _crossings.Count; i += 2 )
					spans.Add( new Vector2( _crossings[i], _crossings[i + 1] ) );
			}
		}

		if ( spans.Count == start ) return;

		spans.Sort( start, spans.Count - start, ByX );

		int w = start;
		for ( int r = start + 1; r < spans.Count; r++ )
		{
			if ( spans[r].x <= spans[w].y )
				spans[w] = new Vector2( spans[w].x, MathF.Max( spans[w].y, spans[r].y ) );
			else
				spans[++w] = spans[r];
		}

		spans.RemoveRange( w + 1, spans.Count - w - 1 );
	}
}
