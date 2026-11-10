using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace Sandbox.Twitch;

internal partial class TwitchAPI
{
	internal const string ApiUrl = "https://api.twitch.tv/helix";
	internal const string ClientId = "lyo7ge5md65toi0f3bjpkbn4u8hwol";

	/// <summary>
	/// A single shared client - creating one per request leaks sockets.
	/// </summary>
	private static readonly HttpClient Http = new();

	/// <summary>
	/// The standard Helix envelope: a single "data" array. The endpoints we use return one item, so
	/// <see cref="FirstOrDefault"/> pulls the first element (or null when the array is empty or absent).
	/// </summary>
	internal class DataResponse<T>
	{
		[JsonPropertyName( "data" )]
		public T[] Data { get; set; }

		public T FirstOrDefault() => Data is { Length: > 0 } ? Data[0] : default;
	}

	/// <summary>
	/// Build a request with the auth headers set. The token can change between
	/// connects, so it's applied per-request rather than on a shared client.
	/// </summary>
	private static HttpRequestMessage BuildRequest( HttpMethod method, string request, string json )
	{
		var token = Engine.Streamer.ServiceToken;

		var message = new HttpRequestMessage( method, $"{ApiUrl}{request}" );
		message.Headers.Add( "Client-ID", ClientId );
		message.Headers.Add( "Authorization", $"Bearer {token.Token}" );

		if ( json != null )
			message.Content = new StringContent( json, Encoding.UTF8, "application/json" );

		return message;
	}

	internal Task<T> Get<T>( string request ) => Send<T>( HttpMethod.Get, request, null );
	internal Task<T> Post<T>( string request, string json ) => Send<T>( HttpMethod.Post, request, json );

	/// <summary>
	/// Send a request and deserialize the response body. Returns default on failure.
	/// </summary>
	private static async Task<T> Send<T>( HttpMethod method, string request, string json )
	{
		try
		{
			using var message = BuildRequest( method, request, json );
			using var response = await Http.SendAsync( message );
			response.EnsureSuccessStatusCode();

			return await response.Content.ReadFromJsonAsync<T>();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Twitch API {method} {request} failed" );
			return default;
		}
	}

	/// <summary>
	/// Send a request, ignoring the response body. Use for endpoints that return no content.
	/// </summary>
	internal async Task Patch( string request, string json )
	{
		try
		{
			using var message = BuildRequest( HttpMethod.Patch, request, json );
			using var response = await Http.SendAsync( message );
			response.EnsureSuccessStatusCode();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Twitch API PATCH {request} failed" );
		}
	}
}
