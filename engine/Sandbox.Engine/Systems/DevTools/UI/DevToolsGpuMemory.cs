using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// Video memory: what the renderer uses against what the driver allows, the texture pool, and - when asked for -
/// everything allocated on the GPU and each block the allocator holds (<see cref="GpuMemoryMap"/>).
/// </summary>
internal sealed class DevToolsVramTab : DevToolsTab
{
	public override string TabId => "vram";
	public override string Title => "VRAM";

	readonly Label _bySize, _byBlock;
	readonly Label _capture;
	readonly DevToolsFacts _facts;
	readonly DevToolsFacts _captured;
	readonly DevToolsGpuMemoryView _view;
	readonly Label _hint;
	double _followUntil;

	public DevToolsVramTab()
	{
		// The same bar as the network tab: which view on the left, what to do on the right
		var toolbar = AddChild( new Panel( null, "net-toolbar span-all" ) );
		var segments = toolbar.AddChild( new Panel( null, "segments" ) );
		_bySize = segments.AddChild( new Label( "By size", "segment" ) );
		_bySize.Tooltip = "Every allocation as big as its memory: what the memory went on";
		_bySize.AddEventListener( "onclick", () => SetBySize( true ) );
		_byBlock = segments.AddChild( new Label( "By block", "segment" ) );
		_byBlock.Tooltip = "Each block the allocator holds and what's in it: what keeps memory from being released";
		_byBlock.AddEventListener( "onclick", () => SetBySize( false ) );

		toolbar.AddChild( new Panel( null, "grow" ) );
		_capture = Button( toolbar, "fiber_manual_record", "Capture what's allocated on the GPU now. Walks the whole allocator, so it's only done when asked", Capture );
		_capture.AddClass( "record" );
		_capture.AddClass( "on" );

		// Not something an icon says by itself
		var defrag = toolbar.AddChild( new Panel( null, "head-button" ) );
		defrag.AddChild( new Label( "compress", "button-icon" ) );
		defrag.AddChild( new Label( "Defrag" ) );
		defrag.Tooltip = "Pack the blocks now and release the ones that empty. Takes a few frames, and hitches while it does";
		defrag.AddEventListener( "onclick", Defrag );

		_facts = AddChild( new DevToolsFacts() );
		_facts.AddClass( "span-all" );
		_captured = AddChild( new DevToolsFacts() );
		_captured.AddClass( "span-all" );
		_captured.AddClass( "hidden" );

		var body = AddChild( new Panel( null, "section span-all" ) );
		_view = body.AddChild( new DevToolsGpuMemoryView() );
		_hint = body.AddChild( new Label( "", "hint" ) );

		SetBySize( true );
	}

	static Label Button( Panel parent, string icon, string tooltip, Action onClick )
	{
		var button = parent.AddChild( new Label( icon, "icon-button" ) );
		button.Tooltip = tooltip;
		button.AddEventListener( "onclick", onClick );
		return button;
	}

	public override void Refresh( DevTools.Summary s )
	{
		var stats = FrameStats.Current;
		var budget = GpuProfilerStats.VideoMemoryBudget;
		var used = GpuProfilerStats.VideoMemoryUsed;

		_facts.Set( "In use", budget > 0 ? DevToolsFormat.BytesOf( used, budget ) : "-", budget > 0 && used > budget * 0.8 ? DevTools.Severity.Warning : null,
			"What the renderer has in textures and buffers, of what the driver allows this process. Less than the driver counts: that includes memory held but not in use" );
		_facts.Set( "Texture pool", stats.TexturePoolLimitBytes > 0
			? DevToolsFormat.BytesOf( stats.TexturePoolUsedBytes, stats.TexturePoolLimitBytes )
			: DevToolsFormat.Bytes( stats.TexturePoolUsedBytes ), tooltip: "Streamed textures, of the most they may take. Streaming drops mips when it's full" );
		_facts.Set( "Pinned textures", DevToolsFormat.Bytes( stats.TexturePoolNonEvictableBytes ), tooltip: "Textures streaming can't drop" );
		_facts.Set( "Streaming", $"{stats.PendingStreamingRequests} pending", stats.PendingStreamingRequests > 32 ? DevTools.Severity.Warning : null );

		// Packing runs over a few frames: keep capturing for a while after it's asked for, to show it happen
		var following = RealTime.Now < _followUntil;
		_capture.SetClass( "active", following );
		if ( following && RealTime.Now - _view.CapturedAt > 0.5 ) Capture();
	}

