using System.Diagnostics;
using System.Text;
using Facepunch.Steps;
using static Facepunch.Constants;

namespace Facepunch.Pipelines;

internal class Pipeline
{
	private readonly List<Step> steps = new();
	private readonly Dictionary<Step, bool> continueOnFailure = new();
	private readonly Dictionary<Step, ExitCode> stepResults = new();
	private readonly string name;
	private readonly bool enableSlackNotifications;
	private readonly string slackWebhookUrl;

	private Platform platform = Platform.Create();

	// Track execution times
	private readonly Stopwatch pipelineStopwatch = new();

	// GitHub API limits
	private const int MAX_GITHUB_FIELD_LENGTH = 65535;
	private const int SUMMARY_LINE_COUNT = 32;

	public Pipeline( string name, bool enableSlackNotifications = false, string slackWebhookUrl = null )
	{
		this.name = name;
		this.enableSlackNotifications = enableSlackNotifications;

		// Use provided webhook URL or try to get from environment
		this.slackWebhookUrl = slackWebhookUrl;
	}

	/// <summary>
	/// Register a step to be run as part of this pipeline
	/// </summary>
	/// <param name="step">The step to register</param>
	/// <param name="continueOnFailure">Whether to continue if this step fails</param>
	public void RegisterStep( Step step, bool continueOnFailure = false )
	{
		steps.Add( step );
		this.continueOnFailure[step] = continueOnFailure;
	}

	/// <summary>
	/// Run the pipeline with all registered steps
	/// </summary>
	/// <returns>Success if all steps succeeded, Failure otherwise</returns>
	public ExitCode Run()
	{
		Log.Info( $"Running pipeline: {name}" );

		// Start the pipeline stopwatch
		pipelineStopwatch.Start();

		bool pipelineStopped = false;

		// Run all steps
		foreach ( var step in steps )
		{
			if ( pipelineStopped )
			{
				// Skip running the step if the pipeline has been stopped
				continue;
			}

			var result = RunStep( step );

			// If the step failed and we shouldn't continue on failure, stop the pipeline
			if ( result != ExitCode.Success && !continueOnFailure[step] )
			{
				Log.Warning( $"Pipeline {name} is stopping due to step failure after {FormatElapsedTime( pipelineStopwatch.Elapsed )}" );
				pipelineStopped = true;
			}
		}

		// Stop the pipeline stopwatch
		pipelineStopwatch.Stop();

		// Log total execution time
		Log.Info( $"Pipeline '{name}' completed in {FormatElapsedTime( pipelineStopwatch.Elapsed )}" );

		// Check if any steps failed
		return stepResults.Any( x => x.Value == ExitCode.Failure ) ? ExitCode.Failure : ExitCode.Success;
	}

	/// <summary>
	/// Format a timespan as minutes and seconds
	/// </summary>
	private string FormatElapsedTime( TimeSpan elapsed )
	{
		if ( elapsed.TotalHours >= 1 )
		{
			return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
		}
		else if ( elapsed.TotalMinutes >= 1 )
		{
			return $"{elapsed.Minutes}m {elapsed.Seconds}s";
		}
		else
		{
			return $"{elapsed.Seconds}s";
		}
	}

