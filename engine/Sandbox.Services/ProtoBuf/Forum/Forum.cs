namespace Sandbox.Protobuf;

public static class ForumMsg
{
	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class ThreadPosted : IMessage
	{
		public static MessageId MessageIdent => MessageId.ForumThreadPosted;

		public long ForumId { get; set; }
		public long ThreadId { get; set; }
		public long PostId { get; set; }
		public long UserId { get; set; }
	}

	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class ReplyPosted : IMessage
	{
		public static MessageId MessageIdent => MessageId.ForumReplyPosted;

		public long ForumId { get; set; }
		public long ThreadId { get; set; }
		public long PostId { get; set; }
		public long UserId { get; set; }
	}

	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class ThreadEdited : IMessage
	{
		public static MessageId MessageIdent => MessageId.ForumThreadEdited;

		public long ForumId { get; set; }
		public long ThreadId { get; set; }
	}

}
