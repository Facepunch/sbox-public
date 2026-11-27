using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;

namespace Borealis.Backend;

public sealed partial class PassModules
{
	/// <summary>What <see cref="CompileMounted"/> did with a package's shaders.</summary>
	public sealed record MountedShaders( int Files, int Modules, int Stages, int Failed, int NoModule, TimeSpan Elapsed );

	/// <summary>
	/// Start compiling, on the compile threads, what a package's outdated shaders (<see cref="ShaderFile.Outdated"/>, compiled by
	/// an older engine) draw first: for each, the module made from its embedded source, every mode's stages with features and
	/// dynamic combos at their first values. A package calls it as it mounts, so its shaders compile behind the loading screen
	/// (<see cref="Compiling"/> counts them) instead of on first draw, and a draw asking for the same stage waits for the same
	/// compile. A stage that doesn't compile is told once (<see cref="Error"/>) and its shader falls back as on draw. Files that
	/// aren't outdated, can't be read or have no module are left alone. The task ends when every stage is compiled or failed.
	/// </summary>
	public Task<MountedShaders> CompileMounted( string package, IEnumerable<string> compiledFiles )
	{
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		var files = compiledFiles.ToList();
		return Task.Run( async () =>
		{
			var started = new List<(PassModule Module, string Shader, string Key, Task<CompiledPassStage> Task)>();
			int modules = 0, noModule = 0;
			foreach ( var file in files )
			{
				ShaderFile shader;
				try
				{
					shader = ShaderFile.LoadOutdated( file );
				}
				catch ( Exception e ) when ( e is InvalidDataException or IOException or EndOfStreamException )
				{
					continue;
				}

				if ( shader.Outdated is null ) continue;
				if ( EmbeddedModule( shader ) is not { } module )
				{
					noModule++;
					continue;
				}

				modules++;
				foreach ( var (kind, values) in FirstStages( module, shader, device.Gpu.Capabilities.VertexWaveOps ) )
				{
					var key = StageKey( module, kind, values );
					if ( Start( module, kind, values, asked: false ) is { } task ) started.Add( (module, Path.GetFileName( file ), key, task) );
				}
			}

			var failedStages = 0;
			foreach ( var (module, shader, key, task) in started )
			{
				try
				{
					await task.ConfigureAwait( false );
				}
				catch ( ShaderCompileException e )
				{
					failedStages++;
					lock ( stages )
					{
						failed[key] = e;
						running.Remove( key );
					}

					bool first;
					lock ( reported ) first = reported.Add( $"{module.Path}|{e.Errors}" );
					if ( first ) Error?.Invoke( $"{package}: {shader} doesn't compile from its embedded source, so it is recompiled through the ShaderCompiler or draws nothing: {FirstLine( e.Errors )}" );
				}
			}

			return new MountedShaders( files.Count, modules, started.Count, failedStages, noModule, System.Diagnostics.Stopwatch.GetElapsedTime( start ) );
		} );
	}

	/// <summary>
	/// Each mode's stages of a module with features and dynamic combos at their first values, as a first draw asks for them;
	/// <paramref name="vertexWaves"/> is whether the device has wave operations in vertex shaders (<see cref="NoVertexWaves"/>).
	/// </summary>
	public static IEnumerable<(StageKind Kind, Dictionary<string, int> Values)> FirstStages( PassModule module, ShaderFile shader, bool vertexWaves )
	{
		var features = FeatureValues( module, shader, [] );
		var seen = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var mode in shader.Modes.Count > 0 ? shader.Modes : [new ShaderMode { Name = "Default", Fallback = "", Settings = [] }] )
		{
			var settings = Settings( mode );
			foreach ( var kind in new[] { StageKind.Vertex, StageKind.Pixel, StageKind.Compute } )
			{
				if ( !module.Has( kind ) ) continue;
				var values = StageValues( module, kind, settings, features, null );
				if ( !module.Has( kind, values ) ) continue;
				if ( kind == StageKind.Vertex && !vertexWaves ) values[NoVertexWaves] = 1;
				if ( seen.Add( StageKey( module, kind, values ) ) ) yield return (kind, values);
			}
		}
	}

	static string FirstLine( string errors ) =>
		(errors.Split( '\n' ).FirstOrDefault( l => l.Contains( "error", StringComparison.OrdinalIgnoreCase ) ) ?? errors.Split( '\n' ).FirstOrDefault( l => l.Trim().Length > 0 ) ?? "").Trim();
}
