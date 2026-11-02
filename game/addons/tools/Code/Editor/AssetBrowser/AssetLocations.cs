using System.IO;

namespace Editor;

public class AssetLocations : TreeView
{
	/// <summary>
	/// Called when a folder is selected.
	/// </summary>
	public Action<LocalAssetBrowser.Location> OnFolderSelected;

	/// <summary>
	/// Called when a "filter" is selected, i.e. "@recent" or "t:vmdl".
	/// </summary>
	public Action<string> OnFilterSelected;

	public static bool IncludePathNames
	{
		get => ProjectCookie.Get( "AssetLocations.IncludePathNames", false );
		set => ProjectCookie.Set( "AssetLocations.IncludePathNames", value );
	}

	public AssetBrowser Browser;

	public AssetLocations( AssetBrowser parent ) : base( parent )
	{
		// Sorry logic
		SetStyles( $"AssetLocations{{ border: 8px solid {Theme.ControlBackground.Hex}; }}" );

		MinimumSize = 200;
		ItemSelected = OnItemClicked;

		BuildLocations();
	}

	[Event( "assetsystem.newfolder" )]
	void Refresh()
	{
		Clear();
		BuildLocations();
	}

	protected virtual void BuildLocations()
	{

	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );

		base.OnPaint();
	}

	protected void OnItemClicked( object value )
	{
		if ( value is not AssetBrowser.Location location )
			return;

		OnFolderSelected?.Invoke( location );
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		// Avoid calling OnItemActivated if we double click the expand button
		var item = GetItemAt( e.LocalPosition );
		if ( e.LeftMouseButton && item is not null && item.HasChildren )
		{
			var expandRect = item.Rect;
			expandRect.Left += IndentWidth * item.Column;
			expandRect.Width = ExpandWidth;

			if ( expandRect.IsInside( e.LocalPosition ) )
			{
				e.Accepted = true;
				return;
			}
		}

		base.OnDoubleClick( e );
	}

	protected override void OnDragHoverItem( DragEvent ev, VirtualWidget item )
	{
		base.OnDragHoverItem( ev, item );
		ev.Action = DropAction.Move;
	}

	protected override void OnDropOnItem( DragEvent ev, VirtualWidget item )
	{
		if ( !ev.Data.HasFileOrFolder )
			return;

		if ( item.Object is not TreeNode node )
			return;

		if ( node.Value is not DirectoryInfo dirInfo )
			return;

		var directory = dirInfo.FullName;

		foreach ( var file in ev.Data.Files )
		{
			var asset = AssetSystem.FindByPath( file );

			if ( asset == null )
				continue;

			ev.Action = ev.HasCtrl ? DropAction.Copy : DropAction.Move;

			if ( ev.HasCtrl )
				EditorUtility.CopyAssetToDirectory( asset, directory );
			else
				EditorUtility.MoveAssetToDirectory( asset, directory );
		}
	}

	/// <summary>
	/// Finds a node with a path and toggles it open
	/// </summary>
	public void SelectFolder( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			throw new ArgumentNullException( nameof( path ) );

		var queue = new Queue<TreeNode>( _items.OfType<TreeNode>() );

		UnselectAll( true );

		while ( queue.Count > 0 )
		{
			var node = queue.Dequeue();

			if ( node is FolderNode folderNode )
			{
				if ( folderNode.Value.Path.Equals( path, StringComparison.OrdinalIgnoreCase ) )
				{
					SetSelected( folderNode, true, true );
					ScrollTo( folderNode.Value );
					return;
				}
			}

			foreach ( var child in node.Children )
			{
				queue.Enqueue( child );
			}
		}
	}

	[Shortcut( "editor.delete", "DEL" )]
	void DeleteSelectedFolder()
	{
		// Always handle DEL here, even when there's nothing deletable, so it doesn't fall through to deleting scene objects
		if ( SelectedItems.FirstOrDefault() is DiskLocation location )
		{
			DeleteFolder( location );
		}
	}

	/// <summary>
	/// Ask to delete a folder. If the browser is inside it, move up to its parent afterwards.
	/// </summary>
	internal void DeleteFolder( DiskLocation location )
	{
		if ( !CanModifyFolder( location ) )
			return;

		var directory = new DirectoryInfo( location.Path );

		AssetList.ConfirmDeleteFolder( directory, () =>
		{
			var current = Browser?.CurrentLocation?.Path?.NormalizeFilename( false, false ).TrimEnd( '/' );
			var deleted = directory.FullName.NormalizeFilename( false, false ).TrimEnd( '/' );

			if ( current is null || directory.Parent is null )
				return;

			if ( current.Equals( deleted, StringComparison.OrdinalIgnoreCase ) || current.StartsWith( deleted + "/", StringComparison.OrdinalIgnoreCase ) )
			{
				Browser.NavigateTo( directory.Parent.FullName );
			}
		} );
	}

	/// <summary>
	/// Can this folder be deleted or renamed from the sidebar? Not if it's (or contains) a project, library or base content root.
	/// </summary>
	internal static bool CanModifyFolder( DiskLocation location )
	{
		if ( location.IsRoot || !Directory.Exists( location.Path ) )
			return false;

		// IsRoot only knows about core and citizen when the path looks like a dev checkout, so check the real roots too
		var roots = EditorUtility.Projects.GetAll().Select( x => x.GetRootPath() )
			.Append( Project.Current?.GetRootPath() )
			.Append( FileSystem.Root.GetFullPath( "/core/" ) )
			.Append( FileSystem.Root.GetFullPath( "/addons/citizen/assets/" ) );

		var path = NormalizeFolder( location.Path );

		foreach ( var root in roots )
		{
			if ( string.IsNullOrEmpty( root ) )
				continue;

			var rootPath = NormalizeFolder( root );
			if ( rootPath.Equals( path, StringComparison.OrdinalIgnoreCase ) || rootPath.StartsWith( path + "/", StringComparison.OrdinalIgnoreCase ) )
				return false;
		}

		return true;
	}

	static string NormalizeFolder( string path ) => Path.GetFullPath( path ).NormalizeFilename( false, false ).TrimEnd( '/' );

	public void SetIncludePathNames( bool enabled )
	{
		IncludePathNames = enabled;
	}
}
