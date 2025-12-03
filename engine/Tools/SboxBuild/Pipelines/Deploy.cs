using Facepunch.Steps;
using static Facepunch.Constants;

namespace Facepunch.Pipelines;

internal class Deploy
{
	public static Pipeline Create( BuildTarget target, bool clean = false )
	{
		var builder = new PipelineBuilder( $"Deploy to {target}" );

		// Version management
		builder.AddStep( new WriteVersion( "Write Version" ) );

		// Build steps
		builder.AddStep( new Steps.InteropGen( "Interop Gen" ) );
		builder.AddStep( new Steps.ShaderProc( "Shader Proc" ) );
		builder.AddStep( new GenerateSolutions( "Generate Retail Solutions", BuildConfiguration.Retail ) );
		builder.AddStep( new BuildNative( "Build Retail Native", BuildConfiguration.Retail, clean ) );
		// We always want a clean rebuild for managed code.
		builder.AddStep( new BuildManaged( "Build Managed", true ) );
		builder.AddStep( new NvPatch( "NvPatch" ) );
		builder.AddStep( new BuildShaders( "Build Shaders" ) );
		builder.AddStep( new BuildContent( "Build Content" ) );

		// Testing
		builder.AddStep( new Test( "Tests" ) );

		// has to run after test because it make s&box.sln.. eh
		builder.AddStep( new BuildAddons( "Build Addons" ) );
		builder.AddStep( new GameCache() );

		// Upload steps
		builder.AddStep( new UploadSymbolsStep( "Upload Symbols" ) );
		builder.AddStep( new SentryRelease( "Sentry Release", "fcpnch", "sbox-native" ) );
		builder.AddStep( new UploadDocumentation( "Upload Documentation" ) );

		// Steam upload
		string branch = target == BuildTarget.Staging ? "staging" : "release";
		builder.AddStep( new UploadSteam( "Upload to Steam", branch ) );

		// Notification
		var commitMessage = Environment.GetEnvironmentVariable( "COMMIT_MESSAGE" ) ?? "Build completed";
		var version = Utility.VersionName();

		if ( !commitMessage.TrimStart().StartsWith( '!' ) )
		{
			builder.AddStep( new DiscordPostStep( "Discord Notification",
				$"New build ({version}) deployed to {target}:\n\n{commitMessage}",
				"Steam" ), continueOnFailure: true );
		}

		var slackWebhook = Environment.GetEnvironmentVariable( "SLACK_WEBHOOK_BUILDPIPELINE" );
		if ( Utility.IsCi() && !string.IsNullOrEmpty( slackWebhook ) )
		{
			builder.WithSlackNotifications( slackWebhook );
		}


		return builder.Build();
	}
}
