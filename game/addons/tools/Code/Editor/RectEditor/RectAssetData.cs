
namespace Editor.RectEditor;

public class RectAssetData
{
	public class Properties
	{
		public bool AllowRotation { get; set; }
		public bool AllowTiling { get; set; }
	}

	public class Subrect
	{
		public int[] Min { get; set; }
		public int[] Max { get; set; }

		public Properties Properties { get; set; }
	};

	public class SubrectSet
	{
		public string Name { get; set; }
		public List<Subrect> Rectangles { get; set; }
	};

	public List<SubrectSet> RectangleSets { get; set; }
	public Settings Settings { get; set; } = new Settings();

	public static RectAssetData Find( Asset asset )
	{
		if ( asset.AssetType == AssetType.Material )
		{
			asset = AssetSystem.FindByPath( asset.FindStringEditInfo( "SubrectDefinition" ) );
			if ( asset is null )
				return null;
		}
		else if ( asset.AssetType.FileExtension != "rect" )
		{
			return null;
		}

		var path = asset.GetSourceFile( true );
		if ( !System.IO.File.Exists( path ) )
			return null;

		var txt = System.IO.File.ReadAllText( path );
		if ( string.IsNullOrWhiteSpace( txt ) )
			return null;

		if ( txt.First() == '<' )
			txt = EditorUtility.KeyValues3ToJson( txt );

		if ( string.IsNullOrWhiteSpace( txt ) )
			return null;

		return Json.Deserialize<RectAssetData>( txt );
	}

	public enum FindSubrectMode
	{
		Next,
		Prev,
		Random,
	}

	public void FindBestSubrectForUVIsland(
		IReadOnlyList<Vector2> uvs,
		Subrect currentRect, bool currentRotated,
		FindSubrectMode mode,
		int mapW, int mapH,
		out Vector2 min, out Vector2 max,
		out bool rotated, out bool tiled )
	{
		min = new( 0, 0 );
		max = new( 1, 1 );
		rotated = false;
		tiled = false;

		if ( RectangleSets == null || RectangleSets.Count == 0 ) return;
		if ( uvs == null || uvs.Count == 0 ) return;
		if ( mapW <= 0 || mapH <= 0 ) return;

		var possible = new List<SelectedRect>();
		FindPossibleSubrectsForUVIsland( uvs, mapW, mapH, possible );
		if ( possible.Count == 0 ) return;

		var currentIndex = -1;
		if ( currentRect != null )
		{
			var cmin = new Vector2( currentRect.Min[0] / R, currentRect.Min[1] / R );
			var cmax = new Vector2( currentRect.Max[0] / R, currentRect.Max[1] / R );

			if ( currentRotated )
			{
				cmin = new( cmin.y, cmin.x );
				cmax = new( cmax.y, cmax.x );
			}

			currentIndex = FindMatchForRectangle( possible, cmin, cmax );
		}

		var pick = 0;
		if ( possible.Count > 1 )
		{
			var c = Math.Max( currentIndex, 0 );
			pick = mode == FindSubrectMode.Next
				? (c + 1) % possible.Count
				: (c + possible.Count - 1) % possible.Count;
		}

		min = possible[pick].Min;
		max = possible[pick].Max;
		rotated = possible[pick].Rotated;
		tiled = possible[pick].Tiling;
	}

	const float R = 1 << 15;
	const float Tol = 1f / 32f;

	struct SnappedRect
	{
		public int SizeX, SizeY;
		public Vector2 Min, Max;
		public bool AllowRotation, AllowTiling;
	}

	struct SelectedRect
	{
		public Vector2 Min, Max;
		public bool Rotated, Tiling;
	}

	static float Snap( float v, float step ) => MathF.Round( v / step ) * step;

	static void ComputeUVBounds( IReadOnlyList<Vector2> uvs, out Vector2 min, out Vector2 max )
	{
		min = new( float.MaxValue, float.MaxValue );
		max = new( -float.MaxValue, -float.MaxValue );

		for ( var i = 0; i < uvs.Count; ++i )
		{
			var uv = uvs[i];
			min = new( uv.x < min.x ? uv.x : min.x, uv.y < min.y ? uv.y : min.y );
			max = new( uv.x > max.x ? uv.x : max.x, uv.y > max.y ? uv.y : max.y );
		}
	}

