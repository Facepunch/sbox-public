namespace Sandbox.Protobuf;

public static class ReactionMsg
{
	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class ReactionAdded : IMessage
	{
		public static MessageId MessageIdent => MessageId.ReactionAdded;

		public Guid TargetGuid { get; set; }
		public long SteamId { get; set; }
		public int Rating { get; set; }
		public string Name { get; set; }
		public string Avatar { get; set; }
	}
}
