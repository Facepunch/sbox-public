using System.IO;

namespace Editor.ProjectSettingPages;


public class WildcardPathWidget : Widget
{
	public Action ValueChanged { get; set; }

	public DirectoryInfo Directory
	{
		set
		{
			var files = value
				.GetFiles( "*", SearchOption.AllDirectories )
				.Select( x => ToLocal( value, x ) )
				.Where( x => PreFilter( x ) ) // filter out JUNK
				.OrderBy( x => x )
				.ToArray();

			listView?.SetItems( files );
		}
	}

	/// <summary>
	/// A list of wildcard files that we don't even want to show here
	/// </summary>
	public string[] IgnoreFiles { get; } = new string[]
	{
		"*.git/*",
		"*.vs/*",
		"*.mayaSwatches/*",
		".localization/*", // will be automatically handled
		".addon",
		".sbproj",
		".gitignore",
		".gitattributes",
		"*.meta",
		".editorconfig",
		"*.csproj",
		"*.sln",
		"*.slnx",
		"*.lutconfig",
		"*.lutignore",
		"*.cs", // we don't ever upload .cs files
		"*.razor", // we don't ever upload .razor files
		"*_c", // don't show compiled files, we'll treat vmdl as wanting the the vmdl_c etc
		"*/obj/*",
		"obj/*",
		"*/launchSettings.json",
		"*_bakeresourcecache.vpk",
		"*_bakeresourcecache/*"
	};

	/// <summary>
	/// These paths will be collapsed so that files look like they're in the root
	/// </summary>
	public string[] CollapsePaths { get; set; } = new string[] { };

	/// <summary>
	/// Hide all assets. That is to say, hide all files that compile into something. We
	/// do this on the resources tab because all of the compiled files are included anyway!
	/// </summary>
	public bool HideAssets { get; set; }

	string[] Wildcards;
	TextEdit textEdit;
	ListView listView;

	public string Value
	{
		get => textEdit.PlainText;
		set => textEdit.PlainText = value;
	}

	public WildcardPathWidget( Widget parent, bool showListView = true ) : base( parent )
	{
		textEdit = new TextEdit( this );
		textEdit.TextChanged = x => ValueChanged?.Invoke();
		textEdit.AcceptDrops = false;

		Layout = Layout.Row();
		Layout.Spacing = 16;
		Layout.Add( textEdit );

		AcceptDrops = true;

		if ( showListView )
		{
			listView = new ListView( this );
			listView.ItemPaint = PaintFileItem;
			listView.ItemSize = new Vector2( -1, 16 );
			listView.OnPaintOverride += PaintListBackground;
			listView.ItemContextMenu += OnItemContextMenu;

			Layout.Add( listView );
		}
	}

	private void OnItemContextMenu( object obj )
	{
		var fn = obj as string;

		var ext = System.IO.Path.GetExtension( fn );
		var dir = System.IO.Path.GetDirectoryName( fn ).Replace( "\\", "/" );

		var menu = new ContextMenu( this );

		menu.AddOption( $"Add {fn}", action: () => Add( fn ) );
		menu.AddOption( $"Add *{ext}", action: () => Add( $"*{ext}" ) );
		menu.AddOption( $"Add {dir}/*", action: () => Add( $"{dir}/*" ) );

		menu.OpenAtCursor( false );
	}

	public override void OnDragDrop( DragEvent ev )
	{
		var assetsFolder = Project.Current.GetAssetsPath().ToLower();
		foreach ( var file in ev.Data.Files )
		{
			// Get relative path only if the path provided is absolute
			var relativePath = file;
			if ( Path.IsPathRooted( file ) )
			{
				relativePath = Path.GetRelativePath( assetsFolder, file ).Replace( '\\', '/' );
			}

			// If this is a directory, add it with a wildcard
			if ( System.IO.Directory.Exists( file ) )
			{
				relativePath = $"{relativePath}/*";
			}

			Add( relativePath );
		}
	}

