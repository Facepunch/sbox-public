
namespace Editor;

public class MountsAssetBrowser : AssetBrowser
{
	protected override string CookieKey => "MountsAssetBrowser";

	public MountsAssetBrowser( Widget parent, List<AssetType> assetTypeFilters ) : base( parent, assetTypeFilters )
	{
	}

	protected override void CreateLocations()
	{
		AssetLocations = new MountsAssetLocations( this );
		AssetLocations.Browser = this;
		AssetLocations.OnFolderSelected = ( directoryInfo ) => NavigateTo( directoryInfo );
	}

	protected override Widget BuildLocationsPanel()
	{
		var panel = new Widget( this );
		panel.Layout = Layout.Column();
		panel.MinimumWidth = 200;

		var search = new LineEdit( panel );
		search.PlaceholderText = "⌕  Search Games";
		search.TextChanged += ( value ) => (AssetLocations as MountsAssetLocations)?.SetFilter( value );

		panel.Layout.Add( search );
		panel.Layout.AddSpacingCell( 2 );
		panel.Layout.Add( AssetLocations, 1 );

		return panel;
	}
}