	void AddSnappedRectangles( float sizeRangeX, float sizeRangeY, List<SnappedRect> outRects )
	{
		for ( var s = 0; s < RectangleSets.Count; ++s )
		{
			var rects = RectangleSets[s]?.Rectangles;
			if ( rects == null ) continue;

			for ( var r = 0; r < rects.Count; ++r )
			{
				var rect = rects[r];
				if ( rect?.Min == null || rect?.Max == null || rect.Min.Length < 2 || rect.Max.Length < 2 )
					continue;

				var rmin = new Vector2( rect.Min[0] / R, rect.Min[1] / R );
				var rmax = new Vector2( rect.Max[0] / R, rect.Max[1] / R );

				var sizeX = (int)Snap( (rmax.x - rmin.x) * sizeRangeX, 1f );
				var sizeY = (int)Snap( (rmax.y - rmin.y) * sizeRangeY, 1f );

				if ( sizeX <= 0 && sizeY <= 0 ) continue;

				outRects.Add( new SnappedRect
				{
					SizeX = sizeX,
					SizeY = sizeY,
					Min = rmin,
					Max = rmax,
					AllowRotation = rect.Properties?.AllowRotation ?? false,
					AllowTiling = rect.Properties?.AllowTiling ?? false
				} );
			}
		}
	}

	static void ComputeRectangleMatchFactors( int islandX, int islandY, in SnappedRect rect, out float scaleFactor, out float aspectFactor )
	{
		var islandAspectX = MathF.Max( (float)islandX / islandY, 1f );
		var islandAspectY = MathF.Max( (float)islandY / islandX, 1f );

		var scaleX = (float)Math.Min( islandX, rect.SizeX ) / Math.Max( islandX, rect.SizeX );
		var scaleY = (float)Math.Min( islandY, rect.SizeY ) / Math.Max( islandY, rect.SizeY );
		scaleFactor = MathF.Min( scaleX, scaleY );

		var rectAspectX = MathF.Max( (float)rect.SizeX / rect.SizeY, 1f );
		var rectAspectY = MathF.Max( (float)rect.SizeY / rect.SizeX, 1f );

		var aspectX = MathF.Min( islandAspectX, rectAspectX ) / MathF.Max( islandAspectX, rectAspectX );
		var aspectY = MathF.Min( islandAspectY, rectAspectY ) / MathF.Max( islandAspectY, rectAspectY );
		aspectFactor = MathF.Min( aspectX, aspectY );
	}

	void FindPossibleSubrectsForUVIsland( IReadOnlyList<Vector2> uvs, int mapW, int mapH, List<SelectedRect> outPossible )
	{
		ComputeUVBounds( uvs, out var islandMin, out var islandMax );

		var islandX = (int)Snap( (islandMax.x - islandMin.x) * mapW, 1f );
		var islandY = (int)Snap( (islandMax.y - islandMin.y) * mapH, 1f );
		if ( islandX <= 0 || islandY <= 0 ) return;

		var snapped = new List<SnappedRect>();
		AddSnappedRectangles( mapW, mapH, snapped );

		outPossible.Clear();

		var bestScale = 0f;
		var bestAspect = 0f;

		for ( var i = 0; i < snapped.Count; ++i )
		{
			var rect = snapped[i];
			var addedThisRect = false;

			for ( var rot = 0; rot < 2; ++rot )
			{
				var isRot = rot == 1;

				var current = new SelectedRect
				{
					Min = rect.Min,
					Max = rect.Max,
					Rotated = isRot,
					Tiling = rect.AllowTiling
				};

				float scale, aspect;

				if ( isRot )
					ComputeRectangleMatchFactors( islandY, islandX, rect, out scale, out aspect );
				else
					ComputeRectangleMatchFactors( islandX, islandY, rect, out scale, out aspect );

				if ( scale > bestScale )
				{
					bestScale = scale;
					bestAspect = aspect;
					outPossible.Clear();
					outPossible.Add( current );
					addedThisRect = true;
				}
				else if ( scale == bestScale )
				{
					if ( aspect > bestAspect )
					{
						bestAspect = aspect;
						outPossible.Clear();
						outPossible.Add( current );
						addedThisRect = true;
					}
					else if ( aspect == bestAspect && !addedThisRect )
					{
						outPossible.Add( current );
						addedThisRect = true;
					}
				}

				if ( !rect.AllowRotation ) break;
			}
		}
	}

	static float FractionalDistance( float a, float b )
	{
		static float F( float x ) => x - MathF.Floor( x );
		var d0 = MathF.Abs( F( a ) - F( b ) );
		var d1 = MathF.Abs( F( a + 0.5f ) - F( b + 0.5f ) );
		return MathF.Min( d0, d1 );
	}

	static float UvDistanceSquared( Vector2 a, Vector2 b )
	{
		var dx = FractionalDistance( a.x, b.x );
		var dy = FractionalDistance( a.y, b.y );
		return dx * dx + dy * dy;
	}

	static int FindMatchForRectangle( List<SelectedRect> rects, Vector2 min, Vector2 max )
	{
		var best = -1;
		var minSum = Tol * Tol * 2f;

		for ( var i = 0; i < rects.Count; ++i )
		{
			var r = rects[i];
			var sum = UvDistanceSquared( min, r.Min ) + UvDistanceSquared( max, r.Max );
			if ( sum >= minSum ) continue;

			minSum = sum;
			best = i;
		}

		return best;
	}
}
