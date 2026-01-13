using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Facepunch;

internal static class GitHub
{
	private static readonly HttpClient httpClient = new();
	private static readonly Dictionary<string, long> checkRunIds = [];

	public record struct CheckRunParams(
		string Name,
		string Status,
		string Conclusion = null,
		string Title = null,
		string Summary = null,
		string Details = null
	);

	/// <summary>
	/// Post a check run status to GitHub
	/// </summary>
	public static bool PostCheckRun( CheckRunParams parameters )
	{
		if ( !Utility.IsCi() )
			return true;

		try
		{
			return PostCheckRunAsync( parameters ).GetAwaiter().GetResult();
		}
		catch ( Exception ex )
		{
			Console.WriteLine( $"Error posting check run: {ex.Message}" );
			return false;
		}
	}

	/// <summary>
	/// Gets the check run ID for a step name if available
	/// </summary>
	public static bool TryGetCheckRunId( string stepName, out long checkRunId )
	{
		return checkRunIds.TryGetValue( stepName, out checkRunId );
	}

	private static async Task<bool> PostCheckRunAsync( CheckRunParams parameters )
	{
		// Validate required environment variables
		if ( !TryGetTokenAndRepositoryFromEnv( out var token, out var repository ) )
			return false;

		// Get the SHA 
		string sha = GetCommitSha();
		if ( string.IsNullOrEmpty( sha ) )
			return false;

		// Get timestamps in ISO 8601 format
		string currentTime = DateTime.UtcNow.ToString( "yyyy-MM-ddTHH:mm:ssZ" );

		// Set headers for all requests
		SetupHttpClient( token );

		// If we have an ID for this check run, update it instead of creating a new one
		if ( checkRunIds.TryGetValue( parameters.Name, out long checkRunId ) )
		{
			if ( await UpdateCheckRun( repository, checkRunId, parameters, currentTime ) )
			{
				return true;
			}
			// If update failed, we'll fall through to create a new check run
		}

		// Create a new check run
		return await CreateCheckRun( repository, sha, parameters, currentTime );
	}

	private static async Task<bool> UpdateCheckRun(
		string repository, long checkRunId, CheckRunParams parameters, string currentTime )
	{
		// Build update payload - slightly different from create payload (no head_sha)
		object updatePayload;
		var outputObject = new
		{
			title = parameters.Title ?? parameters.Name,
			summary = parameters.Summary ?? string.Empty,
			text = parameters.Details ?? string.Empty
		};

		if ( parameters.Status == "completed" )
		{
			updatePayload = new
			{
				status = parameters.Status,
				conclusion = parameters.Conclusion ?? "neutral",
				completed_at = currentTime,
				output = outputObject
			};
		}
		else
		{
			updatePayload = new
			{
				status = parameters.Status,
				output = outputObject
			};
		}

		var jsonContent = JsonSerializer.Serialize( updatePayload );
		using var content = new StringContent( jsonContent, Encoding.UTF8, "application/json" );

		var updateUrl = $"https://api.github.com/repos/{repository}/check-runs/{checkRunId}";

		// Use PATCH to update the existing check run
		var response = await httpClient.PatchAsync( updateUrl, content );

		if ( !response.IsSuccessStatusCode )
		{
			var responseContent = await response.Content.ReadAsStringAsync();
			Console.WriteLine( $"GitHub API error updating check run: {response.StatusCode} - {responseContent}" );

			// If the check run no longer exists (404), remove it from our dictionary
			// and create a new one instead
			if ( response.StatusCode == System.Net.HttpStatusCode.NotFound )
			{
				checkRunIds.Remove( parameters.Name );
			}

			return false;
		}

		return true;
	}

	private static async Task<bool> CreateCheckRun(
		string repository, string sha, CheckRunParams parameters, string currentTime )
	{
		object updatePayload;
		var outputObject = new
		{
			title = parameters.Title ?? parameters.Name,
			summary = parameters.Summary ?? string.Empty,
			text = parameters.Details ?? string.Empty
		};

		if ( parameters.Status == "completed" )
		{
			updatePayload = new
			{
				name = parameters.Name,
				head_sha = sha,
				status = parameters.Status,
				conclusion = parameters.Conclusion ?? "neutral",
				started_at = currentTime,
				completed_at = currentTime,
				output = outputObject
			};
		}
		else
		{
			updatePayload = new
			{
				name = parameters.Name,
				head_sha = sha,
				status = parameters.Status,
				started_at = currentTime,
				output = outputObject
			};
		}

		var jsonContent = JsonSerializer.Serialize( updatePayload );
		using var content = new StringContent( jsonContent, Encoding.UTF8, "application/json" );

		var url = $"https://api.github.com/repos/{repository}/check-runs";

		// Send request to create a new check run
		var response = await httpClient.PostAsync( url, content );

		if ( !response.IsSuccessStatusCode )
		{
			var errorResponseContent = await response.Content.ReadAsStringAsync();
			Console.WriteLine( $"GitHub API error creating check run: {response.StatusCode} - {errorResponseContent}" );
			return false;
		}

		var responseContent = await response.Content.ReadAsStringAsync();

		// Simple parsing of the response to get the ID
		using var jsonDoc = JsonDocument.Parse( responseContent );
		if ( jsonDoc.RootElement.TryGetProperty( "id", out var idElement ) )
		{
			long id = idElement.GetInt64();
			checkRunIds[parameters.Name] = id;
		}
		return true;
	}

	/// <summary>
	/// Gets a URL to the current workflow's logs
	/// </summary>
	public static string GetWorkflowLogsUrl()
	{
		return GetWorkflowLogsUrlAsync().GetAwaiter().GetResult();
	}

