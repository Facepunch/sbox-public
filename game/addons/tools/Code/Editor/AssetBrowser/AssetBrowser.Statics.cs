namespace Editor;

public partial class AssetBrowser
{
	/// <summary>
	/// Gets the closest open <see cref="Project"/> to the current focused widget
	/// </summary>
	public static WrappedAssetBrowser Get()
	{
		WrappedAssetBrowser browser;

		// 1. try to find one for the current focused window
		if ( Application.FocusWidget?.GetWindow() is DockWindow dockable )
		{
			browser = dockable.DockManager.GetDockWidget( "Asset Browser" ) as WrappedAssetBrowser;
			if ( browser.IsValid() ) return browser;
		}

		// 2. try the primary instance
		browser = MainAssetBrowser.Instance;
		if ( browser.IsValid() ) return browser;

		return null;
	}

	/// <summary>
	/// Gets the closest open <see cref="Project"/> to the current focused widget, or creates a new one
	/// </summary>
	private static WrappedAssetBrowser GetOrCreate()
	{
		if ( Get() is { } browser )
			return browser;

		return EditorWindow.DockManager.Create<MainAssetBrowser>();
	}

	/// <summary>
	/// Opens an AssetBrowser to the <paramref name="asset"/>, raising the window into view.
	/// Clears any active search filter so the asset isn't hidden.
	/// If no AssetBrowser is open already, a new one will be opened. 
	/// </summary>
	public static void OpenTo( Asset asset, bool skipEvents = false )
	{
		var wrapped = GetOrCreate();
		EditorWindow.DockManager.RaiseDock( wrapped );

		var browser = wrapped.GetBrowser( asset );
		wrapped.SwitchTo( browser );
		browser.Focus( true );
		browser.Search.Clear();
		browser.FocusOnAsset( asset, skipEvents );
	}

	/// <summary>
	/// Opens an AssetBrowser to the <paramref name="entry"/> location, raising the window into view.
	/// If no AssetBrowser is open already, a new one will be opened. 
	/// </summary>
	public static void OpenTo( AssetEntry entry, bool skipEvents = false )
	{
		if ( entry.Asset is { } asset )
		{
			OpenTo( asset, skipEvents );
			return;
		}

		var wrapped = GetOrCreate();
		EditorWindow.DockManager.RaiseDock( wrapped );

		var browser = wrapped.GetBrowser( entry );
		wrapped.SwitchTo( browser );
		browser.Focus( true );
		browser.Search.Clear();
		browser.NavigateTo( entry.AbsolutePath );
	}
}
