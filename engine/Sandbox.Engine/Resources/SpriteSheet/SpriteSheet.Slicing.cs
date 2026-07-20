using System.Text.Json.Serialization;

namespace Sandbox;

public partial class SpriteSheet
{
	/// <summary>
	/// How a sheet gets cut up.
	/// </summary>
	public enum SliceType
	{
		/// <summary>
		/// Find the artwork by looking for islands of non-transparent pixels. Best for a sheet of
		/// loose parts with space between them, like a character cut into limbs.
		/// </summary>
		[Icon( "auto_fix_high" )] Automatic,

		/// <summary>
		/// A regular grid, where you give the size of one cell.
		/// </summary>
		[Icon( "grid_on" )] GridByCellSize,

		/// <summary>
		/// A regular grid, where you give the number of columns and rows and the cell size is worked
		/// out from the image.
		/// </summary>
		[Icon( "grid_4x4" )] GridByCellCount
	}

	/// <summary>
	/// What happens to slices that are already there when you slice again.
	/// </summary>
	public enum SliceMethod
	{
		/// <summary>
		/// Throw away every existing slice and start over. Names and ids are lost, so anything
		/// pointing at them breaks.
		/// </summary>
		[Icon( "delete_sweep" )] DeleteExisting,

		/// <summary>
		/// Reshape existing slices to match the new cut where they clearly correspond, and add the
		/// rest. Keeps names and ids, so references survive. This is the one to use after renaming.
		/// </summary>
		[Icon( "auto_awesome" )] Smart,

		/// <summary>
		/// Only add slices in space nothing already occupies. Never changes or removes what is there.
		/// </summary>
		[Icon( "shield" )] Safe
	}

	/// <summary>
	/// Everything that controls a slicing pass.
	/// </summary>
	public class SliceSettings
	{
		/// <summary>
		/// How to find the slices.
		/// </summary>
		[Property, EnumDropdown]
		public SliceType Type { get; set; } = SliceType.Automatic;

		/// <summary>True for the grid types, which share most of their settings.</summary>
		[Hide, JsonIgnore]
		public bool IsGrid => Type != SliceType.Automatic;

		/// <summary>
		/// Cell size in pixels.
		/// </summary>
		[Property, Title( "Pixel Size" ), ShowIf( nameof( Type ), SliceType.GridByCellSize )]
		public Vector2Int CellSize { get; set; } = new Vector2Int( 64, 64 );

		/// <summary>
		/// Columns and rows. The cell size is worked out from whatever is left of the image.
		/// </summary>
		[Property, Title( "Column & Row" ), ShowIf( nameof( Type ), SliceType.GridByCellCount )]
		public Vector2Int CellCount { get; set; } = new Vector2Int( 8, 8 );

		/// <summary>
		/// Where the grid starts, in pixels from the top-left.
		/// </summary>
		[Property, ShowIf( nameof( IsGrid ), true )]
		public Vector2Int Offset { get; set; }

		/// <summary>
		/// Gap between cells, in pixels.
		/// </summary>
		[Property, ShowIf( nameof( IsGrid ), true )]
		public Vector2Int Padding { get; set; }

		/// <summary>
		/// Keep cells that turned out to be entirely transparent. Off by default, because a sheet is
		/// rarely completely full.
		/// </summary>
		[Property, ShowIf( nameof( IsGrid ), true )]
		public bool KeepEmptyRects { get; set; }

		/// <summary>
		/// Alpha at or below this counts as transparent. Leave at 0 for clean artwork; raise it for
		/// scans, soft brushes, or anything with grubby edges.
		/// </summary>
		[Property, Range( 0, 254 ), Step( 1 )]
		public byte AlphaTolerance { get; set; }

		/// <summary>
		/// What counts as empty space. Set from the sheet, not edited here.
		/// </summary>
		[Hide, JsonIgnore]
		public BackgroundKey Background { get; set; }

		/// <summary>
		/// Where to put the pivot on every slice this pass produces.
		/// </summary>
		[Property, Header( "Pivot" ), EnumDropdown]
		public PivotAlignment Alignment { get; set; } = PivotAlignment.Center;

