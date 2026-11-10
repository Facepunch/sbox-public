namespace Sandbox.Protobuf;

public static class PackageMsg
{
	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class UsageChanged : IMessage
	{
		public static MessageId MessageIdent => MessageId.PackageUsageChanged;

		public string PackageIdent { get; set; }
		public long UserCount { get; set; }
	}

	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class FavouritesChanged : IMessage
	{
		public static MessageId MessageIdent => MessageId.PackageFavouritesChanged;

		public string PackageIdent { get; set; }
		public long Value { get; set; }
	}

	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class VotesChanged : IMessage
	{
		public static MessageId MessageIdent => MessageId.PackageVotesChanged;

		public string PackageIdent { get; set; }
		public long VotesUp { get; set; }
		public long VotesDown { get; set; }
	}

	/// <summary>
	/// The package name or description or something was updated
	/// </summary>
	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class Changed : IMessage
	{
		public static MessageId MessageIdent => MessageId.PackageChanged;

		public string PackageIdent { get; set; }
	}

	/// <summary>
	/// The package hasd a new version
	/// </summary>
	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class Update : IMessage
	{
		public static MessageId MessageIdent => MessageId.PackageUpdate;

		public string PackageIdent { get; set; }
		public long RevisionId { get; set; }
	}

	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class ViewsChanged : IMessage
	{
		public static MessageId MessageIdent => MessageId.PackageViewsChanged;

		public string PackageIdent { get; set; }
		public long Value { get; set; }
	}


	[ProtoContract( ImplicitFields = ImplicitFields.AllFields )]
	public class ReviewPosted : IMessage
	{
		public static MessageId MessageIdent => MessageId.PackageReviewPosted;

		public string PackageIdent { get; set; }
		public long Score { get; set; }
		public long SteamId { get; set; }
		public string DisplayName { get; set; }
	}

}
