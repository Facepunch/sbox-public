using Info = Editor.TextureResidencyInfo;

namespace Editor;

[Dock( "Editor", "GPU Resources", "memory" )]
public partial class GpuResourceViewer : Widget
{
	static readonly (Info.TextureCategory Flag, string Label, Color Color)[] Tags =
	[
		(Info.TextureCategory.RenderTarget, "RT",    new Color( 0.30f, 0.70f, 1.00f )),
		(Info.TextureCategory.DepthBuffer,  "Depth", new Color( 0.85f, 0.45f, 0.95f )),
		(Info.TextureCategory.UAV,          "UAV",   new Color( 1.00f, 0.65f, 0.25f )),
		(Info.TextureCategory.MSAA,         "MSAA",  new Color( 0.50f, 0.85f, 0.95f )),
		(Info.TextureCategory.Stale,        "Stale", new Color( 1.00f, 0.40f, 0.40f )),
	];

	static readonly Color StreamColor = new( 0.45f, 0.90f, 0.55f );

	ListView List;
	Label SummaryLabel;
	TexturePreviewPanel Preview;
	Widget StatsBar;

	enum ViewMode { List, Treemap }
	ViewMode _viewMode = ViewMode.List;

	List<Info> _all = new();
	List<Info> _filtered = new();
	string _search = "";
	Info.TextureDimension? _dimFilter;
	Info.TextureCategory _catFilter = Info.TextureCategory.Stale;
	bool _autoUpdate = true;
	RealTimeSince _refreshTimer;

	long _totalGpu, _totalDisk, _staleWaste, _otherTexturesMem;
	int _totalCount, _staleCount;
	long[] _catMemory = new long[Tags.Length];
	int[] _catCount = new int[Tags.Length];
	ulong _vmBudget, _vmUsage;

	Dictionary<Texture, Pixmap> _thumbs = new();
	HashSet<Texture> _thumbsLoading = new();

	const int ThumbMaxSize = 16;

	public GpuResourceViewer( Widget parent ) : base( parent )
	{
		MinimumSize = new Vector2( 600, 300 );
		Layout = Layout.Column();
		Layout.Spacing = 0;

		BuildToolbar();
		BuildBody();

		StatsBar = new Widget( this ) { FixedHeight = 56 };
		StatsBar.OnPaintOverride = PaintStatsBar;
		Layout.Add( StatsBar );
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		Preview?.Dispose();

		//_all.Clear();
		//_filtered.Clear();
	}

	void BuildToolbar()
	{
		var toolbar = new ToolBar( this, "GpuResourceViewerToolbar" );
		toolbar.SetIconSize( 16 );
		Layout.Add( toolbar );

		var search = new LineEdit( toolbar ) { PlaceholderText = "⌕  Filter resources...", MinimumWidth = 200 };
		search.TextChanged += t => { _search = t; Refresh(); };
		toolbar.AddWidget( search );
		toolbar.AddSeparator();

		var dim = new ComboBox( toolbar ) { MinimumWidth = 120 };
		dim.AddItem( "All Types", "filter_list", () => { _dimFilter = null; Refresh(); } );
		foreach ( var d in Enum.GetValues<Info.TextureDimension>() )
			dim.AddItem( d.ToString().TrimStart( '_' ), DimIcon( d ), () => { _dimFilter = d; Refresh(); } );
		toolbar.AddWidget( dim );
		toolbar.AddSeparator();

		foreach ( var (flag, label, color) in Tags )
		{
			var btn = new Button( label ) { IsToggle = true, IsChecked = !_catFilter.HasFlag( flag ), FixedHeight = 24 };
			btn.Toggled = () => { _catFilter = btn.IsChecked ? _catFilter & ~flag : _catFilter | flag; Refresh(); };
			btn.OnPaintOverride = () => PaintTagButton( btn, label, color );
			Paint.SetDefaultFont( 8, 600 );
			btn.FixedWidth = (int)(Paint.MeasureText( label ).x + 36);
			toolbar.AddWidget( btn );
		}

		var spacer = new Widget( toolbar ) { Layout = Layout.Row() };
		spacer.Layout.AddStretchCell( 1 );
		toolbar.AddWidget( spacer );

		var listBtn = new Button( "List" ) { ToolTip = "List view", FixedHeight = 24 };
		var treeBtn = new Button( "Treemap" ) { ToolTip = "Treemap view (by size)", FixedHeight = 24 };
		listBtn.Clicked = () => { SetViewMode( ViewMode.List ); listBtn.Update(); treeBtn.Update(); };
		treeBtn.Clicked = () => { SetViewMode( ViewMode.Treemap ); listBtn.Update(); treeBtn.Update(); };
		listBtn.OnPaintOverride = () => PaintViewButton( listBtn, "view_list", "List", _viewMode == ViewMode.List );
		treeBtn.OnPaintOverride = () => PaintViewButton( treeBtn, "grid_view", "Treemap", _viewMode == ViewMode.Treemap );
		Paint.SetDefaultFont( 8, 600 );
		listBtn.FixedWidth = (int)(Paint.MeasureText( "List" ).x + 38);
		treeBtn.FixedWidth = (int)(Paint.MeasureText( "Treemap" ).x + 38);
		toolbar.AddWidget( listBtn );
		toolbar.AddWidget( treeBtn );
		toolbar.AddSeparator();

		SummaryLabel = new Label( toolbar ) { Color = Theme.Text.WithAlpha( 0.6f ) };
		toolbar.AddWidget( SummaryLabel );
		toolbar.AddSeparator();

		toolbar.AddWidget( new IconButton( "autorenew" ) { IsToggle = true, IsActive = _autoUpdate, ToolTip = "Auto-update", IconSize = 16, OnToggled = on => _autoUpdate = on } );
		toolbar.AddWidget( new IconButton( "refresh", FetchData ) { ToolTip = "Refresh now", IconSize = 16 } );
	}