	void Defrag()
	{
		if ( !Graphics.IsAvailable ) return;

		g_pRenderDevice.DefragmentGpuMemory();
		_followUntil = RealTime.Now + 6;
		Capture();
	}

	void Capture()
	{
		var map = GpuMemoryMap.Capture();
		_view.Set( map );
		_captured.SetClass( "hidden", map is null );
		UpdateHint();
		if ( map is null ) return;

		// Unused is what packing could give back; it only matters once it's a lot
		var wasteful = map.UnusedBytes > 384L * 1024 * 1024 && map.UnusedBytes > map.BlockBytes / 4;
		_captured.Set( "Driver", map.DriverBudget > 0 ? DevToolsFormat.BytesOf( map.DriverUsage, map.DriverBudget ) : "-",
			map.DriverBudget > 0 && map.DriverUsage > map.DriverBudget * 0.9 ? DevTools.Severity.Warning : null, "What the driver says this process uses, of what it may. Shrinks when other apps take video memory" );
		_captured.Set( "Blocks", $"{map.Blocks.Count}", tooltip: "Memory comes from the driver a block at a time, and a block only goes back once it's empty" );
		_captured.Set( "In blocks", DevToolsFormat.Bytes( map.BlockBytes ), tooltip: "What the blocks hold from the driver, used or not" );
		_captured.Set( "Unused", DevToolsFormat.Bytes( map.UnusedBytes ), wasteful ? DevTools.Severity.Warning : null, "Held in blocks with nothing allocated in it. Defragmenting packs the blocks and releases the ones that empty" );
		_captured.Set( "Dedicated", DevToolsFormat.Bytes( map.DedicatedBytes ), tooltip: $"{map.Dedicated.Count:N0} allocations with memory of their own, mostly render targets. Released the moment they're freed" );
	}

	void SetBySize( bool bySize )
	{
		_view.BySize = bySize;
		_bySize.SetClass( "active", bySize );
		_byBlock.SetClass( "active", !bySize );
		UpdateHint();
	}

	void UpdateHint()
	{
		if ( !Graphics.IsAvailable ) _hint.Text = "Not available with this renderer";
		else if ( _view.CapturedAt <= 0 ) _hint.Text = "Nothing captured yet. Press record to capture what's allocated on the GPU";
		else if ( _view.BySize ) _hint.Text = "Every allocation, as big as its memory. Hover for its name and where it lives";
		else _hint.Text = "A block goes back to the system only once it's empty. Emptiest first; hover for what's in one. Render targets have memory of their own, in the last row";
	}
}

/// <summary>
/// A captured <see cref="GpuMemoryMap"/>, drawn one of two ways. By block: a block to a row, emptiest first, with what's
/// allocated in it by kind and the gaps between, then a row of the allocations with memory of their own, which is where
/// the render targets are - for seeing what keeps a block from being released. By size: every allocation as a
/// rectangle as big as its memory, grouped by kind - for seeing what the memory went on. Hovering names what's under
/// the cursor.
/// </summary>
internal sealed class DevToolsGpuMemoryView : Panel
{
	const float LegendH = 18;
	const float RowH = 14;
	const float RowGap = 3;
	const float Gutter = 134;
	const float Right = 10;
	const float DedicatedGap = 6;

	const float SizeMapH = 320;
	const float GroupHeadH = 15;

	GpuMemoryMap _map;
	bool _bySize = true;

