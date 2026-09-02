namespace Sandbox.PanelGallery;

/// <summary>
/// The renderer test pages - razor panels compiled into this assembly by the razor source
/// generator. They came from the menu addon; the gallery hosts them now.
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
			.OrderBy( x => x.Order )
			.ThenBy( x => x.Title )
			.ToList();

		foreach ( var page in pages )
		{
			var current = page;
			var icon = string.IsNullOrEmpty( current.Icon ) ? "science" : current.Icon;

			Pages.Add( new GalleryPageInfo( current.Title, icon, () => current.Create<Panel>() ) );
		}
	}
}
