namespace Sandbox.ContentBuild;

/// <summary>
/// Compiles one asset at a time, in its own process or in the editor. It never compiles anything but the
/// asset it is given (generated children stay inside it) and reports what the compile read, so the build
/// can check its order and decide next time whether the outputs are still current.
/// </summary>
public interface IAssetCompiler
{
	/// <summary>
	/// How sources with this extension (".vmat") are built, and the extension of their primary output (".vmat_c").
	/// </summary>
	AssetKind Classify( string sourceExtension, out string outputExtension );

	/// <summary>
	/// Compiles <see cref="CompileJob.Source"/>, writing every output under <see cref="CompileJob.Staging"/>
	/// as staging/&lt;mod&gt;/&lt;root-relative path&gt;. Published outputs of every mounted root stay
	/// visible to it. See <see cref="JobMode"/> for the other two things it can be asked.
	/// </summary>
	CompileResult Compile( CompileJob job, CancellationToken token );

	/// <summary>
	/// The current fingerprint of each special dependency (compiler versions, shader versions), or null
	/// where it can't be calculated.
	/// </summary>
	IReadOnlyList<uint?> Fingerprints( IReadOnlyList<SpecialDependency> dependencies );

	/// <summary>
	/// These outputs were just published or removed, before anything that reads them starts: nothing may give a later job
	/// what it loaded of them before.
	/// </summary>
	void Published( IReadOnlyList<string> paths ) { }

	/// <summary>
	/// Let go of anything kept between jobs (worker processes). Whoever made the compiler says when.
	/// </summary>
	void Finished() { }
}

public enum AssetKind
{
	/// <summary>Not compiled: an input of other assets, or something else entirely.</summary>
	None,

	/// <summary>Compiled by the engine, but not by this builder yet. Reported, never silently skipped.</summary>
	Unsupported,

	/// <summary>Audited: reads no other asset's compiled output.</summary>
	Compiled,

	/// <summary>Reads other assets' compiled outputs; describing it lists them before it compiles.</summary>
	Described,
}

public enum JobMode
{
	Compile,

	/// <summary>Report the compiled outputs the current source would read; write nothing.</summary>
	Describe,

	/// <summary>
	/// Report what the existing output was built from if it is all still current; compile nothing. Fails
	/// if there is no current output. A root built before this builder existed starts from these.
	/// </summary>
	Adopt,
}

/// <param name="Root">The root the source is in.</param>
/// <param name="Mounts">What the build mounts (<see cref="BuildRequest.Mounts"/>), for a compiler that has to mount it itself.</param>
/// <param name="Source">Absolute path of the source file.</param>
/// <param name="Staging">Empty directory the compile writes into.</param>
public sealed record CompileJob( ContentRoot Root, IReadOnlyList<(string Mod, string Path)> Mounts, string Source, string Staging, IReadOnlyList<string> CompilerArgs, JobMode Mode = JobMode.Compile );

/// <param name="Outputs">Absolute paths of everything written, under the staging directory.</param>
/// <param name="Inputs">Every file read, with the checksum the compile saw, including optional ones that didn't exist.</param>
/// <param name="Arguments">Names of the compiler arguments the compile asked for.</param>
/// <param name="Prerequisites">Other assets' compiled outputs the compile read.</param>
public sealed record CompileResult(
	bool Success,
	string Log,
	IReadOnlyList<string> Outputs,
	IReadOnlyList<InputFact> Inputs,
	IReadOnlyList<SpecialDependency> Special,
	IReadOnlyList<string> Arguments,
	IReadOnlyList<PrerequisiteRead> Prerequisites );

/// <param name="Path">Absolute path, as the compiler resolved it.</param>
/// <param name="Exists">False for an optional input that wasn't there; it matters if it appears.</param>
/// <param name="Checksum">Low 32 bits of the file's XXH3-64.</param>
/// <param name="Game">Another asset's compiled output that was read, which records keep as a prerequisite.</param>
public sealed record InputFact( string Path, bool Exists, uint Checksum, bool Game = false );

public sealed record SpecialDependency( string Compiler, uint Data, string Name, uint Fingerprint );

/// <param name="Source">Absolute path of the source whose compiled output is read.</param>
/// <param name="Compiled">Absolute path of the compiled file that was there to read, or null.</param>
/// <param name="OrderOnly">Built first, but its contents changing doesn't make the reader stale.</param>
public sealed record PrerequisiteRead( string Source, string Compiled, bool OrderOnly = false );

/// <summary>
/// The tab-separated facts resourcecompiler reports for a build (ResourceCompilerContext::ReportBuildFact),
/// between a "buildreport 1" header and a "result 0|1" trailer. Anything else fails the job: a crashed
/// or truncated compile mustn't look like one that read nothing.
/// </summary>
public static class BuildReportParser
{
	public static CompileResult Parse( IEnumerable<string> lines, bool success, string log )
	{
		var outputs = new List<string>();
		var inputs = new List<InputFact>();
		var special = new List<SpecialDependency>();
		var arguments = new List<string>();
		var prerequisites = new List<PrerequisiteRead>();
		CompileResult Malformed( string why ) => new( false, $"malformed build report: {why}\n{log}", [], [], [], [], [] );

		var list = lines.Select( x => x.TrimEnd( '\r', '\0' ) ).Where( x => x.Length > 0 ).ToList();
		if ( list.Count < 2 || list[0] != "buildreport\t1" ) return Malformed( "no header" );
		if ( list[^1] is not ("result\t0" or "result\t1") ) return Malformed( "no result" );

		foreach ( var line in list.Skip( 1 ).SkipLast( 1 ) )
		{
			var f = line.Split( '\t' );
			var ok = (f[0], f.Length) switch
			{
				("output", 2 ) => Add( outputs, Path.GetFullPath( f[1] ) ),
				("input", 4 ) => Add( inputs, new InputFact( f[3], (Hex( f[1] ) & 2) != 0, Hex( f[2] ), (Hex( f[1] ) & 4) != 0 ) ),
				("special", 5 ) => Add( special, new SpecialDependency( f[1], uint.Parse( f[2] ), f[4], Hex( f[3] ) ) ),
				("arg", 2 ) => Add( arguments, f[1] ),
				("prereq", 3 or 4 ) => f[1].Length == 0 || (f.Length == 3 || f[3] == "order") && Add( prerequisites, new PrerequisiteRead( f[1], f[2].Length > 0 ? Path.GetFullPath( f[2] ) : null, f.Length == 4 ) ),
				_ => false,
			};
			if ( !ok ) return Malformed( line );
		}

		return new CompileResult( success && list[^1] == "result\t1", log, outputs, inputs, special, arguments, prerequisites );
	}

	static bool Add<T>( List<T> list, T item )
	{
		list.Add( item );
		return true;
	}

	static uint Hex( string value ) => uint.Parse( value, System.Globalization.NumberStyles.HexNumber );
}
