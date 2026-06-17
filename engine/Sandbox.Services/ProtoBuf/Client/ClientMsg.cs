namespace Sandbox.Protobuf;

/// <summary>
/// Messages targeted at a single user's game session (sent via
/// <c>MessageService.Send( steamId, ... )</c>), as opposed to the broadcast
/// "gameclient" group messages.
/// </summary>
public static class ClientMsg
{
	/// <summary>
	/// Show a generic toast/popup on the client. Used when the server wants to
	/// surface a UI notice that isn't tied to a typed domain event (e.g. reward
	/// drops). Type is a free-form discriminator the client can switch on for
	/// styling.
	/// </summary>
	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class Notice : IMessage
	{
		public static MessageId MessageIdent => MessageId.ClientNotice;

		public string Type { get; set; }
		public string Title { get; set; }
		public string Text { get; set; }
		public string Icon { get; set; }
		public string Link { get; set; }
	}

	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class AchievementUnlocked : IMessage
	{
		public static MessageId MessageIdent => MessageId.ClientAchievementUnlocked;

		public string Title { get; set; }
		public string Description { get; set; }
		public string Icon { get; set; }
		public int ScoreAdded { get; set; }
		public int PackageScore { get; set; }
		public int PlayerScore { get; set; }
		public int PackageUnlocks { get; set; }
		public int PlayerUnlocks { get; set; }
	}

	/// <summary>
	/// The user's account was edited — tells their in-game session to refresh
	/// account info.
	/// </summary>
	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class AccountEdited : IMessage
	{
		public static MessageId MessageIdent => MessageId.ClientAccountEdited;

		public long UserId { get; set; }
	}

	/// <summary>
	/// A third-party service link (e.g. Twitch) was created or removed for this
	/// user — pushed to their in-game session so it can update without polling.
	/// Carries the linked account's display name + avatar so the client doesn't
	/// need a follow-up request. <see cref="Linked"/> is false when unlinked.
	/// </summary>
	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class ServiceLinked : IMessage
	{
		public static MessageId MessageIdent => MessageId.ClientServiceLinked;

		public string Service { get; set; }
		public string Id { get; set; }
		public string Name { get; set; }
		public string Avatar { get; set; }
		public bool Linked { get; set; }
	}
}
