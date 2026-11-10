namespace Sandbox;

public static partial class Streamer
{
	public struct User
	{
		public string Id { get; internal set; }
		public string Login { get; internal set; }
		public string DisplayName { get; internal set; }
		public string UserType { get; internal set; }
		public string BroadcasterType { get; internal set; }
		public string Description { get; internal set; }
		public string ProfileImageUrl { get; internal set; }
		public string OfflineImageUrl { get; internal set; }
		public int ViewCount { get; internal set; }
		public string Email { get; internal set; }
		public DateTimeOffset CreatedAt { get; internal set; }
	}
}
