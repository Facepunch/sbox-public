using Facepunch.Steps;
using static Facepunch.Constants;

namespace Facepunch.Pipelines;

internal class Upload
{
	public static Pipeline Create( BuildTarget target )
	{
		var builder = new PipelineBuilder( $"Upload for {target}" );

		builder.AddStep( new GameCache() );
		builder.AddStep( new UploadSymbolsStep( "Upload Symbols" ) );
		builder.AddStep( new UploadDocumentation( "Upload Documentation" ) );
		builder.AddStep( new SentryRelease( "Sentry Release", "fcpnch", "sbox-native" ) );

		string branch = BuildTargetToSteamBranch( target );
		builder.AddStep( new UploadSteam( "Upload to Steam", branch ) );

		var commitMessage = Environment.GetEnvironmentVariable( "COMMIT_MESSAGE" ) ?? "Build completed";
		var version = Utility.VersionName();

		if ( !commitMessage.TrimStart().StartsWith( '!' ) )
		{
			builder.AddStep( new DiscordPostStep( "Discord Notification",
				$"New build ({version}) ready for {target}:\n\n{commitMessage}",
				"Build" ), continueOnFailure: true );
		}

		var slackWebhook = Environment.GetEnvironmentVariable( "SLACK_WEBHOOK_BUILDPIPELINE" );
		if ( Utility.IsCi() && !string.IsNullOrEmpty( slackWebhook ) )
		{
			builder.WithSlackNotifications( slackWebhook );
		}

		return builder.Build();
	}
}
