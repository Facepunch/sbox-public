using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox;

public static partial class Http
{
	/// <summary>
	/// Send a HTTP request to the specified URI and return the response body as a string in an asynchronous operation.
	/// </summary>
	/// <param name="requestUri">The URI to request.</param>
	/// <param name="method">The HTTP verb for the request (eg. GET, POST, etc.).</param>
	/// <param name="content">The content to include within the request, or null if none should be sent.</param>
	/// <param name="headers">Headers to add to the request, or null if none should be added.</param>
	/// <param name="cancellationToken">An optional cancellation token for canceling this request.</param>
	/// <returns>An asynchronous task which resolves to the response body as a string.</returns>
	/// <exception cref="HttpRequestException">The request responded with a non-2xx HTTP status code.</exception>
	/// <exception cref="InvalidOperationException">The request was not allowed, either an unallowed URI or header.</exception>
	public static async Task<string> RequestStringAsync( string requestUri, string method = "GET", HttpContent content = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default )
	{
		using var response = await RequestAsync( requestUri, method, content, headers: headers, cancellationToken: cancellationToken );
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsStringAsync( cancellationToken );
	}

	/// <summary>
	/// Send a HTTP request to the specified URI and return the response body as a byte array in an asynchronous operation.
	/// </summary>
	/// <param name="requestUri">The URI to request.</param>
	/// <param name="method">The HTTP verb for the request (eg. GET, POST, etc.).</param>
	/// <param name="content">The content to include within the request, or null if none should be sent.</param>
	/// <param name="headers">Headers to add to the request, or null if none should be added.</param>
	/// <param name="cancellationToken">An optional cancellation token for canceling this request.</param>
	/// <returns>An asynchronous task which resolves to the response body as a byte array.</returns>
	/// <exception cref="HttpRequestException">The request responded with a non-2xx HTTP status code.</exception>
	/// <exception cref="InvalidOperationException">The request was not allowed, either an unallowed URI or header.</exception>
	public static async Task<byte[]> RequestBytesAsync( string requestUri, string method = "GET", HttpContent content = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default )
	{
		using var response = await RequestAsync( requestUri, method, content, headers: headers, cancellationToken: cancellationToken );
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsByteArrayAsync( cancellationToken );
	}

	/// <summary>
	/// Send a HTTP request to the specified URI and return the response body as a stream in an asynchronous operation.
	/// </summary>
	/// <param name="requestUri">The URI to request.</param>
	/// <param name="method">The HTTP verb for the request (eg. GET, POST, etc.).</param>
	/// <param name="content">The content to include within the request, or null if none should be sent.</param>
	/// <param name="headers">Headers to add to the request, or null if none should be added.</param>
	/// <param name="cancellationToken">An optional cancellation token for canceling this request.</param>
	/// <returns>An asynchronous task which resolves to the response body as a <see cref="System.IO.Stream"/>.</returns>
	/// <exception cref="HttpRequestException">The request responded with a non-2xx HTTP status code.</exception>
	/// <exception cref="InvalidOperationException">The request was not allowed, either an unallowed URI or header.</exception>
	public static async Task<System.IO.Stream> RequestStreamAsync( string requestUri, string method = "GET", HttpContent content = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default )
	{
		using var response = await RequestAsync( requestUri, method, content, headers: headers, cancellationToken: cancellationToken );
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsStreamAsync( cancellationToken );
	}

	/// <summary>
	/// Sends a HTTP request to the specified URI and return the response body as a JSON deserialized object in an asynchronous operation.
	/// </summary>
	/// <param name="requestUri">The URI to request.</param>
	/// <param name="method">The HTTP verb for the request (eg. GET, POST, etc.).</param>
	/// <param name="content">The content to include within the request, or null if none should be sent.</param>
	/// <param name="headers">Headers to add to the request, or null if none should be added.</param>
	/// <param name="cancellationToken">An optional cancellation token for canceling this request.</param>
	/// <returns>An asynchronous task which resolves to the response body deserialized from JSON.</returns>
	/// <exception cref="HttpRequestException">The request responded with a non-2xx HTTP status code.</exception>
	/// <exception cref="InvalidOperationException">The request was not allowed, either an unallowed URI or header.</exception>
	public static async Task<T> RequestJsonAsync<T>( string requestUri, string method = "GET", HttpContent content = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default )
	{
		using var response = await RequestAsync( requestUri, method, content, headers: headers, cancellationToken: cancellationToken );
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<T>( cancellationToken: cancellationToken );
	}

	/// <summary>
	/// Sends a HTTP request to the specified URI and returns the response in an asynchronous operation. 
	/// </summary>
	/// <param name="requestUri">The URI to request.</param>
	/// <param name="method">The HTTP verb for the request (eg. GET, POST, etc.).</param>
	/// <param name="content">The content to include within the request, or null if none should be sent.</param>
	/// <param name="headers">Headers to add to the request, or null if none should be added.</param>
	/// <param name="cancellationToken">An optional cancellation token for canceling this request.</param>
	/// <returns>An asynchronous task which resolves to a <see cref="HttpResponseMessage"/> containing the response for the request.</returns>
	/// <exception cref="HttpRequestException">The request responded with a non-2xx HTTP status code.</exception>
	/// <exception cref="InvalidOperationException">The request was not allowed, either an unallowed URI or header.</exception>
	public static async Task<HttpResponseMessage> RequestAsync( string requestUri, string method = "GET", HttpContent content = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default )
	{
		if ( string.IsNullOrWhiteSpace( method ) )
		{
			throw new ArgumentNullException( nameof( method ) );
		}

		using var request = CreateRequest( new HttpMethod( method ), requestUri, headers );
		request.Content = content;
		return await Client.SendAsync( request, cancellationToken );
	}

	/// <summary>
	/// Creates a new <see cref="HttpContent"/> instance containing the specified object serialized to JSON.
	/// </summary>
	public static HttpContent CreateJsonContent<T>( T target )
	{
		return JsonContent.Create( target, new MediaTypeHeaderValue( "application/json" ) );
	}

	internal static HttpRequestMessage CreateRequest( HttpMethod method, string requestUri, Dictionary<string, string> headers )
	{
		var uri = new Uri( requestUri, UriKind.Absolute );
		if ( !IsAllowed( uri ) )
		{
			throw new InvalidOperationException( $"Access to '{uri}' is not allowed." );
		}

		var userInfo = uri.UserInfo;
		if ( !string.IsNullOrEmpty( userInfo ) )
		{
			var bytes = System.Text.Encoding.UTF8.GetBytes( userInfo );

			headers ??= new Dictionary<string, string>();
			headers["Authorization"] = $"Basic {Convert.ToBase64String( bytes )}";

			var uriBuilder = new UriBuilder( uri )
			{
				UserName = string.Empty,
				Password = string.Empty
			};

			uri = uriBuilder.Uri;
		}

		var request = new HttpRequestMessage( method, uri );
		if ( headers != null )
		{
			foreach ( var (key, value) in headers )
			{
				if ( !IsHeaderAllowed( key ) )
				{
					throw new InvalidOperationException( $"Not allowed to set header '{key}'." );
				}

				request.Headers.TryAddWithoutValidation( key, value );
			}
		}

		return request;
	}
}
