using System.IO;

namespace Editor;

public class PathWidget : Widget
{
	private AssetBrowser Browser { get; init; }

	public Action<string> OnPathEdited;

	private LineEdit LineEdit;
	private Widget SegmentParent;

	private Layout SegmentLayout => SegmentParent.Layout;
	private Layout EditLayout;

	private List<PathSegment> Segments = new();
	private PathElipses ElipsesWidget;

	public PathWidget( AssetBrowser assetBrowser ) : base( assetBrowser )
	{
		Browser = assetBrowser;

		MouseClick += StartEditing;

		Layout = Layout.Row();

		EditLayout = Layout.AddRow( 1 );

		var segmentRow = Layout.AddRow();

		SegmentParent = segmentRow.Add( new Widget(), 1 );
		SegmentParent.Layout = Layout.Row();
		SegmentLayout.Margin = new Sandbox.UI.Margin( 2, 0 );
		SegmentParent.HorizontalSizeMode = SizeMode.CanShrink;
		segmentRow.AddStretchCell();

		LineEdit = EditLayout.Add( new LineEdit(), 1 );
		LineEdit.EditingFinished += StopEditing;
		LineEdit.HorizontalSizeMode = SizeMode.Flexible;
		LineEdit.Visible = false;

		Width = 125;
	}

	private void StartEditing()
	{
		if ( LineEdit.IsFocused )
			return;

		LineEdit.Visible = true;
		LineEdit.Focus();
		LineEdit.SelectAll();

		SegmentParent.Visible = false;
	}

	protected override void OnResize()
	{
		base.OnResize();
		LineEdit.Visible = false;
		SegmentParent.Visible = true;
		UpdateSegments();
	}

	/// <summary>
	/// Rebuild the path segments based on current location
	/// </summary>
	public void Rebuild()
	{
		if ( SegmentLayout == null )
			return;

		SegmentLayout.Clear( true );
		Segments.Clear();

		var location = Browser.CurrentLocation;
		if ( location is null )
			return;

		LineEdit.Visible = false;
		LineEdit.Value = location.Path;
		SegmentParent.Visible = true;

		if ( string.IsNullOrWhiteSpace( location.RelativePath ) && !location.IsRoot )
		{
			// not a real path
			var seg = new PathSegment( Browser, location.Name, location.Path );
			SegmentLayout.Add( seg );
			Segments.Add( seg );
			Enabled = false;
			return;
		}

		Enabled = true;

		bool hasRoot = location.RootPath != null;
		var segments = location.RelativePath.NormalizeFilename( hasRoot, false ).Split( '/' );
		string currentPath = "";

		// stuff that doesn't fit is tucked into an elipses menu
		ElipsesWidget = new PathElipses( Browser );
		SegmentLayout.Add( ElipsesWidget );

		for ( int i = 0; i < segments.Length; i++ )
		{
			var segment = segments[i];
			if ( i > 0 && segment.Length == 0 )
				continue;

			currentPath += (i > 0 ? "/" : "") + segment;
			var absolutePath = location.RootPath == null ? currentPath : $"{location.RootPath}{currentPath}";
			if ( !absolutePath.EndsWith( "/" ) )
				absolutePath += "/";

			string label = GetSegmentLabel( i, segment, location );

			var seg = new PathSegment( Browser, label, absolutePath );
			SegmentLayout.Add( seg );
			Segments.Add( seg );

			// add separators except after last segment
			if ( i < segments.Length - 1 )
			{
				seg.Separator = new PathSeparator( Browser, absolutePath );
				seg.Separator.Visible = false;
				SegmentLayout.Add( seg.Separator );
			}
		}

		SegmentLayout.AddStretchCell( 1 );

		UpdateSegments();
	}

