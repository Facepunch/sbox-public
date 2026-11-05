using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Sandbox.ContentBuild;

namespace ContentBuild;

/// <summary>
/// A throwaway content root for one scenario, mounted as its own mod. Sources are written inline or
/// copied from shipped content, and every build writes inside <see cref="Parent"/>: outputs land in
/// <see cref="Root"/> beside their sources, anything compiled for another mod lands beside it.
/// </summary>
public sealed class ContentFixture : IDisposable
{
	public static string Engine => Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ) ?? throw new InvalidOperationException( "FACEPUNCH_ENGINE not set" );

	public string Mod { get; }
	public string Parent { get; }
	public string Root { get; }
	internal ResourceCompiler Compiler { get; set; }

	/// <param name="mod">The root's mod, when the content in it is another root's that names its own (a copy of a shipped root).</param>
	public ContentFixture( string mod = null )
	{
		var id = "cbt" + Guid.NewGuid().ToString( "N" )[..8];
		Mod = mod ?? id;
		Parent = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "sbox-contentbuild", id );
		Root = System.IO.Path.Combine( Parent, Mod );
		Directory.CreateDirectory( Root );
	}

	public void Dispose()
	{
		Compiler?.Finished();
		if ( Environment.GetEnvironmentVariable( "CONTENTBUILD_KEEP" ) is not null ) return;
		try { Directory.Delete( Parent, true ); } catch ( IOException ) { } catch ( UnauthorizedAccessException ) { }

		// The asset system caches each mod it scans (one of its own; another root's cache isn't ours to delete)
		var cache = System.IO.Path.Combine( Engine, ".source2", $"assets.{Mod}.cache" );
		if ( Mod.StartsWith( "cbt", StringComparison.Ordinal ) && File.Exists( cache ) ) File.Delete( cache );
	}

	public string Path( string rel ) => System.IO.Path.Combine( Root, rel );
	public bool Exists( string rel ) => File.Exists( Path( rel ) );

	public void Write( string rel, string text )
	{
		Directory.CreateDirectory( System.IO.Path.GetDirectoryName( Path( rel ) ) );
		File.WriteAllText( Path( rel ), text.ReplaceLineEndings( "\r\n" ) );
	}

	/// <summary>
	/// Copies a shipped source file, given relative to game/, into the root.
	/// </summary>
	public void CopyShipped( string engineRel, string rel )
	{
		var source = System.IO.Path.Combine( Engine, engineRel );
		if ( !File.Exists( source ) ) Assert.Fail( $"Shipped fixture source is gone: {source}" );

		Directory.CreateDirectory( System.IO.Path.GetDirectoryName( Path( rel ) ) );
		File.Copy( source, Path( rel ), true );
	}

	public void Delete( string rel ) => File.Delete( Path( rel ) );

	public void Move( string from, string to )
	{
		Directory.CreateDirectory( System.IO.Path.GetDirectoryName( Path( to ) ) );
		File.Move( Path( from ), Path( to ) );
	}

	public byte[] Read( string rel ) => File.ReadAllBytes( Path( rel ) );

	/// <summary>
	/// The output a material generates for a texture input, e.g. materials/a.tga gives
	/// materials/a_tga_&lt;crc&gt;.generated.vtex_c. Null if there is none, fails if there are several.
	/// </summary>
	public string Child( string inputRel )
	{
		var prefix = inputRel.Replace( '.', '_' ) + "_";
		var matches = Outputs().Keys.Where( x => x.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) && x.EndsWith( ".generated.vtex_c", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		Assert.IsTrue( matches.Length <= 1, $"Several children for {inputRel}: {string.Join( ", ", matches )}" );
		return matches.FirstOrDefault();
	}

	public readonly record struct FileState( long WriteTime, long Length, string Hash );

	/// <summary>
	/// Every compiled file under the root, keyed by root-relative path with forward slashes.
	/// </summary>
	public Dictionary<string, FileState> Outputs()
	{
		var outputs = new Dictionary<string, FileState>( StringComparer.OrdinalIgnoreCase );
		foreach ( var file in new DirectoryInfo( Root ).EnumerateFiles( "*_c", SearchOption.AllDirectories ) )
		{
			var rel = System.IO.Path.GetRelativePath( Root, file.FullName ).Replace( '\\', '/' );
			outputs[rel] = new FileState( file.LastWriteTimeUtc.Ticks, file.Length, Convert.ToHexString( SHA1.HashData( File.ReadAllBytes( file.FullName ) ) ) );
		}
		return outputs;
	}

	public BuildRun Build( IContentBuilder builder, BuildOptions options = null, CancellationToken token = default )
	{
		var before = Outputs();
		var timer = Stopwatch.StartNew();
		var build = Task.Run( () => builder.Build( this, options ?? new BuildOptions(), token ) );
		if ( Task.WhenAny( build, Task.Delay( TimeSpan.FromMinutes( 3 ) ) ).Result != build ) Assert.Fail( $"[{builder.Name}] build hung" );
		var result = build.GetAwaiter().GetResult();
		var elapsed = timer.Elapsed;
		var after = Outputs();

		var twice = result.Jobs.GroupBy( x => x, StringComparer.OrdinalIgnoreCase ).Where( x => x.Count() > 1 ).Select( x => x.Key ).ToArray();
		Assert.IsTrue( twice.Length <= result.Reruns, $"[{builder.Name}] compiled more than once: {string.Join( ", ", twice )}" );

		var written = after.Where( x => !before.TryGetValue( x.Key, out var old ) || old.WriteTime != x.Value.WriteTime ).Select( x => x.Key );
		var changed = after.Where( x => !before.TryGetValue( x.Key, out var old ) || old.Hash != x.Value.Hash ).Select( x => x.Key );
		var removed = before.Keys.Where( x => !after.ContainsKey( x ) );
		var invalid = after.Keys.Where( x => !IsValidResource( Path( x ) ) );

		var run = new BuildRun( result, Sorted( written ), Sorted( changed ), Sorted( removed ), Sorted( invalid ), elapsed );
		Console.WriteLine( $"[{builder.Name}] {run}" );
		return run;
	}

	/// <summary>
	/// Rebuilds everything from scratch and checks the result is byte-identical to what incremental
	/// builds left behind: no stale outputs, no missing ones, no out-of-date bytes. Compiled files
	/// embed absolute source paths, so this only compares builds of the same root.
	/// </summary>
	public void AssertMatchesCleanBuild( IContentBuilder builder )
	{
		var incremental = Outputs();
		builder.Clean( this );
		foreach ( var rel in incremental.Keys ) File.Delete( Path( rel ) );

		var clean = Build( builder );
		Assert.IsTrue( clean.Success, $"Clean build failed: {clean}\n{clean.Result.Log}" );
		Assert.AreEqual( 0, clean.Invalid.Length, $"Clean build left incomplete outputs: {clean}" );
		var outputs = Outputs();

		var stale = incremental.Keys.Where( x => !outputs.ContainsKey( x ) ).ToArray();
		var missing = outputs.Keys.Where( x => !incremental.ContainsKey( x ) ).ToArray();
		var differ = outputs.Keys.Where( x => incremental.TryGetValue( x, out var old ) && old.Hash != outputs[x].Hash ).ToArray();

		Assert.IsTrue( stale.Length + missing.Length + differ.Length == 0,
			$"Incremental outputs differ from a clean build.\n  stale: {string.Join( ", ", stale )}\n  missing: {string.Join( ", ", missing )}\n  different bytes: {string.Join( ", ", differ )}\nclean build: {clean}" );
	}

	/// <summary>
	/// A complete resource file: the current header version, and at least as long as its header says.
	/// Catches torn and truncated writes, not bad content.
	/// </summary>
	public static bool IsValidResource( string path )
	{
		var bytes = File.ReadAllBytes( path );
		if ( bytes.Length < 16 ) return false;

		var nonStreamingSize = BitConverter.ToUInt32( bytes, 0 );
		var headerVersion = BitConverter.ToUInt16( bytes, 4 );
		return headerVersion == 12 && nonStreamingSize >= 16 && nonStreamingSize <= bytes.Length;
	}

	static string[] Sorted( IEnumerable<string> paths ) => paths.Order( StringComparer.OrdinalIgnoreCase ).ToArray();
}

