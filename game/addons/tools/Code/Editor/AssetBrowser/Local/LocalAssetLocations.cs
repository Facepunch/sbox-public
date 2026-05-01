using System.IO;

namespace Editor;

public class LocalAssetLocations : AssetLocations
{
	TreeNode LibrariesNode;
	TreeNode PinsNode;
	TreeNode PinsNodeSpacer;
	TreeNode BaseContentNode;

	internal static List<string> Pins = null;

	public LocalAssetLocations( LocalAssetBrowser parent ) : base( parent )
	{
		if ( Pins == null )
		{
			Pins = ProjectCookie.Get<List<string>>( "AssetLocations.Pins", new() );
		}

		RefreshPins();
	}

	protected override void BuildLocations()
	{
		//
		//
		//
		{
			var recents = new FolderNode( new RecentsLocation() );
			AddItem( recents );

			var everything = new FolderNode( new EverythingLocation() );
			AddItem( everything );
		}

		//
		// Pins
		//
		{
			AddItem( new TreeNode.Spacer( 10 ) );
			PinsNode = new TreeNode.SmallHeader( "push_pin", "Pinned" );
			PinsNode.Enabled = false;
			AddItem( PinsNode );

			PinsNodeSpacer = AddItem( new TreeNode.Spacer( 10 ) );
		}

		//
		// Project
		//
		{
			var d = new DirectoryInfo( Project.Current.GetRootPath() );
			var loc = new DiskLocation( d );
			var ProjectNode = new ProjectNode( loc );

			AddItem( ProjectNode );
			Open( ProjectNode );
		}

		//
		// Parent Project
		//
		var parentPackage = Project.Current.Config.GetMetaOrDefault( "ParentPackage", "" );
		if ( Project.Current.Config.Type == "addon" && !string.IsNullOrWhiteSpace( parentPackage ) && Package.TryGetCached( parentPackage, out Package package ) )
		{
			AddItem( new TreeNode.Spacer( 10 ) );
			var loc = new PackageLocation( package );
			var ProjectNode = new ProjectNode( loc );

			AddItem( ProjectNode );
			Open( ProjectNode );
		}

		//
		// Libraries
		//
		{
			AddItem( new TreeNode.Spacer( 10 ) );
			LibrariesNode = new TreeNode.SmallHeader( "class", "Libraries" );
			AddItem( LibrariesNode );
			Open( LibrariesNode );
		}

		//
		// Base
		//
		BaseContentNode = new TreeNode.SmallHeader( "dns", "s&box" );

		{
			var d = new DirectoryInfo( FileSystem.Root.GetFullPath( "/addons/citizen/assets/" ) );
			var loc = new DiskLocation( d );
			BaseContentNode.AddItem( new FolderNode( loc ) );
		}

		{
			var d = new DirectoryInfo( FileSystem.Root.GetFullPath( "/core/" ) );
			var loc = new DiskLocation( d );
			BaseContentNode.AddItem( new FolderNode( loc ) );
		}

		AddItem( BaseContentNode );
		Open( BaseContentNode );

		UpdateAddonList();
	}

	[Event( "localaddons.changed" )]
	void UpdateAddonList()
	{
		LibrariesNode.Clear();

		foreach ( var project in EditorUtility.Projects.GetAll().OrderBy( x => x.Config.Title ) )
		{
			if ( project == Project.Current )
				continue;

			// Don't show menu addon in the libraries list
			if ( project.Config.Ident == "menu" )
				continue;

			var d = new DirectoryInfo( project.GetRootPath() );
			if ( !d.Exists )
				continue;

			var loc = new DiskLocation( d );
			LibrariesNode.AddItem( new FolderNode( loc ) );
		}

		LibrariesNode.Enabled = LibrariesNode.HasChildren;
	}

	internal void AddPinnedFolder( string filter )
	{
		// We gotta compare each filter separately, in case they are out of order.
		var filters = filter.SplitQuotesStrings();
		foreach ( var entry in Pins )
		{
			var entries = entry.SplitQuotesStrings();
			if ( entries.All( filters.Contains ) && filters.All( entries.Contains ) ) return;
		}

		Pins.Add( filter );

		ProjectCookie.Set( $"AssetLocations.Pins", Pins );
		RefreshPins();
	}

	void RemovePinnedFolder( string filters )
	{
		Pins.Remove( filters );

		ProjectCookie.Set( $"AssetLocations.Pins", Pins );
		RefreshPins();
	}

	[EditorEvent.Frame]
	public void OnFrame()
	{
		if ( PinsNode?.Children.Count() == Pins.Count() && !_pinsDirty )
			return;

		_pinsDirty = false;

		PinsNode?.Clear();

		if ( PinsNode == null )
		{
			PinsNode = new TreeNode.Header( "push_pin", "Favorites" );

			AddItem( PinsNode );
		}

		PinsNode.Enabled = Pins.Any();
		PinsNodeSpacer.Enabled = PinsNode.Enabled;
		Rebuild();

		foreach ( var pin in Pins )
		{
			var di = new DirectoryInfo( pin );

			if ( !di.Exists )
				continue;

			var item = new PinnedFolderNode( new DiskLocation( di ) );
			item.OnContextMenuOpen = () =>
			{
				var m = new ContextMenu();
				m.AddOption( "Unpin", "close", () => { RemovePinnedFolder( pin ); } );

				m.OpenAt( Application.CursorPosition );
			};

			PinsNode.AddItem( item );
		}

		Open( PinsNode );
	}

	private bool _pinsDirty = false;

	void RefreshPins()
	{
		_pinsDirty = true;
	}
}
