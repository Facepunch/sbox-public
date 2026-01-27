using System.Text;
using System.Runtime.InteropServices;

namespace Sandbox.Utility;

public static class Steam
{
	internal static ulong BaseFakeSteamId => 90071996842377216;

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

	internal static void InitializeClient()
	{
		if ( Application.IsUnitTest )
			return;

		var sf = NativeEngine.Steam.SteamFriends();
		var su = NativeEngine.Steam.SteamUser();
		var utils = NativeEngine.Steam.SteamUtils();

		if ( Application.IsJoinLocal && Application.LocalInstanceId > 0 )
		{
			SteamId = BaseFakeSteamId + (ulong)Application.LocalInstanceId;
		}
		else if ( su.IsValid )
		{
			SteamId = su.GetSteamID();
		}

		if ( sf.IsValid )
		{
			PersonaName = sf.GetPersonaName();
		}

		if ( utils.IsValid )
		{
			utils.InitFilterText( 0 );
		}
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
	public static string FilterText( string input, SteamId? from = null ) => FilterText( input, Steamworks.TextFilteringContext.GameContent, from );

	/// <summary>
	/// Filters chat messages using Steam's text filter.
	/// </summary>
	public static string FilterChat( string input, SteamId? from = null ) => FilterText( input, Steamworks.TextFilteringContext.Chat, from );

	/// <summary>
	/// Filters player names using Steam's text filter.
	/// </summary>
	public static string FilterName( string input, SteamId? from = null ) => FilterText( input, Steamworks.TextFilteringContext.Name, from );

	internal static string FilterText( string input, Steamworks.TextFilteringContext context, SteamId? from = null )
	{
		if ( string.IsNullOrWhiteSpace( input ) )
			return input;

		var utils = NativeEngine.Steam.SteamUtils();
		if ( !utils.IsValid )
			return input;

		var size = Encoding.UTF8.GetByteCount( input ) + 1;
		var buffer = Marshal.AllocHGlobal( size );

		try
		{
			utils.FilterText( context, from ?? SteamId, input, buffer, (uint)size );
			return Marshal.PtrToStringUTF8( buffer ) ?? string.Empty;
		}
		finally
		{
			Marshal.FreeHGlobal( buffer );
		}
	}
}
