namespace Borealis.Shadows;

internal partial class ShadowMapper
{
	/// <summary>
	/// The sun's last cascade, of two or more, is kept between frames in a cache: a square of the shadow distance around the
	/// camera, on a world grid of eighths of it, facing the sun as it was when drawn. Turning and zooming don't change it.
	/// It's held wrapped around, so when the camera crosses a grid line only the strip that comes into it is drawn, and a
	/// change to a caster it holds redraws only the cells the caster covers. It holds static and settled casters
	/// (<see cref="SettledCasters"/>); each frame it's unwrapped into the cascade and only the casters that move are drawn on
	/// top. Off: the last cascade is fit to the view and drawn whole every frame, like the others (and native's).
	/// </summary>
	internal static bool FarCascadeCache { get; set; } = true;

	/// <summary>
	/// A check (the lab's <c>-capture-walk</c>): draw the far cascade's cache whole every frame, at the window it would have
	/// kept, so a frame drawn from strips can be compared with one drawn whole.
	/// </summary>
	internal static bool FarCacheRedrawWhole { get; set; }

	/// <summary>
	/// This view's far cascade cache, for the shadow map debug view; null when its last cascade isn't cached.
	/// </summary>
	internal ShadowMap FarCacheMap { get; private set; }

	/// <summary>
	/// How far from the camera the cached far cascade shadows, as a fraction of <see cref="CascadeDistance"/>: about where the
	/// last cascade fit to a 90 degree view ends, which shrinks the shadow distance for its field of view.
	/// </summary>
	internal const float FarCacheDistanceScale = 0.6f;

	/// <summary>
	/// Cells along each side of the cached far cascade: the grid its centre snaps to and the unit it's redrawn in.
	/// </summary>
	internal const int FarCacheCells = 8;

	/// <summary>
	/// How far the sun turns, in degrees, before the cached far cascade is drawn again facing its new way. A 2,000 unit tall
	/// caster's shadow moves under one of its texels.
	/// </summary>
	const float FarCacheSunDegrees = 0.25f;

	/// <summary>
	/// How long one camera's cache is kept without being used.
	/// </summary>
	const float FarCacheEvictionTime = 2.0f;

	/// <summary>
	/// More separate regions than this in a frame and the whole cache is drawn instead.
	/// </summary>
	const int FarCacheMaxRegions = 8;

	/// <summary>
	/// The cache's depth runs this far either side of the plane across the sun through the world's origin, the same for every
	/// window, so a texel drawn in one frame means the same in the next wherever the camera is (about 0.02 units of float
	/// precision at worst over the whole range).
	/// </summary>
	internal const float FarCacheDepthRange = 131072f;

	/// <summary>
	/// One camera's cached far cascade.
	/// </summary>
	internal sealed class FarCascade
	{
		internal WeakReference<object> Camera;
		internal ShadowMap Cache;

		/// <summary>
		/// A cleared cell, copied over a region before it's drawn again.
		/// </summary>
		internal ShadowMap Blank;
		internal ulong BlankFrame;

		/// <summary>
		/// Whether <see cref="Cache"/> holds what the rest describes.
		/// </summary>
		internal bool Valid;

		// What it was drawn for
		internal object Light;
		internal float Side;
		internal Rotation Rotation;
		internal Vector3 Direction;
		internal StaticFilter Filter;
		internal uint[] RenderTags, ExcludeTags;
		internal ulong SubWorlds;
		internal RenderWorldChanges Changes;
		internal long StaticRevision, SettledRevision;

		/// <summary>
		/// The cell the window it holds is centred on, in light space.
		/// </summary>
		internal long CellX, CellY;

		/// <summary>
		/// The <see cref="SettledCasters"/> stamp it was drawn at: casters that moved after it are drawn on top each frame.
		/// </summary>
		internal ulong SettledStamp, RestlessStamp;

		internal float DrawnAt;
		internal ulong DrawnFrame;
		internal ShadowMapper DrawnBy;
		internal float LastUsed;

		/// <summary>
		/// The casters drawn on top last frame that have settled since: drawn again, the cache would hold them.
		/// </summary>
		internal readonly CasterCount NewlySettled = new();

		/// <summary>
		/// Whole redraws, and cells redrawn in parts, for tests and the lab.
		/// </summary>
		internal int Draws, CellsDrawn;
	}

	/// <summary>
	/// A world's change rings the cache was drawn against.
	/// </summary>
	internal readonly record struct RenderWorldChanges( StaticCasterChanges Static, StaticCasterChanges Settled );