	/// <summary>
	/// One rectangle of the size map: an allocation, or the ones too small to draw taken together.
	/// </summary>
	readonly record struct Cell( Rect Rect, string Name, long Size, GpuMemoryMap.Kind Kind, GpuMemoryMap.Block Block, int Count );

	readonly record struct Group( Rect Rect, GpuMemoryMap.Kind Kind, long Size, int Count );

	readonly List<Cell> _cells = new();
	readonly List<Group> _groups = new();
	float _cellsWidth;

	public DevToolsGpuMemoryView()
	{
		AddClass( "overlay-view" );
		Style.Height = 0;
	}

	public void Set( GpuMemoryMap map )
	{
		_map = map;
		_cellsWidth = 0;
		UpdateHeight();
	}

	public double CapturedAt => _map?.CapturedAt ?? 0;

	/// <summary>
	/// Draw by size rather than by block.
	/// </summary>
	public bool BySize
	{
		get => _bySize;
		set
		{
			_bySize = value;
			UpdateHeight();
		}
	}

	void UpdateHeight()
	{
		if ( _map is null ) Style.Height = 0;
		else if ( _bySize ) Style.Height = SizeMapH;
		else Style.Height = LegendH + (_map.Blocks.Count + (_map.Dedicated.Count > 0 ? 1 : 0)) * (RowH + RowGap) + DedicatedGap + 4;
	}

	internal static Color ColorOf( GpuMemoryMap.Kind kind ) => kind switch
	{
		GpuMemoryMap.Kind.Texture => DevToolsStyles.Series[0],
		GpuMemoryMap.Kind.RenderTarget => DevToolsStyles.Series[2],
		GpuMemoryMap.Kind.Buffer => DevToolsStyles.Series[4],
		_ => DevToolsStyles.Series[6],
	};

	public override void OnDraw( Painter painter )
	{
		if ( _map is null ) return;

		var scale = ScaleToScreen;
		var w = Box.Rect.Width / scale;
		var h = Box.Rect.Height / scale;
		if ( w < Gutter + Right + 20 ) return;

		using var state = painter.Scope();
		painter.Scale( scale );
		painter.Stroke = Stroke.None;

		if ( _bySize ) DrawBySize( painter, w, h );
		else DrawByBlock( painter, w );
	}

	//
	// By block
	//

	void DrawByBlock( Painter painter, float w )
	{
		var plotW = w - Gutter - Right;
		DrawLegend( painter );

		var y = LegendH;
		foreach ( var block in _map.Blocks )
		{
			// Nearly empty, and something is still in it: this is the memory to get back
			var sparse = block.Used > 0 && block.Fill < 0.25f;
			DrawText( painter, DevToolsFormat.Bytes( block.Size ), new Rect( 0, y, 56, RowH ), TextFlag.RightCenter, DevToolsStyles.Dim );
			DrawText( painter, $"{block.Fill * 100:0}%", new Rect( 58, y, 34, RowH ), TextFlag.RightCenter, sparse ? DevToolsStyles.Warning : DevToolsStyles.Text );
			if ( !block.Movable ) DrawText( painter, "fixed", new Rect( 98, y, 32, RowH ), TextFlag.LeftCenter, DevToolsStyles.Faint, 9 );

			painter.Fill = DevToolsStyles.Plot;
			painter.Rect( new Rect( Gutter, y, plotW, RowH ) );

			var perByte = plotW / MathF.Max( 1, block.Size );
			foreach ( var run in block.Runs )
			{
				painter.Fill = ColorOf( run.Kind ).WithAlpha( 0.85f );
				painter.Rect( new Rect( Gutter + run.Offset * perByte, y, MathF.Max( 0.5f, run.Size * perByte ), RowH ) );
			}

			y += RowH + RowGap;
		}

		if ( _map.Dedicated.Count > 0 )
		{
			y += DedicatedGap;
			DrawText( painter, DevToolsFormat.Bytes( _map.DedicatedBytes ), new Rect( 0, y, 56, RowH ), TextFlag.RightCenter, DevToolsStyles.Dim );
			DrawText( painter, "dedicated", new Rect( 64, y, 66, RowH ), TextFlag.LeftCenter, DevToolsStyles.Faint, 9 );

			// No free space here: each has exactly the memory it needs, so they're laid end to end, biggest first
			var perByte = plotW / MathF.Max( 1, _map.DedicatedBytes );
			var x = Gutter;
			foreach ( var a in _map.Dedicated )
			{
				var width = a.Size * perByte;
				painter.Fill = ColorOf( a.Kind ).WithAlpha( 0.85f );
				painter.Rect( new Rect( x, y, MathF.Max( 0.5f, width - 0.75f ), RowH ) );
				x += width;
			}
		}

		if ( HasHovered ) HoverByBlock( painter, w, plotW );
	}

