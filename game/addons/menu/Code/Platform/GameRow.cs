using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// A row of games on a shelf - what it's called, where its games come from, and them once they've been fetched
/// </summary>
public sealed class GameRow( string title, string icon, Func<Task<WorkshopItemDetails[]>> find, float maxAge = 0,
	Storage.SortOrder? browseSort = null )
{
	public string Title => title;
	public string Icon => icon;

	/// <summary>
	/// What its See more browses the Workshop by - null for none
	/// </summary>
	public Storage.SortOrder? BrowseSort => browseSort;

	/// <summary>
	/// The games, as the Workshop has them - null until they've been fetched
	/// </summary>
	public WorkshopItemDetails[] Games { get; private set; }

	RealTimeSince sinceFetched;

	public async Task RefreshAsync()
	{
		if ( Games is not null && sinceFetched < maxAge ) return;

		var games = await find();
		if ( games is null ) return;

		Games = games;
		sinceFetched = 0;
		LoadArt( games );
	}

	public void MoveToFront( ulong id )
	{
		if ( Games?.FirstOrDefault( x => x.Id == id ) is not { } game ) return;

		Games = Games.Where( x => x != game ).Prepend( game ).ToArray();
	}

	/// <summary>
	/// The rows' art, by url - held, or textures only being held weakly, it'd be gone again before a shelf's opened
	/// </summary>
	static readonly Dictionary<string, Texture> Art = new();

	static void LoadArt( IEnumerable<WorkshopItemDetails> games )
	{
		foreach ( var url in games.Select( x => x.Thumb ).Where( x => x is not null ) )
		{
			if ( !Art.ContainsKey( url ) )
				Art[url] = Texture.Load( url, false );
		}
	}
}