		/// <summary>
		/// The pivot, normalized within each slice, when <see cref="Alignment"/> is Custom.
		/// </summary>
		[Property, Title( "Custom Pivot" ), Range( 0, 1 ), ShowIf( nameof( Alignment ), PivotAlignment.Custom )]
		public Vector2 CustomPivot { get; set; } = new Vector2( 0.5f, 0.5f );

		/// <summary>
		/// What happens to slices that are already there.
		/// </summary>
		[Property, Header( "Method" ), EnumDropdown]
		public SliceMethod Method { get; set; } = SliceMethod.DeleteExisting;

		internal Vector2 ResolvePivot()
			=> Alignment == PivotAlignment.Custom ? CustomPivot : Slice.GetPivot( Alignment );
	}

	/// <summary>
	/// The smallest run of pixels automatic slicing will treat as artwork rather than noise.
	/// </summary>
	/// <remarks>
	/// Deliberately not a setting. Unity exposed this once and took it back out, on the grounds that
	/// hunting for the right number is worse than slicing roughly and fixing the few rects that came
	/// out wrong. Their note: "4 seems to be a pretty nice min size for a automatic sprite slicing.
	/// It used to be exposed to the slicing dialog, but it is actually better workflow to slice &amp;
	/// crop manually than find a suitable size number".
	/// </remarks>
	public const int MinimumAutomaticSliceSize = 4;

	// How near a perfect fit an existing slice has to be before Smart will reshape it rather than
	// treat the incoming rect as something new. 0 is exact containment; beyond this they are
	// different things that happen to touch.
	const float BestFitTolerance = 0.5f;

	// Two candidates this close in fit are a tie, broken by preferring the smaller existing slice.
	const float OverlapTolerance = 0.00001f;

	/// <summary>
	/// Cut a sheet up and fold the result into whatever is already there.
	/// </summary>
	/// <param name="pixels">The source image, row-major from the top-left</param>
	/// <param name="width">Source image width in pixels</param>
	/// <param name="height">Source image height in pixels</param>
	/// <param name="settings">How to cut</param>
	/// <param name="existing">Slices already on the sheet. Not modified; the result is a new list.</param>
	/// <param name="namePrefix">Stem for generated names, usually the asset's name</param>
	public static List<Slice> CreateSlices( Color32[] pixels, int width, int height, SliceSettings settings, IEnumerable<Slice> existing = null, string namePrefix = "slice" )
	{
		ArgumentNullException.ThrowIfNull( settings );

		var found = settings.Type switch
		{
			SliceType.GridByCellSize => SliceGridByCellSize( pixels, width, height, settings ),
			SliceType.GridByCellCount => SliceGridByCellCount( pixels, width, height, settings ),
			_ => SliceAutomatic( pixels, width, height, settings ),
		};

		return Merge( existing, found, settings, namePrefix );
	}

	/// <summary>
	/// A regular grid from an explicit cell size.
	/// </summary>
	public static List<Rect> SliceGridByCellSize( Color32[] pixels, int width, int height, SliceSettings settings )
	{
		var cell = new Vector2Int(
			Math.Clamp( settings.CellSize.x, 1, Math.Max( 1, width ) ),
			Math.Clamp( settings.CellSize.y, 1, Math.Max( 1, height ) ) );

		return BuildGrid( pixels, width, height, cell, settings );
	}

	/// <summary>
	/// A regular grid from a column and row count, working the cell size out from what is left of the
	/// image once the offset and the gaps between cells are taken off.
	/// </summary>
	public static List<Rect> SliceGridByCellCount( Color32[] pixels, int width, int height, SliceSettings settings )
	{
		var columns = Math.Max( 1, settings.CellCount.x );
		var rows = Math.Max( 1, settings.CellCount.y );

		var cell = new Vector2Int(
			(width - settings.Offset.x - (settings.Padding.x * Math.Max( 0, columns - 1 ))) / columns,
			(height - settings.Offset.y - (settings.Padding.y * Math.Max( 0, rows - 1 ))) / rows );

		cell = new Vector2Int(
			Math.Clamp( cell.x, 1, Math.Max( 1, width ) ),
			Math.Clamp( cell.y, 1, Math.Max( 1, height ) ) );

		return BuildGrid( pixels, width, height, cell, settings, columns, rows );
	}

