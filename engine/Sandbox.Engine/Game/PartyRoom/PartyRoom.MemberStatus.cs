using System.Globalization;

namespace Sandbox;

public partial class PartyRoom
{
	/// <summary>
	/// What a party member is up to, as far as following the leader goes.
	/// </summary>
	/// <param name="Stage">Where they are in joining the leader's game. The leader reports
	/// <see cref="JoinStage.Downloading"/> while loading their game and <see cref="JoinStage.Connected"/> once it's up.</param>
	/// <param name="Progress">How far through their download they are, 0 to 1, when they're downloading.</param>
	public readonly record struct MemberStatus( JoinStage Stage, float? Progress );

	/// <summary>
	/// What this party member is doing - downloading the game, waiting, in it. Each member shares their own
	/// through the lobby, so this is at most half a second old.
	/// </summary>
	public MemberStatus GetMemberStatus( Friend member )
	{
		if ( member.Id == Owner.Id )
		{
			return JoinState switch
			{
				OwnerJoinState.Loading => new MemberStatus( JoinStage.Downloading, (float?)HostDownloadProgress?.Fraction ),
				OwnerJoinState.Ready => new MemberStatus( JoinStage.Connected, null ),
				OwnerJoinState.Unavailable => new MemberStatus( JoinStage.Unavailable, null ),
				_ => new MemberStatus( JoinStage.None, null ),
			};
		}

		if ( member.IsMe )
			return new MemberStatus( JoiningStage, (float?)DownloadProgress?.Fraction );

		var stage = Enum.TryParse<JoinStage>( steamLobby.GetMemberData( member.Internal, "join_stage" ), out var s ) && Enum.IsDefined( s ) ? s : JoinStage.None;
		float? progress = float.TryParse( steamLobby.GetMemberData( member.Internal, "join_progress" ), NumberStyles.Float, CultureInfo.InvariantCulture, out var p ) && float.IsFinite( p )
			? Math.Clamp( p, 0, 1 )
			: null;

		return new MemberStatus( stage, stage == JoinStage.Downloading ? progress : null );
	}

	string _sharedStage;
	string _sharedProgress;

	/// <summary>
	/// Tell everyone else in the party how we're getting on joining the leader. Only writes when it
	/// changes - every write is a lobby update for every member.
	/// </summary>
	void ShareMemberStatus()
	{
		var stage = Owner.IsMe ? JoinStage.None : JoiningStage;
		var progress = stage == JoinStage.Downloading && DownloadProgress is { } download
			? Math.Clamp( download.Fraction, 0, 1 ).ToString( "F2", CultureInfo.InvariantCulture )
			: "";

		var stageText = stage.ToString();

		if ( _sharedStage != stageText )
		{
			_sharedStage = stageText;
			steamLobby.SetMemberData( "join_stage", stageText );
		}

		if ( _sharedProgress != progress )
		{
			_sharedProgress = progress;
			steamLobby.SetMemberData( "join_progress", progress );
		}
	}
}