/// <param name="Written">Outputs created or rewritten by this build.</param>
/// <param name="Changed">Outputs whose bytes are new.</param>
/// <param name="Removed">Outputs that existed before and are gone.</param>
/// <param name="Invalid">Outputs that aren't complete resource files.</param>
public sealed record BuildRun( BuildResult Result, string[] Written, string[] Changed, string[] Removed, string[] Invalid, TimeSpan Elapsed )
{
	public bool Success => Result.Success;

	public bool Wrote( string rel ) => Written.Contains( rel, StringComparer.OrdinalIgnoreCase );

	public override string ToString() =>
		$"{(Success ? "ok" : "FAILED")} in {Elapsed.TotalSeconds:F1}s" + (Result.Reruns > 0 ? $", {Result.Reruns} reruns" : "") +
		$"\n  jobs: {string.Join( ", ", Result.Jobs )}" +
		$"\n  written: {string.Join( ", ", Written )}" +
		(Removed.Length > 0 ? $"\n  removed: {string.Join( ", ", Removed )}" : "") +
		(Result.Failed.Count > 0 ? $"\n  failed: {string.Join( ", ", Result.Failed )}" : "") +
		(Invalid.Length > 0 ? $"\n  INVALID: {string.Join( ", ", Invalid )}" : "");
}
