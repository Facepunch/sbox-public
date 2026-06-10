using Sandbox.Twitch;

namespace Sandbox;

public static partial class Streamer
{
	internal struct Broadcast
	{
		internal Broadcast( TwitchAPI.StreamResponse stream )
		{
			Id = stream.Id;
			UserId = stream.UserId;
			Username = stream.UserLogin;
			DisplayName = stream.UserName;
			GameId = stream.GameId;
			GameName = stream.GameName;
			Type = stream.Type;
			Title = stream.Title;
			ViewerCount = stream.ViewerCount;
			StartedAt = stream.StartedAt;
			Language = stream.Language;
			ThumbnailUrl = stream.ThumbnailUrl;
			TagIds = stream.TagIds;
			IsMature = stream.IsMature;
		}

		public string Id { get; internal set; }
		public string UserId { get; internal set; }
		public string Username { get; internal set; }
		public string DisplayName { get; internal set; }
		public string GameId { get; internal set; }
		public string GameName { get; internal set; }
		public string Type { get; internal set; }
		public string Title { get; internal set; }
		public int ViewerCount { get; internal set; }
		public DateTimeOffset StartedAt { get; internal set; }
		public string Language { get; internal set; }
		public string ThumbnailUrl { get; internal set; }
		public string[] TagIds { get; internal set; }
		public bool IsMature { get; internal set; }
	}
}
