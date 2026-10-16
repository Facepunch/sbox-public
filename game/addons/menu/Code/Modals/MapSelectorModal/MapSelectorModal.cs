using Sandbox.UI;

namespace MenuProject.Modals;

/// <summary>
/// Selects a Workshop or mounted map using the shared package picker.
/// </summary>
[StyleSheet( "/Modals/PackageSelectionModal.razor.scss" )]
public class MapSelectorModal : PackageSelectionModal
{
	/// <summary>
	/// Called with the selected package identifier or mounted scene path.
	/// </summary>
	public Action<string> OnSelected { get; set; }

	/// <summary>
	/// Enables Workshop maps and mounted games in the shared picker.
	/// </summary>
	public MapSelectorModal()
	{
		PackageQuery = "type:map sort:trending";
		ShowMapSources = true;
		OnPackageSelected = package => OnSelected?.Invoke( package.FullIdent );
		OnMountedMapSelected = path => OnSelected?.Invoke( path );
	}

	/// <summary>
	/// Starts on the mounted game containing the current map, or on Workshop.
	/// </summary>
	public void SetSelected( string map )
	{
		SetSelectedMount( map );
	}
}
