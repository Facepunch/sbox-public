using static Facepunch.Constants;

namespace Facepunch.Steps;

internal static class NotifySlack
{
	internal static ExitCode Run( string message = null )
	{
		var webhookUrl = Environment.GetEnvironmentVariable( "SLACK_WEBHOOK_BUILDPIPELINE" );
		if ( string.IsNullOrEmpty( webhookUrl ) )
		{
			Log.Warning( "SLACK_WEBHOOK_BUILDPIPELINE not set; skipping Slack notification." );
			return ExitCode.Success;
		}

		string repository = Environment.GetEnvironmentVariable( "GITHUB_REPOSITORY" ) ?? "unknown";
		string workflow = Environment.GetEnvironmentVariable( "GITHUB_WORKFLOW" ) ?? "unknown workflow";
		string branch = Environment.GetEnvironmentVariable( "GITHUB_REF_NAME" ) ?? "unknown branch";
		string runId = Environment.GetEnvironmentVariable( "GITHUB_RUN_ID" ) ?? "";
		string commitSha = Environment.GetEnvironmentVariable( "GITHUB_SHA" ) ?? "";
		string actor = Environment.GetEnvironmentVariable( "GITHUB_ACTOR" ) ?? "unknown";
		string shortSha = commitSha.Length > 7 ? commitSha[..7] : commitSha;

		string runUrl = !string.IsNullOrEmpty( runId ) && !string.IsNullOrEmpty( repository )
			? $"https://github.com/{repository}/actions/runs/{runId}"
			: null;

		string commitUrl = !string.IsNullOrEmpty( repository ) && !string.IsNullOrEmpty( commitSha )
			? $"https://github.com/{repository}/commit/{commitSha}"
			: null;

		string fallbackText = message ?? $":x: Workflow Failed: {workflow} in {repository} ({branch})";

		var blocks = BuildBlocks( repository, workflow, branch, actor, message, runUrl, commitUrl, shortSha );

		var success = Slack.SendMessage( webhookUrl, new Slack.MessageParams(
			Text: fallbackText,
			Username: "GitHub Actions",
			IconEmoji: ":github:",
			Blocks: blocks
		) );

		return success ? ExitCode.Success : ExitCode.Failure;
	}

	private static object[] BuildBlocks( string repository, string workflow, string branch, string actor,
		string message, string runUrl, string commitUrl, string shortSha )
	{
		var blocks = new List<object>
		{
			new {
				type = "header",
				text = new { type = "plain_text", text = "🚨 Workflow Failed", emoji = true }
			},
			new { type = "divider" },
			new {
				type = "section",
				fields = new object[]
				{
					new { type = "mrkdwn", text = $"*Repository:*\n{repository}" },
					new { type = "mrkdwn", text = $"*Workflow:*\n{workflow}" },
					new { type = "mrkdwn", text = $"*Branch:*\n{branch}" },
					new { type = "mrkdwn", text = $"*Triggered by:*\n@{actor}" },
				}
			}
		};

		if ( !string.IsNullOrEmpty( message ) )
		{
			blocks.Add( new
			{
				type = "section",
				text = new { type = "mrkdwn", text = message }
			} );
		}

		blocks.Add( new
		{
			type = "actions",
			elements = BuildButtons( runUrl, commitUrl, shortSha )
		} );

		return blocks.ToArray();
	}

	private static object[] BuildButtons( string runUrl, string commitUrl, string shortSha )
	{
		var buttons = new List<object>();

		if ( !string.IsNullOrEmpty( runUrl ) )
		{
			buttons.Add( new
			{
				type = "button",
				text = new { type = "plain_text", text = "View Run", emoji = true },
				url = runUrl,
				style = "primary"
			} );
		}

		if ( !string.IsNullOrEmpty( commitUrl ) )
		{
			buttons.Add( new
			{
				type = "button",
				text = new { type = "plain_text", text = $"Commit {shortSha}", emoji = true },
				url = commitUrl
			} );
		}

		return buttons.ToArray();
	}
}