	static bool PaintTagButton( Button btn, string label, Color color )
	{
		var r = btn.LocalRect.Shrink( 2 );
		var on = btn.IsChecked;
		var hover = Paint.HasMouseOver;

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( on ? color.WithAlpha( hover ? 0.25f : 0.15f ) : Theme.ControlBackground.Lighten( hover ? 0.15f : 0 ) );
		Paint.DrawRect( r, r.Height * 0.5f );

		Paint.SetBrush( on ? color : Theme.Text.WithAlpha( 0.2f ) );
		Paint.DrawRect( new Rect( r.Left + 8, r.Center.y - 4, 8, 8 ), 4 );

		Paint.ClearBrush();
		Paint.SetPen( on ? color : Theme.Text.WithAlpha( 0.4f ) );
		Paint.SetDefaultFont( 8, 600 );
		Paint.DrawText( new Rect( r.Left + 20, r.Top, r.Width - 28, r.Height ), label, TextFlag.LeftCenter );

		return true;
	}

	static bool PaintViewButton( Button btn, string icon, string label, bool active )
	{
		var r = btn.LocalRect.Shrink( 2 );
		var hover = Paint.HasMouseOver;

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( active ? Theme.Primary.WithAlpha( hover ? 0.9f : 0.8f ) : Theme.ControlBackground.Lighten( hover ? 0.15f : 0 ) );
		Paint.DrawRect( r, 3 );

		var fg = active ? Color.White : Theme.Text.WithAlpha( hover ? 0.9f : 0.55f );
		Paint.SetPen( fg );
		Paint.DrawIcon( new Rect( r.Left + 7, r.Top, 16, r.Height ), icon, 15, TextFlag.LeftCenter );

		Paint.SetPen( fg );
		Paint.SetDefaultFont( 8, 600 );
		Paint.DrawText( new Rect( r.Left + 26, r.Top, r.Width - 30, r.Height ), label, TextFlag.LeftCenter );

		return true;
	}

	void BuildBody()
	{
		var splitter = Layout.Add( new Splitter( this ), 1 );
		splitter.IsHorizontal = true;

		var leftPane = new Widget( this ) { Layout = Layout.Column() };
		leftPane.Layout.Spacing = 0;

		List = new ListView( leftPane ) { ItemSize = new Vector2( 0, 36 ), ItemSpacing = 0, Margin = 0 };
		List.ItemPaint = PaintRow;
		List.ItemSelected = o => { if ( o is Info info ) Preview.SetTexture( info ); };
		leftPane.Layout.Add( List, 1 );

		Treemap = new TreemapView( leftPane, this );
		leftPane.Layout.Add( Treemap, 1 );
		Treemap.Visible = false;

		splitter.AddWidget( leftPane );
		splitter.SetStretch( 0, 3 );

		Preview = new TexturePreviewPanel( this );
		splitter.AddWidget( Preview );
		splitter.SetStretch( 1, 1 );
	}

	void SetViewMode( ViewMode mode )
	{
		if ( _viewMode == mode ) return;
		_viewMode = mode;
		List.Visible = mode == ViewMode.List;
		Treemap.Visible = mode == ViewMode.Treemap;
		Treemap.SetItems( _filtered );
	}