	/// <summary>
	/// Run a single step and record its result
	/// </summary>
	/// <param name="step">The step to run</param>
	/// <returns>The result of running the step</returns>
	private ExitCode RunStep( Step step )
	{
		// Post "in_progress" check run for this step
		if ( Utility.IsCi() )
		{
			// Get logs URL for in-progress message
			string logsUrl = GitHub.GetWorkflowLogsUrl();
			string inProgressSummary = $"{step.Name} is running...";

			// Add logs link if available
			if ( !string.IsNullOrEmpty( logsUrl ) )
			{
				inProgressSummary += $"\n\n[▶ View live logs]({logsUrl})";
			}

			GitHub.PostCheckRun(
				new GitHub.CheckRunParams
				{
					Name = $"{step.Name} ({platform.PlatformID})",
					Status = "in_progress",
					Title = $"{step.Name} is running...",
					Summary = inProgressSummary
				}
			);
		}


		// Set up console output capture
		var originalOut = Console.Out;
		var originalError = Console.Error;

		using ConsoleOutputCapture outputCapture = new ConsoleOutputCapture( originalOut, 2000 );
		using ConsoleOutputCapture errorCapture = new ConsoleOutputCapture( originalError, 2000 );

		// Log this before running the step
		// we dont want to capture this in the output
		if ( Utility.IsCi() ) Log.Info( $"::group::{step.Name}" );

		// Redirect console output
		Console.SetOut( outputCapture );
		Console.SetError( errorCapture );

		// Start timing the step
		var stepStopwatch = Stopwatch.StartNew();

		ExitCode stepResult;
		try
		{
			// Run the step
			stepResult = step.Run();
			stepResults[step] = stepResult;
		}
		finally
		{
			// Stop timing
			stepStopwatch.Stop();

			// Restore original console writers
			Console.SetOut( originalOut );
			Console.SetError( originalError );

			// Log this after restoring the console output
			if ( Utility.IsCi() ) Log.Info( $"::endgroup::" );
		}

		// Send Slack notification if enabled and step failed
		if ( enableSlackNotifications && stepResult == ExitCode.Failure && !string.IsNullOrEmpty( slackWebhookUrl ) )
		{
			SendSlackNotification( step, stepResult, stepStopwatch.Elapsed, errorCapture.GetCapturedOutput() );
		}

		// Post "completed" check run with the result and console output
		if ( Utility.IsCi() )
		{
			string conclusion = stepResult == ExitCode.Success ? "success" : "failure";

			// Create summary including limited console output
			StringBuilder summaryBuilder = new StringBuilder();
			summaryBuilder.AppendLine( $"{(stepResult == ExitCode.Success ? "✅" : "❌")} '{step.Name}' {(stepResult == ExitCode.Success ? "completed successfully" : "failed with error code " + stepResult)}" );
			summaryBuilder.AppendLine( $"⏱️ Execution time: {FormatElapsedTime( stepStopwatch.Elapsed )}" );

			// Add up to SUMMARY_LINE_COUNT lines of error output to the summary if it's different from standard output
			string errorOutput = errorCapture.GetCapturedOutput();
			string standardOutput = outputCapture.GetCapturedOutput();
			bool hasUniqueErrorOutput = errorCapture.HasContent && errorOutput != standardOutput;

			if ( hasUniqueErrorOutput )
			{
				summaryBuilder.AppendLine( $"\n### Error Output (last {SUMMARY_LINE_COUNT} lines)" );
				summaryBuilder.AppendLine( "```" );
				summaryBuilder.AppendLine( errorCapture.GetLastLines( SUMMARY_LINE_COUNT ) );
				summaryBuilder.AppendLine( "```" );
			}

			// Add up to SUMMARY_LINE_COUNT lines of console output to the summary
			if ( outputCapture.HasContent )
			{
				summaryBuilder.AppendLine( $"\n### Console Output (last {SUMMARY_LINE_COUNT} lines)" );
				summaryBuilder.AppendLine( "```" );
				summaryBuilder.AppendLine( outputCapture.GetLastLines( SUMMARY_LINE_COUNT ) );
				summaryBuilder.AppendLine( "```" );
			}

			// Get logs URL
			string logsUrl = GitHub.GetWorkflowLogsUrl();
			string viewLogsLink = string.IsNullOrEmpty( logsUrl ) ? "" : $"[▶ View complete logs]({logsUrl})";

			summaryBuilder.AppendLine( $"\nMore logs in the details below or {viewLogsLink}." );

			// Create details with full console output (up to limit)
			StringBuilder detailsBuilder = new StringBuilder();
			detailsBuilder.AppendLine( $"## Step: {step.Name}" );
			detailsBuilder.AppendLine( $"* Exit code: {(int)stepResult}" );
			detailsBuilder.AppendLine( $"* Status: {(stepResult == ExitCode.Success ? "Success" : "Failure")}" );
			detailsBuilder.AppendLine( $"* Execution time: {FormatElapsedTime( stepStopwatch.Elapsed )}" );
			detailsBuilder.AppendLine( $"* Run date: {DateTime.UtcNow.ToString( "yyyy-MM-dd HH:mm:ss" )} UTC" );

			// A more accurate estimate of overhead for section headers, code blocks, truncation message, etc.
			const int OUTPUT_SECTION_RESERVED_CHAR_COUNT = 250; // ~50 chars for headers, ~50 for markdown, ~50 for truncation message, ~100 for view logs link
			const string TRUNCATION_MESSAGE = "... Output truncated (showing only most recent content) ...\n";

			// Add error output first (with priority)
			if ( hasUniqueErrorOutput )
			{
				// Reserve space for the error section
				int availableSpace = MAX_GITHUB_FIELD_LENGTH - detailsBuilder.Length - OUTPUT_SECTION_RESERVED_CHAR_COUNT;
				bool needsTruncation = errorOutput.Length > availableSpace;

				// Add error output section
				detailsBuilder.AppendLine( needsTruncation ? "\n## Error Output (Truncated)" : "\n## Error Output" );
				detailsBuilder.AppendLine( "```" );

				if ( needsTruncation )
				{
					// Truncate from the beginning to keep the most recent output
					detailsBuilder.AppendLine( TRUNCATION_MESSAGE +
						errorOutput.Substring( errorOutput.Length - availableSpace ) );
					detailsBuilder.AppendLine( "```" );
					detailsBuilder.AppendLine();
					detailsBuilder.AppendLine( viewLogsLink );
				}
				else
				{
					detailsBuilder.AppendLine( errorOutput );
					detailsBuilder.AppendLine( "```" );
				}
			}

			// Add console output if we have enough space left
			if ( outputCapture.HasContent )
			{
				int availableSpace = MAX_GITHUB_FIELD_LENGTH - detailsBuilder.Length - OUTPUT_SECTION_RESERVED_CHAR_COUNT;
				bool needsTruncation = standardOutput.Length > availableSpace;

				detailsBuilder.AppendLine( needsTruncation ? "\n## Console Output (Truncated)" : "\n## Console Output" );
				detailsBuilder.AppendLine( "```" );

				if ( needsTruncation )
				{
					// Truncate from the beginning to keep the most recent output
					detailsBuilder.AppendLine( TRUNCATION_MESSAGE +
						standardOutput.Substring( standardOutput.Length - availableSpace ) );
					detailsBuilder.AppendLine( "```" );
					detailsBuilder.AppendLine();
					detailsBuilder.AppendLine( viewLogsLink );
				}
				else
				{
					detailsBuilder.AppendLine( standardOutput );
					detailsBuilder.AppendLine( "```" );
				}
			}

			// Ensure we don't exceed GitHub's character limits
			string summary = summaryBuilder.ToString().Substring( 0, Math.Min( MAX_GITHUB_FIELD_LENGTH, summaryBuilder.Length ) );
			string details = detailsBuilder.ToString().Substring( 0, Math.Min( MAX_GITHUB_FIELD_LENGTH, detailsBuilder.Length ) );

			GitHub.PostCheckRun(
				new GitHub.CheckRunParams
				{
					Name = $"{step.Name} ({platform.PlatformID})",
					Status = "completed",
					Conclusion = conclusion,
					Title = $"{step.Name}",
					Summary = summary,
					Details = details
				}
			);
		}

		return stepResult;
	}

