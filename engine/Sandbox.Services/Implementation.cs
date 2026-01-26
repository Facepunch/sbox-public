using Refit;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using static Sandbox.Services.ServiceApi;

namespace Sandbox;

public static class Backend
{
	public static IPackageApi Package { get; private set; }
	public static IVersionApi Version { get; private set; }
	public static IStatsApi Stats { get; private set; }
	public static ILeaderboardApi Leaderboards { get; private set; }
	public static IAccountApi Account { get; private set; }
	public static IBenchmarkApi Benchmarks { get; private set; }
	public static IAchievementApi Achievements { get; private set; }
	public static IPlayerApi Players { get; private set; }
	public static INewsApi News { get; private set; }
	public static INotificationApi Notifications { get; private set; }
	public static IStorageApi Storage { get; private set; }
	public static IUtilityApi Utility { get; private set; }

	private static HttpClient httpClient = null;

	public static void Initialize( DelegatingHandler httpHandler, string url = "https://services.facepunch.com/sbox" )
	{
		var refitSettings = BuildSettings( url );

		httpClient = new HttpClient( httpHandler )
		{
			BaseAddress = new Uri( url ),
			Timeout = TimeSpan.FromMinutes( 15 )
		};

		Package = RestService.For<IPackageApi>( httpClient, refitSettings );
		Version = RestService.For<IVersionApi>( httpClient, refitSettings );
		Stats = RestService.For<IStatsApi>( httpClient, refitSettings );
		Leaderboards = RestService.For<ILeaderboardApi>( httpClient, refitSettings );
		Account = RestService.For<IAccountApi>( httpClient, refitSettings );
		Benchmarks = RestService.For<IBenchmarkApi>( httpClient, refitSettings );
		Achievements = RestService.For<IAchievementApi>( httpClient, refitSettings );
		Players = RestService.For<IPlayerApi>( httpClient, refitSettings );
		News = RestService.For<INewsApi>( httpClient, refitSettings );
		Notifications = RestService.For<INotificationApi>( httpClient, refitSettings );
		Storage = RestService.For<IStorageApi>( httpClient, refitSettings );
		Utility = RestService.For<IUtilityApi>( httpClient, refitSettings );
	}

	static RefitSettings BuildSettings( string url )
	{
		return new RefitSettings
		{
			UrlParameterFormatter = new Sandbox.Services.ParameterFormatter(),
			ContentSerializer = new CustomJsonContentSerializer(),
			Buffered = true
		};
	}

}

class CustomJsonContentSerializer : IHttpContentSerializer
{
	public async Task<T> FromHttpContentAsync<T>( HttpContent content, CancellationToken cancellationToken = default )
	{
		var str = await content.ReadAsStringAsync();
		if ( string.IsNullOrWhiteSpace( str ) ) return default;

		return System.Text.Json.JsonSerializer.Deserialize<T>( str, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true } );
	}

	public string GetFieldNameForProperty( PropertyInfo propertyInfo )
	{
		if ( propertyInfo is null )
			throw new ArgumentNullException( nameof( propertyInfo ) );

		return propertyInfo.GetCustomAttributes<JsonPropertyNameAttribute>( true )
				   .Select( a => a.Name )
				   .FirstOrDefault();
	}

	public HttpContent ToHttpContent<T>( T values )
	{
		var json = System.Text.Json.JsonSerializer.Serialize( values );
		var jsonBytes = Encoding.UTF8.GetBytes( json );
		var jsonStream = new MemoryStream( jsonBytes );

		var content = new ByteArrayContent( jsonBytes );
		content.Headers.ContentType = new MediaTypeHeaderValue( "application/json" );
		return content;

	}
}