	bool PaintStatsBar()
	{
		var rect = StatsBar.LocalRect;
		Paint.SetBrushAndPen( Theme.SidebarBackground, Color.Transparent, 0 );
		Paint.DrawRect( rect );
		Paint.ClearPen();
		Paint.SetBrush( Theme.Text.WithAlpha( 0.06f ) );
		Paint.DrawRect( new Rect( rect.Left, rect.Bottom - 1, rect.Width, 1 ) );

		var area = rect.Shrink( 12, 6 );
		var statRow = area with { Height = 18 };
		var budget = (long)_vmBudget;
		var usage = (long)_vmUsage;
		var otherGpu = Math.Max( 0, usage - _totalGpu );
		var free = budget > 0 ? Math.Max( 0, budget - usage ) : 0;
		var streamPct = _totalDisk > 0 ? (float)_totalGpu / _totalDisk * 100f : 100f;

		float x = area.Left;
		x = DrawStat( statRow, x, "Textures", _totalGpu.FormatBytes(), new Color( 0.45f, 0.50f, 0.85f ) );
		if ( otherGpu > 0 )
			x = DrawStat( statRow, x, "Buffers", otherGpu.FormatBytes(), new Color( 0.85f, 0.60f, 0.25f ) );
		if ( budget > 0 )
			x = DrawStat( statRow, x, "Budget", budget.FormatBytes(), Theme.Text.WithAlpha( 0.5f ) );
		x = DrawStat( statRow, x, "Streamed", $"{streamPct:F0}%", StreamColor );
		if ( _staleCount > 0 )
			x = DrawStat( statRow, x, "Stale", $"{_staleWaste.FormatBytes()} ({_staleCount})", Tags[^1].Color );
		DrawStat( statRow, x, "Count", _totalCount.ToString(), Theme.Text.WithAlpha( 0.5f ) );

		// Memory bar
		var barRect = new Rect( area.Left, area.Top + 26, area.Width, 10 );
		Paint.ClearPen();
		Paint.SetBrush( Theme.Text.WithAlpha( 0.05f ) );
		Paint.DrawRect( barRect, 4 );

		var barTotal = budget > 0 ? Math.Max( budget, usage ) : Math.Max( usage, _totalGpu );
		if ( barTotal <= 0 ) return true;

		var segments = new List<(string Label, Color Color, long Memory)>();
		for ( int i = 0; i < Tags.Length; i++ )
		{
			if ( _catCount[i] > 0 )
				segments.Add( ($"{Tags[i].Label} {_catMemory[i].FormatBytes()}", Tags[i].Color, _catMemory[i]) );
		}
		if ( _otherTexturesMem > 0 )
			segments.Add( ($"Other {_otherTexturesMem.FormatBytes()}", new Color( 0.45f, 0.50f, 0.85f ), _otherTexturesMem) );
		if ( otherGpu > 0 )
			segments.Add( ($"Buffers {otherGpu.FormatBytes()}", new Color( 0.85f, 0.60f, 0.25f ), otherGpu) );

		float bx = barRect.Left, lx = barRect.Left;
		Paint.SetDefaultFont( 7, 500 );

		foreach ( var seg in segments )
		{
			var segW = barRect.Width * ((float)seg.Memory / barTotal);
			if ( segW >= 1 )
			{
				Paint.ClearPen();
				Paint.SetBrush( seg.Color.WithAlpha( 0.6f ) );
				Paint.DrawRect( new Rect( bx, barRect.Top, segW, barRect.Height ), bx == barRect.Left ? 4 : 0 );
				bx += segW;
			}

			if ( seg.Memory > 0 )
			{
				Paint.SetPen( seg.Color.WithAlpha( 0.7f ) );
				var lw = Paint.MeasureText( seg.Label ).x;
				Paint.DrawText( new Rect( lx, barRect.Bottom + 2, lw, 12 ), seg.Label, TextFlag.LeftCenter );
				lx += lw + 12;
			}
		}

		if ( free > 0 )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.2f ) );
			var label = $"Free {free.FormatBytes()}";
			Paint.DrawText( new Rect( lx, barRect.Bottom + 2, Paint.MeasureText( label ).x, 12 ), label, TextFlag.LeftCenter );
		}

		return true;
	}

	float DrawStat( Rect row, float x, string label, string value, Color color )
	{
		Paint.SetDefaultFont( 8 );
		Paint.SetPen( Theme.Text.WithAlpha( 0.4f ) );
		var lw = Paint.MeasureText( label ).x;
		Paint.DrawText( new Rect( x, row.Top, lw, row.Height ), label, TextFlag.LeftCenter );

		Paint.SetDefaultFont( 8, 600 );
		Paint.SetPen( color );
		var vw = Paint.MeasureText( value ).x;
		Paint.DrawText( new Rect( x + lw + 4, row.Top, vw, row.Height ), value, TextFlag.LeftCenter );

		return x + lw + vw + 20;
	}

	void PaintRow( VirtualWidget vw )
	{
		if ( vw.Object is not Info info ) return;

		var rect = vw.Rect;
		var content = rect.Shrink( 8, 2 );
		var fg = Paint.HasSelected ? Color.White : Theme.Text;

		// Background
		Paint.ClearPen();
		if ( Paint.HasSelected )
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.9f ) );
		else if ( Paint.HasMouseOver )
			Paint.SetBrush( Color.White.WithAlpha( 0.05f ) );
		else
			Paint.SetBrush( Theme.ControlBackground.WithAlpha( vw.Row % 2 == 0 ? 0.8f : 0.5f ) );
		Paint.DrawRect( rect );

		// Thumbnail
		const float thumbSize = 24f;
		var thumbRect = new Rect( content.Left, content.Center.y - thumbSize * 0.5f, thumbSize, thumbSize );

		if ( GetThumb( info ) is { } pixmap )
		{
			Paint.Draw( thumbRect, pixmap );
		}
		else
		{
			Paint.SetBrush( Theme.ControlBackground.Darken( 0.1f ) );
			Paint.DrawRect( thumbRect, 3 );
			Paint.SetPen( Theme.Text.WithAlpha( 0.15f ) );
			Paint.DrawIcon( thumbRect, DimIcon( info.Dimension ), 12, TextFlag.Center );
		}

		// Row layout
		var left = content.Left + thumbSize + 8;
		var topHalf = content with { Left = left, Height = content.Height * 0.5f };
		var botHalf = content with { Left = left, Top = content.Center.y, Height = content.Height * 0.5f };

		// Memory size (right-aligned, full height)
		Paint.SetDefaultFont( 8, 600 );
		Paint.SetPen( Paint.HasSelected ? fg : MemoryColor( info.Loaded.MemorySize ) );
		var memText = info.Loaded.MemorySize.FormatBytes();
		var memW = Paint.MeasureText( memText ).x;
		Paint.DrawText( content with { Left = left }, memText, TextFlag.RightCenter );

		// Top: category pills then name
		var pillRight = content.Right - memW - 8;

		if ( IsPartiallyStreamed( info ) )
			pillRight = DrawPill( pillRight, topHalf, "Partial", StreamColor );

		for ( int i = Tags.Length - 1; i >= 0; i-- )
			if ( info.Categories.HasFlag( Tags[i].Flag ) )
				pillRight = DrawPill( pillRight, topHalf, Tags[i].Label, Tags[i].Color );

		Paint.SetDefaultFont( 8 );
		Paint.SetPen( fg );
		Paint.DrawText( topHalf with { Width = pillRight - topHalf.Left - 4 }, info.Name ?? "(unnamed)", TextFlag.LeftCenter | TextFlag.SingleLine );

		// Bottom: info badges + streaming bar
		float bx = botHalf.Left;
		bx = DrawBadge( bx, botHalf, FormatDim( info.Dimension ), DimColor( info.Dimension, fg.WithAlpha( 0.5f ) ), DimIcon( info.Dimension ) );
		bx = DrawBadge( bx, botHalf, info.Format.ToString(), fg.WithAlpha( 0.5f ), "palette" );
		bx = DrawBadge( bx, botHalf, FormatRes( info ), fg.WithAlpha( 0.5f ), "aspect_ratio" );
		if ( info.RefCount > 0 )
			bx = DrawBadge( bx, botHalf, $"{info.RefCount}", RefCountColor( info.RefCount ), "link" );

		if ( info.Categories.HasFlag( Info.TextureCategory.Streaming ) && info.Disk.MemorySize > 0 )
			DrawStreamBar( botHalf, content.Right - memW - 8, info );
	}

	void FetchData()
	{
		_all = Info.GetAll().ToList();

		var liveTextures = _all
			.Where( x => x.Texture is { IsValid: true } )
			.Select( x => x.Texture )
			.ToHashSet();

		foreach ( var staleTexture in _thumbs.Keys.Where( x => !liveTextures.Contains( x ) ).ToArray() )
		{
			_thumbs.Remove( staleTexture );
		}

		_thumbsLoading.RemoveWhere( x => !liveTextures.Contains( x ) );

		Refresh();
	}

	void Refresh()
	{
		var query = _search?.Trim() ?? "";

		_filtered = _all.Where( r =>
		{
			if ( _dimFilter is not null && r.Dimension != _dimFilter ) return false;
			if ( _catFilter != 0 && (r.Categories & _catFilter) != 0 ) return false;
			if ( query.Length > 0
				&& !r.Name.Contains( query, StringComparison.OrdinalIgnoreCase )
				&& !r.Format.ToString().Contains( query, StringComparison.OrdinalIgnoreCase ) )
				return false;
			return true;
		} ).ToList();

		_filtered.Sort( ( a, b ) => b.Loaded.MemorySize.CompareTo( a.Loaded.MemorySize ) );
		List?.SetItems( _filtered.Cast<object>() );
		Treemap?.SetItems( _filtered );
		UpdateStats();

		var count = _filtered.Count == _all.Count ? $"{_all.Count}" : $"{_filtered.Count}/{_all.Count}";
		SummaryLabel.Text = $"{count} textures";
	}

	void UpdateStats()
	{
		_totalGpu = _totalDisk = _staleWaste = _otherTexturesMem = 0;
		_totalCount = _all.Count;
		_staleCount = 0;
		_vmBudget = Graphics.VideoMemoryBudget;
		_vmUsage = Graphics.VideoMemoryUsed;
		Array.Clear( _catMemory );
		Array.Clear( _catCount );

		foreach ( var r in _all )
		{
			_totalGpu += r.Loaded.MemorySize;
			_totalDisk += r.Disk.MemorySize;

			bool assigned = false;
			for ( int i = 0; i < Tags.Length; i++ )
			{
				if ( !r.Categories.HasFlag( Tags[i].Flag ) ) continue;
				_catCount[i]++;
				if ( !assigned )
				{
					_catMemory[i] += r.Loaded.MemorySize;
					assigned = true;
				}
			}

			if ( !assigned )
				_otherTexturesMem += r.Loaded.MemorySize;

			if ( r.Categories.HasFlag( Info.TextureCategory.Stale ) )
			{
				_staleWaste += r.Loaded.MemorySize;
				_staleCount++;
			}
		}

		StatsBar?.Update();
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( !Visible || _refreshTimer < 2f || !_autoUpdate || Preview.HasStreamingSelection ) return;
		// maybe dont autoupdate if treemap?
		//if ( _viewMode == ViewMode.Treemap ) return;
		_refreshTimer = 0;
		FetchData();
	}

	static float DrawPill( float right, Rect row, string label, Color color )
	{
		Paint.SetDefaultFont( 7, 500 );
		var w = Paint.MeasureText( label ).x + 8;
		right -= w + 2;

		var pill = new Rect( right, row.Center.y - 7, w, 14 );
		Paint.ClearPen();
		Paint.SetBrush( color.WithAlpha( 0.15f ) );
		Paint.DrawRect( pill, 7 );
		Paint.SetPen( color );
		Paint.DrawText( pill, label, TextFlag.Center );

		return right;
	}

	static float DrawBadge( float x, Rect row, string text, Color color, string icon = null )
	{
		Paint.SetDefaultFont( 7, 500 );
		var tw = Paint.MeasureText( text ).x;
		var h = row.Height - 2;
		var w = icon is not null ? tw + h + 6 : tw + 8;
		var r = new Rect( x, row.Center.y - h * 0.5f, w, h );

		Paint.ClearPen();
		Paint.SetBrush( color.WithAlpha( 0.08f ) );
		Paint.DrawRect( r, 3 );
		Paint.SetPen( color );

		if ( icon is not null )
		{
			var iconSize = h - 4;
			Paint.DrawIcon( new Rect( r.Left + 2, r.Top + 2, iconSize, iconSize ), icon, iconSize, TextFlag.Center );
			Paint.DrawText( new Rect( r.Left + h, r.Top, tw + 4, h ), text, TextFlag.Center );
		}
		else
		{
			Paint.DrawText( r, text, TextFlag.Center );
		}

		return x + w + 3;
	}

	static void DrawStreamBar( Rect row, float right, Info info )
	{
		var fill = MathF.Min( (float)info.Loaded.MemorySize / info.Disk.MemorySize, 1f );
		var pct = $"{fill * 100f:F0}%";
		const float barW = 40f, barH = 3f;

		Paint.SetDefaultFont( 7 );
		var pctW = Paint.MeasureText( pct ).x;
		var barX = right - pctW - 4 - barW;
		var barY = row.Center.y - barH * 0.5f;

		Paint.ClearPen();
		Paint.SetBrush( StreamColor.WithAlpha( 0.1f ) );
		Paint.DrawRect( new Rect( barX, barY, barW, barH ), 1.5f );
		Paint.SetBrush( StreamColor.WithAlpha( 0.5f ) );
		Paint.DrawRect( new Rect( barX, barY, barW * fill, barH ), 1.5f );

		Paint.SetPen( StreamColor.WithAlpha( 0.5f ) );
		Paint.DrawText( new Rect( barX + barW + 3, row.Top, pctW, row.Height ), pct, TextFlag.LeftCenter );
	}

	static string DimIcon( Info.TextureDimension d ) => d switch
	{
		Info.TextureDimension.Cube or Info.TextureDimension.CubeArray => "view_in_ar",
		Info.TextureDimension._3D or Info.TextureDimension._2DArray => "layers",
		Info.TextureDimension.Buffer => "memory",
		Info.TextureDimension._1D => "horizontal_rule",
		_ => "image"
	};

	static Color DimColor( Info.TextureDimension d, Color fallback ) => d switch
	{
		Info.TextureDimension.Cube or Info.TextureDimension.CubeArray => new( 0.7f, 0.5f, 1f ),
		Info.TextureDimension._3D => new( 1f, 0.7f, 0.4f ),
		Info.TextureDimension._2DArray => new( 0.5f, 0.8f, 1f ),
		_ => fallback
	};

	static string FormatDim( Info.TextureDimension d ) => d switch
	{
		Info.TextureDimension._2DArray => "2D[]",
		Info.TextureDimension.CubeArray => "Cube[]",
		_ => d.ToString().TrimStart( '_' )
	};

	static string FormatRes( Info r ) => r.Loaded.Depth > 1
		? $"{r.Loaded.Width}×{r.Loaded.Height}×{r.Loaded.Depth}"
		: $"{r.Loaded.Width}×{r.Loaded.Height}";

	static Color MemoryColor( long bytes ) => bytes switch
	{
		> 64 * 1024 * 1024 => new( 1f, 0.4f, 0.3f ),
		> 16 * 1024 * 1024 => new( 1f, 0.8f, 0.3f ),
		> 4 * 1024 * 1024 => new( 0.6f, 0.85f, 1f ),
		_ => Theme.Text.WithAlpha( 0.5f )
	};

	static Color RefCountColor( int refs ) => refs switch
	{
		0 => new( 1f, 0.4f, 0.3f ),
		1 => Theme.Text.WithAlpha( 0.45f ),
		< 8 => new( 0.6f, 0.85f, 1f ),
		< 32 => new( 1f, 0.8f, 0.3f ),
		_ => new( 1f, 0.55f, 0.85f )
	};

	static bool IsPartiallyStreamed( Info info ) =>
		info.Categories.HasFlag( Info.TextureCategory.Streaming )
		&& info.Disk.MemorySize > 0
		&& info.Loaded.MemorySize < info.Disk.MemorySize;

	Pixmap GetThumb( Info info )
	{
		if ( info.Texture is not { IsValid: true } tex ) return null;
		if ( _thumbs.TryGetValue( tex, out var p ) ) return p;
		if ( (info.Categories & (Info.TextureCategory.DepthBuffer | Info.TextureCategory.MSAA)) != 0 ) return null;
		if ( !_thumbsLoading.Add( tex ) ) return null;

		_ = LoadThumbAsync( tex );
		return null;
	}

	async Task LoadThumbAsync( Texture tex )
	{
		try
		{
			Pixmap pm = null;
			await Task.Run( () =>
			{
				pm = Pixmap.FromTexture( tex );
				if ( pm is null ) return;

				// Downsample to a small thumb - we never display them big
				int w = pm.Width, h = pm.Height;
				int maxSide = Math.Max( w, h );
				if ( maxSide > ThumbMaxSize )
				{
					float scale = (float)ThumbMaxSize / maxSide;
					var resized = pm.Resize( Math.Max( 1, (int)(w * scale) ), Math.Max( 1, (int)(h * scale) ) );
					if ( resized is not null ) pm = resized;
				}
			} );
			_thumbs[tex] = pm;
			List?.Update();
			Treemap?.Update();
		}
		catch { _thumbs[tex] = null; }
		finally { _thumbsLoading.Remove( tex ); }
	}

	class TexturePreviewPanel : Widget, IDisposable
	{
		public bool HasSelection => _current is not null;
		public bool HasStreamingSelection => _current?.Categories.HasFlag( Info.TextureCategory.Streaming ) ?? false;

		Info _current;
		Texture _previewTex, _resolvedCopy;
		Scene _scene;
		CameraComponent _cam;
		SceneRenderingWidget _render;
		Widget _header;
		Widget _info;
		float _time;
		SpriteRenderer _sprite;
		EnvmapProbe _probe;
		PreviewMode _mode;

		enum PreviewMode { Sprite, Cube, Depth }

		public TexturePreviewPanel( Widget parent ) : base( parent )
		{
			MinimumWidth = 250;
			Layout = Layout.Column();
			Layout.Spacing = 0;

			_header = Layout.Add( new Widget( this ) { FixedHeight = 34, Visible = false } );
			_header.OnPaintOverride = PaintHeader;

			_render = Layout.Add( new SceneRenderingWidget( this ), 1 );
			_render.OnPreFrame += Tick;

			_info = Layout.Add( new Widget( this ) { MinimumHeight = 140, MaximumHeight = 200 } );
			_info.OnPaintOverride = PaintInfo;
		}

		public void SetTexture( Info info )
		{
			_current = info;
			_header.Visible = true;
			_header.Update();
			_resolvedCopy?.Dispose();
			_resolvedCopy = null;

			if ( info.Texture is not { IsValid: true } tex )
			{
				Teardown();
				_render.Scene = null;
				_render.Camera = null;
				_info.Update();
				return;
			}

			if ( info.Categories.HasFlag( Info.TextureCategory.MSAA ) )
			{
				_resolvedCopy = Texture.Create( tex.Width, tex.Height, tex.ImageFormat ).Finish();
				try { Graphics.CopyTexture( tex, _resolvedCopy ); } catch { }
				tex = _resolvedCopy;
			}

			var wantMode = info.Dimension is Info.TextureDimension.Cube or Info.TextureDimension.CubeArray
				? PreviewMode.Cube
				: info.Categories.HasFlag( Info.TextureCategory.DepthBuffer )
					? PreviewMode.Depth
					: PreviewMode.Sprite;

			if ( _mode != wantMode ) Teardown();
			_mode = wantMode;
			_previewTex = tex;
			EnsureScene();

			using ( _scene.Push() )
			{
				if ( _mode == PreviewMode.Cube )
				{
					_probe.Texture = tex;
					for ( int i = 0; i < 4; i++ )
						_scene.EditorTick( _time += 0.1f, 0.1f );
				}
				else
				{
					if ( _mode == PreviewMode.Sprite )
						_sprite.Sprite = new Sprite { Animations = [new Sprite.Animation { Name = "Default", Frames = [new Sprite.Frame { Texture = tex }] }] };

					_scene.EditorTick( _time += 0.1f, 0.1f );
				}
			}

			_info.Update();
		}

		void EnsureScene()
		{
			if ( _scene.IsValid() ) return;

			_scene = Scene.CreateEditorScene();
			_scene.Name = "GPU Resource Preview";

			using ( _scene.Push() )
			{
				_cam = new GameObject( true, "cam" ).AddComponent<CameraComponent>();
				_cam.BackgroundColor = new Color( 0.12f, 0.12f, 0.12f );

				if ( _mode == PreviewMode.Cube )
				{
					_cam.FieldOfView = 30;
					_cam.ZNear = 0.1f;
					_cam.ZFar = 5000f;

					var dist = MathX.SphereCameraDistance( 32f, _cam.FieldOfView );
					_cam.WorldPosition = new Angles( 15, 0, 0 ).ToRotation().Forward * -dist;
					_cam.WorldRotation = Rotation.LookAt( -_cam.WorldPosition );

					var sphere = new GameObject( true, "sphere" ).AddComponent<ModelRenderer>();
					sphere.Model = Model.Sphere;
					sphere.MaterialOverride = Material.Load( "materials/dev/dev_metal_rough00.vmat" );

					_probe = new GameObject( true, "envmap" ).AddComponent<EnvmapProbe>();
					_probe.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
					_probe.Bounds = BBox.FromPositionAndSize( Vector3.Zero, 100000 );
				}
				else
				{
					_cam.Orthographic = true;
					_cam.OrthographicHeight = 16;
					_cam.ZFar = 1000f;
					_cam.WorldPosition = Vector3.Forward * -200;
					_cam.WorldRotation = Rotation.LookAt( Vector3.Forward );

					if ( _mode == PreviewMode.Sprite )
					{
						_sprite = new GameObject( true, "sprite" ).AddComponent<SpriteRenderer>();
						_sprite.Size = new Vector2( 16, 16 );
					}
				}

				for ( int i = 0; i < 4; i++ )
					_scene.EditorTick( _time += 0.1f, 0.1f );
			}

			_render.Scene = _scene;
			_render.Camera = _cam;
		}

		void Tick()
		{
			if ( !_scene.IsValid() ) return;

			if ( _resolvedCopy is not null && _current?.Texture is { IsValid: true } srcTex )
				try { Graphics.CopyTexture( srcTex, _resolvedCopy ); } catch { }

			using ( _scene.Push() )
			{
				if ( _mode == PreviewMode.Cube && _probe.IsValid() )
					_probe.WorldRotation = new Angles( 0, _time * 15f, 0 ).ToRotation();

				_scene.EditorTick( _time += RealTime.Delta, RealTime.Delta );

				if ( _mode == PreviewMode.Depth && _previewTex is { IsValid: true } )
				{
					var size = _render.Size * _render.DpiScale;
					_cam.Hud.DrawTexture( _previewTex, new Rect( 0, 0, size.x, size.y ) );
				}
			}
		}

		void Teardown()
		{
			_cam = null;
			_sprite = null;
			_probe = null;
			_previewTex = null;
			_resolvedCopy?.Dispose();
			_resolvedCopy = null;
			_scene?.Destroy();
			_scene = null;
		}

		public void Dispose()
		{
			_render.OnPreFrame -= Tick;
			Teardown();
		}

		bool PaintHeader()
		{
			if ( _current is not { } info )
				return false;

			var rect = _header.LocalRect;
			var accent = DimColor( info.Dimension, Theme.Primary );

			Paint.ClearPen();
			Paint.SetBrush( Theme.SidebarBackground );
			Paint.DrawRect( rect );

			// Accent line along the bottom edge, tinted by texture dimension
			Paint.SetBrush( accent.WithAlpha( 0.6f ) );
			Paint.DrawRect( rect with { Top = rect.Bottom - 2 } );

			var content = rect.Shrink( 10, 0 );

			// Dimension icon
			Paint.SetPen( accent );
			Paint.DrawIcon( content with { Width = 18 }, DimIcon( info.Dimension ), 16, TextFlag.LeftCenter );

			var left = content.Left + 24;

			// Category pills, right-aligned
			var pillRight = content.Right;
			for ( int i = Tags.Length - 1; i >= 0; i-- )
				if ( info.Categories.HasFlag( Tags[i].Flag ) )
					pillRight = DrawPill( pillRight, content, Tags[i].Label, Tags[i].Color );

			// Name
			Paint.SetDefaultFont( 10, 600 );
			Paint.SetPen( Theme.Text );
			Paint.DrawText( content with { Left = left, Width = MathF.Max( 10, pillRight - left - 6 ) }, info.Name ?? "(unnamed)", TextFlag.LeftCenter | TextFlag.SingleLine );

			return true;
		}

		bool PaintInfo()
		{
			Paint.SetBrushAndPen( Theme.SidebarBackground, Color.Transparent, 0 );

			if ( _current is not { } info )
			{
				_render.Visible = false;
				Paint.SetPen( Theme.Text.WithAlpha( 0.3f ) );
				Paint.SetDefaultFont( 11 );
				Paint.DrawText( _info.LocalRect, "Select a texture to preview", TextFlag.Center );
				return false;
			}

			Paint.DrawRect( _info.LocalRect );
			_render.Visible = true;

			var r = _info.LocalRect.Shrink( 12, 10 );

			Paint.SetDefaultFont( 8 );
			Prop( ref r, "Type", FormatDim( info.Dimension ) );
			Prop( ref r, "Format", info.Format.ToString() );
			Prop( ref r, "Size", FormatRes( info ) );
			Prop( ref r, "Mips", info.MipCount.ToString() );
			Prop( ref r, "GPU", info.Loaded.MemorySize.FormatBytes(), MemoryColor( info.Loaded.MemorySize ) );
			Prop( ref r, "Disk", info.Disk.MemorySize.FormatBytes() );
			Prop( ref r, "Loaded", info.Disk.MemorySize > 0 ? $"{(float)info.Loaded.MemorySize / info.Disk.MemorySize * 100f:F0}%" : "100%" );
			Prop( ref r, "Refs", info.RefCount.ToString(), RefCountColor( info.RefCount ) );

			if ( info.LastUsedFrames >= 0 )
				Prop( ref r, "Last Used", info.LastUsedFrames switch { 0 => "This frame", 1 => "1 frame ago", >= 1000 => "Stale", _ => $"{info.LastUsedFrames}f ago" } );

			return true;
		}

		static void Prop( ref Rect r, string label, string value, Color? valueColor = null )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( r with { Width = 80, Height = 16 }, label, TextFlag.LeftCenter );
			Paint.SetPen( valueColor ?? Theme.Text.WithAlpha( 0.9f ) );
			Paint.DrawText( r with { Left = r.Left + 80, Height = 16 }, value, TextFlag.LeftCenter );
			r.Top += 16;
		}
	}

}