	private bool PaintListBackground()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );

		if ( listView.IsValid() )
		{
			Paint.DrawRect( listView.LocalRect );
		}

		return false;
	}

	void Add( string str )
	{
		var text = textEdit.PlainText;

		// Add newline if necessary
		if ( text.Length > 0 && !text.EndsWith( "\n" ) )
		{
			text += "\n";
		}

		text += $"{str}";
		textEdit.PlainText = text;
	}

	private void PaintFileItem( VirtualWidget widget )
	{
		var fn = widget.Object as string;

		Test( fn, Wildcards, out var inc, out var exc );

		var col = Color.Gray;
		if ( inc ) col = Color.White;

		var hasPath = fn.Contains( '/' );

		var filename = hasPath ? System.IO.Path.GetFileName( fn ) : fn;
		var path = hasPath ? "/" + fn.Substring( 0, fn.Length - filename.Length ) : "/";

		if ( widget.Hovered )
		{
			Paint.ClearPen();
			Paint.SetBrush( Color.White.WithAlpha( 0.1f ) );
			Paint.DrawRect( widget.Rect.Grow( 2 ), 3 );
		}

		Paint.SetPen( col.WithAlpha( 0.7f ) );
		var r = Paint.DrawText( widget.Rect, path, TextFlag.LeftCenter );
		r.Left = r.Right + 2;
		r.Right = widget.Rect.Right;

		Paint.SetPen( col.WithAlpha( 1f ) );
		Paint.DrawText( r, filename, TextFlag.LeftCenter );
	}

	string _oldValue;

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( _oldValue == textEdit.PlainText )
			return;

		_oldValue = textEdit.PlainText;
		UpdateInfo();
	}

	void UpdateInfo()
	{
		Wildcards = textEdit.PlainText.Split( "\n", System.StringSplitOptions.RemoveEmptyEntries )
			.Select( x => x.Replace( "\\", "/" ).TrimStart( '/' ) )
			.ToArray();

		listView?.Update();
	}

	private bool PreFilter( string x )
	{
		//
		// Here we try to cut down on clutter in the Resource Files project tab.
		// Basically hiding things that are ALWAYS shipped with the game or have
		// no business being shipped as a free-floating file.
		//
		if ( HideAssets )
		{
			if ( x.EndsWith( "_c" ) ) return false;
			if ( x.EndsWith( ".vpk" ) ) return false;

			var a = AssetSystem.FindByPath( x );

			if ( a is not null && a.AssetType.IsGameResource ) return false;
			if ( a is not null && a.AssetType == AssetType.Model ) return false;
			if ( a is not null && a.AssetType == AssetType.Material ) return false;
			if ( a is not null && a.AssetType == AssetType.Texture ) return false;
			if ( a is not null && a.AssetType == AssetType.MapFile ) return false;
#pragma warning disable CS0618
			if ( a is not null && a.AssetType == AssetType.LegacyAnimationGraph ) return false;
#pragma warning restore CS0618

			// hide any image files that aren't being used by 
			//if ( a is not null && a.AssetType == AssetType.ImageFile )
			//{
			//		if ( a.GetDependants( true ).Any() )
			//			return false;
			//}
		}

		foreach ( var wc in IgnoreFiles )
		{
			if ( x.WildcardMatch( wc ) )
			{
				return false;
			}
		}

		return true;
	}

	private void Test( string x, string[] wildcards, out bool include, out bool exclude )
	{
		include = false;
		exclude = false;

		if ( wildcards == null )
			return;

		foreach ( var wc in wildcards )
		{
			if ( x.WildcardMatch( wc ) )
			{
				include = true;
			}
		}
	}

	string ToLocal( DirectoryInfo directory, FileInfo file )
	{
		var path = file.FullName.Replace( directory.FullName, "" ).TrimStart( '\\' ).Replace( "\\", "/" );

		foreach ( var subPath in CollapsePaths )
		{
			var cleanSub = subPath.Replace( "\\", "/" ).TrimStart( '/' );
			if ( !cleanSub.EndsWith( "/" ) ) cleanSub = $"{cleanSub}/";

			if ( path.StartsWith( cleanSub ) )
			{
				path = path.Substring( cleanSub.Length );
				break;
			}
		}


		return path;
	}
}