	/// <summary>
	/// Update visibility of each segment, and elipses menu if needed
	/// </summary>
	private void UpdateSegments()
	{
		if ( Segments.Count == 0 )
			return;

		float currentWidth = 0;
		bool hasVisible = false;
		int truncIdx = -1;

		float availableWidth = LocalRect.Width - 32 - 64;

		// work out which segments fit
		for ( int i = Segments.Count - 1; i >= 0; i-- )
		{
			float segmentWidth = MeasureTextWidth( Segments[i].Label );
			if ( currentWidth + segmentWidth + 32 > availableWidth && hasVisible )
			{
				truncIdx = i;
				break;
			}

			currentWidth += segmentWidth;
			hasVisible = true;
		}

		ElipsesWidget.Paths.Clear();

		// what doesn't fit should be hidden, and added to the elipses menu
		for ( int i = 0; i < Segments.Count; i++ )
		{
			Segments[i].Visible = i > truncIdx;

			if ( !Segments[i].Visible )
			{
				ElipsesWidget.Paths.Add( (Segments[i].Label, Segments[i].TargetPath) );
			}
		}

		ElipsesWidget.Visible = truncIdx != -1;
	}

	string GetSegmentLabel( int index, string segment, AssetBrowser.Location location )
	{
		if ( index == 0 && location.RootTitle is not null )
			return location.RootTitle;

		return segment.Contains( ':' ) ? $"Drive ({segment})" : segment;
	}

	public static float MeasureTextWidth( string text )
	{
		Paint.SetDefaultFont( 8 );
		return Paint.MeasureText( text ).x + (8 * 2);
	}


	private void StopEditing()
	{
		LineEdit.Visible = false;
		OnPathEdited?.Invoke( LineEdit.Value );
		Rebuild();
		Update();
	}

	protected override void OnPaint()
	{
		if ( LineEdit.Visible )
			return;

		Paint.ClearBrush();
		Paint.ClearPen();

		var rect = LocalRect;

		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( rect, Theme.ControlRadius );
	}


	class PathSegment : Widget
	{
		public string Label { get; private set; }
		public string TargetPath { get; private set; }
		public string RelativePath
		{
			get
			{
				var assetsPath = Project.Current.GetAssetsPath();
				var relativePath = System.IO.Path.GetRelativePath( assetsPath, TargetPath );
				relativePath = relativePath.Replace( '\\', '/' );
				if ( relativePath.StartsWith( ".." ) ) return null;
				return relativePath.ToLower();
			}
		}

		public PathSeparator Separator { get; set; }

		private AssetBrowser Browser { get; init; }

		public PathSegment( AssetBrowser browser, string text, string path ) : base( null )
		{
			Browser = browser;
			Label = text;

			Paint.SetDefaultFont( 8 );
			FixedWidth = MeasureTextWidth( text );

			AcceptDrops = true;
			TargetPath = path;

			Cursor = CursorShape.Finger;
		}

		protected override void OnVisibilityChanged( bool visible )
		{
			base.OnVisibilityChanged( visible );
			Separator?.Visible = visible;
		}

		protected override void OnMousePress( MouseEvent e )
		{
			if ( e.LeftMouseButton )
			{
				if ( !AssetBrowser.Location.TryParse( TargetPath, out var location ) )
					return;

				Browser.NavigateTo( location );
			}
			else if ( e.RightMouseButton )
			{
				var menu = new Menu();
				menu.AddOption( "Show in Explorer", "drive_file_move", action: () => EditorUtility.OpenFileFolder( TargetPath ) );
				menu.AddSeparator();
				var relativePath = RelativePath;
				menu.AddOption( $"Copy Relative Path", "content_paste_go", action: () => EditorUtility.Clipboard.Copy( relativePath ) ).Enabled = relativePath is not null;
				menu.AddOption( $"Copy Absolute Path", "content_paste", action: () => EditorUtility.Clipboard.Copy( TargetPath ) );
				menu.OpenAtCursor();
			}

			e.Accepted = false;
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			Paint.ClearBrush();
			Paint.ClearPen();

			if ( Paint.HasMouseOver )
			{
				Paint.SetBrush( Color.White.WithAlpha( 0.1f ) );
				Paint.DrawRect( LocalRect.Shrink( 0, 2 ) );
			}

			Paint.SetPen( Theme.TextControl );
			Paint.SetDefaultFont( 8 );
			Paint.DrawText( LocalRect.Shrink( 8, 0 ), Label, TextFlag.LeftCenter );
		}

		public override void OnDragHover( DragEvent ev )
		{
			if ( !ev.Data.Files.Any() )
			{
				ev.Action = DropAction.Ignore;
				return;
			}

			ev.Action = ev.HasCtrl ? DropAction.Copy : DropAction.Move;
		}

