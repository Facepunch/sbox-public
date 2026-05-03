using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Editor.Copilot;

/// <summary>
/// Implements the GitHub OAuth 2.0 Device Flow.
///
/// Flow:
///   1.  POST /login/device/code  → receive user_code + device_code
///   2.  User visits https://github.com/login/device and enters user_code
///   3.  Poll /login/oauth/access_token until approved or expired
///   4.  Store access_token in <see cref="CopilotPreferences"/>
/// </summary>
public static class GitHubOAuth
{
	private const string DeviceCodeUrl  = "https://github.com/login/device/code";
	private const string TokenUrl       = "https://github.com/login/oauth/access_token";
	private const string UserApiUrl     = "https://api.github.com/user";
	private const string CopilotScope   = "copilot";

	private static readonly HttpClient _http = new()
	{
		DefaultRequestHeaders =
		{
			Accept = { new MediaTypeWithQualityHeaderValue( "application/json" ) },
			UserAgent = { new ProductInfoHeaderValue( "sbox-copilot", "1.0" ) }
		}
	};

	// ── public result types ──────────────────────────────────────────────────

	public class DeviceCodeResult
	{
		[JsonPropertyName( "device_code" )]     public string DeviceCode     { get; set; }
		[JsonPropertyName( "user_code" )]       public string UserCode       { get; set; }
		[JsonPropertyName( "verification_uri" )]public string VerificationUri{ get; set; }
		[JsonPropertyName( "expires_in" )]      public int    ExpiresIn      { get; set; }
		[JsonPropertyName( "interval" )]        public int    Interval       { get; set; }
	}

	public class OAuthError
	{
		[JsonPropertyName( "error" )]             public string Error            { get; set; }
		[JsonPropertyName( "error_description" )] public string ErrorDescription { get; set; }
	}

	private class TokenResponse : OAuthError
	{
		[JsonPropertyName( "access_token" )] public string AccessToken { get; set; }
		[JsonPropertyName( "token_type" )]   public string TokenType   { get; set; }
		[JsonPropertyName( "scope" )]        public string Scope       { get; set; }
	}

	private class GitHubUser
	{
		[JsonPropertyName( "login" )] public string Login { get; set; }
	}

	// ── public API ───────────────────────────────────────────────────────────

	/// <summary>
	/// Step 1: Request a device code from GitHub.
	/// Returns null and logs an error if the client_id is missing or the request fails.
	/// </summary>
	public static async Task<DeviceCodeResult> RequestDeviceCodeAsync( string clientId )
	{
		if ( string.IsNullOrWhiteSpace( clientId ) )
			throw new InvalidOperationException( "GitHub OAuth App Client ID is not configured." );

		var form = new FormUrlEncodedContent( new Dictionary<string, string>
		{
			["client_id"] = clientId,
			["scope"]      = CopilotScope
		} );

		var response = await _http.PostAsync( DeviceCodeUrl, form );
		var json     = await response.Content.ReadAsStringAsync();

		if ( !response.IsSuccessStatusCode )
			throw new HttpRequestException( $"GitHub device code request failed ({response.StatusCode}): {json}" );

		return JsonSerializer.Deserialize<DeviceCodeResult>( json );
	}

	/// <summary>
	/// Step 2: Poll GitHub until the user approves the device or the code expires.
	/// Calls <paramref name="onTokenReceived"/> on the main thread when done.
	/// Calls <paramref name="onError"/> on the main thread on failure.
	/// </summary>
	public static async Task PollForTokenAsync(
		string clientId,
		string deviceCode,
		int    pollingIntervalSeconds,
		Action<string> onTokenReceived,
		Action<string> onError,
		CancellationToken cancellation = default )
	{
		var form = new Dictionary<string, string>
		{
			["client_id"]   = clientId,
			["device_code"] = deviceCode,
			["grant_type"]  = "urn:ietf:params:oauth:grant-type:device_code"
		};

		var interval = TimeSpan.FromSeconds( Math.Max( 5, pollingIntervalSeconds ) );

		while ( !cancellation.IsCancellationRequested )
		{
			await Task.Delay( interval, cancellation );

			try
			{
				var response = await _http.PostAsync( TokenUrl, new FormUrlEncodedContent( form ), cancellation );
				var json     = await response.Content.ReadAsStringAsync( cancellation );
				var result   = JsonSerializer.Deserialize<TokenResponse>( json );

				switch ( result.Error )
				{
					case null when !string.IsNullOrEmpty( result.AccessToken ):
						MainThread.Queue( () => onTokenReceived( result.AccessToken ) );
						return;

					case "authorization_pending":
						// User hasn't approved yet — keep polling
						continue;

					case "slow_down":
						interval += TimeSpan.FromSeconds( 5 );
						continue;

					case "expired_token":
						MainThread.Queue( () => onError( "The device code has expired. Please sign in again." ) );
						return;

					case "access_denied":
						MainThread.Queue( () => onError( "Sign-in was cancelled." ) );
						return;

					default:
						MainThread.Queue( () => onError( result.ErrorDescription ?? result.Error ?? "Unknown error" ) );
						return;
				}
			}
			catch ( OperationCanceledException ) { return; }
			catch ( Exception ex )
			{
				MainThread.Queue( () => onError( ex.Message ) );
				return;
			}
		}
	}

	/// <summary>
	/// Fetch the authenticated user's GitHub login and store it in preferences.
	/// </summary>
	public static async Task<string> FetchUsernameAsync( string accessToken )
	{
		using var request = new HttpRequestMessage( HttpMethod.Get, UserApiUrl );
		request.Headers.Authorization = new AuthenticationHeaderValue( "token", accessToken );

		var response = await _http.SendAsync( request );
		var json     = await response.Content.ReadAsStringAsync();

		if ( !response.IsSuccessStatusCode )
			return "";

		var user = JsonSerializer.Deserialize<GitHubUser>( json );
		return user?.Login ?? "";
	}
}