	/// <summary>
	/// Cells of the cached far cascade to draw this frame, [X0, X1) by [Y0, Y1) in light-space cells.
	/// </summary>
	readonly record struct CellRect( long X0, long Y0, long X1, long Y1 )
	{
		public bool Empty => X1 <= X0 || Y1 <= Y0;
		public long Cells => Empty ? 0 : (X1 - X0) * (Y1 - Y0);
		public CellRect Clip( CellRect o ) => new( Math.Max( X0, o.X0 ), Math.Max( Y0, o.Y0 ), Math.Min( X1, o.X1 ), Math.Min( Y1, o.Y1 ) );
	}

	/// <summary>
	/// What <see cref="FitFarCascade"/> decided for <see cref="RenderFarCascade"/>.
	/// </summary>
	struct FarFit
	{
		public bool Whole;
		public float Grid, Near, Range;
		public int Regions;
	}

	static readonly List<FarCascade> FarCascades = new();
	readonly List<CellRect> farRegions = new();
	readonly List<(Vector3 Min, Vector3 Max)> farChanges = new();

	/// <summary>
	/// Each camera's cached far cascade, for tests and the lab.
	/// </summary>
	internal static IReadOnlyList<FarCascade> FarCaches => FarCascades;

	/// <summary>
	/// The camera's cached far cascade, made if it has none.
	/// </summary>
	FarCascade FarCascadeFor( object camera, int resolution )
	{
		FarCascade found = null;
		foreach ( var far in FarCascades )
		{
			if ( far.Camera.TryGetTarget( out var c ) && c == camera ) { found = far; break; }
		}

		if ( found is null )
		{
			found = new FarCascade { Camera = new WeakReference<object>( camera ) };
			FarCascades.Add( found );
		}

		if ( found.Cache is null || found.Cache.Resolution != resolution )
		{
			found.Cache?.Dispose();
			found.Blank?.Dispose();
			found.Cache = new ShadowMap( resolution, false, TextureFormat.D32 );
			found.Blank = new ShadowMap( Math.Max( resolution / FarCacheCells, 1 ), false, TextureFormat.D32 );
			found.BlankFrame = 0;
			found.Valid = false;
		}

		found.LastUsed = RealTime.Now;
		return found;
	}