	void DrawLegend( Painter painter )
	{
		var x = Gutter;
		foreach ( var kind in Enum.GetValues<GpuMemoryMap.Kind>() )
		{
			var text = GpuMemoryMap.Describe( kind );
			painter.Fill = ColorOf( kind );
			painter.Rect( new Rect( x, LegendH / 2 - 6, 8, 8 ) );
			DrawText( painter, text, new Rect( x + 13, 0, text.Length * 6.4f + 4, LegendH - 4 ), TextFlag.LeftCenter, DevToolsStyles.Dim, 10 );
			x += text.Length * 6.4f + 28;
		}

		painter.Fill = DevToolsStyles.Plot;
		painter.Rect( new Rect( x, LegendH / 2 - 6, 8, 8 ) );
		DrawText( painter, "free", new Rect( x + 13, 0, 40, LegendH - 4 ), TextFlag.LeftCenter, DevToolsStyles.Dim, 10 );
	}

	/// <summary>
	/// What's under the cursor: an allocation by name, or how big the free space is.
	/// </summary>
	void HoverByBlock( Painter painter, float w, float plotW )
	{
		var m = MousePosition / ScaleToScreen;
		if ( m.y < LegendH || m.x < Gutter || m.x > Gutter + plotW ) return;

		var row = (int)((m.y - LegendH) / (RowH + RowGap));
		if ( row >= _map.Blocks.Count )
		{
			// The dedicated row
			var rowTop = LegendH + _map.Blocks.Count * (RowH + RowGap) + DedicatedGap;
			if ( _map.Dedicated.Count == 0 || m.y < rowTop || m.y > rowTop + RowH ) return;

			var at = (long)((m.x - Gutter) / plotW * _map.DedicatedBytes);
			long start = 0;
			var found = _map.Dedicated[^1];
			foreach ( var a in _map.Dedicated )
			{
				if ( at < start + a.Size ) { found = a; break; }
				start += a.Size;
			}

			DrawHoverBox( painter, m, w, NameOf( found.Name ), $"{DevToolsFormat.Bytes( found.Size )} {GpuMemoryMap.Describe( found.Kind )}, memory of its own" );
			return;
		}

		var block = _map.Blocks[row];
		var top = LegendH + row * (RowH + RowGap);
		var offset = (long)((m.x - Gutter) / plotW * block.Size);

		if ( block.At( offset ) is not { } hit )
		{
			DrawHoverBox( painter, m, w, "free", $"{DevToolsFormat.Bytes( block.Size - block.Used )} free in this block, {DevToolsFormat.Bytes( block.LargestFree )} the largest" );
			return;
		}

		var perByte = plotW / MathF.Max( 1, block.Size );
		Outline( painter, new Rect( Gutter + hit.Offset * perByte, top, MathF.Max( 1.5f, hit.Size * perByte ), RowH ) );
		DrawHoverBox( painter, m, w, NameOf( hit.Name ), $"{DevToolsFormat.Bytes( hit.Size )} {GpuMemoryMap.Describe( hit.Kind )}" );
	}

	//
	// By size
	//

