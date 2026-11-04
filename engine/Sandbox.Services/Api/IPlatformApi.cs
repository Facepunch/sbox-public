using Refit;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.Services;

public partial class ServiceApi
{
	/// <summary>
	/// Aggregate platform activity, API discovery and service authentication.
	/// </summary>
	public interface IPlatformApi
	{
		[Get( "/stats/platform" )]
		Task<PlatformStats> GetStats();

		/// <summary>UTC ISO-8601 range; interval is hour, day or week.</summary>
		[Get( "/stats/platform/history" )]
		Task<PlatformStatsHistory> GetHistory( string from, string to, string interval );

		/// <summary>Retention for a cohort date in YYYY-MM-DD format.</summary>
		[Get( "/stats/platform/retention" )]
		Task<RetentionCohort> GetRetention( string cohort );

		[Get( "/package/{ident}/retention" )]
		Task<RetentionCohort> GetPackageRetention( string ident, string cohort );

		[Get( "/api/list" )]
		Task<ApiCatalogue> GetApiList();

		[Get( "/health" )]
		Task<string> GetHealth();

		/// <summary>Consumes a short-lived player token issued for an external service.</summary>
		[Post( "/auth/token" )]
		Task<AuthTokenResult> ConsumeAuthToken( [Body] AuthTokenRequest input );
	}
}

public class ApiCatalogue
{
	public List<ApiMethod> Methods { get; set; } = [];
}

public class ApiMethod
{
	public string Path { get; set; }
	public string HttpMethod { get; set; }
	public List<ApiParameter> Parameters { get; set; } = [];
}

public class ApiParameter
{
	public string Name { get; set; }
	public string Type { get; set; }
	public bool Optional { get; set; }
	public JsonElement? Default { get; set; }
	[JsonPropertyName( "in" )]
	public string In { get; set; }
}

public class AuthTokenRequest
{
	[JsonPropertyName( "SteamID" )]
	public long SteamId { get; set; }
	public string Token { get; set; }
}

public class AuthTokenResult
{
	public long SteamId { get; set; }
	public string Status { get; set; }
}
