using ProtoBuf;

namespace Sandbox.Protobuf;

public static class GameMsg
{

	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class UpdatePublished : IMessage
	{
		public static MessageId MessageIdent => MessageId.GameUpdatePublished;
	}

}
