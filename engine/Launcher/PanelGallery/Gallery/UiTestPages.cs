namespace Sandbox.PanelGallery;

/// <summary>
/// Discovers Razor test pages for the gallery sidebar.
/// </summary>
public static class UiTestPages
{
	/// <summary>
	/// The test pages as sidebar entries.
	/// </summary>
	public static List<GalleryPageInfo> Pages { get; } = new();

	/// <summary>
	/// Pull every UiTestPage out of the assembly.
	/// </summary>
	internal static void Register( System.Reflection.Assembly assembly )
	{
		var pages = assembly.GetTypes()
			.Where( x => !x.IsAbstract && x.IsSubclassOf( typeof( global::PanelGallery.UiTests.UiTestPage ) ) )
			.Select( x => Game.TypeLibrary.GetType( x ) )
			.Where( x => x is not null )
			.OrderBy( x => x.Title, StringComparer.OrdinalIgnoreCase )
			.ToList();

		foreach ( var page in pages )
		{
			var current = page;
			var icon = string.IsNullOrEmpty( current.Icon ) ? "science" : current.Icon;

			// A leading slash selects a top-level gallery category; other groups live under CSS.
			var folder = current.Group?.StartsWith( '/' ) == true ? current.Group.TrimStart( '/' )
				: string.IsNullOrEmpty( current.Group ) ? "Css Styles" : $"Css Styles/{current.Group}";
			Pages.Add( new GalleryPageInfo( current.Title, icon, () => current.Create<Panel>(), folder ) );
		}
	}
}