	static List<Rect> BuildGrid( Color32[] pixels, int width, int height, Vector2Int cell, SliceSettings settings, int? columnLimit = null, int? rowLimit = null )
	{
		var rects = new List<Rect>();

		var stepX = cell.x + settings.Padding.x;
		var stepY = cell.y + settings.Padding.y;

		int column = 0;
		for ( int x = settings.Offset.x; x + cell.x <= width; x += stepX )
		{
			if ( columnLimit.HasValue && column >= columnLimit.Value ) break;

			int row = 0;
			for ( int y = settings.Offset.y; y + cell.y <= height; y += stepY )
			{
				if ( rowLimit.HasValue && row >= rowLimit.Value ) break;

				var rect = new Rect( x, y, cell.x, cell.y );

				if ( settings.KeepEmptyRects || !IsEmpty( pixels, width, height, rect, settings.AlphaTolerance, settings.Background ) )
				{
					rects.Add( rect );
				}

				row++;
			}

			column++;
		}

		// Row-major, so frames come out in the order they read on the sheet rather than column-first.
		return rects.OrderBy( r => r.Top ).ThenBy( r => r.Left ).ToList();
	}

	/// <summary>
	/// Find the artwork by looking for connected islands of opaque pixels, and take the bounding box
	/// of each.
	/// </summary>
	public static List<Rect> SliceAutomatic( Color32[] pixels, int width, int height, SliceSettings settings )
	{
		var rects = FindIslands( pixels, width, height, settings?.AlphaTolerance ?? 0, settings?.Background ?? default );

		// Two islands whose boxes overlap are almost always one thing that happens to be drawn in
		// separate pieces - a highlight sitting over a limb, a dotted outline. Fold them together
		// until nothing overlaps any more.
		MergeOverlapping( rects );

		rects.RemoveAll( r => r.Width < MinimumAutomaticSliceSize || r.Height < MinimumAutomaticSliceSize );

		return SortReadingOrder( rects );
	}

	/// <summary>
	/// Bounding boxes of every 8-connected island of non-transparent pixels.
	/// </summary>
	static List<Rect> FindIslands( Color32[] pixels, int width, int height, byte alphaTolerance, BackgroundKey background )
	{
		var rects = new List<Rect>();
		if ( pixels is null || width <= 0 || height <= 0 ) return rects;

		var count = Math.Min( pixels.Length, width * height );
		var visited = new bool[count];

		// Explicit stack rather than recursion - a 4096x4096 sheet is 16 million pixels and one
		// island can span all of them, which would blow the call stack instantly.
		var stack = new Stack<int>();

		for ( int start = 0; start < count; start++ )
		{
			if ( visited[start] ) continue;
			visited[start] = true;

			if ( background.IsEmpty( pixels[start], alphaTolerance ) ) continue;

			int minX = start % width, maxX = minX;
			int minY = start / width, maxY = minY;

			stack.Push( start );

			while ( stack.Count > 0 )
			{
				var index = stack.Pop();
				var x = index % width;
				var y = index / width;

				if ( x < minX ) minX = x;
				if ( x > maxX ) maxX = x;
				if ( y < minY ) minY = y;
				if ( y > maxY ) maxY = y;

				for ( int dy = -1; dy <= 1; dy++ )
				{
					var ny = y + dy;
					if ( ny < 0 || ny >= height ) continue;

					for ( int dx = -1; dx <= 1; dx++ )
					{
						if ( dx == 0 && dy == 0 ) continue;

						var nx = x + dx;
						if ( nx < 0 || nx >= width ) continue;

						var neighbour = (ny * width) + nx;
						if ( neighbour >= count || visited[neighbour] ) continue;

						visited[neighbour] = true;
						if ( background.IsEmpty( pixels[neighbour], alphaTolerance ) ) continue;

						stack.Push( neighbour );
					}
				}
			}

			rects.Add( new Rect( minX, minY, maxX - minX + 1, maxY - minY + 1 ) );
		}

		return rects;
	}

