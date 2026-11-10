using System.Runtime.InteropServices;

namespace Editor.ProjectSettingPages;

[Title( "Cursors" ), Icon( "mouse" )]
internal sealed class CursorCategory : ProjectSettingsWindow.Category
{
	static readonly Dictionary<string, string[]> SystemCursors = new()
	{
		["arrow"] = [],
		["ibeam"] = ["text"],
		["crosshair"] = [],
		["hand"] = ["pointer"],
		["wait"] = ["hourglass"],
		["progress"] = [],
		["move"] = [],
		["not-allowed"] = [],
		["sizewe"] = ["ew-resize"],
		["sizens"] = ["ns-resize"],
		["sizenesw"] = ["nesw-resize"],
		["sizenwse"] = ["nwse-resize"],
	};

	static string DisplayName( string name )
	{
		if ( SystemCursors.TryGetValue( name, out var aliases ) && aliases.Length > 0 )
			return $"{name}, {string.Join( ", ", aliases )}";

		return name;
	}

	CursorSettings _settings;
	Layout _customListLayout;
	Layout _systemListLayout;

	public override void OnInit( Project project )
	{
		base.OnInit( project );

		_settings = EditorUtility.LoadProjectSettings<CursorSettings>( "Cursors.config" );
		_settings.Cursors ??= [];

		BodyLayout.Add( new InformationBox(
			"""
			<p>Define custom cursors for your game. You can register new cursor types or override built-in system cursors.</p>
			<p>Set the cursor at runtime with <b>Mouse.CursorType = "name"</b></p>
			""" ) );

		BodyLayout.AddSpacingCell( 12 );
		BodyLayout.Add( new Label.Header( "Custom Cursors" ) );

		_customListLayout = BodyLayout.AddColumn();
		_customListLayout.Spacing = 4;
		RebuildCustomList();

		BodyLayout.AddSpacingCell( 4 );
		var footer = BodyLayout.AddRow();
		footer.Spacing = 4;
		footer.Margin = new Margin( 8, 4, 8, 4 );

		var nameEntry = footer.Add( new LineEdit() { MaximumHeight = 24, PlaceholderText = "Cursor Name..." }, 2 );
		var addButton = footer.Add( new IconButton( "add" ) { ToolTip = "Add cursor", IconSize = 14, Background = Color.Transparent, Enabled = false } );

		bool IsValidName( string text )
		{
			var name = text?.Trim();
			if ( string.IsNullOrWhiteSpace( name ) ) return false;
			if ( _settings.Cursors.ContainsKey( name ) ) return false;
			if ( SystemCursors.ContainsKey( name.ToLower() ) ) return false;
			if ( SystemCursors.Values.Any( aliases => aliases.Contains( name.ToLower() ) ) ) return false;
			return true;
		}

		nameEntry.TextEdited += ( text ) => addButton.Enabled = IsValidName( text );

		void AddAction()
		{
			var name = nameEntry.Text?.Trim();
			if ( !IsValidName( name ) ) return;

			_settings.Cursors[name] = new CursorSettings.Cursor();
			nameEntry.Text = "";
			addButton.Enabled = false;
			RebuildCustomList();
			StateHasChanged();
		}

		nameEntry.ReturnPressed += AddAction;
		addButton.MouseLeftPress = AddAction;

		BodyLayout.AddSpacingCell( 16 );
		BodyLayout.Add( new Label.Header( "System Cursor Overrides" ) );
		BodyLayout.Add( new InformationBox( "Override the built-in system cursors with your own images." ) );
		BodyLayout.AddSpacingCell( 4 );

		_systemListLayout = BodyLayout.AddColumn();
		_systemListLayout.Spacing = 4;
		RebuildSystemList();
	}

	void RebuildCustomList()
	{
		_customListLayout.Clear( true );

		var customCursors = _settings.Cursors
			.Where( kv => !SystemCursors.ContainsKey( kv.Key.ToLower() ) )
			.ToArray();

		if ( customCursors.Length == 0 )
		{
			_customListLayout.Add( new Label( "No custom cursors yet - enter a name below to create one." )
			{
				Margin = 16,
				Color = Theme.TextControl.WithAlpha( 0.5f )
			} );
			return;
		}

		foreach ( var kv in customCursors )
			AddCursorEntry( _customListLayout, kv.Key, canDelete: true );
	}

