using Sandbox.Utility;

namespace Sandbox.Services.Players;

/// <summary>
/// Player profile
/// </summary>
public sealed class Profile
{
	public SteamId Id { get; init; }
	public string Name
	{
		get => Preferences.StreamerMode ? Steam.GetAnonymousName( Id ) : field;
		init;
	}
	public string Url { get; init; }
	public bool Online { get; init; }
	public bool Private { get; init; }
	public int Score { get; init; }
	public string Avatar => $"avatar:{Id}";
	public bool IsFriend => Steam.IsFriend( Id );

	public static Profile Local
	{
		get
		{
			return new Profile
			{
				Id = Steam.SteamId,
				Name = Steam.PersonaName,
				Url = "",
				Online = true,
				Private = false,
				Score = (int)AccountInformation.Score,
			};
		}
	}

	public static async Task<Profile> Get( SteamId steamid )
	{
		try
		{
			return From( await Sandbox.Backend.Players.Get( steamid ) );
		}
		catch ( Exception )
		{
			return default;
		}
	}

	internal static Profile From( Sandbox.Services.Player p )
	{
		if ( p is null ) return default;

		return new Profile
		{
			Id = p.Id,
			Name = p.Name,
			Url = p.Url,
			Online = p.Online,
			Private = p.Private,
			Score = p.Score
		};
	}
}
