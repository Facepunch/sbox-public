namespace Sandbox.Protobuf;

public static class OrgMsg
{
	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class Created : IMessage
	{
		public static MessageId MessageIdent => MessageId.OrgCreated;
		public string Ident { get; set; }
	}

	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class Edited : IMessage
	{
		public static MessageId MessageIdent => MessageId.OrgEdited;

		public string Ident { get; set; }
	}
}