	/// <summary>
	/// Send a notification to Slack about a step failure using Slack Block Kit
	/// </summary>
	private void SendSlackNotification( Step step, ExitCode result, TimeSpan executionTime, string errorOutput )
	{
		try
		{
			// Get workflow info
			string repository = Environment.GetEnvironmentVariable( "GITHUB_REPOSITORY" ) ?? "unknown repository";
			string workflow = Environment.GetEnvironmentVariable( "GITHUB_WORKFLOW" ) ?? "unknown workflow";
			string branch = Environment.GetEnvironmentVariable( "GITHUB_REF_NAME" ) ?? "unknown branch";
			string logsUrl = GitHub.GetWorkflowLogsUrl();
			string runId = Environment.GetEnvironmentVariable( "GITHUB_RUN_ID" ) ?? "unknown";
			string commitSha = Environment.GetEnvironmentVariable( "GITHUB_SHA" ) ?? "unknown";
			string actor = Environment.GetEnvironmentVariable( "GITHUB_ACTOR" ) ?? "unknown";

			// Short SHA for display
			string shortSha = commitSha.Length > 7 ? commitSha.Substring( 0, 7 ) : commitSha;

			// Get direct link to check run if available
			string checkRunUrl = null;
			if ( GitHub.TryGetCheckRunId( step.Name, out long checkRunId ) )
			{
				// Direct link to the specific check run
				checkRunUrl = $"https://github.com/{repository}/runs/{checkRunId}";
			}
			else if ( !string.IsNullOrEmpty( repository ) && !string.IsNullOrEmpty( runId ) )
			{
				// Fallback to checks tab if specific ID not available
				checkRunUrl = $"https://github.com/{repository}/actions/runs/{runId}/checks";
			}

			// Create commit URL if possible
			string commitUrl = null;
			if ( !string.IsNullOrEmpty( repository ) && commitSha != "unknown" )
			{
				commitUrl = $"https://github.com/{repository}/commit/{commitSha}";
			}

			// Simple fallback text for notifications and clients that don't support blocks
			string fallbackText = $":x: Pipeline Step Failed: {step.Name} in {repository} ({branch})";

			// Create blocks for the message
			var blocks = new object[]
			{
            // Header with emoji and title
            new {
				type = "header",
				text = new {
					type = "plain_text",
					text = "🚨 Pipeline Step Failed",
					emoji = true
				}
			},
            
            // Divider after header
            new {
				type = "divider"
			},
            
            // Main section with context
            new {
				type = "section",
				fields = new object[]
				{
					new {
						type = "mrkdwn",
						text = $"*Repository:*\n{repository}"
					},
					new {
						type = "mrkdwn",
						text = $"*Pipeline:*\n{name}"
					},
					new {
						type = "mrkdwn",
						text = $"*Step:*\n{step.Name}"
					},
					new {
						type = "mrkdwn",
						text = $"*Duration:*\n{FormatElapsedTime(executionTime)}"
					},
					new {
						type = "mrkdwn",
						text = $"*Branch:*\n{branch}"
					},
					new {
						type = "mrkdwn",
						text = $"*Triggered by:*\n@{actor}"
					}
				}
			},
            // Actions (buttons) for links
            new {
				type = "actions",
				elements = CreateLinkButtons(logsUrl, checkRunUrl, commitUrl, shortSha)
			}
			};

			// Send the notification with a call to action for the check run button
			Slack.SendMessage( slackWebhookUrl, new Slack.MessageParams(
				Text: fallbackText,
				Username: "GitHub Actions",
				IconEmoji: ":github:",
				Blocks: blocks
			) );
		}
		catch ( Exception ex )
		{
			// Log the error but don't fail the pipeline
			Console.WriteLine( $"Error sending Slack notification: {ex.Message}" );
		}
	}

