namespace Sandbox;

public partial class SandboxVoice : Voice
{
	/// <summary>
	/// A list of muted users.
	/// </summary>
	public static HashSet<SteamId> MutedList { get; } = new();

	/// <summary>
	/// Toggles mute for a user
	/// </summary>
	/// <param name="id"></param>
	public static void Mute( SteamId id )
	{
		if ( MutedList.Contains( id ) )
		{
			MutedList.Remove( id );
			return;
		}

		MutedList.Add( id );
	}

	/// <summary>
	/// Is this user muted?
	/// </summary>
	/// <param name="id"></param>
	/// <returns></returns>
	public static bool IsMuted( SteamId id ) => MutedList.Contains( id );

	protected override bool ShouldHearVoice( Connection connection )
	{
		return !MutedList.Contains( connection.SteamId );
	}
}
