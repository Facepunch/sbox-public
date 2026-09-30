using System;

namespace Sandbox;

public static partial class SandboxMenuExtensions
{
	/// <summary>
	/// Mark this package as a favourite. Applied right away and settled on the backend's answer.
	/// </summary>
	public static async Task SetFavouriteAsync( this Package package, bool state )
	{
		var previous = package.Interaction;
		var previousCount = package.Favourited;

		var i = package.Interaction;
		i.Favourite = state;
		i.FavouriteCreated = DateTime.UtcNow;
		package.Interaction = i;
		if ( state != previous.Favourite ) package.Favourited = Math.Max( 0, previousCount + (state ? 1 : -1) );

		try
		{
			// Success is false when nothing changed, State is the server's truth either way
			var f = await Sandbox.Backend.Package.SetFavourite( package.FullIdent, state );

			i = package.Interaction;
			i.Favourite = f.State;
			package.Interaction = i;
			package.Favourited = f.Total;

			AccountInformation.Favourites.RemoveAll( x => x.FullIdent == package.FullIdent );

			if ( f.State && package is RemotePackage rp )
			{
				AccountInformation.Favourites.Add( rp );
			}
		}
		catch ( Exception e )
		{
			package.Interaction = previous;
			package.Favourited = previousCount;
			Log.Warning( $"Couldn't set favourite {package.FullIdent} ({e.Message})" );
		}
	}

	/// <summary>
	/// Add your vote for this package. Applied right away and rolled back if the backend refuses.
	/// </summary>
	public static async Task SetVoteAsync( this Package package, bool up )
	{
		var value = up ? 0 : 1;

		// already is this
		if ( package.Interaction.Rating.HasValue && package.Interaction.Rating.Value == value )
			return;

		var previous = package.Interaction;
		var previousUp = package.VotesUp;
		var previousDown = package.VotesDown;

		var i = package.Interaction;
		i.Rating = value;
		i.RatingCreated = DateTime.UtcNow;
		package.Interaction = i;
		if ( previous.Rating == 0 ) package.VotesUp = Math.Max( 0, previousUp - 1 );
		if ( previous.Rating == 1 ) package.VotesDown = Math.Max( 0, previousDown - 1 );
		if ( up ) package.VotesUp++; else package.VotesDown++;

		try
		{
			var f = await Sandbox.Backend.Package.SetRating( package.FullIdent, value );
			if ( !f.Success )
			{
				package.Interaction = previous;
				package.VotesUp = previousUp;
				package.VotesDown = previousDown;
				return;
			}

			package.VotesUp = f.VotesUp;
			package.VotesDown = f.VotesDown;
		}
		catch ( Exception e )
		{
			package.Interaction = previous;
			package.VotesUp = previousUp;
			package.VotesDown = previousDown;
			Log.Warning( $"Couldn't rate {package.FullIdent} ({e.Message})" );
		}
	}

	/// <summary>
	/// Hide this package from the local player's discovery and search. False if the backend refused.
	/// </summary>
	public static async Task<bool> SetHiddenAsync( this Package package, bool hidden )
	{
		try
		{
			await Sandbox.Backend.Account.SetPackageHidden( package.FullIdent, hidden );
			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't hide {package.FullIdent} ({e.Message})" );
			return false;
		}
	}

	/// <summary>
	/// Open a modal for the specific package. This will open the correct modal
	/// </summary>
	public static void OpenModal( this Package package )
	{
		if ( package.TypeName == "game" )
		{
			Game.Overlay.ShowGameModal( package.FullIdent );
			return;
		}

		if ( package.TypeName == "map" )
		{
			Game.Overlay.ShowMapModal( package.FullIdent );
			return;
		}

		Game.Overlay.ShowPackageModal( package.FullIdent );
	}
}