	void RebuildSystemList()
	{
		_systemListLayout.Clear( true );

		foreach ( var name in SystemCursors.Keys )
		{
			if ( _settings.Cursors.ContainsKey( name ) )
			{
				AddCursorEntry( _systemListLayout, name, canDelete: false );
				continue;
			}

			var card = new Widget( null );
			card.Layout = Layout.Row();
			card.Layout.Margin = new Margin( 8, 6, 8, 6 );
			card.Layout.Spacing = 8;
			card.OnPaintOverride += () =>
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.3f ) );
				Paint.DrawRect( card.LocalRect, Theme.ControlRadius );
				return true;
			};
			_systemListLayout.Add( card );

			var row = card.Layout;

			var cursorPixmap = SystemCursorPixmapCache.Get( name );
			if ( cursorPixmap is not null )
			{
				row.Add( new SystemCursorPreviewWidget( cursorPixmap ) { FixedWidth = 32, FixedHeight = 32 } );
			}
			else
			{
				row.Add( new IconButton( "mouse" )
				{
					IconSize = 16,
					FixedWidth = 32,
					FixedHeight = 32,
					Background = Color.Transparent,
					TransparentForMouseEvents = true
				} );
			}

			row.Add( new Label( DisplayName( name ) ) { Color = Theme.Text }, 1 );
			row.Add( new IconButton( "edit" ) { ToolTip = "Override cursor", IconSize = 14, Background = Color.Transparent } ).MouseLeftPress = () =>
			{
				_settings.Cursors[name] = new CursorSettings.Cursor();
				RebuildSystemList();
				StateHasChanged();
			};
		}
	}

	void AddCursorEntry( Layout parent, string name, bool canDelete )
	{
		var card = new Widget( null );
		card.Layout = Layout.Column();
		card.Layout.Spacing = 2;
		card.Layout.Margin = new Margin( 8, 6, 8, 6 );
		card.OnPaintOverride += () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.5f ) );
			Paint.DrawRect( card.LocalRect, Theme.ControlRadius );
			return true;
		};
		parent.Add( card );

		var header = card.Layout.AddRow();
		header.Add( new Label( DisplayName( name ) ) { Color = Theme.Text } ).SetStyles( "font-weight: 600;" );
		header.AddStretchCell();

		header.Add( new IconButton( canDelete ? "close" : "undo" )
		{
			ToolTip = canDelete ? "Remove cursor" : "Clear override",
			IconSize = 14,
			Background = Color.Transparent
		} ).MouseLeftPress = () =>
		{
			_settings.Cursors.Remove( name );

			if ( SystemCursors.ContainsKey( name.ToLower() ) )
				RebuildSystemList();
			else
				RebuildCustomList();

			StateHasChanged();
		};

		var so = _settings.Cursors[name].GetSerialized();

		var row = card.Layout.AddRow();
		row.Spacing = 8;

		var preview = new CursorPreviewWidget();
		row.Add( preview );

		void UpdatePreview()
		{
			var path = so.GetProperty( nameof( CursorSettings.Cursor.Image ) ).As.String;
			var fullPath = string.IsNullOrWhiteSpace( path ) ? null : FileSystem.Content.GetFullPath( path );
			preview.CursorPixmap = fullPath is not null ? Pixmap.FromFile( fullPath ) : null;
			preview.Hotspot = so.GetProperty( nameof( CursorSettings.Cursor.Hotspot ) ).As.Vector2;
		}

		UpdatePreview();

		var controls = row.AddColumn( 1 );
		controls.Spacing = 4;
		controls.Add( ControlWidget.Create( so.GetProperty( nameof( CursorSettings.Cursor.Image ) ) ) ).FixedHeight = Theme.RowHeight;
		controls.Add( ControlWidget.Create( so.GetProperty( nameof( CursorSettings.Cursor.Hotspot ) ) ) ).FixedHeight = Theme.RowHeight;

		so.OnPropertyChanged += ( prop ) =>
		{
			_settings.Cursors[name] = new CursorSettings.Cursor
			{
				Image = so.GetProperty( nameof( CursorSettings.Cursor.Image ) ).As.String,
				Hotspot = so.GetProperty( nameof( CursorSettings.Cursor.Hotspot ) ).As.Vector2
			};
			UpdatePreview();
			preview.Update();
			StateHasChanged();
		};
	}

	public override void OnSave()
	{
		EditorUtility.SaveProjectSettings( _settings, "Cursors.config" );
		base.OnSave();
	}
}

