namespace Sandbox.Protobuf;

/// <summary>
/// Realtime updates for individual news posts.
/// </summary>
public static class NewsMsg
{
	/// <summary>
	/// A package or platform news post has been published.
	/// </summary>
	[ProtoContract]
	public class Published : IMessage
	{
		/// <summary>
		/// Identifies this message on the wire.
		/// </summary>
		public static MessageId MessageIdent => MessageId.NewsPublished;

		/// <summary>
		/// Identifies the single published post.
		/// </summary>
		[ProtoMember( 1 )]
		public Guid PostId { get; set; }

		/// <summary>
		/// Full package ident; platform news uses facepunch.news.
		/// </summary>
		[ProtoMember( 2 )]
		public string PackageIdent { get; set; }

		/// <summary>
		/// The published post's title.
		/// </summary>
		[ProtoMember( 3 )]
		public string Title { get; set; }

		/// <summary>
		/// Site-relative URL of the published post.
		/// </summary>
		[ProtoMember( 4 )]
		public string Url { get; set; }
	}
}
