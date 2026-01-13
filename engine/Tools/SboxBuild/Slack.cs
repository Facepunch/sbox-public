using System.Text;
using System.Text.Json;

namespace Facepunch;

/// <summary>
/// Generic Slack integration utility for sending messages via webhooks
/// </summary>
internal static class Slack
{
	private static readonly HttpClient httpClient = new();

	/// <summary>
	/// Parameters for sending a Slack message
	/// </summary>
	public record struct MessageParams(
		string Text,
		string Username = null,
		string IconEmoji = null,
		string Channel = null,
		object[] Attachments = null,
		object[] Blocks = null
	);

	/// <summary>
	/// Send a message to Slack using a webhook URL
	/// </summary>
	/// <param name="webhookUrl">The Slack webhook URL</param>
	/// <param name="parameters">The message parameters</param>
	/// <returns>True if the message was sent successfully</returns>
	public static bool SendMessage( string webhookUrl, MessageParams parameters )
	{
		try
		{
			return SendMessageAsync( webhookUrl, parameters ).GetAwaiter().GetResult();
		}
		catch ( Exception ex )
		{
			Console.WriteLine( $"Error sending Slack message: {ex.Message}" );
			return false;
		}
	}

	private static async Task<bool> SendMessageAsync( string webhookUrl, MessageParams parameters )
	{
		if ( string.IsNullOrEmpty( webhookUrl ) )
		{
			Console.WriteLine( "Slack webhook URL is empty" );
			return false;
		}

		// Build the payload
		var payload = new
		{
			text = parameters.Text,
			username = parameters.Username,
			icon_emoji = parameters.IconEmoji,
			channel = parameters.Channel,
			attachments = parameters.Attachments,
			blocks = parameters.Blocks
		};

		var jsonContent = JsonSerializer.Serialize( payload, new JsonSerializerOptions
		{
			DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
		} );

		using var content = new StringContent( jsonContent, Encoding.UTF8, "application/json" );

		// Send the request
		var response = await httpClient.PostAsync( webhookUrl, content );

		if ( !response.IsSuccessStatusCode )
		{
			var responseContent = await response.Content.ReadAsStringAsync();
			Console.WriteLine( $"Slack API error: {response.StatusCode} - {responseContent}" );
			return false;
		}

		return true;
	}
}