	void DrawBySize( Painter painter, float w, float h )
	{
		if ( MathF.Abs( _cellsWidth - w ) > 1 ) Layout( new Rect( 0, 0, w, h ) );

		foreach ( var group in _groups )
		{
			var title = $"{GpuMemoryMap.Describe( group.Kind )} ({group.Count:N0})  {DevToolsFormat.Bytes( group.Size )}";
			painter.Fill = Shade( ColorOf( group.Kind ), 0.45f );
			painter.Rect( new Rect( group.Rect.Left, group.Rect.Top, group.Rect.Width - 1, GroupHeadH - 1 ) );
			FitText( painter, title, new Rect( group.Rect.Left + 4, group.Rect.Top, group.Rect.Width - 8, GroupHeadH - 1 ), DevToolsStyles.Bright, 10 );
		}

		var ink = Color.Parse( "#161616" ).Value;
		foreach ( var cell in _cells )
		{
			var r = cell.Rect;
			if ( r.Width < 1 || r.Height < 1 ) continue;

			// Cells of one kind are told apart by the gap between them, and a little by shade
			painter.Fill = Shade( ColorOf( cell.Kind ), cell.Count > 1 ? 0.6f : 0.85f + (cell.Size % 7) * 0.02f );
			painter.Rect( new Rect( r.Left, r.Top, MathF.Max( 0.5f, r.Width - 1 ), MathF.Max( 0.5f, r.Height - 1 ) ) );

			if ( r.Width < 36 || r.Height < 14 ) continue;
			var twoLines = r.Height >= 30;
			var name = cell.Count > 1 ? $"{cell.Count:N0} smaller" : ShortName( cell.Name );
			var text = cell.Count > 1 ? DevToolsStyles.Bright : ink;
			FitText( painter, name, new Rect( r.Left + 4, r.Top + (twoLines ? r.Height / 2 - 13 : 0), r.Width - 8, twoLines ? 13 : r.Height - 1 ), text, 10 );
			if ( twoLines ) FitText( painter, DevToolsFormat.Bytes( cell.Size ), new Rect( r.Left + 4, r.Top + r.Height / 2, r.Width - 8, 13 ), text.WithAlpha( 0.75f ), 10 );
		}

		if ( !HasHovered ) return;

		var m = MousePosition / ScaleToScreen;
		foreach ( var cell in _cells )
		{
			var r = cell.Rect;
			if ( m.x < r.Left || m.x >= r.Right || m.y < r.Top || m.y >= r.Bottom ) continue;

			Outline( painter, new Rect( r.Left, r.Top, MathF.Max( 1.5f, r.Width - 1 ), MathF.Max( 1.5f, r.Height - 1 ) ) );

			var kind = GpuMemoryMap.Describe( cell.Kind );
			if ( cell.Count > 1 )
				DrawHoverBox( painter, m, w, $"{cell.Count:N0} smaller {kind} allocations", $"{DevToolsFormat.Bytes( cell.Size )} together, each too small to draw" );
			else
				DrawHoverBox( painter, m, w, NameOf( cell.Name ), cell.Block is null
					? $"{DevToolsFormat.Bytes( cell.Size )} {kind}, memory of its own"
					: $"{DevToolsFormat.Bytes( cell.Size )} {kind}, in a {DevToolsFormat.Bytes( cell.Block.Size )} block {cell.Block.Fill * 100:0}% full" );
			return;
		}
	}