	private static async Task<string> GetWorkflowLogsUrlAsync()
	{
		// Get required environment variables
		if ( !TryGetTokenAndRepositoryFromEnv( out var token, out var repository ) )
			return string.Empty;

		SetupHttpClient( token );

		var runId = Environment.GetEnvironmentVariable( "GITHUB_RUN_ID" );
		if ( string.IsNullOrEmpty( runId ) )
		{
			Console.WriteLine( "GITHUB_RUN_ID not set" );
			return string.Empty;
		}

		long jobId = await FindJobId( repository, runId );

		// Construct the logs URL
		if ( jobId != -1 )
		{
			return $"https://github.com/{repository}/actions/runs/{runId}/job/{jobId}";
		}

		return $"https://github.com/{repository}/actions/runs/{runId}";
	}

	private static async Task<long> FindJobId( string repository, string runId )
	{
		long jobId = -1;
		try
		{
			// Query the API to get jobs for this run
			var jobsUrl = $"https://api.github.com/repos/{repository}/actions/runs/{runId}/jobs";
			var response = await httpClient.GetAsync( jobsUrl );

			if ( !response.IsSuccessStatusCode )
			{
				var errorContent = await response.Content.ReadAsStringAsync();
				Console.WriteLine( $"GitHub API error: {response.StatusCode} - {errorContent}" );
				return jobId;
			}

			var content = await response.Content.ReadAsStringAsync();
			using var doc = JsonDocument.Parse( content );

			// Get the current job name
			var jobName = Environment.GetEnvironmentVariable( "GITHUB_JOB" );

			// Make sure the jobs property exists
			if ( !doc.RootElement.TryGetProperty( "jobs", out var jobs ) )
			{
				Console.WriteLine( "Jobs property not found in API response" );
				return jobId;
			}

			// First pass: try to find exact job name match
			foreach ( var job in jobs.EnumerateArray() )
			{
				if ( job.TryGetProperty( "name", out var nameElement ) &&
					string.Equals( nameElement.GetString(), jobName, StringComparison.Ordinal ) )
				{
					jobId = job.GetProperty( "id" ).GetInt64();
					break;
				}
			}

			// Second pass: try case-insensitive match if exact match failed
			if ( jobId == -1 )
			{
				foreach ( var job in jobs.EnumerateArray() )
				{
					if ( job.TryGetProperty( "name", out var nameElement ) &&
						string.Equals( nameElement.GetString(), jobName, StringComparison.OrdinalIgnoreCase ) )
					{
						jobId = job.GetProperty( "id" ).GetInt64();
						break;
					}
				}
			}

			// If we still couldn't find by name, use the first job
			if ( jobId == -1 && jobs.GetArrayLength() > 0 )
			{
				var firstJob = jobs[0];
				jobId = firstJob.GetProperty( "id" ).GetInt64();
			}
		}
		catch ( Exception ex )
		{
			Console.WriteLine( $"Error getting job ID: {ex.Message}" );
		}

		return jobId;
	}

	private static string GetCommitSha()
	{
		string sha;
		var eventName = Environment.GetEnvironmentVariable( "GITHUB_EVENT_NAME" );

		if ( eventName == "pull_request" )
		{
			// For pull requests, we need to get the head SHA from the event payload
			sha = Environment.GetEnvironmentVariable( "GITHUB_EVENT_PULL_REQUEST_HEAD_SHA" );

			if ( string.IsNullOrEmpty( sha ) )
			{
				// Try to get it from the event file
				try
				{
					var eventPath = Environment.GetEnvironmentVariable( "GITHUB_EVENT_PATH" );
					if ( !string.IsNullOrEmpty( eventPath ) )
					{
						var eventJson = System.IO.File.ReadAllText( eventPath );
						using ( var doc = JsonDocument.Parse( eventJson ) )
						{
							sha = doc.RootElement
								.GetProperty( "pull_request" )
								.GetProperty( "head" )
								.GetProperty( "sha" )
								.GetString();
						}
					}
				}
				catch
				{
					// If we can't get the SHA from the event file, fall back to GITHUB_SHA
					sha = Environment.GetEnvironmentVariable( "GITHUB_SHA" );
				}
			}
		}
		else
		{
			// For other events, use the commit SHA
			sha = Environment.GetEnvironmentVariable( "GITHUB_SHA" );
		}

		if ( string.IsNullOrEmpty( sha ) )
		{
			Console.WriteLine( "Commit SHA not available" );
		}

		return sha ?? "";
	}

	private static bool TryGetTokenAndRepositoryFromEnv( out string token, out string repository )
	{
		token = Environment.GetEnvironmentVariable( "GITHUB_TOKEN" ) ?? string.Empty;
		if ( string.IsNullOrEmpty( token ) )
		{
			Console.WriteLine( "GITHUB_TOKEN not set" );
			repository = string.Empty;
			return false;
		}

		repository = Environment.GetEnvironmentVariable( "GITHUB_REPOSITORY" ) ?? string.Empty;
		if ( string.IsNullOrEmpty( repository ) )
		{
			Console.WriteLine( "GITHUB_REPOSITORY not set" );
			return false;
		}

		return true;
	}

	private static void SetupHttpClient( string token )
	{
		httpClient.DefaultRequestHeaders.Clear();
		httpClient.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/vnd.github+json" ) );
		httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", token );
		httpClient.DefaultRequestHeaders.Add( "User-Agent", "SboxBuild" );
		httpClient.DefaultRequestHeaders.Add( "X-GitHub-Api-Version", "2022-11-28" );
	}
}
