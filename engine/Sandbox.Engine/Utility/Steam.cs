namespace Sandbox.Utility;

public static class Steam
{
	internal static ulong BaseFakeSteamId => 90071996842377216;

	/// <summary>
	/// Is this a fake SteamId? i.e. a bot or local (split-screen/local-instance) player rather than a real Steam account.
	/// </summary>
	internal static bool IsFakeSteamId( ulong steamId ) => steamId >= BaseFakeSteamId;

	/// <summary>
	/// Return what type os SteamId this is
	/// </summary>
	public static SteamId.AccountTypes CategorizeSteamId( SteamId steamid ) => steamid.AccountType;

	/// <summary>
	/// The current user's SteamId
	/// </summary>
	public static SteamId SteamId { get; private set; } = new SteamId( 710013 ); // default number so can search the code and find it hasn't been initialized

	/// <summary>
	/// The current user's persona name (Steam name)
	/// </summary>
	public static string PersonaName { get; private set; } = "Unnammed Player";

	private static readonly string[] LocalInstanceNames =
	[
		"Homer", "Marge", "Bart", "Lisa", "Maggie",
		"Ned", "Burns", "Smithers", "Moe", "Barney",
		"Krusty", "Milhouse", "Nelson", "Ralph", "Wiggum"
	];

	/// <summary>
	/// Get an anonymous alias name for a Steam ID used for Streamer Mode.
	/// The same input always returns the same name output, using the same pool of names we assign to fake/bot players.
	/// </summary>
	internal static string GetAnonymousName( SteamId steamId )
	{
		var index = (int)(steamId.ValueUnsigned % (ulong)LocalInstanceNames.Length);
		return LocalInstanceNames[index];
	}

	internal static void InitializeClient()
	{
		if ( Application.IsUnitTest )
			return;

		if ( Application.IsDedicatedServer )
		{
			SteamId = SteamNative.GameServer.GetSteamId();
			return;
		}


		if ( Application.IsJoinLocal && Application.LocalInstanceId > 0 )
		{
			SteamId = BaseFakeSteamId + (ulong)Application.LocalInstanceId;
			PersonaName = LocalInstanceNames[Random.Shared.Next( LocalInstanceNames.Length )];
		}
		else if ( SteamNative.User.IsAvailable() )
		{
			SteamId = SteamNative.User.GetSteamId();
		}

		if ( SteamNative.Friends.IsAvailable() && !Application.IsJoinLocal )
		{
			PersonaName = SteamNative.Friends.GetPersonaName();
		}

		SteamNative.Utils.InitFilterText();
	}

	/// <summary>
	/// Return true if this is a friend
	/// </summary>
	public static bool IsFriend( SteamId steamid )
	{
		return new Friend( steamid.Value ).IsFriend;
	}

	/// <summary>
	/// Return true if this person is online
	/// </summary>
	public static bool IsOnline( SteamId steamid )
	{
		return new Friend( steamid.Value ).IsOnline;
	}

	/// <summary>
	/// Filters text for game content using Steam's text filter.
	/// </summary>
	public static string FilterText( string input, SteamId? from = null ) => FilterText( input, Sandbox.SteamApi.TextFilteringContext.GameContent, from );

	/// <summary>
	/// Filters chat messages using Steam's text filter.
	/// </summary>
	public static string FilterChat( string input, SteamId? from = null ) => FilterText( input, Sandbox.SteamApi.TextFilteringContext.Chat, from );

	/// <summary>
	/// Filters player names using Steam's text filter.
	/// </summary>
	public static string FilterName( string input, SteamId? from = null ) => FilterText( input, Sandbox.SteamApi.TextFilteringContext.Name, from );

	internal static string FilterText( string input, Sandbox.SteamApi.TextFilteringContext context, SteamId? from = null )
	{
		if ( string.IsNullOrWhiteSpace( input ) )
			return input;

		return SteamNative.Utils.FilterText( context, from ?? SteamId, input );
	}
}
