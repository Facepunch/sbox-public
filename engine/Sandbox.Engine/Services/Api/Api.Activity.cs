using Sandbox.Engine;
using Sandbox.UI;
using System.Diagnostics;
using System.Threading;
using static Sandbox.Diagnostics.PerformanceStats;

namespace Sandbox;

internal static partial class Api
{
	public static class Activity
	{
		static int ActivityCount;
		static string[] lastAddons;
		static int sessionHashCode;

		static SemaphoreSlim ActivityMutex = new SemaphoreSlim( 1, 1 );

		public static bool IsSessionActive => SessionId != Guid.Empty;

		public static float SessionSeconds => IsSessionActive ? (float)SessionTimer.ElapsedSeconds : 0.0f;
		public static FastTimer SessionTimer;

		public static async Task UpdateActivity( string game, string gameVersion, string map, string[] addons )
		{
			var shc = HashCode.Combine( game );
			bool newSessionHash = shc != sessionHashCode;
			sessionHashCode = shc;

			try
			{
				await ActivityMutex.WaitAsync();
				var performanceData = Performance.Flip();

				if ( Application.IsEditor ) return;

				if ( Application.IsDedicatedServer )
				{
					await UpdateDedicatedServerActivity( game, gameVersion, map, addons, performanceData );
					return;
				}

				//
				// Start new session hash if not set
				//
				if ( newSessionHash )
				{
					await CloseActivity( performanceData );

					if ( game == null || game.Contains( "#local" ) || game.StartsWith( "local." ) )
						return;

					//Log.Info( $"New Session Started! ({game}/{map})" );

					SessionId = Guid.NewGuid();

					NativeErrorReporter.SetTag( "activity_session_id", SessionId.ToString() );

					SessionTimer = FastTimer.StartNew();
					ActivityCount = 0;
					performanceData = null;
				}

				lastAddons = addons;

				// Something went wrong
				if ( !IsSessionActive )
					return;

				if ( game != null ) game = game.Trim().ToLower();

				var data = new Dictionary<string, object>();
				data.Add( "game", game );
				data.Add( "gameversion", gameVersion );
				data.Add( "map", map );
				data.Add( "content", lastAddons );
				data.Add( "st", SessionSeconds.FloorToInt() );
				data.Add( "sh", SessionId.ToString() );
				data.Add( "performance", performanceData );
				data.Add( "config", GetConfig() );
				data.Add( "hardware", Engine.SystemInfo.AsObject() );
				data.Add( "i", ActivityCount++ );

				if ( newSessionHash )
					data.Add( "open", 1 );

				await Sandbox.Backend.Account?.Activity( data );

			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
			finally
			{
				ActivityMutex.Release();
			}
		}

		private static Task UpdateDedicatedServerActivity( string game, string gameVersion, string map, string[] addons, object performanceData )
		{
			var e = new Api.Events.EventRecord( "DedicatedStatus" );

			e.SetValue( "Machine", Environment.MachineName );
			e.SetValue( "System", new { Hardware = SystemInfo.AsObject(), Config = Sandbox.Api.GetConfig() } );
			e.SetValue( "Game", game );
			e.SetValue( "Map", map );
			e.SetValue( "Clients", Connection.All.Count() );
			e.SetValue( "Addons", addons );
			e.SetValue( "Hostname", Networking.ServerName );

			e.SetValue( "Uptime", RealTime.Now );
			e.SetValue( "ApproximateProcessMemoryUsage", PerformanceStats.ApproximateProcessMemoryUsage );

			e.SetValue( "Timings", Timings.All.ToDictionary( x => x.Key, x => x.Value.AverageMs( int.MaxValue ) ) );
			e.SetValue( "PerformanceData", performanceData );

			e.Submit();

			return Task.CompletedTask;
		}

		internal static async Task Shutdown()
		{
			if ( !IsSessionActive ) return;

			var performanceData = Performance.Flip();
			await CloseActivity( performanceData );
		}

		static async Task CloseActivity( object performanceData )
		{
			if ( !IsSessionActive ) return;

			// wait for the stats to flush first - we need the session hash!
			await Stats.ForceFlushAsync();

			var data = new Dictionary<string, object>();
			data.Add( "game", "" );
			data.Add( "st", SessionSeconds.FloorToInt() );
			data.Add( "sh", SessionId.ToString() );
			data.Add( "i", ActivityCount++ );
			data.Add( "content", lastAddons );
			data.Add( "performance", performanceData );
			data.Add( "config", GetConfig() );
			data.Add( "hardware", Engine.SystemInfo.AsObject() );
			data.Add( "close", 1 );

			try
			{
				await Sandbox.Backend.Account?.Activity( data );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Error when closing activity {e.Message}" );
			}

			SessionId = Guid.Empty;
			SessionTimer = default;
			ActivityCount = -1;
			lastAddons = null;
		}
	}
}
