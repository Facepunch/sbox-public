using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// The Avatar page's Workshop tab - the community's clothing, to try on and vote for
/// </summary>
partial class AvatarShelf
{
	/// <summary>
	/// The Workshop's order - newest first, or the best voted ("quality")
	/// </summary>
	static string WorkshopSort = "newest";

	/// <summary>
	/// What's been found on the Workshop for each order and search - kept while the menu is. Null while it's being
	/// looked for.
	/// </summary>
	static readonly Dictionary<string, List<Package>> workshop = new();

	static List<Package> WorkshopItems => workshop.GetValueOrDefault( WorkshopKey );

	static string WorkshopKey => $"{WorkshopSort}|{workshopQuery}";

	/// <summary>
	/// What the Workshop was last searched for - the search, once it's stopped being typed
	/// </summary>
	static string workshopQuery = "";

	void SortWorkshop( string sort )
	{
		if ( WorkshopSort == sort ) return;

		WorkshopSort = sort;
		if ( Picked is not null ) PutDown();
		_ = LoadWorkshop();
		ScrollToTop();
	}

	/// <summary>
	/// The community's clothing, in the order picked - as the avatar editor's Workshop page finds it
	/// </summary>
	static async Task LoadWorkshop()
	{
		var key = WorkshopKey;
		if ( workshop.ContainsKey( key ) ) return;

		workshop[key] = null;

		var found = await Package.FindAsync( $"type:clothing sort:{WorkshopSort} {workshopQuery}".Trim(), 200, 0 );
		workshop[key] = found?.Packages?.ToList() ?? [];

		if ( found is null ) workshop.Remove( key );
	}

	Package Picked;

	bool Previewing;

	/// <summary>
	/// Goes up with each vote - for its card to show it
	/// </summary>
	int votes;

	async void PickPackage( Package package )
	{
		if ( Picked == package )
		{
			PutDown();
			return;
		}

		Viewing = null;
		Picked = package;
		Previewing = true;

		try
		{
			var clothing = await Cloud.Load<Clothing>( package.FullIdent );

			// Picked something else since, or put it away
			if ( Picked != package ) return;

			Draft.Preview( clothing );
			FrameOnItem( clothing );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't try on {package.FullIdent}" );
		}
		finally
		{
			if ( Picked == package ) Previewing = false;
		}
	}

	static bool VotedUp( Package package ) => package.Interaction.Rating == 0;
	static bool VotedDown( Package package ) => package.Interaction.Rating == 1;

	static string VoteClass( Package package ) => VotedUp( package ) ? "voted-up" : VotedDown( package ) ? "voted-down" : "";

	async void Vote( Package package, bool up )
	{
		await package.SetVoteAsync( up );
		votes++;
	}
}
