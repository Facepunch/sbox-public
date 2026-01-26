using Refit;
using System.Reflection;

namespace Sandbox.Services;

public partial class ServiceApi : IDisposable
{
	public IPackageApi Package { get; }
	public IVersionApi Version { get; }
	public IStatsApi Stats { get; }
	public ILeaderboardApi Leaderboards { get; }
	public IAchievementApi Achievements { get; }
	public IPlayerApi Player { get; }
	public INewsApi News { get; }
	public INotificationApi Notification { get; }
	public IStorageApi Storage { get; }
	public IUtilityApi Utility { get; }

	HttpClient client;

	public ServiceApi( string url = "https://services.facepunch.com/sbox" )
	{
		var refitSettings = new RefitSettings
		{
			UrlParameterFormatter = new ParameterFormatter(),
		};

#pragma warning disable CA2000 // Dispose objects before losing scope
		// HttpClient will dispose the handlers
		client = new HttpClient( new LoggingHandler( new HttpClientHandler() ) )
		{
			BaseAddress = new Uri( url )
		};
#pragma warning restore CA2000 // Dispose objects before losing scope

		Package = RestService.For<IPackageApi>( client, refitSettings );
		Version = RestService.For<IVersionApi>( client, refitSettings );
		Stats = RestService.For<IStatsApi>( client, refitSettings );
		Leaderboards = RestService.For<ILeaderboardApi>( client, refitSettings );
		Achievements = RestService.For<IAchievementApi>( client, refitSettings );
		Player = RestService.For<IPlayerApi>( client, refitSettings );
		News = RestService.For<INewsApi>( client, refitSettings );
		Notification = RestService.For<INotificationApi>( client, refitSettings );
		Storage = RestService.For<IStorageApi>( client, refitSettings );
		Utility = RestService.For<IUtilityApi>( client, refitSettings );
	}

	public void SetApiKey( string apiKey )
	{
		client.DefaultRequestHeaders.Add( "Authorization", $"Bearer {apiKey}" );
	}

	public void Dispose()
	{
		client?.Dispose();
	}
}



class ParameterFormatter : DefaultUrlParameterFormatter
{
	public override string Format( object value, ICustomAttributeProvider attributeProvider, Type type )
	{
		// make sure bool is lowercase! what are you doing c#
		if ( value is bool b )
		{
			return b.ToString().ToLower();
		}

		return base.Format( value, attributeProvider, type );
	}
}

class LoggingHandler : DelegatingHandler
{
	public LoggingHandler( HttpMessageHandler innerHandler ) : base( innerHandler ) { }

	protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
	{
		// Log request details
		Console.WriteLine( $"Request: {request.Method} {request.RequestUri}" );

		foreach ( var h in request.Headers )
		{
			Console.WriteLine( $"	{h.Key}: {h.Value.First()}" );
		}

		if ( request.Content != null )
		{
			var requestBody = await request.Content.ReadAsStringAsync( cancellationToken );
			Console.WriteLine( $"Request Body: {requestBody}" );

			foreach ( var h in request.Content.Headers )
			{
				Console.WriteLine( $"	{h.Key}: {h.Value.First()}" );
			}
		}

		// Call the inner handler
		var response = await base.SendAsync( request, cancellationToken );

		// Log response details
		Console.WriteLine( $"Response: {(int)response.StatusCode} {response.ReasonPhrase}" );
		if ( response.Content != null )
		{
			var responseBody = await response.Content.ReadAsStringAsync( cancellationToken );
			Console.WriteLine( $"Response Body: {responseBody}" );
		}

		return response;
	}
}