	/// <summary>
	/// Fit the camera's cached far cascade for this frame and work out which of its cells to draw: all of them when it's new,
	/// the sun has turned <see cref="FarCacheSunDegrees"/>, its settings changed, the camera moved a whole window or cast
	/// casters have settled; otherwise the strip the camera's move brought in, and the cells changed casters cover.
	/// </summary>
	Cascade FitFarCascade( FarCascade far, in ShadowLight light, StaticFilter filter, RenderWorldChanges changes, out FarFit fit )
	{
		var distance = CascadeDistance * FarCacheDistanceScale;

		// The camera can be half a cell from the centre on each axis, and a sphere of the distance around it must fit
		var side = distance * 2.0f / (1.0f - 1.0f / FarCacheCells);
		var grid = side / FarCacheCells;

		var renderTags = View.RenderTags ?? [];
		var excludeTags = View.ExcludeTags ?? [];

		var whole = !far.Valid || far.Light != light.Key || far.Side != side || far.Filter != filter || far.SubWorlds != View.SubWorlds
			|| !SameTags( far.RenderTags, renderTags ) || !SameTags( far.ExcludeTags, excludeTags ) || far.Changes != changes;

		// The sun as it was drawn, until it has turned enough to show
		var turned = Vector3.Dot( far.Direction, light.Direction ) < MathF.Cos( FarCacheSunDegrees * MathF.PI / 180.0f );
		whole |= turned;
		var rotation = whole ? light.Direction.EulerAngles.ToRotation() : far.Rotation;

		Vector3 right = rotation.Right, up = rotation.Up, forward = rotation.Forward;
		var camera = View.CameraPosition;
		var cellX = (long)MathF.Round( Vector3.Dot( camera, right ) / grid );
		var cellY = (long)MathF.Round( Vector3.Dot( camera, up ) / grid );

		whole |= FarCacheRedrawWhole;
		whole |= Math.Abs( cellX - far.CellX ) >= FarCacheCells || Math.Abs( cellY - far.CellY ) >= FarCacheCells;

		// Casters drawn on top every frame have settled: draw them into the cache, but not more often than they settle
		whole |= far.NewlySettled.Value > 0 && RealTime.Now - far.DrawnAt >= SettledCasters.Seconds;

		const int half = FarCacheCells / 2;
		var window = new CellRect( cellX - half, cellY - half, cellX + half, cellY + half );
		farRegions.Clear();
		if ( !whole )
		{
			// The strip the move brought in: new columns over the whole window, then new rows over the columns it kept
			var old = new CellRect( far.CellX - half, far.CellY - half, far.CellX + half, far.CellY + half );
			if ( cellX > far.CellX ) farRegions.Add( window with { X0 = old.X1 } );
			if ( cellX < far.CellX ) farRegions.Add( window with { X1 = old.X0 } );
			var kept = window.Clip( old );
			if ( cellY > far.CellY ) farRegions.Add( kept with { Y0 = old.Y1 } );
			if ( cellY < far.CellY ) farRegions.Add( kept with { Y1 = old.Y0 } );

			// Cells changed casters cover, where they were and are
			whole |= !AddChangedCells( changes.Static, far.StaticRevision, window, right, up, grid );
			whole |= !AddChangedCells( changes.Settled, far.SettledRevision, window, right, up, grid );
			whole |= farRegions.Count > FarCacheMaxRegions;
		}

		if ( whole )
		{
			farRegions.Clear();
			farRegions.Add( window );
			far.Valid = true;
			far.Light = light.Key;
			far.Side = side;
			far.Rotation = rotation;
			far.Direction = light.Direction;
			far.Filter = filter;
			far.RenderTags = renderTags.Length == 0 ? [] : (uint[])renderTags.Clone();
			far.ExcludeTags = excludeTags.Length == 0 ? [] : (uint[])excludeTags.Clone();
			far.SubWorlds = View.SubWorlds;
			far.Changes = changes;
			far.SettledStamp = SettledCasters.Stamp;
			far.RestlessStamp = SettledCasters.RestlessStamp;
			far.DrawnAt = RealTime.Now;
			far.Draws++;
		}

		if ( farRegions.Count > 0 )
		{
			far.DrawnFrame = FrameCount;
			far.DrawnBy = this;
		}

		far.CellX = cellX;
		far.CellY = cellY;
		if ( changes.Static is { } s ) far.StaticRevision = s.Revision;
		if ( changes.Settled is { } t ) far.SettledRevision = t.Revision;

		// One depth encoding for every window (FarCacheDepthRange)
		const float near = -FarCacheDepthRange;
		const float range = FarCacheDepthRange * 2.0f;
		fit = new FarFit { Whole = whole, Grid = grid, Near = near, Range = range, Regions = farRegions.Count };

		return new Cascade
		{
			Origin = right * (cellX * grid) + up * (cellY * grid) + forward * near,
			Angles = rotation.Angles(),
			Near = 0f,
			Far = range,
			Width = side,
			Height = side,
			SphereCenter = camera,
			SphereRadius = distance,
		};
	}

	/// <summary>
	/// Add the window's cells that changes since <paramref name="since"/> cover to <see cref="farRegions"/>; false when the
	/// changes are too old to know.
	/// </summary>
	bool AddChangedCells( StaticCasterChanges changes, long since, CellRect window, Vector3 right, Vector3 up, float grid )
	{
		if ( changes is null || changes.Revision == since ) return true;
		if ( !changes.ChangesSince( since, farChanges ) ) return false;

		foreach ( var (min, max) in farChanges )
		{
			// The box's extent across the light, in cells
			var center = (min + max) * 0.5f;
			var extents = (max - min) * 0.5f;
			var x = Vector3.Dot( center, right );
			var y = Vector3.Dot( center, up );
			var ex = MathF.Abs( extents.x * right.x ) + MathF.Abs( extents.y * right.y ) + MathF.Abs( extents.z * right.z );
			var ey = MathF.Abs( extents.x * up.x ) + MathF.Abs( extents.y * up.y ) + MathF.Abs( extents.z * up.z );
			var cells = new CellRect( (long)MathF.Floor( (x - ex) / grid ), (long)MathF.Floor( (y - ey) / grid ), (long)MathF.Floor( (x + ex) / grid ) + 1, (long)MathF.Floor( (y + ey) / grid ) + 1 ).Clip( window );
			if ( !cells.Empty ) farRegions.Add( cells );
		}

		return true;
	}

