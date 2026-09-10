using Sandbox.Engine;
using Sandbox.Services;
using System.Threading;

namespace Sandbox;

internal static partial class Api
{
	internal static partial class Benchmarks
	{
		private static SemaphoreSlim FlushSemaphore = new SemaphoreSlim( 1, 1 );

		/// <summary>
		/// Post a batch of analytic events. Analytic events are things like compile or load times to 
		/// help us find, fix and track performance issues.
		/// </summary>
		public static async Task<Guid> Post( BenchmarkRecord[] records, CancellationToken token )
		{
			if ( Sandbox.Backend.Benchmarks is null )
			{
				Log.Warning( "Benchmark api was missing" );
				return Guid.Empty;
			}

			if ( records.Length == 0 )
			{
				Log.Warning( "Tried to post 0 benchmarks" );
				return Guid.Empty;
			}

			await FlushSemaphore.WaitAsync();

			try
			{
				var data = new BenchmarkInput
				{
					Session = Api.SessionId,
					Package = Application.GameIdent,
					Host = Environment.GetEnvironmentVariable( "SBOXHOST" ) ?? Environment.MachineName,
					Version = Application.Version,
					VersionDate = Application.VersionDate,
					System = new { Hardware = Engine.SystemInfo.AsObject(), Config = GetConfig() },
					Entries = records
				};

				var result = await Sandbox.Backend.Benchmarks.Submit( data );

				if ( !string.IsNullOrEmpty( BenchmarkOrchestrator.ExportPath ) )
				{
					try
					{
						var json = System.Text.Json.JsonSerializer.Serialize( new
						{
							BatchId = result.Id,
							data.Host,
							data.Version,
							data.VersionDate,
							data.System,
							data.Package,
							data.Entries,
						}, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } );
						System.IO.File.WriteAllText( BenchmarkOrchestrator.ExportPath, json );
						Log.Info( $"Benchmark results written to {BenchmarkOrchestrator.ExportPath}" );
					}
					catch ( System.Exception ex )
					{
						Log.Warning( ex, "Failed to write benchmark export JSON" );
					}
				}

				return result.Id;
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when flushing benchmarks ({e.Message})" );
				return Guid.Empty;
			}
			finally
			{
				FlushSemaphore.Release();
			}
		}
	}






}