		public override void OnDragDrop( DragEvent ev )
		{
			ev.Action = ev.HasCtrl ? DropAction.Copy : DropAction.Move;

			foreach ( var file in ev.Data.Files )
			{
				var asset = AssetSystem.FindByPath( file );

				if ( asset is null )
				{
					if ( !Path.Exists( file ) ) continue;

					// This isn't an asset so just copy the file in directly
					var destinationFile = Path.Combine( TargetPath, Path.GetFileName( file ) );

					if ( Directory.Exists( file ) )
					{
						// Move Directory
						EditorUtility.RenameDirectory( file, destinationFile );
						DirectoryEntry.RenameMetadata( file, destinationFile );
					}
					else
					{
						// Move File
						if ( Path.GetFullPath( file ) == Path.GetFullPath( destinationFile ) )
							continue;

						if ( ev.Action == DropAction.Copy )
							File.Copy( file, destinationFile );
						else
							File.Move( file, destinationFile );
					}
				}
				else
				{
					if ( asset.IsDeleted ) continue;
					if ( ev.Action == DropAction.Copy )
						EditorUtility.CopyAssetToDirectory( asset, TargetPath );
					else
						EditorUtility.MoveAssetToDirectory( asset, TargetPath );
				}
			}
		}
	}

	class PathSeparator : Widget
	{
		private ContextMenu menu;

		private AssetBrowser Browser { get; init; }
		public string AbsolutePath { get; init; }

		public PathSeparator( AssetBrowser browser, string absolutePath ) : base( null )
		{
			MinimumWidth = 16;
			AbsolutePath = absolutePath;
			Browser = browser;
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			Paint.ClearBrush();
			Paint.ClearPen();

			if ( Paint.HasMouseOver )
			{
				Paint.SetBrush( Color.White.WithAlpha( 0.1f ) );
				Paint.DrawRect( LocalRect.Shrink( 0, 2 ) );
			}

			var rect = LocalRect;

			Paint.SetPen( Theme.TextControl );

			if ( menu.IsValid() )
			{
				Paint.Rotate( 90, rect.Position + new Vector2( 8, 8 ) );
				Paint.DrawIcon( rect + new Vector2( 4, -2 ), "arrow_forward_ios", 8f, TextFlag.Center );
			}
			else
			{
				Paint.DrawIcon( rect, "arrow_forward_ios", 8f, TextFlag.Center );
			}
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			base.OnMouseClick( e );

			menu?.Close();
			menu = new ContextMenu();

			if ( !AssetBrowser.Location.TryParse( AbsolutePath, out var location ) )
				return;

			foreach ( var subDirectory in location.GetDirectories() )
			{
				menu.AddOption( subDirectory.Name, action: () => Browser.NavigateTo( subDirectory ) );
			}

			menu.OpenAt( ScreenRect.BottomLeft );

			e.Accepted = true;
		}
	}

	class PathElipses : Widget
	{
		private ContextMenu menu;

		private AssetBrowser Browser { get; init; }
		public List<(string Label, string Path)> Paths { get; init; }

		public PathElipses( AssetBrowser browser ) : base( null )
		{
			FixedWidth = 32;
			Paths = new();
			Browser = browser;
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			Paint.ClearBrush();
			Paint.ClearPen();

			if ( Paint.HasMouseOver )
			{
				Paint.SetBrush( Color.White.WithAlpha( 0.1f ) );
				Paint.DrawRect( LocalRect.Shrink( 0, 2 ) );
			}

			Paint.SetPen( Theme.TextControl );
			Paint.SetDefaultFont( 8 );
			Paint.DrawText( LocalRect.Shrink( 8, 0 ), "...", TextFlag.Center );
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			base.OnMouseClick( e );

			menu?.Close();
			menu = new ContextMenu();

			foreach ( var entry in Paths.Reverse<(string Label, string Path)>() )
			{
				if ( !AssetBrowser.Location.TryParse( entry.Path, out var location ) )
					continue;

				menu.AddOption( entry.Label, action: () => Browser.NavigateTo( location ) );
			}

			menu.OpenAt( ScreenRect.BottomLeft );

			e.Accepted = true;
		}
	}
}