	static void MergeOverlapping( List<Rect> rects )
	{
		bool merged = true;

		while ( merged )
		{
			merged = false;

			for ( int i = 0; i < rects.Count && !merged; i++ )
			{
				for ( int j = i + 1; j < rects.Count; j++ )
				{
					if ( !Overlaps( rects[i], rects[j] ) ) continue;

					rects[i] = Union( rects[i], rects[j] );
					rects.RemoveAt( j );
					merged = true;
					break;
				}
			}
		}
	}

	/// <summary>
	/// Put rects in the order someone would read them off the sheet: banded into rows by vertical
	/// overlap, then left to right within each row.
	/// </summary>
	/// <remarks>
	/// Sorting on Top alone looks right until automatic slicing returns rects trimmed to their
	/// artwork, at which point two frames on the same row differ by a few pixels and interleave with
	/// the row below. Frame order decides animation order, so that matters.
	/// </remarks>
	static List<Rect> SortReadingOrder( List<Rect> rects )
	{
		var sorted = rects.OrderBy( r => r.Top ).ToList();
		var result = new List<Rect>( sorted.Count );

		int index = 0;
		while ( index < sorted.Count )
		{
			var band = new List<Rect> { sorted[index] };
			var bandBottom = sorted[index].Top + sorted[index].Height;
			index++;

			// Anything starting before the band ends shares a row with it.
			while ( index < sorted.Count && sorted[index].Top < bandBottom )
			{
				bandBottom = Math.Max( bandBottom, sorted[index].Top + sorted[index].Height );
				band.Add( sorted[index] );
				index++;
			}

			result.AddRange( band.OrderBy( r => r.Left ) );
		}

		return result;
	}

	/// <summary>
	/// Shrink a rect until it just contains the artwork inside it. Returns an empty rect if there is
	/// nothing there.
	/// </summary>
	/// <param name="pixels">The source image, row-major from the top-left</param>
	/// <param name="width">Source image width in pixels</param>
	/// <param name="height">Source image height in pixels</param>
	/// <param name="rect">The rect to tighten</param>
	/// <param name="alphaTolerance">Alpha at or below this counts as transparent</param>
	/// <param name="background">A solid colour to treat as empty, for artwork with no alpha</param>
	public static Rect Trim( Color32[] pixels, int width, int height, Rect rect, byte alphaTolerance = 0, BackgroundKey background = default )
	{
		if ( pixels is null || width <= 0 || height <= 0 ) return default;

		var left = Math.Max( 0, (int)rect.Left );
		var top = Math.Max( 0, (int)rect.Top );
		var right = Math.Min( width, (int)(rect.Left + rect.Width) );
		var bottom = Math.Min( height, (int)(rect.Top + rect.Height) );

		int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

		for ( int y = top; y < bottom; y++ )
		{
			for ( int x = left; x < right; x++ )
			{
				var index = (y * width) + x;
				if ( index >= pixels.Length ) continue;
				if ( background.IsEmpty( pixels[index], alphaTolerance ) ) continue;

				if ( x < minX ) minX = x;
				if ( x > maxX ) maxX = x;
				if ( y < minY ) minY = y;
				if ( y > maxY ) maxY = y;
			}
		}

		if ( minX > maxX || minY > maxY ) return default;

		return new Rect( minX, minY, maxX - minX + 1, maxY - minY + 1 );
	}