	/// <summary>
	/// Helper method to create link buttons for the Slack message
	/// </summary>
	private object[] CreateLinkButtons( string logsUrl, string checkRunUrl, string commitUrl, string shortSha )
	{
		var buttons = new List<object>();

		// Check run button is now primary since it's the most direct link to the failure
		if ( !string.IsNullOrEmpty( checkRunUrl ) )
		{
			buttons.Add( new
			{
				type = "button",
				text = new
				{
					type = "plain_text",
					text = "View Failure",
					emoji = true
				},
				url = checkRunUrl,
				style = "primary"  // Make this the primary button
			} );
		}

		if ( !string.IsNullOrEmpty( logsUrl ) )
		{
			buttons.Add( new
			{
				type = "button",
				text = new
				{
					type = "plain_text",
					text = "View Logs",
					emoji = true
				},
				url = logsUrl
			} );
		}

		if ( !string.IsNullOrEmpty( commitUrl ) )
		{
			buttons.Add( new
			{
				type = "button",
				text = new
				{
					type = "plain_text",
					text = $"Commit {shortSha}",
					emoji = true
				},
				url = commitUrl
			} );
		}

		return buttons.ToArray();
	}
}

/// <summary>
/// Builder pattern for creating pipeline with steps
/// </summary>
internal class PipelineBuilder
{
	private readonly List<Step> steps = new();
	private readonly Dictionary<Step, bool> continueOnFailure = new();
	private readonly string name;
	private bool enableSlackNotifications = false;
	private string slackWebhookUrl = null;

	public PipelineBuilder( string name )
	{
		this.name = name;
	}

	/// <summary>
	/// Add a step to the pipeline
	/// </summary>
	/// <param name="step">The step to add</param>
	/// <param name="continueOnFailure">Whether to continue if this step fails</param>
	/// <returns>The builder for chaining</returns>
	public PipelineBuilder AddStep( Step step, bool continueOnFailure = false )
	{
		steps.Add( step );
		this.continueOnFailure[step] = continueOnFailure;
		return this;
	}

	public PipelineBuilder AddStepGroup( string groupName, IEnumerable<Step> groupSteps, bool continueOnFailure = false )
	{
		var group = new StepGroup( groupName, groupSteps.ToList(), continueOnFailure );
		steps.Add( group );
		this.continueOnFailure[group] = continueOnFailure;
		return this;
	}

	/// <summary>
	/// Enable Slack notifications for this pipeline
	/// </summary>
	/// <param name="webhookUrl">Optional webhook URL (defaults to SLACK_WEBHOOK_URL environment variable)</param>
	/// <returns>The builder for chaining</returns>
	public PipelineBuilder WithSlackNotifications( string webhookUrl = null )
	{
		enableSlackNotifications = true;
		slackWebhookUrl = webhookUrl;
		return this;
	}

	/// <summary>
	/// Build the pipeline with all registered steps
	/// </summary>
	/// <returns>A pipeline with the configured steps</returns>
	public Pipeline Build()
	{
		var pipeline = new Pipeline( name, enableSlackNotifications, slackWebhookUrl );

		// Register all steps with the pipeline
		foreach ( var step in steps )
		{
			pipeline.RegisterStep( step, continueOnFailure[step] );
		}

		return pipeline;
	}
}