	/// <summary>
	/// Place every allocation: the kinds share out the whole area by their totals, then each kind's allocations share
	/// out its area, biggest first. Done when captured or resized, not every frame.
	/// </summary>
	void Layout( Rect bounds )
	{
		_cellsWidth = bounds.Width;
		_cells.Clear();
		_groups.Clear();

		var byKind = new Dictionary<GpuMemoryMap.Kind, List<(GpuMemoryMap.Allocation Allocation, GpuMemoryMap.Block Block)>>();
		void Add( GpuMemoryMap.Allocation a, GpuMemoryMap.Block block )
		{
			if ( a.Size <= 0 ) return;
			if ( !byKind.TryGetValue( a.Kind, out var list ) ) byKind[a.Kind] = list = new();
			list.Add( (a, block) );
		}

		foreach ( var block in _map.Blocks )
			foreach ( var a in block.Allocations ) Add( a, block );
		foreach ( var a in _map.Dedicated ) Add( a, null );

		var kinds = byKind.Select( x => (Kind: x.Key, Items: x.Value, Size: x.Value.Sum( i => i.Allocation.Size )) ).OrderByDescending( x => x.Size ).ToList();
		var total = kinds.Sum( x => (double)x.Size );
		if ( total <= 0 || bounds.Width < 8 || bounds.Height < 8 ) return;

		var area = (double)bounds.Width * bounds.Height;
		var rects = new List<Rect>();
		Squarify( kinds.Select( x => x.Size / total * area ).ToList(), bounds, rects );

		var cellRects = new List<Rect>();
		var areas = new List<double>();
		for ( int g = 0; g < kinds.Count; g++ )
		{
			var (kind, items, size) = kinds[g];
			var rect = rects[g];
			_groups.Add( new Group( rect, kind, size, items.Count ) );

			var inner = new Rect( rect.Left, rect.Top + GroupHeadH, rect.Width, rect.Height - GroupHeadH );
			if ( inner.Width < 4 || inner.Height < 4 ) continue;

			// Thousands are too small to see, so those go in one cell at the end
			items.Sort( ( a, b ) => b.Allocation.Size.CompareTo( a.Allocation.Size ) );
			var perByte = (double)inner.Width * inner.Height / size;
			var shown = items.Count;
			while ( shown > 0 && items[shown - 1].Allocation.Size * perByte < 48 ) shown--;
			var rest = items.Skip( shown ).Sum( x => x.Allocation.Size );

			// Keeps the list biggest first, which the layout needs
			var restAt = shown;
			if ( rest > 0 )
				while ( restAt > 0 && items[restAt - 1].Allocation.Size < rest ) restAt--;

			areas.Clear();
			for ( int i = 0; i < shown; i++ )
			{
				if ( rest > 0 && i == restAt ) areas.Add( rest * perByte );
				areas.Add( items[i].Allocation.Size * perByte );
			}
			if ( rest > 0 && restAt == shown ) areas.Add( rest * perByte );

			cellRects.Clear();
			Squarify( areas, inner, cellRects );

			var at = 0;
			for ( int i = 0; i < shown; i++ )
			{
				if ( rest > 0 && i == restAt ) _cells.Add( new Cell( cellRects[at++], null, rest, kind, null, items.Count - shown ) );
				_cells.Add( new Cell( cellRects[at++], items[i].Allocation.Name, items[i].Allocation.Size, kind, items[i].Block, 1 ) );
			}
			if ( rest > 0 && restAt == shown ) _cells.Add( new Cell( cellRects[at], null, rest, kind, null, items.Count - shown ) );
		}
	}

	/// <summary>
	/// A squarified treemap: fill the rect with one rect per area, in order, keeping them as near square as it can.
	/// Areas are biggest first and add up to the rect's. Each strip goes along the shorter side, and takes areas until
	/// another would make its worst shape worse.
	/// </summary>
	static void Squarify( List<double> areas, Rect rect, List<Rect> output )
	{
		int i = 0;
		while ( i < areas.Count )
		{
			var side = MathF.Min( rect.Width, rect.Height );
			if ( side <= 0.01f )
			{
				for ( ; i < areas.Count; i++ ) output.Add( new Rect( rect.Left, rect.Top, 0, 0 ) );
				return;
			}

			var start = i;
			double sum = 0, max = 0, min = double.MaxValue, worst = double.MaxValue;
			var sideSquared = (double)side * side;
			while ( i < areas.Count )
			{
				var a = Math.Max( areas[i], 0.0001 );
				var newSum = sum + a;
				var newMax = Math.Max( max, a );
				var newMin = Math.Min( min, a );
				var newWorst = Math.Max( sideSquared * newMax / (newSum * newSum), newSum * newSum / (sideSquared * newMin) );
				if ( i > start && newWorst > worst ) break;

				sum = newSum;
				max = newMax;
				min = newMin;
				worst = newWorst;
				i++;
			}

			var thickness = (float)(sum / side);
			var wide = rect.Width >= rect.Height;
			var along = wide ? rect.Top : rect.Left;
			for ( int k = start; k < i; k++ )
			{
				var length = (float)(Math.Max( areas[k], 0.0001 ) / thickness);
				output.Add( wide ? new Rect( rect.Left, along, thickness, length ) : new Rect( along, rect.Top, length, thickness ) );
				along += length;
			}

			rect = wide
				? new Rect( rect.Left + thickness, rect.Top, MathF.Max( 0, rect.Width - thickness ), rect.Height )
				: new Rect( rect.Left, rect.Top + thickness, rect.Width, MathF.Max( 0, rect.Height - thickness ) );
		}
	}