file class CursorPreviewWidget : Widget
{
	public Pixmap CursorPixmap { get; set; }
	public Vector2 Hotspot { get; set; }

	public CursorPreviewWidget() : base( null )
	{
		FixedSize = 64;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		if ( CursorPixmap is not null )
			Paint.Draw( LocalRect.Shrink( 2 ), CursorPixmap );
		else
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.3f ) );
			Paint.DrawIcon( LocalRect, "mouse", 24, TextFlag.Center );
		}

		var ir = LocalRect.Shrink( 2 );
		var hx = (CursorPixmap is not null && CursorPixmap.Width > 0) ? ir.Left + (Hotspot.x / CursorPixmap.Width) * ir.Width : ir.Left + Hotspot.x;
		var hy = (CursorPixmap is not null && CursorPixmap.Height > 0) ? ir.Top + (Hotspot.y / CursorPixmap.Height) * ir.Height : ir.Top + Hotspot.y;
		var hp = new Vector2( hx, hy );

		Paint.SetPen( Color.Red, 1 );
		Paint.DrawLine( hp - new Vector2( 6, 0 ), hp + new Vector2( 6, 0 ) );
		Paint.DrawLine( hp - new Vector2( 0, 6 ), hp + new Vector2( 0, 6 ) );
		Paint.ClearBrush();
		Paint.SetPen( Color.Red.WithAlpha( 0.8f ), 1.5f );
		Paint.DrawCircle( hp, 4 );
	}
}

file class SystemCursorPreviewWidget( Pixmap pixmap ) : Widget( null )
{
	readonly Pixmap _pixmap = pixmap;

	protected override void OnPaint()
	{
		if ( _pixmap is null ) return;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 2 );
		Paint.Draw( LocalRect, _pixmap );
	}
}

