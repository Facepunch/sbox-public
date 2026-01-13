using System.Text;
using System.Text.Json;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class DiscordPostStep( string name, string message, string author ) : Step( name )
{
	public string Message { get; } = message;
	public string Author { get; } = author;

	protected override ExitCode RunInternal()
	{
		try
		{
			// Run the async method synchronously
			return PostToDiscordAsync().GetAwaiter().GetResult();
		}
		catch ( Exception ex )
		{
			Log.Error( $"Discord post failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	private async Task<ExitCode> PostToDiscordAsync()
	{
		// Validate inputs
		if ( string.IsNullOrWhiteSpace( Message ) )
		{
			Log.Warning( "Discord message was empty, skipping post" );
			return ExitCode.Success; // Not treating this as a failure
		}

		var hook = Environment.GetEnvironmentVariable( "DISCORD_WEBHOOK" );
		if ( string.IsNullOrEmpty( hook ) )
		{
			Log.Error( "DISCORD_WEBHOOK environment variable was empty" );
			return ExitCode.Failure;
		}

		Log.Info( $"Posting to Discord webhook as {Author}" );

		try
		{
			using ( var client = new HttpClient() )
			{
				var payload = new
				{
					content = Message.Substring( 0, Math.Min( Message.Length, 2000 ) ), // Discord message limit is 2000 characters
					username = Author
				};

				// Serialize and post
				using var content = new StringContent( JsonSerializer.Serialize( payload ), Encoding.UTF8, "application/json" );
				var response = await client.PostAsync( hook, content );

				if ( !response.IsSuccessStatusCode )
				{
					var responseString = await response.Content.ReadAsStringAsync();
					Log.Error( $"Discord returned {response.StatusCode}: {responseString}" );
					return ExitCode.Failure;
				}

				Log.Info( "Successfully posted to Discord" );
				return ExitCode.Success;
			}
		}
		catch ( Exception ex )
		{
			Log.Error( $"Discord post error: {ex}" );
			return ExitCode.Failure;
		}
	}
}