	//
	// Shared
	//

	static string NameOf( string name ) => string.IsNullOrEmpty( name ) ? "(unnamed)" : name;

	/// <summary>
	/// Names are paths. The file is what tells them apart in a small cell.
	/// </summary>
	static string ShortName( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return "(unnamed)";
		var slash = name.LastIndexOf( '/' );
		return slash >= 0 && slash < name.Length - 1 ? name[(slash + 1)..] : name;
	}

	static Color Shade( Color c, float by ) => new( c.r * by, c.g * by, c.b * by, 1 );

	static void Outline( Painter painter, Rect rect )
	{
		painter.Stroke = Stroke.Solid( DevToolsStyles.Bright.WithAlpha( 0.8f ), 1 );
		painter.Fill = Color.Transparent;
		painter.Rect( rect );
		painter.Stroke = Stroke.None;
	}

	void DrawHoverBox( Painter painter, Vector2 m, float w, string title, string detail )
	{
		// Names are paths: keep the end, which is what tells them apart
		const int MaxChars = 64;
		if ( title.Length > MaxChars ) title = "…" + title[^(MaxChars - 1)..];

		var boxW = MathF.Max( title.Length * 6.6f, detail.Length * 6.1f ) + 16;
		var h = Box.Rect.Height / ScaleToScreen;
		var box = new Rect( Math.Clamp( m.x + 14, 0, MathF.Max( 0, w - boxW - 4 ) ), m.y + 16 + 36 > h ? m.y - 44 : m.y + 16, boxW, 36 );
		painter.Fill = Color.Parse( "#1c1c1c" ).Value;
		painter.Stroke = Stroke.Solid( DevToolsStyles.Line, 1 );
		painter.Rect( box );
		painter.Stroke = Stroke.None;
		DrawText( painter, title, new Rect( box.Left + 8, box.Top + 3, box.Width - 16, 15 ), TextFlag.LeftCenter, DevToolsStyles.Bright, 11, "Inter" );
		DrawText( painter, detail, new Rect( box.Left + 8, box.Top + 18, box.Width - 16, 15 ), TextFlag.LeftCenter, DevToolsStyles.Dim, 10 );
	}

	/// <summary>
	/// Text cut to what fits the rect, and nothing once only a letter or two would show.
	/// </summary>
	static void FitText( Painter painter, string text, Rect rect, Color color, float size )
	{
		var chars = (int)(rect.Width / (size * 0.62f));
		if ( chars < 3 ) return;
		if ( text.Length > chars ) text = text[..(chars - 1)].TrimEnd() + "…";
		DrawText( painter, text, rect, TextFlag.LeftCenter, color, size );
	}

	static void DrawText( Painter painter, string text, Rect rect, TextFlag flags, Color color, float size = 10.5f, string font = "Roboto Mono" )
	{
		var scope = new TextRendering.Scope( text, color, size, font, 500 );
		DebugOverlay.DrawText( painter, scope, rect, flags );
	}
}
