using System.Text.Json.Serialization;

namespace Sandbox.Twitch;

internal partial class TwitchAPI
{
	public class UserResponse
	{
		[JsonPropertyName( "id" )]
		public string Id { get; set; }

		[JsonPropertyName( "login" )]
		public string Login { get; set; }

		[JsonPropertyName( "display_name" )]
		public string DisplayName { get; set; }

		[JsonPropertyName( "type" )]
		public string UserType { get; set; }

		[JsonPropertyName( "broadcaster_type" )]
		public string BroadcasterType { get; set; }

		[JsonPropertyName( "description" )]
		public string Description { get; set; }

		[JsonPropertyName( "profile_image_url" )]
		public string ProfileImageUrl { get; set; }

		[JsonPropertyName( "offline_image_url" )]
		public string OfflineImageUrl { get; set; }

		[JsonPropertyName( "view_count" )]
		public int ViewCount { get; set; }

		[JsonPropertyName( "email" )]
		public string Email { get; set; }

		[JsonPropertyName( "created_at" )]
		public DateTimeOffset CreatedAt { get; set; }
	}

	public async Task<UserResponse> GetUser( string username = null )
	{
		var response = await Get<DataResponse<UserResponse>>( string.IsNullOrEmpty( username ) ?
			$"/users" :
			$"/users?login={System.Uri.EscapeDataString( username )}" );

		return response?.FirstOrDefault();
	}
}

