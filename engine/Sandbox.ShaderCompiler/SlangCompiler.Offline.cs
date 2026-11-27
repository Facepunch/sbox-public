using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace Sandbox.ShaderCompiler;

/// <summary>What <see cref="SlangCompiler.CompileOfflineAsync"/> did with a source.</summary>
public enum OfflineResult
{
	/// <summary>Its offline compile was made from this source and compiler already: nothing to do.</summary>
	UpToDate,

	/// <summary>Compiled and written.</summary>
	Compiled,

	/// <summary>Not a module this compiles (a library module, a <c>.slangh</c>-style include).</summary>
	Skipped,

	/// <summary>Some of it didn't compile; what did is written, the rest compiles in game.</summary>
	Failed,
}

public sealed partial class SlangCompiler
{
	/// <summary>
	/// Compile the <c>.slang</c> at <paramref name="source"/> offline, into its <see cref="SlangOutput"/> (<paramref name="output"/>,
	/// <c>name.slang_c</c> beside it unless given): a surface's or raw module's every pass and layout, or every stage of a pass
	/// module with every combination of its combos' values that its rules allow and that has an entry point, as vfx compiles
	/// a <c>.shader</c>. Nothing is compiled when the output was made from this source and compiler and every file it read is
	/// unchanged. <see cref="CacheFolder"/> is used for what's already compiled. Errors go to <paramref name="log"/>.
	/// </summary>
	public async Task<OfflineResult> CompileOfflineAsync( string source, string output = null, Action<string> log = null )
	{
		source = Path.GetFullPath( source );
		output ??= SlangOutput.PathOf( source );
		var text = File.ReadAllText( source );
		var pass = text.Contains( "sbox/pass.slangh", StringComparison.Ordinal );
		var raw = !pass && IsRaw( text );
		if ( !pass && !raw )
		{
			MaterialSource.Rewrite( text, out var declared );
			if ( declared.Count == 0 ) return OfflineResult.Skipped;
		}

		var paths = Paths( source );
		var identity = $"{CompileCache.Identity( slangDirectory )}|{(pass ? "pass" : raw ? "raw" : "surface")}";
		if ( SlangOutput.At( output ) is { } existing && existing.Current( identity, text, paths ) ) return OfflineResult.UpToDate;

		var entries = new ConcurrentDictionary<string, byte[]>( StringComparer.Ordinal );
		var read = new ConcurrentDictionary<(string, string), bool>();
		var failed = 0;
		var collector = new Action<string, byte[], List<(string Name, string Hash)>>( ( key, bytes, dependencies ) =>
		{
			entries[key] = bytes;
			foreach ( var dependency in dependencies ) read[dependency] = true;
		} );

		// One collector per compiler: an offline compile runs one source at a time per compiler
		var offline = new SlangCompiler( slangDirectory, searchPaths ) { CacheFolder = CacheFolder, UseOffline = false, Warning = Warning };
		offline.saved = collector;
		var jobs = new List<Task>();
		var module = pass ? PassModule.Read( source, searchPaths ) : null;
		if ( pass )
		{
			foreach ( var stage in Enum.GetValues<StageKind>() )
			{
				if ( !module.Has( stage ) ) continue;
				foreach ( var values in Combinations( module.Combos( stage ) ) )
				{
					// A mode sets its own S_MODE_ combo, one at a time; then what the rules allow and has an entry point
					if ( OtherModes( module.Combos( stage ), values ) || !module.Allows( stage, values ) || !module.Has( stage, values ) ) continue;
					jobs.Add( Report( offline.CompilePassAsync( module, stage, values, CompilePriority.Background() ), $"{Path.GetFileName( source )} {stage} {string.Join( ' ', values.Select( v => $"{v.Key}={v.Value}" ) )}" ) );
				}
			}
		}
		else
		{
			jobs.Add( Report( raw ? offline.CompileRawAsync( source, CompilePriority.Background() ) : offline.CompileSurfaceAsync( source, CompilePriority.Background() ), Path.GetFileName( source ) ) );
		}

		await Task.WhenAll( jobs ).ConfigureAwait( false );
		if ( entries.IsEmpty ) return OfflineResult.Failed;

		SlangOutput.Write( output, identity, text, failed == 0, read.Keys, entries, module?.Declarations() );
		return failed > 0 ? OfflineResult.Failed : OfflineResult.Compiled;

		async Task Report<T>( Task<T> job, string what )
		{
			try
			{
				await job.ConfigureAwait( false );
			}
			catch ( ShaderCompileException e )
			{
				System.Threading.Interlocked.Increment( ref failed );
				log?.Invoke( $"{what}: {e.Errors}" );
			}
		}
	}

	/// <summary>
	/// The combinations of a pass module's combos that <see cref="CompileOfflineAsync"/> compiles for a stage: one mode at a
	/// time, what the rules allow, with an entry point.
	/// </summary>
	public static IEnumerable<Dictionary<string, int>> OfflineCombinations( PassModule module, StageKind stage ) =>
		module.Has( stage ) ? Combinations( module.Combos( stage ) ).Where( v => !OtherModes( module.Combos( stage ), v ) && module.Allows( stage, v ) && module.Has( stage, v ) ) : [];

	/// <summary>Whether more than one mode's combo (<c>S_MODE_*</c>) is set: no mode draws with two.</summary>
	static bool OtherModes( IReadOnlyList<PassCombo> combos, Dictionary<string, int> values ) =>
		combos.Count( c => c.Feature is null && c.Name.StartsWith( "S_MODE_", StringComparison.Ordinal ) && values[c.Name] != c.Min ) > 1;

	/// <summary>Every combination of the combos' values, the first combo changing fastest.</summary>
	static IEnumerable<Dictionary<string, int>> Combinations( IReadOnlyList<PassCombo> combos )
	{
		var values = combos.ToDictionary( c => c.Name, c => c.Min );
		while ( true )
		{
			yield return new Dictionary<string, int>( values );
			var i = 0;
			for ( ; i < combos.Count; i++ )
			{
				var combo = combos[i];
				if ( values[combo.Name] < combo.Max )
				{
					values[combo.Name]++;
					break;
				}

				values[combo.Name] = combo.Min;
			}

			if ( i == combos.Count ) yield break;
		}
	}
}
