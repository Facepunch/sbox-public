using Microsoft.Extensions.Caching.Memory;
using Sandbox.UI;
using System.Net.Http;
using System.Threading;

namespace Sandbox;

internal static partial class Api
{
	public static Guid SessionId { get; private set; }

	internal static void Init()
	{
		if ( Application.IsStandalone )
			return;

#pragma warning disable CA2000 // Dispose objects before losing scope
		// Backend/HttpClient will take over ownership/disposal of this handler
		Sandbox.Backend.Initialize( new CachingHandler() );
#pragma warning restore CA2000 // Dispose objects before losing scope
	}

	internal static void Shutdown()
	{
		if ( Application.IsStandalone )
			return;

		var timer = FastTimer.StartNew();

		Task.WaitAll( Events.Shutdown(), Stats.Shutdown(), Activity.Shutdown() );

		if ( timer.ElapsedSeconds > 0.5f )
		{
			Log.Info( $"Api Flush took {timer.ElapsedSeconds:0.00}s" );
		}
	}

	internal static object GetConfig()
	{
		return new
		{
			Editor = Application.IsEditor,
			ScreenWidth = Screen.Size.x,
			ScreenHeight = Screen.Size.y,
			Version = Application.Version,
			VersionDate = Application.VersionDate,
			VR = Application.IsVR,

			// Try to incldue any convars that could make a meaningful impact on performance

			FpsMax = ConVarSystem.GetInt( "fps_max", 0, true ),
			MSAA = NativeEngine.RenderService.GetMultisampleType(),
			VolumeFogDepth = ConVarSystem.GetInt( "volume_fog_depth", 0, true ),
			Application.ExceptionCount
		};
	}
}

public class CachingHandler : DelegatingHandler
{
	private readonly MemoryCache _cache;

	public CachingHandler()
	{
		var options = new MemoryCacheOptions();

		_cache = new MemoryCache( options );
		InnerHandler = new BackendHttpHandler();
	}

	protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
	{
		if ( request.Method != HttpMethod.Get )
		{
			return await base.SendAsync( request, cancellationToken );
		}

		var cacheKey = request.RequestUri.ToString();

		if ( _cache.TryGetValue( cacheKey, out string cachedContent ) )
		{
			return new HttpResponseMessage( System.Net.HttpStatusCode.OK )
			{
				Content = new StringContent( cachedContent )
			};
		}

		{
			var response = await base.SendAsync( request, cancellationToken );

			if ( response.IsSuccessStatusCode )
			{
				var content = await response.Content.ReadAsStringAsync();
				_cache.Set( cacheKey, content, TimeSpan.FromMinutes( 1 ) );

				return new HttpResponseMessage( System.Net.HttpStatusCode.OK )
				{
					Content = new StringContent( content )
				};
			}

			return response;
		}
	}
}


class BackendHttpHandler : DelegatingHandler
{
	[ConVar( "backend_debug", ConVarFlags.Protected, Help = "Print backend queries" )]
	public static bool backend_debug { get; set; } = false;

	public BackendHttpHandler()
	{
		// default handler
		InnerHandler = new HttpClientHandler()
		{
			// Skip revocation checks
			CheckCertificateRevocationList = false,

			// Log any SSL exceptions, but let them continue. People in very forign countries
			// regularly have problems due to having to use proxies etc
			ServerCertificateCustomValidationCallback = ( message, cert, chain, errors ) =>
			{
				if ( errors != System.Net.Security.SslPolicyErrors.None )
				{
					Log.Warning( $"SSL Error: {errors}" );
				}

				// allow even with the ssl error
				return true;
			}
		};
	}

	protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
	{
		var url = request.RequestUri.ToString();

		var eventRecord = new Api.Events.EventRecord( "BackendRequest" );
		eventRecord.SetValue( "url", url );
		eventRecord.SetValue( "method", request.Method.ToString() );
		using var timer = eventRecord.ScopeTimer( "ms" );

		if ( backend_debug )
		{
			Log.Info( $"[Api] [{request.Method}] {url}" );
		}

		int tries = 0;

		retry:

		AddHeaders( request );

		tries++;
		var response = await base.SendAsync( request, cancellationToken );

		if ( backend_debug )
		{
			Log.Info( $"[Api] Response: [{response.StatusCode}]" );
		}

		//
		// Our servers are down, retry?
		//
		if ( response.StatusCode >= System.Net.HttpStatusCode.InternalServerError )
		{
			if ( tries <= 5 )
			{
				await Task.Delay( 500 * tries );
				goto retry;
			}
		}

		if ( response.StatusCode == System.Net.HttpStatusCode.BadRequest )
		{
			eventRecord.SetValue( "message", response.Content.ReadAsStringAsync().Result );
		}

		eventRecord.SetValue( "status", (int)response.StatusCode );
		eventRecord.SetValue( "tries", (int)tries );

		//
		// don't record event/batch api calls, because then we'll create another event!
		//
		if ( !url.Contains( "event/batch" ) && !Application.IsDedicatedServer )
		{
			eventRecord.Submit();
		}

		return response;

	}

	private void AddHeaders( HttpRequestMessage request )
	{
		//
		// Session authorization
		//
		if ( AccountInformation.Session is not null )
		{
			request.Headers.Remove( "Authorization" );
			request.Headers.Add( "Authorization", $"session {AccountInformation.Session}" );

			request.Headers.Remove( "X-User-Id" );
			request.Headers.Add( "X-User-Id", $"{AccountInformation.SteamId}" );

			//	Log.Info( $"{AccountInformation.Session}" );
			//	Log.Info( $"{Steamworks.SteamClient.SteamId}" );
		}

		//
		// Just for fun and diagnostics
		//
		if ( !request.Headers.Contains( "X-Api-Version" ) )
		{
			request.Headers.Add( "Api-Version", "v3" );
			request.Headers.Add( "X-Api-Version", "25" );
			request.Headers.Add( "X-Engine-Version", $"{Sandbox.Engine.Protocol.Api}" );
			request.Headers.Add( "X-Network-Version", $"{Sandbox.Engine.Protocol.Network}" );
			request.Headers.Add( "X-Session", Api.LaunchGuid );
			request.Headers.Add( "X-Framework", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription );
			request.Headers.Add( "User-Agent", $"Sandbox/1.0 ({System.Runtime.InteropServices.RuntimeInformation.OSDescription}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})" );
		}
	}
}
