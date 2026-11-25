namespace Sandbox.Protobuf;

/// <summary>
/// Realtime game jam updates for connected clients.
/// </summary>
public static class JamMsg
{
	/// <summary>
	/// Updates one package's weighted votes after a vote is added, moved or removed.
	/// Contains public tallies only, with the same eligibility filtering as the voting API.
	/// </summary>
	[ProtoContract]
	public class VotesChanged : IMessage
	{
		/// <summary>
		/// Identifies this message on the wire.
		/// </summary>
		public static MessageId MessageIdent => MessageId.JamVotesChanged;

		/// <summary>
		/// The jam's URL name, as used by the jam API.
		/// </summary>
		[ProtoMember( 1 )]
		public string JamIdent { get; set; }

		/// <summary>
		/// The category whose tally changed.
		/// </summary>
		[ProtoMember( 2 )]
		public int CategoryId { get; set; }

		/// <summary>
		/// The round being counted; zero means nominations.
		/// </summary>
		[ProtoMember( 3 )]
		public int Round { get; set; }

		/// <summary>
		/// Full ident of the individual package whose count changed.
		/// </summary>
		[ProtoMember( 4 )]
		public string PackageIdent { get; set; }

		/// <summary>
		/// Sum of captured trust scores across all eligible votes in this category and round, rounded down to a whole number.
		/// </summary>
		[ProtoMember( 5 )]
		public int TotalVotes { get; set; }

		/// <summary>
		/// Sum of captured trust scores for this package's eligible votes, rounded down to a whole number.
		/// Includes zero after removal.
		/// </summary>
		[ProtoMember( 6 )]
		public int Votes { get; set; }

	}
}