file static class SystemCursorPixmapCache
{
	[SkipHotload]
	static readonly Dictionary<string, Pixmap> Cache = [];

	static readonly Dictionary<string, int> CursorNameToId = new()
	{
		["arrow"] = 32512,
		["ibeam"] = 32513,
		["text"] = 32513,
		["crosshair"] = 32515,
		["hand"] = 32649,
		["pointer"] = 32649,
		["wait"] = 32514,
		["hourglass"] = 32514,
		["progress"] = 32650,
		["move"] = 32646,
		["not-allowed"] = 32648,
		["ew-resize"] = 32644,
		["sizewe"] = 32644,
		["ns-resize"] = 32645,
		["sizens"] = 32645,
		["nesw-resize"] = 32643,
		["sizenesw"] = 32643,
		["nwse-resize"] = 32642,
		["sizenwse"] = 32642,
	};

	public static Pixmap Get( string name )
	{
		if ( Cache.TryGetValue( name, out var cached ) ) return cached;
		if ( !CursorNameToId.TryGetValue( name, out var cursorId ) ) return null;

		var pixmap = ExtractCursorPixmap( cursorId );
		if ( pixmap is not null ) Cache[name] = pixmap;
		return pixmap;
	}

	static Pixmap ExtractCursorPixmap( int cursorId )
	{
		if ( !OperatingSystem.IsWindows() ) return null;

		var hCursor = Win32.LoadCursorW( IntPtr.Zero, (IntPtr)cursorId );
		if ( hCursor == IntPtr.Zero ) return null;

		int w = Win32.GetSystemMetrics( 13 ), h = Win32.GetSystemMetrics( 14 );
		if ( w <= 0 || h <= 0 ) return null;

		var onBlack = Win32.RenderIcon( hCursor, w, h, 0x00 );
		var onWhite = Win32.RenderIcon( hCursor, w, h, 0xFF );
		if ( onBlack is null || onWhite is null ) return null;

		var bgra = ComputeAlpha( onBlack, onWhite, w * h );
		CenterContent( bgra, w, h );

		var pixmap = new Pixmap( w, h );
		pixmap.UpdateFromPixels( bgra, w, h, ImageFormat.BGRA8888 );
		return pixmap;
	}

	static byte[] ComputeAlpha( byte[] onBlack, byte[] onWhite, int pixelCount )
	{
		var bgra = new byte[pixelCount * 4];

		for ( int i = 0; i < pixelCount; i++ )
		{
			int idx = i * 4;

			int diff = Math.Max( onWhite[idx + 2] - onBlack[idx + 2],
				Math.Max( onWhite[idx + 1] - onBlack[idx + 1], onWhite[idx] - onBlack[idx] ) );
			int a = Math.Clamp( 255 - diff, 0, 255 );

			if ( a > 0 )
			{
				bgra[idx] = (byte)Math.Clamp( onBlack[idx] * 255 / a, 0, 255 );
				bgra[idx + 1] = (byte)Math.Clamp( onBlack[idx + 1] * 255 / a, 0, 255 );
				bgra[idx + 2] = (byte)Math.Clamp( onBlack[idx + 2] * 255 / a, 0, 255 );
			}
			bgra[idx + 3] = (byte)a;
		}

		return bgra;
	}

	static void CenterContent( byte[] data, int w, int h )
	{
		int minX = w, minY = h, maxX = 0, maxY = 0;
		for ( int i = 3; i < data.Length; i += 4 )
		{
			if ( data[i] == 0 ) continue;
			int x = (i / 4) % w, y = (i / 4) / w;
			if ( x < minX ) minX = x; if ( y < minY ) minY = y;
			if ( x > maxX ) maxX = x; if ( y > maxY ) maxY = y;
		}

		if ( maxX < minX ) return;

		int ox = (w - (maxX - minX + 1)) / 2 - minX;
		int oy = (h - (maxY - minY + 1)) / 2 - minY;
		if ( ox == 0 && oy == 0 ) return;

		int rowBytes = (maxX - minX + 1) * 4;
		var result = new byte[data.Length];
		for ( int y = minY; y <= maxY; y++ )
			Buffer.BlockCopy( data, (y * w + minX) * 4, result, ((y + oy) * w + minX + ox) * 4, rowBytes );
		Array.Copy( result, data, data.Length );
	}

	static class Win32
	{
		[DllImport( "user32.dll" )] public static extern IntPtr LoadCursorW( IntPtr hInstance, IntPtr lpCursorName );
		[DllImport( "user32.dll" )] public static extern bool DrawIconEx( IntPtr hdc, int x, int y, IntPtr hIcon, int w, int h, int frame, IntPtr hbrFlickerFree, int flags );
		[DllImport( "user32.dll" )] public static extern int GetSystemMetrics( int index );
		[DllImport( "gdi32.dll" )] public static extern IntPtr CreateCompatibleDC( IntPtr hdc );
		[DllImport( "gdi32.dll" )] public static extern IntPtr CreateDIBSection( IntPtr hdc, ref BITMAPINFO bmi, int usage, out IntPtr bits, IntPtr hSection, int offset );
		[DllImport( "gdi32.dll" )] public static extern IntPtr SelectObject( IntPtr hdc, IntPtr obj );
		[DllImport( "gdi32.dll" )] public static extern bool DeleteObject( IntPtr obj );
		[DllImport( "gdi32.dll" )] public static extern bool DeleteDC( IntPtr hdc );

		[StructLayout( LayoutKind.Sequential )]
		public struct BITMAPINFO
		{
			public int Size, Width, Height;
			public short Planes, BitCount;
			public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
		}

		public static byte[] RenderIcon( IntPtr hIcon, int w, int h, byte bg )
		{
			var bmi = new BITMAPINFO { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
			var hdc = CreateCompatibleDC( IntPtr.Zero );
			var hBmp = CreateDIBSection( hdc, ref bmi, 0, out var bits, IntPtr.Zero, 0 );
			if ( hBmp == IntPtr.Zero ) { DeleteDC( hdc ); return null; }

			var old = SelectObject( hdc, hBmp );
			unsafe { new Span<byte>( (void*)bits, w * h * 4 ).Fill( bg ); }
			DrawIconEx( hdc, 0, 0, hIcon, w, h, 0, IntPtr.Zero, 3 );
			SelectObject( hdc, old );

			var pixels = new byte[w * h * 4];
			Marshal.Copy( bits, pixels, 0, pixels.Length );
			DeleteObject( hBmp );
			DeleteDC( hdc );
			return pixels;
		}
	}
}