	/// <summary>
	/// Turn the last cascade's view into the cached far cascade's: the cells to draw drawn into the cache first, each part
	/// that doesn't wrap around a view of its own, then the cache unwrapped into the cascade with the casters that move drawn
	/// on top.
	/// </summary>
	void RenderFarCascade( FarCascade far, ref ShadowViewDesc view, in FarFit fit, bool baked )
	{
		view.Rotation = far.Rotation;
		view.TexelSized = true;
		view.TexelLods = true;

		var resolution = far.Cache.Resolution;
		var cellTexels = resolution / FarCacheCells;
		Vector3 right = far.Rotation.Right, up = far.Rotation.Up, forward = far.Rotation.Forward;

		if ( fit.Regions > 0 && far.BlankFrame == 0 )
		{
			// The cleared cell the regions start from, cleared once
			Renderer.RenderShadowView( new ShadowViewDesc
			{
				Name = "CSM Far Blank",
				Rotation = far.Rotation,
				Orthographic = true,
				Width = 1,
				Height = 1,
				ZFar = 1,
				Resolution = far.Blank.Resolution,
				Target = far.Blank,
				StaticFilter = StaticFilter.Nothing,
			} );
			far.BlankFrame = FrameCount;
		}

		var region = 0;
		foreach ( var cells in farRegions )
		{
			// Split where the cache wraps, at multiples of its cells
			for ( var x0 = cells.X0; x0 < cells.X1; )
			{
				var x1 = Math.Min( cells.X1, FloorTo( x0, FarCacheCells ) + FarCacheCells );
				for ( var y0 = cells.Y0; y0 < cells.Y1; )
				{
					var y1 = Math.Min( cells.Y1, FloorTo( y0, FarCacheCells ) + FarCacheCells );
					var part = view;
					part.Name = "CSM Far Cache";
					part.Target = far.Cache;
					part.Region = ++region;
					part.StaticFilter = baked ? StaticFilter.SettledNoStatic : StaticFilter.Settled;
					part.SettledStamp = far.SettledStamp;
					part.RestlessStamp = far.RestlessStamp;
					part.Position = right * ((x0 + x1) * 0.5f * fit.Grid) + up * ((y0 + y1) * 0.5f * fit.Grid) + forward * fit.Near;
					part.Width = (x1 - x0) * fit.Grid;
					part.Height = (y1 - y0) * fit.Grid;
					part.Viewport = new Rect( Wrap( x0 ) * cellTexels, Wrap( -y1 ) * cellTexels, (x1 - x0) * cellTexels, (y1 - y0) * cellTexels );
					part.Blank = far.Blank;
					part.CacheOffset = default;
					Renderer.RenderShadowView( part );
					if ( !fit.Whole ) far.CellsDrawn += (int)((x1 - x0) * (y1 - y0));
					y0 = y1;
				}

				x0 = x1;
			}
		}

		// The window's top left cell, wrapped, is where the cascade starts in the cache
		const int half = FarCacheCells / 2;
		view.CachedStatic = far.Cache;
		view.CacheOffset = new Vector2Int( (int)Wrap( far.CellX - half ) * cellTexels, (int)Wrap( -(far.CellY + half) ) * cellTexels );
		view.StaticFilter = StaticFilter.Unsettled;
		view.SettledStamp = far.SettledStamp;
		view.RestlessStamp = far.RestlessStamp;
		far.NewlySettled.Value = 0;
		view.NewlySettled = far.NewlySettled;
	}

	static long Wrap( long cell ) => ((cell % FarCacheCells) + FarCacheCells) % FarCacheCells;

	static long FloorTo( long value, long step ) => value - (((value % step) + step) % step);

	/// <summary>
	/// Forget the far cascades a frame that was never submitted drew.
	/// </summary>
	void AbandonFarCascades()
	{
		foreach ( var far in FarCascades )
		{
			if ( far.DrawnBy == this && far.DrawnFrame == FrameCount ) far.Valid = false;
			if ( far.BlankFrame == FrameCount ) far.BlankFrame = 0;
		}
	}

	/// <summary>
	/// Drop the caches of cameras that are gone or haven't drawn for a while.
	/// </summary>
	static void EvictFarCascades( float now )
	{
		for ( int i = FarCascades.Count - 1; i >= 0; i-- )
		{
			var far = FarCascades[i];
			if ( far.Camera.TryGetTarget( out _ ) && now - far.LastUsed < FarCacheEvictionTime ) continue;

			far.Cache?.Dispose();
			far.Blank?.Dispose();
			FarCascades.RemoveAt( i );
		}
	}

	static void ResetFarCascades()
	{
		foreach ( var far in FarCascades )
		{
			far.Cache?.Dispose();
			far.Blank?.Dispose();
		}

		FarCascades.Clear();
	}
}
