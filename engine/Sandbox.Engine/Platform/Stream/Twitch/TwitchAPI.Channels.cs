using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.Twitch;

internal partial class TwitchAPI
{
	/// <summary>
	/// A channel update. Only the non-null fields are sent, so each setter
	/// patches a single property.
	/// </summary>
	internal class UpdateChannelRequest
	{
		[JsonPropertyName( "broadcaster_language" )]
		public string Language { get; set; }

		[JsonPropertyName( "delay" )]
		public int? Delay { get; set; }
	}

	private static readonly JsonSerializerOptions IgnoreNulls = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};
	public Task SetChannelLanguage( string userId, string language )
	{
		return UpdateChannel( userId, new UpdateChannelRequest { Language = language } );
	}

	public Task SetChannelDelay( string userId, int delay )
	{
		return UpdateChannel( userId, new UpdateChannelRequest { Delay = delay } );
	}

	private Task UpdateChannel( string userId, UpdateChannelRequest update )
	{
		return Patch( $"/channels?broadcaster_id={userId}", JsonSerializer.Serialize( update, IgnoreNulls ) );
	}
}
