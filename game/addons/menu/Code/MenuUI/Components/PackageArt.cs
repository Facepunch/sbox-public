using Sandbox;

namespace MenuProject;

/// <summary>
/// Pictures of a game for putting behind things - the party card, a friend's card.
/// </summary>
public static class PackageArt
{
	/// <summary>
	/// A shot of the game being played rather than its logo art, sized down - it's a faded background,
	/// it doesn't need the full resolution. Falls back to the wide thumbnail, then the plain one. The
	/// package needs its full details (not a partial fetch) for the screenshots to be there.
	/// </summary>
	public static string Background( Package package )
	{
		if ( package is null ) return null;

		var shot = package.Screenshots?.FirstOrDefault( x => !x.IsVideo );
		if ( shot is not null )
			return string.IsNullOrWhiteSpace( shot.Thumb ) ? shot.Url : shot.GetThumbUrl( 1024, 576 );

		return package.ThumbWide ?? package.Thumb;
	}
}
