using Sandbox;
using Sandbox.Mounting;
using MountDirectory = Sandbox.Mounting.Directory;

namespace MenuProject.Modals;

public partial class PackageSelectionModal
{
	/// <summary>
	/// Includes Workshop and mounted games as map sources.
	/// </summary>
	protected bool ShowMapSources { get; set; }

	/// <summary>
	/// Called with the chosen mounted scene's path after closing the picker.
	/// </summary>
	protected Action<string> OnMountedMapSelected { get; set; }

	string selectedMount;
	string selectedMountTitle;
	ResourceLoader[] mountedMaps = [];
	bool IsMountedSource => selectedMount is not null;

	static IEnumerable<ResourceLoader> GetMountMaps( string ident )
	{
		if ( MountDirectory.Get( ident ) is not { IsMounted: true } mount ) return [];

		// Match the sidebar count to the maps that can actually be selected.
		return mount.GetAll( ResourceType.Scene ).Where( scene => !scene.Flags.Contains( ResourceFlags.DeveloperOnly ) );
	}

	/// <summary>
	/// Selects the mounted game containing the supplied map, when it is available.
	/// </summary>
	protected void SetSelectedMount( string map )
	{
		var isMounted = MountUtility.TryParse( map, out string ident )
			&& MountDirectory.Get( ident ) is { IsMounted: true };

		SelectSource( isMounted ? ident : null );
	}

	void SelectSource( string ident )
	{
		if ( ident is not null && MountDirectory.Get( ident ) is not { IsMounted: true } ) return;

		selectedMount = ident;
		selectedMountTitle = ident is null ? null : MountDirectory.Get( ident ).Title;
		searchText = "";
		debouncedSearch = "";
		searchPending = false;
		RefreshMountedMaps();
		StateHasChanged();
	}

	void RefreshMountedMaps()
	{
		if ( selectedMount is null )
		{
			mountedMaps = [];
			return;
		}

		mountedMaps = GetMountMaps( selectedMount )
			.Where( scene => string.IsNullOrWhiteSpace( searchText ) || scene.Name.Contains( searchText, StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( scene => scene.Name )
			.ToArray();
	}

	void SelectMountedMap( string path )
	{
		CloseModal( true );
		OnMountedMapSelected?.Invoke( path );
	}
}
