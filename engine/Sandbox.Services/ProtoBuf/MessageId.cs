namespace Sandbox.Protobuf;

/// <summary>
/// Central registry of every protobuf message id on the wire. Add new
/// messages here first, then reference the enum value from the class —
/// this keeps the ids in one place so collisions are obvious at a glance.
/// </summary>
public enum MessageId : ushort
{
	GameUpdatePublished = 1000,

	ReactionAdded = 2001,

	ClientNotice = 3000,

	PackageUsageChanged = 5000,
	PackageFavouritesChanged = 5001,
	PackageVotesChanged = 5002,
	PackageChanged = 5003,
	PackageUpdate = 5004,
	PackageViewsChanged = 5005,
	PackageReviewPosted = 5006,

	ClientAchievementUnlocked = 6001,

	ForumThreadPosted = 7000,
	ForumReplyPosted = 7001,
	ForumThreadEdited = 7002,

	OrgCreated = 8000,
	OrgEdited = 8001,

	ClientAccountEdited = 9000,
	ClientServiceLinked = 9001,
}
