using Sandbox.Mounting;

namespace Editor;

public class MountsAssetLocations : AssetLocations, IMountEvents
{
	private string _filter;
	private readonly HashSet<string> _initialisedGroups = new();

	public MountsAssetLocations( MountsAssetBrowser parent ) : base( parent )
	{

	}

	public void SetFilter( string filter )
	{
		_filter = filter;
		BuildLocations();
	}

	protected override void BuildLocations()
	{
		Clear();

		var entries = Directory.GetAll()
			.Where( Matches )
			.OrderBy( x => x.Title )
			.ToArray();

		AddGroup( "Installed", "check_circle", entries.Where( x => x.Available ), open: true );
		AddGroup( "Not Installed", "remove_circle", entries.Where( x => !x.Available ), open: false );
	}

	bool Matches( MountInfo entry )
	{
		return string.IsNullOrWhiteSpace( _filter ) || entry.Title.Contains( _filter, StringComparison.OrdinalIgnoreCase );
	}

	void AddGroup( string title, string icon, IEnumerable<MountInfo> entries, bool open )
	{
		if ( !entries.Any() )
			return;

		var header = new TreeNode.Header( icon, title, showCounts: true ) { Value = title };
		AddItem( header );

		foreach ( var entry in entries )
		{
			var mount = Directory.Get( entry.Ident );
			header.AddItem( new MountSourceNode( new MountLocation( mount ), mount.IsMounted ) );
		}

		if ( _initialisedGroups.Add( title ) && open )
			Open( header );
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var menu = new ContextMenu();

		foreach ( var entry in Directory.GetAll().OrderBy( x => x.Title ) )
		{
			var m = menu.AddOption( entry.Title );
			m.Checkable = true;
			m.Checked = entry.Mounted;
			m.Enabled = entry.Available;
			m.Toggled = ( b ) =>
			{
				// Do we need to show progress here, maybe?
				EditorUtility.Mounting.SetMounted( entry.Ident, b );
			};
		}

		menu.AddSeparator();

		menu.AddOption( "Refresh All", "refresh", async () =>
		{
			foreach ( var e in Directory.GetAll() )
			{
				await EditorUtility.Mounting.Refresh( e.Ident );
			}

			Rebuild();

		} );

		menu.OpenAtCursor();
		e.Accepted = true;
	}

	void IMountEvents.OnMountEnabled( BaseGameMount source )
	{
		BuildLocations();
	}

	void IMountEvents.OnMountDisabled( BaseGameMount source )
	{
		BuildLocations();
	}
}
