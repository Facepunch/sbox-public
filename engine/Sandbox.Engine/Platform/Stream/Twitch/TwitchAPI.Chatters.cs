using System.Text.Json.Serialization;

namespace Sandbox.Twitch;

internal partial class TwitchAPI
{
	internal class ChatterResponse
	{
		[JsonPropertyName( "user_id" )]
		public string UserId { get; set; }

		[JsonPropertyName( "user_login" )]
		public string UserLogin { get; set; }

		[JsonPropertyName( "user_name" )]
		public string UserName { get; set; }
	}

	internal class ChattersResponse
	{
		internal class Page
		{
			[JsonPropertyName( "cursor" )]
			public string Cursor { get; set; }
		}

		[JsonPropertyName( "data" )]
		public ChatterResponse[] Chatters { get; set; }

		[JsonPropertyName( "pagination" )]
		public Page Pagination { get; set; }

		[JsonPropertyName( "total" )]
		public int Total { get; set; }
	}

	/// <summary>
	/// Page through Get Chatters and return everyone currently connected to the channel's chat, keyed
	/// by login (lowercase) with their display name and user id as the value. Pages are pulled back-to-back via the
	/// cursor (1000 per request), so even a large channel costs only a handful of points and a second or
	/// two of wall-clock. Returns null if any page fails - e.g. the token lacks the
	/// moderator:read:chatters scope - so callers can tell a failed poll from a genuinely empty chat and
	/// avoid wiping the roster on a partial result.
	/// </summary>
	public async Task<Dictionary<string, (string DisplayName, string UserId)>> GetChatters( string userId )
	{
		var present = new Dictionary<string, (string, string)>( StringComparer.OrdinalIgnoreCase );

		string cursor = null;
		do
		{
			var request = $"/chat/chatters?broadcaster_id={userId}&moderator_id={userId}&first=1000";
			if ( !string.IsNullOrEmpty( cursor ) )
				request += $"&after={cursor}";

			var response = await Get<ChattersResponse>( request );
			if ( response?.Chatters is null )
				return null; // request failed (or a page dropped) - abandon the whole snapshot

			foreach ( var chatter in response.Chatters )
			{
				if ( string.IsNullOrEmpty( chatter.UserLogin ) )
					continue;

				present[chatter.UserLogin] = (chatter.UserName, chatter.UserId);
			}

			cursor = response.Pagination?.Cursor;
		}
		while ( !string.IsNullOrEmpty( cursor ) );

		return present;
	}
}
