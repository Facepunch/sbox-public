using Sandbox.Mounting;

/// <summary>
/// This component has a kill icon that can be used in the killfeed, or somewhere else.
/// </summary>
[Title( "#spawnmenu.tab.games" ), Order( 2050 ), Icon( "🧩" )]
public class MountsPage : BaseSpawnMenu
{
	Dictionary<string, SpawnMenuOption> mountOptions = new ();

	protected override void OnVisibilityChanged()
	{
		base.OnVisibilityChanged();

		if ( IsVisible )
		{
			UpdateMounts();
		}
	}

	protected override void Rebuild()
	{
		var all = Sandbox.Mounting.Directory.GetAll().ToArray();
		var available = all.Where( x => x.Available ).ToArray();
		var unavailable = all.Where( x => !x.Available ).ToArray();

		if ( available.Any() )
		{
			AddHeader( "#spawnmenu.section.local" );

			foreach ( var entry in available.OrderBy( x => x.Title ) )
			{
				var ident = entry.Ident;
				var o = AddOption( "sports_esports", entry.Title, () => new MountContent() { Ident = ident } );
				o.Enabled = entry.Mounted;
				o.Key = ident;
				o.LoadChildren = () => FolderOptions( ident, Sandbox.Mounting.Directory.Get( ident )?.RootFolder );

				mountOptions[entry.Ident] = o;
			}
		}

		if ( unavailable.Any() )
		{
			AddHeader( "#spawnmenu.section.not_installed" );
			
			foreach ( var entry in unavailable.OrderBy( x => x.Title ) )
			{
				var o = AddOption( entry.Title, null );
				o.Enabled = false;

				mountOptions[entry.Ident] = o;
			}
		}
	}

	/// <summary>
	/// The folders under this one that have models somewhere inside, each opening onto its own.
	/// </summary>
	IEnumerable<SpawnMenuOption> FolderOptions( string ident, ResourceFolder folder )
	{
		if ( folder is null ) yield break;

		foreach ( var child in folder.Folders.OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase ) )
		{
			if ( !child.ContainsType( ResourceType.Model ) ) continue;

			var captured = child;
			yield return new SpawnMenuOption
			{
				Icon = "folder",
				Name = child.Name,
				Key = $"{ident}:{child.Path}",
				Tokenize = false,
				PanelCreator = () => new MountContent() { Ident = ident, Folder = captured },
				LoadChildren = captured.Folders.Any( x => x.ContainsType( ResourceType.Model ) ) ? () => FolderOptions( ident, captured ) : null
			};
		}
	}

	private void UpdateMounts()
	{
		foreach ( var entry in Sandbox.Mounting.Directory.GetAll() )
		{
			if ( mountOptions.TryGetValue( entry.Ident, out var o ) )
			{
				o.Enabled = entry.Mounted;
			}
		}

		StateHasChanged();
	}
}