	/// <summary>
	/// Whether a rect contains no artwork at all.
	/// </summary>
	public static bool IsEmpty( Color32[] pixels, int width, int height, Rect rect, byte alphaTolerance = 0, BackgroundKey background = default )
	{
		if ( pixels is null || width <= 0 || height <= 0 ) return true;

		var left = Math.Max( 0, (int)rect.Left );
		var top = Math.Max( 0, (int)rect.Top );
		var right = Math.Min( width, (int)(rect.Left + rect.Width) );
		var bottom = Math.Min( height, (int)(rect.Top + rect.Height) );

		for ( int y = top; y < bottom; y++ )
		{
			for ( int x = left; x < right; x++ )
			{
				var index = (y * width) + x;
				if ( index >= pixels.Length ) continue;
				if ( !background.IsEmpty( pixels[index], alphaTolerance ) ) return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Fold a freshly cut set of rects into the slices already on a sheet.
	/// </summary>
	static List<Slice> Merge( IEnumerable<Slice> existing, List<Rect> found, SliceSettings settings, string namePrefix )
	{
		var result = existing?.ToList() ?? new List<Slice>();

		if ( settings.Method == SliceMethod.DeleteExisting )
		{
			result.Clear();
		}

		// Lets us reuse the sheet's own uniqueness check while the list is still being built.
		var scratch = new SpriteSheet { Slices = result };
		var nextIndex = 0;

		string NextFreeName()
		{
			while ( true )
			{
				var candidate = $"{namePrefix}_{nextIndex++}";
				if ( scratch.GetSliceIndex( candidate ) == -1 ) return candidate;
			}
		}

		foreach ( var rect in found )
		{
			if ( settings.Method == SliceMethod.Smart )
			{
				var match = FindBestFit( result, rect );
				if ( match is not null )
				{
					// Reshape in place. The name and the id are what references hang off, so they
					// have to survive a re-slice - that is the entire point of this method.
					//
					// The pivot is left alone too, which is a deliberate difference from Unity: it
					// reapplies the dialog's alignment here. Placing pivots on joints is the slow,
					// careful part of rigging a character, and re-slicing to pick up a few missed
					// limbs should not throw that away.
					match.Rect = rect;
					continue;
				}
			}
			else if ( settings.Method == SliceMethod.Safe )
			{
				if ( result.Any( s => Overlaps( s.Rect, rect ) ) ) continue;
			}

			var slice = new Slice
			{
				Rect = rect,
				Alignment = settings.Alignment,
				Pivot = settings.ResolvePivot()
			};

			slice.Name = NextFreeName();
			result.Add( slice );
		}

		return result;
	}

	/// <summary>
	/// The existing slice an incoming rect most likely *is*, or null if it is something new.
	/// </summary>
	static Slice FindBestFit( List<Slice> slices, Rect rect )
	{
		var area = rect.Width * rect.Height;
		if ( area <= 0 ) return null;

		Slice best = null;
		var bestRatio = float.MaxValue;

		foreach ( var slice in slices )
		{
			if ( !Overlaps( slice.Rect, rect ) ) continue;

			var overlap = Intersection( slice.Rect, rect );
			// 0 means the incoming rect sits entirely inside the existing one.
			var ratio = MathF.Abs( ((overlap.Width * overlap.Height) / area) - 1f );

			if ( ratio < bestRatio - OverlapTolerance )
			{
				best = slice;
				bestRatio = ratio;
				continue;
			}

			// Equally good fits: prefer the tighter existing slice.
			if ( MathF.Abs( ratio - bestRatio ) <= OverlapTolerance && best is not null )
			{
				if ( (slice.Rect.Width * slice.Rect.Height) < (best.Rect.Width * best.Rect.Height) )
				{
					best = slice;
					bestRatio = ratio;
				}
			}
		}

		return bestRatio > BestFitTolerance ? null : best;
	}

	static bool Overlaps( Rect a, Rect b )
		=> a.Left < b.Left + b.Width && b.Left < a.Left + a.Width
		&& a.Top < b.Top + b.Height && b.Top < a.Top + a.Height;

	static Rect Intersection( Rect a, Rect b )
	{
		var left = Math.Max( a.Left, b.Left );
		var top = Math.Max( a.Top, b.Top );
		var right = Math.Min( a.Left + a.Width, b.Left + b.Width );
		var bottom = Math.Min( a.Top + a.Height, b.Top + b.Height );

		if ( right <= left || bottom <= top ) return default;

		return new Rect( left, top, right - left, bottom - top );
	}

	static Rect Union( Rect a, Rect b )
	{
		var left = Math.Min( a.Left, b.Left );
		var top = Math.Min( a.Top, b.Top );
		var right = Math.Max( a.Left + a.Width, b.Left + b.Width );
		var bottom = Math.Max( a.Top + a.Height, b.Top + b.Height );

		return new Rect( left, top, right - left, bottom - top );
	}
}
