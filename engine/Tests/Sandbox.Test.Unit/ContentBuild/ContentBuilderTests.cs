using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Sandbox.ContentBuild;

namespace ContentBuild;

/// <summary>
/// The planner, scheduler, records and publication, against a compiler that follows a script in each
/// source: "reads X" reads X's compiled output (X can be "mod:path" in another root, or "*path" found
/// through the mounts), "orders X" reads it order only, "hidden X" reads it without describing it, "child R C"
/// writes another output, "generates R" writes one into transient, "input R" reads a file, "version" depends on a compiler version, "sleep N" and
/// "fail" do what they say. Any other line is just content. .vmat and .vmdl are described; .vtex isn't
/// and reads nothing unless a test says otherwise.
/// </summary>
[TestClass]
public class ContentBuilderTests
{
	sealed class ScriptCompiler : IAssetCompiler
	{
		public ConcurrentDictionary<string, uint> Versions { get; } = new() { ["version"] = 1 };
		public ConcurrentQueue<string> Compiled { get; } = new();

		/// <summary>Compiles started ("compile b.vmat") and outputs published ("published a.vtex_c"), in order.</summary>
		public ConcurrentQueue<string> Events { get; } = new();
		public ConcurrentQueue<int> Threads { get; } = new();
		public int MaxConcurrent;
		int running;

		/// <summary>Game resources: built by the editor, not by SboxBuild.</summary>
		public bool BuildsSounds { get; init; }

		/// <summary>Runs inside the compile, after it read its inputs.</summary>
		public Action<CompileJob> During { get; set; }

		public Func<CompileJob, CompileResult> Describer { get; set; }
		public Func<CompileJob, CompileResult> Adopter { get; set; }

		/// <summary>Where transient is mounted for this build, if anywhere.</summary>
		public string Transient { get; set; }

		/// <summary>Outputs record the compiled files they read, as the worker's do; off for an older compiler's.</summary>
		public bool RecordsReads { get; set; } = true;

		public AssetKind Classify( string sourceExtension, out string outputExtension )
		{
			outputExtension = SoundOrNull( sourceExtension ) ?? sourceExtension + "_c";
			return sourceExtension switch
			{
				".wav" or ".mp3" => AssetKind.Compiled,
				".vmat" or ".vmdl" => AssetKind.Described,
				".vtex" => AssetKind.Compiled,
				".sound" when BuildsSounds => AssetKind.Compiled,
				".vmap" => AssetKind.Unsupported,
				_ => AssetKind.None,
			};
		}

		public CompileResult Compile( CompileJob job, CancellationToken token )
		{
			if ( job.Mode == JobMode.Describe ) return Describer?.Invoke( job ) ?? Describe( job );
			if ( job.Mode == JobMode.Adopt ) return Adopter?.Invoke( job ) ?? Adopt( job, Transient );
			Threads.Enqueue( Environment.CurrentManagedThreadId );

			var now = Interlocked.Increment( ref running );
			lock ( this ) MaxConcurrent = Math.Max( MaxConcurrent, now );
			try { return Run( job ); }
			finally { Interlocked.Decrement( ref running ); }
		}

		/// <summary>
		/// An output is current if it was compiled from the source as it is now, and every compiled output it read
		/// (which it recorded, like the worker's edit info) is still what it read.
		/// </summary>
		static CompileResult Adopt( CompileJob job, string transient )
		{
			var output = job.Source + "_c";
			var marker = job.Source + ".src_c";
			var stale = new CompileResult( false, "stale", [], [], [], [], [] );
			if ( !File.Exists( output ) || !File.Exists( marker ) ) return stale;

			var recorded = File.ReadAllLines( marker );
			if ( recorded[0] != SourceHash( job ) ) return stale;

			var reads = recorded.Skip( 1 ).Select( x => x.Split( '\t' ) ).Select( x => new InputFact( x[1], true, uint.Parse( x[2] ), true ) ).ToList();
			if ( reads.Any( x => !File.Exists( x.Path ) || Hashing.Checksum( x.Path ) != x.Checksum ) ) return stale;

			// Like the edit info: outputs including generated children wherever they resolve, no reads
			var lines = File.ReadAllLines( job.Source ).Select( x => x.Split( ' ' ) ).ToList();
			var children = lines.Where( x => x[0] == "child" ).Select( x => Path.Combine( job.Root.Path, x[1] ) );
			var generated = lines.Where( x => x[0] == "generates" ).Select( x => Path.Combine( transient ?? job.Root.Path, x[1] ) );
			var foreign = lines.Where( x => x[0] == "foreign" ).Select( x => Path.GetFullPath( Path.Combine( job.Root.Path, "..", x[1] ) ) );
			return new CompileResult( true, "", [output, marker, .. children, .. generated, .. foreign], [Input( job.Source ), .. reads], [], [], [] );
		}

		/// <summary>Sounds compile to .vsnd_c whatever their source format, so two sources can make one output.</summary>
		static string SoundOrNull( string extension ) => extension is ".wav" or ".mp3" ? ".vsnd_c" : null;

		static string SourceHash( CompileJob job ) => File.ReadAllText( job.Source ).GetHashCode().ToString();

		static CompileResult Describe( CompileJob job )
		{
			var reads = File.ReadAllLines( job.Source ).Select( x => x.Split( ' ' ) ).Where( x => x[0] is "reads" or "orders" )
				.Select( x => new PrerequisiteRead( Resolve( job, x[1] ), null, x[0] == "orders" ) ).ToList();
			return new CompileResult( true, "", [], [], [], [], reads );
		}

		CompileResult Run( CompileJob job )
		{
			var rel = Path.GetRelativePath( job.Root.Path, job.Source ).Replace( '\\', '/' );
			Compiled.Enqueue( rel );
			Events.Enqueue( $"compile {rel}" );

			var content = new StringBuilder( $"compiled {rel}\n" );
			var sourceHash = SourceHash( job );
			var inputsRead = Input( job.Source );
			During?.Invoke( job );
			var outputs = new List<string>();
			var inputs = new List<InputFact> { inputsRead };
			var special = new List<SpecialDependency>();
			var reads = new List<PrerequisiteRead>();
			var success = true;

			foreach ( var line in File.ReadAllLines( job.Source ) )
			{
				var f = line.Split( ' ' );
				switch ( f[0] )
				{
					case "reads":
					case "hidden":
						var read = Resolve( job, f[1] ) + "_c";
						reads.Add( new PrerequisiteRead( Resolve( job, f[1] ), File.Exists( read ) ? read : null ) );
						content.AppendLine( File.Exists( read ) ? File.ReadAllText( read ) : "missing" );
						break;
					case "orders":
						var ordered = Resolve( job, f[1] ) + "_c";
						reads.Add( new PrerequisiteRead( Resolve( job, f[1] ), File.Exists( ordered ) ? ordered : null, true ) );
						content.AppendLine( File.Exists( ordered ) ? "present" : "missing" );
						break;
					case "child": outputs.Add( Write( job, f[1], f[2] ) ); break;
					case "generates": outputs.Add( Write( job, f[1], $"generated by {rel}", "transient" ) ); break;
					case "input": inputs.Add( Input( Resolve( job, f[1] ) ) ); break;
					case "version": special.Add( new SpecialDependency( "Script", 0, "version", Versions["version"] ) ); break;
					case "sleep": Thread.Sleep( int.Parse( f[1] ) ); break;
					case "fail": success = false; break;
					default: content.AppendLine( line ); break;
				}
			}

			outputs.Add( Write( job, SoundOrNull( Path.GetExtension( rel ) ) is { } sound ? Path.ChangeExtension( rel, sound ) : rel + "_c", content.ToString() ) );
			var readsRecorded = reads.Where( x => RecordsReads && x.Compiled is not null && !x.OrderOnly ).Select( x => $"read\t{x.Compiled}\t{Hashing.Checksum( x.Compiled )}" );
			outputs.Add( Write( job, rel + ".src_c", string.Join( "\n", readsRecorded.Prepend( sourceHash ) ) ) );
			return new CompileResult( success, success ? "" : "scripted failure", outputs, inputs, special, [], reads );
		}

		public IReadOnlyList<uint?> Fingerprints( IReadOnlyList<SpecialDependency> dependencies ) =>
			dependencies.Select( x => Versions.TryGetValue( x.Name, out var v ) ? v : (uint?)null ).ToArray();

		public void Published( IReadOnlyList<string> paths )
		{
			foreach ( var path in paths ) Events.Enqueue( $"published {Path.GetFileName( path )}" );
		}

		/// <summary>
		/// A path in the job's root, "mod:path" in another root of the build, or "*path" in the last-mounted
		/// root that has it, as a search path would find it.
		/// </summary>
		static string Resolve( CompileJob job, string path )
		{
			if ( path.StartsWith( '*' ) )
			{
				var found = job.Mounts.Reverse().Select( x => x.Path ).FirstOrDefault( x => File.Exists( Path.Combine( x, path[1..] ) ) || File.Exists( Path.Combine( x, path[1..] + "_c" ) ) ) ?? job.Root.Path;
				return Path.Combine( found, path[1..] );
			}

			var colon = path.IndexOf( ':' );
			var root = colon < 0 ? job.Root.Path : job.Mounts.Last( x => x.Mod == path[..colon] ).Path;
			return Path.Combine( root, path[(colon + 1)..] );
		}

		static InputFact Input( string path ) => new( path, File.Exists( path ), File.Exists( path ) ? Hashing.Checksum( path ) : 0 );

		static string Write( CompileJob job, string rel, string text, string mod = null )
		{
			var path = Path.Combine( job.Staging, mod ?? job.Root.Mod, rel );
			Directory.CreateDirectory( Path.GetDirectoryName( path ) );
			File.WriteAllText( path, text );
			return path;
		}
	}

	sealed class Fixture : IDisposable
	{
		public readonly string Dir = Directory.CreateTempSubdirectory( "sbox-contentbuild-unit" ).FullName;
		public string Root => Path.Combine( Dir, "unit" );
		public string State => Path.Combine( Dir, "state" );
		public readonly ScriptCompiler Compiler = new();

		public Fixture() => Directory.CreateDirectory( Root );
		public void Dispose() => Directory.Delete( Dir, true );

		public void Write( string rel, params string[] lines )
		{
			Directory.CreateDirectory( Path.GetDirectoryName( Path.Combine( Root, rel ) ) );
			File.WriteAllLines( Path.Combine( Root, rel ), lines );
		}

		public string Read( string rel ) => File.ReadAllText( Path.Combine( Root, rel ) );
		public bool Exists( string rel ) => File.Exists( Path.Combine( Root, rel ) );

		public BuildReport Build( IReadOnlyList<string> select = null, int maxJobs = 4, Action<string> beforeCommit = null, ScriptCompiler compiler = null, CancellationToken token = default, TimeSpan? lockTimeout = null, string transient = null )
		{
			while ( Compiler.Compiled.TryDequeue( out _ ) ) { }
			var outputRoots = transient is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["transient"] = Path.Combine( Dir, transient ) };
			(compiler ?? Compiler).Transient = transient is null ? null : Path.Combine( Dir, transient );
			var request = new BuildRequest { Roots = [new ContentRoot( "unit", Root, State )], Select = select?.Select( x => Path.Combine( Root, x ) ).ToList(), MaxJobs = maxJobs, BeforeCommit = beforeCommit, LockTimeout = lockTimeout ?? TimeSpan.FromSeconds( 30 ), OutputRoots = outputRoots };
			return ContentBuilder.Build( request, compiler ?? Compiler, token );
		}
	}

	static void AssertJobs( BuildReport report, params string[] expected ) =>
		CollectionAssert.AreEquivalent( expected, report.Jobs.ToArray(), $"Jobs: {string.Join( ", ", report.Jobs )}\n{report.Log}" );

	[TestMethod]
	public void StagingDirectoriesAreReusedAcrossJobs()
	{
		using var fx = new Fixture();
		for ( var i = 0; i < 40; i++ )
		{
			fx.Write( $"t{i}.vtex", "red" );
			fx.Write( $"m{i}.vmat", $"reads *t{i}.vtex" );
		}

		var staging = new ConcurrentDictionary<string, byte>( StringComparer.OrdinalIgnoreCase );
		fx.Compiler.During = job => staging.TryAdd( job.Staging, 0 );
		var report = fx.Build( maxJobs: 2 );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( 80, report.Jobs.Count );
		Assert.IsTrue( staging.Count <= 4, $"{staging.Count} staging directories for 80 jobs" );
	}

	[TestMethod]
	public void ShippedMountsNeedNoSourcesOrBuildStateAndProjectOverridesWin()
	{
		using var fx = new Fixture();
		var shipped = Path.Combine( fx.Dir, "shipped" );
		Directory.CreateDirectory( shipped );
		var output = Path.Combine( shipped, "a.vtex_c" );
		File.WriteAllText( output, "shipped red" );
		// A leftover source with missing dependencies must not be compiled or adopted on an installed editor.
		File.WriteAllText( Path.Combine( shipped, "unused.vmat" ), "fail" );
		fx.Write( "b.vmat", "reads *a.vtex" );
		BuildReport Build() => ContentBuilder.Build( new BuildRequest
		{
			Roots = [new ContentRoot( "unit", fx.Root, fx.State )],
			ReadOnlyMounts = [("shipped", shipped)]
		}, fx.Compiler );

		var cold = Build();
		Assert.IsTrue( cold.Success, cold.Log.ToString() );
		AssertJobs( cold, "b.vmat" );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "shipped red" );
		AssertJobs( Build() );
		File.WriteAllText( output, "shipped blue" );
		AssertJobs( Build(), "b.vmat" );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "shipped blue" );
		Assert.AreEqual( 2, Directory.GetFiles( shipped, "*", SearchOption.AllDirectories ).Length );
		Assert.AreEqual( 0, Directory.GetDirectories( shipped ).Length );

		// The project's copy takes precedence over shipped content, and removing it restores the shipped copy.
		fx.Write( "a.vtex", "project green" );
		AssertJobs( Build(), "a.vtex", "b.vmat" );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "project green" );
		File.Delete( Path.Combine( fx.Root, "a.vtex" ) );
		AssertJobs( Build(), "b.vmat" );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "shipped blue" );
	}

	[TestMethod]
	public void UnchangedPrerequisiteDoesNotNeedItsContentsRead()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );
		Assert.IsTrue( fx.Build().Success );

		// File metadata remains readable, but hashing the bytes would fail on Windows.
		using ( var held = new FileStream( Path.Combine( fx.Root, "a.vtex_c" ), FileMode.Open, FileAccess.Read, FileShare.None ) )
		{
			var warm = fx.Build();
			Assert.IsTrue( warm.Success, warm.Log.ToString() );
			AssertJobs( warm );
		}

		// A changed producer still rebuilds its reader, even though the old output had a reusable stamp.
		fx.Write( "a.vtex", "blue" );
		AssertJobs( fx.Build(), "a.vtex", "b.vmat" );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "blue" );
	}

	[TestMethod]
	public void OutputTouchedWithSameBytesIsHashedOnce()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		Assert.IsTrue( fx.Build().Success );

		// A checkout or copy moves its time, not its bytes: still current, and its new stamp is recorded
		var output = Path.Combine( fx.Root, "a.vtex_c" );
		File.SetLastWriteTimeUtc( output, File.GetLastWriteTimeUtc( output ).AddHours( 1 ) );
		AssertJobs( fx.Build() );

		// Hashing it again would fail on Windows while it's held
		using var held = new FileStream( output, FileMode.Open, FileAccess.Read, FileShare.None );
		var warm = fx.Build();
		Assert.IsTrue( warm.Success, warm.Log.ToString() );
		AssertJobs( warm );
	}

	[TestMethod]
	[DataRow( JobMode.Describe )]
	[DataRow( JobMode.Adopt )]
	[DataRow( JobMode.Compile )]
	public void AsyncBuildYieldsAndPublishesOnCallingThread( JobMode mode )
	{
		using var fx = new Fixture();
		fx.Write( "a.vmat", "red" );
		if ( mode == JobMode.Adopt ) fx.Write( "a.vmat_c", "old" );
		using var release = new ManualResetEventSlim();
		var caller = Environment.CurrentManagedThreadId;
		var calls = 0;
		void WaitForEditor()
		{
			Assert.AreNotEqual( caller, Environment.CurrentManagedThreadId );
			Assert.IsTrue( release.Wait( TimeSpan.FromSeconds( 10 ) ), "The caller must be able to run while a compiler waits" );
		}
		if ( mode == JobMode.Describe ) fx.Compiler.Describer = _ => { WaitForEditor(); return new( true, "", [], [], [], [], [] ); };
		if ( mode == JobMode.Adopt ) fx.Compiler.Adopter = _ => { WaitForEditor(); return new( false, "stale", [], [], [], [], [] ); };
		if ( mode == JobMode.Compile ) fx.Compiler.During = _ => WaitForEditor();

		Sandbox.Tasks.AsyncPump.RunSynchronously( async () =>
		{
			var request = new BuildRequest
			{
				Roots = [new ContentRoot( "unit", fx.Root, fx.State )],
				MaxJobs = 1,
				Published = _ => { Assert.AreEqual( caller, Environment.CurrentManagedThreadId ); calls++; },
				JobFinished = _ => Assert.AreEqual( caller, Environment.CurrentManagedThreadId )
			};
			var build = ContentBuilder.BuildAsync( request, fx.Compiler );
			Assert.IsFalse( build.IsCompleted );
			SynchronizationContext.Current.Post( _ => release.Set(), null );
			var report = await build;
			Assert.IsTrue( report.Success, report.Log.ToString() );
			Assert.AreEqual( 1, calls );
		} );
	}

	[TestMethod]
	public async Task AsyncCancellationReleasesRootsAndKeepsLastGoodOutput()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		Assert.IsTrue( fx.Build().Success );
		var original = fx.Read( "a.vtex_c" );
		fx.Write( "a.vtex", "blue" );
		using var cancel = new CancellationTokenSource();
		fx.Compiler.During = _ => cancel.Cancel();
		var report = await ContentBuilder.BuildAsync( new BuildRequest { Roots = [new ContentRoot( "unit", fx.Root, fx.State )] }, fx.Compiler, cancel.Token );
		Assert.IsTrue( report.Cancelled );
		Assert.AreEqual( original, fx.Read( "a.vtex_c" ) );
		fx.Compiler.During = null;
		AssertJobs( fx.Build( lockTimeout: TimeSpan.Zero ), "a.vtex" );
		StringAssert.Contains( fx.Read( "a.vtex_c" ), "blue" );
	}

	[TestMethod]
	public void NoOpBuildStartsNoJobs()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex", "version" );
		fx.Write( "c.vmdl", "reads b.vmat" );
		Assert.IsTrue( fx.Build().Success );

		var report = fx.Build();

		Assert.IsTrue( report.Success, report.Log.ToString() );
		AssertJobs( report );
		Assert.AreEqual( 0, report.Described.Count, "Nothing changed, nothing to describe" );
		Assert.AreEqual( 0, fx.Compiler.Compiled.Count );
	}

	[TestMethod]
	public void OrderOnlyPrerequisiteBuildsFirstButOnlyItsPresenceRebuildsTheDependent()
	{
		using var fx = new Fixture();
		fx.Write( "b.vmat", "red" );
		fx.Write( "c.vmdl", "orders b.vmat" );

		var report = fx.Build();
		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "b.vmat", "c.vmdl" }, report.Jobs.ToArray() );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Root, "c.vmdl_c" ) ), "present" );

		fx.Write( "b.vmat", "blue" );
		AssertJobs( fx.Build(), "b.vmat" );
		AssertJobs( fx.Build() );

		File.Delete( Path.Combine( fx.Root, "b.vmat" ) );
		AssertJobs( fx.Build(), "c.vmdl" );
	}

	[TestMethod]
	public void PrerequisiteBuildsFirstAndRebuildsItsDependent()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );
		Assert.IsTrue( fx.Build().Success );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "red" );

		fx.Write( "a.vtex", "blue" );
		var report = fx.Build();

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "a.vtex", "b.vmat" }, report.Jobs.ToArray() );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "blue" );
	}

	[TestMethod]
	public void CompilerHearsOfAPublishedOutputBeforeWhatReadsItCompiles()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );
		Assert.IsTrue( fx.Build().Success );

		fx.Write( "a.vtex", "blue" );
		while ( fx.Compiler.Events.TryDequeue( out _ ) ) { }
		Assert.IsTrue( fx.Build().Success );

		var events = fx.Compiler.Events.ToList();
		var published = events.IndexOf( "published a.vtex_c" );
		Assert.IsTrue( published >= 0 && published < events.IndexOf( "compile b.vmat" ), string.Join( "; ", events ) );
	}

	[TestMethod]
	public void ColdBuildOrdersFromDescriptionsWithoutMisses()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red", "sleep 200" );
		fx.Write( "m.vmat", "reads a.vtex" );
		fx.Write( "model.vmdl", "reads m.vmat" );

		var report = fx.Build( maxJobs: 4 );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( 0, report.DescribeMisses, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "a.vtex", "m.vmat", "model.vmdl" }, report.Jobs.ToArray() );
		CollectionAssert.AreEquivalent( new[] { "m.vmat", "model.vmdl" }, report.Described.ToArray() );
		StringAssert.Contains( fx.Read( "model.vmdl_c" ), "red" );
	}

	[TestMethod]
	public void ChangedSourceIsDescribedAgain()
	{
		using var fx = new Fixture();
		fx.Write( "a.vmat", "first" );
		fx.Write( "b.vmat", "second" );
		fx.Write( "model.vmdl", "reads a.vmat" );
		Assert.IsTrue( fx.Build().Success );

		// The model now reads b, which changed too: only a fresh description orders b first
		fx.Write( "b.vmat", "second edited", "sleep 200" );
		fx.Write( "model.vmdl", "reads b.vmat" );
		var report = fx.Build( maxJobs: 4 );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( 0, report.DescribeMisses, report.Log.ToString() );
		CollectionAssert.AreEquivalent( new[] { "b.vmat", "model.vmdl" }, report.Described.ToArray(), "Only assets whose source changed are described" );
		StringAssert.Contains( fx.Read( "model.vmdl_c" ), "second edited" );
		AssertJobs( fx.Build() );
	}

	[TestMethod]
	public void UndescribedReadIsAMissRerunOnce()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red", "sleep 300" );
		fx.Write( "b.vmat", "hidden a.vtex" );

		var report = fx.Build( maxJobs: 2 );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( 1, report.DescribeMisses, report.Log.ToString() );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "red" );

		// The record has the edge now
		fx.Write( "a.vtex", "blue", "sleep 300" );
		report = fx.Build( maxJobs: 2 );
		Assert.AreEqual( 0, report.DescribeMisses, report.Log.ToString() );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "blue" );
	}

	[TestMethod]
	public void InlineBuildCompilesOnTheCallingThread()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );

		var report = fx.Build( maxJobs: 1 );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.IsTrue( fx.Compiler.Threads.All( x => x == Environment.CurrentManagedThreadId ) );
	}

	[TestMethod]
	public void UnsupportedTypeIsReportedNotSkipped()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "level.vmap", "map" );

		var report = fx.Build();

		Assert.IsFalse( report.Success );
		CollectionAssert.AreEqual( new[] { "level.vmap" }, report.Unsupported.ToArray() );
		AssertJobs( report, "a.vtex" );
	}

	/// <summary>
	/// What the editor's asset system does when it compiles one asset by itself: the same worker, writing straight
	/// into the root, with no record.
	/// </summary>
	static void CompileOutside( Fixture fx, string rel )
	{
		var root = new ContentRoot( "unit", fx.Root, fx.State );
		var staging = Path.Combine( fx.Dir, $"outside-{Guid.NewGuid():N}" );
		var result = fx.Compiler.Compile( new CompileJob( root, [(root.Mod, root.Path)], Path.Combine( fx.Root, rel ), staging, [] ), default );
		foreach ( var staged in result.Outputs )
			File.Copy( staged, Path.Combine( fx.Root, Path.GetRelativePath( Path.Combine( staging, "unit" ), staged ) ), true );
	}

	[TestMethod]
	public void SingleAssetCompiledOutsideTheBuildIsAdoptedNotRepeated()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );
		fx.Write( "c.vmdl", "reads b.vmat" );
		Assert.IsTrue( fx.Build().Success );

		fx.Write( "b.vmat", "reads a.vtex", "edited" );
		CompileOutside( fx, "b.vmat" );
		var report = fx.Build();

		// Only what reads it is left to build
		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "b.vmat" }, report.Adopted.ToArray() );
		AssertJobs( report, "c.vmdl" );
		StringAssert.Contains( fx.Read( "c.vmdl_c" ), "edited" );
		AssertJobs( fx.Build() );
	}

	[TestMethod]
	public void SingleAssetCompiledOutsideTheBuildWithUnchangedInputsIsANoOp()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );
		Assert.IsTrue( fx.Build().Success );

		CompileOutside( fx, "b.vmat" );

		AssertJobs( fx.Build() );
	}

	[TestMethod]
	public void SingleAssetCompileWhoseInputChangedSinceIsRebuilt()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		Assert.IsTrue( fx.Build().Success );

		fx.Write( "a.vtex", "blue" );
		CompileOutside( fx, "a.vtex" );
		fx.Write( "a.vtex", "green" );
		var report = fx.Build();

		Assert.AreEqual( 0, report.Adopted.Count );
		AssertJobs( report, "a.vtex" );
		StringAssert.Contains( fx.Read( "a.vtex_c" ), "green" );
	}

	[TestMethod]
	public void SingleAssetCompileThatReadAnOutputRebuiltSinceIsRebuilt()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );
		Assert.IsTrue( fx.Build().Success );

		fx.Write( "b.vmat", "reads a.vtex", "edited" );
		CompileOutside( fx, "b.vmat" );
		fx.Write( "a.vtex", "blue" );
		var report = fx.Build();

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "a.vtex", "b.vmat" }, report.Jobs.ToArray() );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "blue" );
	}

	[TestMethod]
	public void AdoptedOutputKeepsReadsItsDescriptionMissed()
	{
		// The output says it read a.vtex, which its description doesn't list: adopted with that edge anyway
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "hidden a.vtex" );
		Assert.IsTrue( fx.Build().Success );
		Directory.Delete( Path.Combine( fx.State, "records" ), true );

		var report = fx.Build();
		CollectionAssert.AreEquivalent( new[] { "a.vtex", "b.vmat" }, report.Adopted.ToArray(), report.Log.ToString() );

		fx.Write( "a.vtex", "blue" );
		report = fx.Build();
		CollectionAssert.AreEqual( new[] { "a.vtex", "b.vmat" }, report.Jobs.ToArray(), report.Log.ToString() );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "blue" );
	}

	[TestMethod]
	public void OutputThatDoesntRecordWhatItReadIsCompiledOnce()
	{
		// Built by a compiler from before compiled reads were recorded
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );
		Assert.IsTrue( fx.Build().Success );

		fx.Write( "b.vmat", "reads a.vtex", "edited" );
		CompileOutside( fx, "b.vmat" );
		var marker = Path.Combine( fx.Root, "b.vmat.src_c" );
		File.WriteAllText( marker, File.ReadAllLines( marker )[0] );
		var report = fx.Build();

		Assert.AreEqual( 0, report.Adopted.Count );
		AssertJobs( report, "b.vmat" );
		AssertJobs( fx.Build() );
	}

	[TestMethod]
	public void RecordedOutputDamagedWithNothingChangedIsRebuilt()
	{
		// Its edit info still validates, but nothing it was built from changed, so no other compile made these bytes
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		Assert.IsTrue( fx.Build().Success );
		var built = fx.Read( "a.vtex_c" );
		File.WriteAllText( Path.Combine( fx.Root, "a.vtex_c" ), "damaged" );

		var report = fx.Build();

		Assert.AreEqual( 0, report.Adopted.Count, report.Log.ToString() );
		AssertJobs( report, "a.vtex" );
		Assert.AreEqual( built, fx.Read( "a.vtex_c" ) );
	}

	[TestMethod]
	public void AdoptAnswerWithoutItsOutputIsCompiledNotAdopted()
	{
		// The source went between the inventory and its adopt job: a success that names nothing, or not its output
		foreach ( var other in new[] { false, true } )
		{
			using var fx = new Fixture();
			fx.Write( "a.vtex", "red" );
			fx.Write( "a.vtex_c", "left by something" );
			fx.Write( "other.txt", "not its output" );
			fx.Compiler.Adopter = job => new CompileResult( true, "", other ? [Path.Combine( fx.Root, "other.txt" )] : [], [], [], [], [] );

			var report = fx.Build();

			Assert.AreEqual( 0, report.Adopted.Count, report.Log.ToString() );
			AssertJobs( report, "a.vtex" );
		}
	}

	[TestMethod]
	public void OutsideCompileThatDropsAChildIsCompiledNotAdopted()
	{
		using var fx = new Fixture();
		fx.Write( "b.vmat", "red", "child c.vtex_c one" );
		Assert.IsTrue( fx.Build().Success );

		// Still making its child: adopted with it
		fx.Write( "b.vmat", "blue", "child c.vtex_c one" );
		CompileOutside( fx, "b.vmat" );
		var report = fx.Build();
		CollectionAssert.AreEqual( new[] { "b.vmat" }, report.Adopted.ToArray(), report.Log.ToString() );
		AssertJobs( report );

		// Not any more: the compile removes the child it owned, which adopting would have forgotten
		fx.Write( "b.vmat", "green" );
		CompileOutside( fx, "b.vmat" );
		report = fx.Build();
		Assert.AreEqual( 0, report.Adopted.Count, report.Log.ToString() );
		AssertJobs( report, "b.vmat" );
		Assert.IsFalse( fx.Exists( "c.vtex_c" ) );
	}

	[TestMethod]
	public void OutsideCompileThatDropsAChildAnotherAssetStillOwnsIsAdopted()
	{
		using var fx = new Fixture();
		fx.Write( "a.vmat", "red", "child shared.vtex_c same" );
		fx.Write( "b.vmat", "blue", "child shared.vtex_c same" );
		Assert.IsTrue( fx.Build().Success );

		fx.Write( "a.vmat", "green" );
		CompileOutside( fx, "a.vmat" );
		var report = fx.Build();

		CollectionAssert.AreEqual( new[] { "a.vmat" }, report.Adopted.ToArray(), report.Log.ToString() );
		AssertJobs( report );
		Assert.IsTrue( fx.Exists( "shared.vtex_c" ) );
	}

	/// <summary>
	/// Compiles a.vtex at once; every other compile waits until it is cancelled.
	/// </summary>
	sealed class WaitingCompiler : IAssetCompiler
	{
		public int Active;
		public bool FinishedWhileActive;

		public AssetKind Classify( string sourceExtension, out string outputExtension )
		{
			outputExtension = sourceExtension + "_c";
			return sourceExtension == ".vtex" ? AssetKind.Compiled : AssetKind.None;
		}

		public CompileResult Compile( CompileJob job, CancellationToken token )
		{
			if ( Path.GetFileName( job.Source ) != "a.vtex" )
			{
				Interlocked.Increment( ref Active );
				token.WaitHandle.WaitOne();
				Interlocked.Decrement( ref Active );
				return new CompileResult( false, "cancelled", [], [], [], [], [] );
			}

			var output = Path.Combine( job.Staging, job.Root.Mod, "a.vtex_c" );
			Directory.CreateDirectory( Path.GetDirectoryName( output ) );
			File.WriteAllText( output, "compiled" );
			return new CompileResult( true, "", [output], [new InputFact( job.Source, true, Hashing.Checksum( job.Source ) )], [], [], [] );
		}

		public IReadOnlyList<uint?> Fingerprints( IReadOnlyList<SpecialDependency> dependencies ) => dependencies.Select( _ => (uint?)null ).ToArray();

		public void Finished() => FinishedWhileActive = Volatile.Read( ref Active ) > 0;
	}

	[TestMethod]
	public void ErrorSettlingAJobStopsTheOtherCompilesBeforeTheRootsAreFreed()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vtex", "green" );
		fx.Write( "c.vtex", "blue" );
		var compiler = new WaitingCompiler();
		var request = new BuildRequest { Roots = [new ContentRoot( "unit", fx.Root, fx.State )], MaxJobs = 3, BeforeCommit = _ => throw new InvalidOperationException( "crash" ) };

		Assert.ThrowsException<InvalidOperationException>( () => ContentBuilder.Build( request, compiler ) );

		Assert.AreEqual( 0, compiler.Active );
		Assert.IsFalse( compiler.FinishedWhileActive );
		Assert.IsNull( fx.Build( lockTimeout: TimeSpan.Zero ).Error, "the roots are free again" );
	}

	[TestMethod]
	public void GeneratedFileInTheFallbackIsPresentAndNothingIsWrittenThere()
	{
		// Another project opens: its transient folder is empty, but the shipped one it mounts after has the file
		using var fx = new Fixture();
		fx.Write( "e.vtex", "red", "generates gen/e.vtex_c" );
		Assert.IsTrue( fx.Build( transient: "first" ).Success );
		var shipped = Path.Combine( fx.Dir, "shipped" );
		Directory.CreateDirectory( Path.Combine( shipped, "gen" ) );
		File.Copy( Path.Combine( fx.Dir, "first", "gen", "e.vtex_c" ), Path.Combine( shipped, "gen", "e.vtex_c" ) );
		BuildReport Build( string transient, string fallback ) => ContentBuilder.Build( new BuildRequest
		{
			Roots = [new ContentRoot( "unit", fx.Root, fx.State )],
			OutputRoots = new Dictionary<string, string> { ["transient"] = Path.Combine( fx.Dir, transient ) },
			OutputFallbacks = fallback is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["transient"] = fallback },
		}, fx.Compiler );

		AssertJobs( Build( "second", shipped ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( fx.Dir, "second" ) ) );

		// In neither: it is missing, and builds into the project's folder
		AssertJobs( Build( "third", null ), "e.vtex" );
		Assert.IsTrue( File.Exists( Path.Combine( fx.Dir, "third", "gen", "e.vtex_c" ) ) );
	}

	[TestMethod]
	public void FallbackNeverShadowsARootsOwnOutput()
	{
		// The menu ships compiled copies of cloud assets: a project that builds the same paths reads its own
		using var fx = new Fixture();
		var shipped = Path.Combine( fx.Dir, "shipped" );
		Directory.CreateDirectory( shipped );
		File.WriteAllText( Path.Combine( shipped, "m.vmat_c" ), "shipped stale" );
		fx.Write( "m.vmat", "project fresh" );
		fx.Write( "b.vmdl", "reads *m.vmat" );

		var report = ContentBuilder.Build( new BuildRequest
		{
			Roots = [new ContentRoot( "unit", fx.Root, fx.State )],
			OutputRoots = new Dictionary<string, string> { ["transient"] = Path.Combine( fx.Dir, "project" ) },
			OutputFallbacks = new Dictionary<string, string> { ["transient"] = shipped },
		}, fx.Compiler );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		AssertJobs( report, "m.vmat", "b.vmdl" );
		StringAssert.Contains( fx.Read( "b.vmdl_c" ), "project fresh" );
	}

	[TestMethod]
	public void OutsideCompileWhoseGeneratedFileIsInTheFallbackIsAdoptedFromThere()
	{
		using var fx = new Fixture();
		fx.Write( "e.vtex", "red", "generates gen/e.vtex_c" );
		Assert.IsTrue( fx.Build( transient: "first" ).Success );
		var shipped = Path.Combine( fx.Dir, "shipped", "gen", "e.vtex_c" );
		Directory.CreateDirectory( Path.GetDirectoryName( shipped ) );
		File.Copy( Path.Combine( fx.Dir, "first", "gen", "e.vtex_c" ), shipped );
		var before = (File.ReadAllText( shipped ), File.GetLastWriteTimeUtc( shipped ));
		var project = Path.Combine( fx.Dir, "second", "gen", "e.vtex_c" );

		// The asset system compiled it, finding the generated file where it is mounted: the shipped transients
		BuildReport AdoptEdited( string content )
		{
			fx.Write( "e.vtex", content, "generates gen/e.vtex_c" );
			var staging = Path.Combine( fx.Dir, $"outside-{Guid.NewGuid():N}" );
			var root = new ContentRoot( "unit", fx.Root, fx.State );
			foreach ( var staged in fx.Compiler.Compile( new CompileJob( root, [(root.Mod, root.Path)], Path.Combine( fx.Root, "e.vtex" ), staging, [] ), default ).Outputs.Where( x => x.StartsWith( Path.Combine( staging, "unit" ) ) ) )
				File.Copy( staged, Path.Combine( fx.Root, Path.GetRelativePath( Path.Combine( staging, "unit" ), staged ) ), true );
			fx.Compiler.Transient = Path.Combine( fx.Dir, "shipped" );
			return ContentBuilder.Build( new BuildRequest
			{
				Roots = [new ContentRoot( "unit", fx.Root, fx.State )],
				OutputRoots = new Dictionary<string, string> { ["transient"] = Path.Combine( fx.Dir, "second" ) },
				OutputFallbacks = new Dictionary<string, string> { ["transient"] = Path.Combine( fx.Dir, "shipped" ) },
			}, fx.Compiler );
		}

		var report = AdoptEdited( "green" );
		CollectionAssert.AreEqual( new[] { "e.vtex" }, report.Adopted.ToArray(), report.Log.ToString() );
		AssertJobs( report );
		CollectionAssert.Contains( report.Changed, shipped );
		Assert.IsTrue( report.Changed.All( File.Exists ), string.Join( ", ", report.Changed ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( fx.Dir, "second" ) ) );
		Assert.AreEqual( before, (File.ReadAllText( shipped ), File.GetLastWriteTimeUtc( shipped )) );

		// In both: the project's own file is the one that's there
		Directory.CreateDirectory( Path.GetDirectoryName( project ) );
		File.Copy( shipped, project );
		report = AdoptEdited( "blue" );
		CollectionAssert.AreEqual( new[] { "e.vtex" }, report.Adopted.ToArray(), report.Log.ToString() );
		CollectionAssert.Contains( report.Changed, project );
		CollectionAssert.DoesNotContain( report.Changed, shipped );
		Assert.AreEqual( before, (File.ReadAllText( shipped ), File.GetLastWriteTimeUtc( shipped )) );
	}

	[TestMethod]
	public void ProjectMountedFromAnEngineRootBuildsItUnderItsOwnIdent()
	{
		var game = Path.Combine( Path.GetTempPath(), "game" );
		var engine = ContentRoots.Engine( game );
		var menu = engine.Single( x => x.Mod == "menu" );
		var citizen = engine.Single( x => x.Mod == "citizen" );

		// The menu project itself: its compiles write to local.menu, with the engine root's records and lock
		var opened = ContentRoots.WithProjects( engine, [("local.menu", Path.Combine( game, "addons", "menu", "Assets" ), Path.Combine( game, "addons", "menu" ))] );
		CollectionAssert.AreEqual( new[] { "core", "citizen", "content", "local.menu" }, opened.Select( x => x.Mod ).ToArray() );
		Assert.AreEqual( menu.Path, opened[3].Path );
		Assert.AreEqual( menu.StateDirectory, opened[3].StateDirectory );

		// Any other project: the engine roots as they are, the project after them with its own state
		var other = Path.Combine( Path.GetTempPath(), "game_project" );
		var game2 = ContentRoots.WithProjects( engine, [("local.game", Path.Combine( other, "Assets" ), other)] );
		CollectionAssert.AreEqual( new[] { "core", "citizen", "content", "menu", "local.game" }, game2.Select( x => x.Mod ).ToArray() );
		Assert.AreEqual( ContentRoots.ProjectState( other ), game2[4].StateDirectory );

		// An engine root mounted as a library moves to where the library is mounted, before the project
		var library = ContentRoots.WithProjects( engine, [("local.citizenlib", citizen.Path, Path.GetDirectoryName( citizen.Path )), ("local.game", Path.Combine( other, "Assets" ), other)] );
		CollectionAssert.AreEqual( new[] { "core", "content", "menu", "local.citizenlib", "local.game" }, library.Select( x => x.Mod ).ToArray() );
		Assert.AreEqual( citizen.StateDirectory, library[3].StateDirectory );
	}

	[TestMethod]
	public void EachPublicationIsReportedAsItHappens()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );
		var published = new List<string>();

		var report = ContentBuilder.Build( new BuildRequest { Roots = [new ContentRoot( "unit", fx.Root, fx.State )], MaxJobs = 1, Published = published.AddRange }, fx.Compiler );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEquivalent( report.Changed.ToArray(), published.ToArray() );
		Assert.IsTrue( published.IndexOf( Path.Combine( fx.Root, "a.vtex_c" ) ) < published.IndexOf( Path.Combine( fx.Root, "b.vmat_c" ) ) );
	}

	[TestMethod]
	public void FirstBuildStartsTypicallySlowerTypesFirst()
	{
		// Nothing timed yet: materials typically take longer than textures, so b.vmat goes before a.vtex
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "blue" );

		CollectionAssert.AreEqual( new[] { "b.vmat", "a.vtex" }, fx.Build( maxJobs: 1 ).Jobs.ToArray() );
	}

	[TestMethod]
	public void OrderingNeverRunsACompileBeforeWhatItReads()
	{
		// The material is typically slowest, but it reads the texture, which compiles first; once ready, it goes before
		// the sound
		using var fx = new Fixture();
		fx.Write( "t.vtex", "red" );
		fx.Write( "m.vmat", "reads t.vtex" );
		fx.Write( "z.wav", "wave" );

		CollectionAssert.AreEqual( new[] { "t.vtex", "m.vmat", "z.wav" }, fx.Build( maxJobs: 1 ).Jobs.ToArray() );
	}

	// Wall-clock times: a loaded machine (CI) can make the short compile take longer than the long one
	[TestMethod, TestCategory( "Slow" )]
	public void WhatTookLongestLastTimeCompilesFirst()
	{
		// The first build goes by type (material before texture); after it, by what each actually took. The first job
		// of a cold run on a slow machine can take longer than the sleep, so the times compared come from a warm build.
		// The material sleeps too: a time that rounds to zero isn't recorded, and the material falls back to its typical.
		using var fx = new Fixture();
		fx.Write( "a.vmat", "red", "sleep 50" );
		fx.Write( "z.vtex", "blue", "sleep 1000" );
		CollectionAssert.AreEqual( new[] { "a.vmat", "z.vtex" }, fx.Build( maxJobs: 1 ).Jobs.ToArray() );

		fx.Write( "a.vmat", "red", "sleep 50", "edited" );
		fx.Write( "z.vtex", "blue", "sleep 1000", "edited" );
		fx.Build( maxJobs: 1 );

		fx.Write( "a.vmat", "red", "sleep 50" );
		fx.Write( "z.vtex", "blue", "sleep 1000" );
		CollectionAssert.AreEqual( new[] { "z.vtex", "a.vmat" }, fx.Build( maxJobs: 1 ).Jobs.ToArray() );
	}

	[TestMethod]
	public void CurrentIsWhatBuiltOrStayedCurrent()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "bad.vtex", "fail" );
		fx.Write( "b.vmat", "reads bad.vtex" );
		fx.Write( "level.vmap", "map" );
		fx.Write( "s.wav", "wave" );
		fx.Write( "s.mp3", "mpeg" );

		var report = fx.Build();

		// Not the failed, blocked, unsupported or clashing ones
		CollectionAssert.AreEquivalent( new[] { Path.Combine( fx.Root, "a.vtex" ) }, report.Current.ToArray() );

		// A no-op build knows as much as the one that compiled
		CollectionAssert.AreEquivalent( report.Current.ToArray(), fx.Build().Current.ToArray() );
	}

	[TestMethod]
	public void HiddenSourceIsAsCurrentAsTheOneHidingIt()
	{
		using var fx = new Fixture();
		WriteIn( fx, "r1/s.wav", "wave" );
		WriteIn( fx, "r2/s.mp3", "mpeg" );
		WriteIn( fx, "r1/t.wav", "wave" );
		WriteIn( fx, "r2/t.mp3", "mpeg", "fail" );

		var report = BuildRoots( fx, ["r1", "r2"] );

		CollectionAssert.AreEquivalent( new[] { "r1/s.wav", "r2/s.mp3" }.Select( x => Path.GetFullPath( Path.Combine( fx.Dir, x ) ) ).ToArray(), report.Current.ToArray() );
	}

	[TestMethod]
	public void SharedOutputsAreWhatRecordsStillList()
	{
		using var fx = new Fixture();
		fx.Write( "d1.vtex", "red", "generates gen/one.vtex_c" );
		fx.Write( "d2.vtex", "blue", "generates gen/two.vtex_c" );
		var one = Path.Combine( fx.Dir, "p", "gen", "one.vtex_c" );
		var two = Path.Combine( fx.Dir, "p", "gen", "two.vtex_c" );

		CollectionAssert.AreEquivalent( new[] { one, two }, fx.Build( transient: "p" ).SharedOutputs.ToArray() );

		// Left on disk for the orphan collector, which may now delete it
		File.Delete( Path.Combine( fx.Root, "d1.vtex" ) );
		var report = fx.Build( transient: "p" );
		Assert.IsTrue( File.Exists( one ) );
		CollectionAssert.AreEquivalent( new[] { two }, report.SharedOutputs.ToArray() );
	}

	[TestMethod]
	public void PrerequisiteCycleFailsWithoutHanging()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "reads b.vtex" );
		fx.Write( "b.vtex", "reads a.vtex" );

		var report = fx.Build();

		Assert.IsFalse( report.Success );
		CollectionAssert.AreEquivalent( new[] { "a.vtex", "b.vtex" }, report.Failed.ToArray(), report.Log.ToString() );
		StringAssert.Contains( report.Log.ToString(), "cycle" );
	}

	[TestMethod]
	public void RuntimeReferenceCycleBuilds()
	{
		using var fx = new Fixture();
		fx.Write( "a.vmdl", "ref b.vmdl" );
		fx.Write( "b.vmdl", "ref a.vmdl" );

		var report = fx.Build();

		Assert.IsTrue( report.Success, report.Log.ToString() );
		AssertJobs( report, "a.vmdl", "b.vmdl" );
	}

	[TestMethod]
	public void FailedPrerequisiteBlocksDependentUntilFixed()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );
		Assert.IsTrue( fx.Build().Success );
		var good = fx.Read( "b.vmat_c" );

		fx.Write( "a.vtex", "blue", "fail" );
		fx.Write( "b.vmat", "reads a.vtex", "edited" );
		var report = fx.Build();

		Assert.IsFalse( report.Success );
		CollectionAssert.AreEqual( new[] { "a.vtex" }, report.Failed.ToArray() );
		CollectionAssert.AreEqual( new[] { "b.vmat" }, report.Blocked.ToArray() );
		AssertJobs( report, "a.vtex" );
		Assert.AreEqual( good, fx.Read( "b.vmat_c" ), "A blocked asset's last good output must stay" );

		fx.Write( "a.vtex", "blue" );
		report = fx.Build();
		Assert.IsTrue( report.Success, report.Log.ToString() );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "blue" );
	}

	[TestMethod]
	public void FailedPrerequisiteFoundWhileCompilingBlocks()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "fail", "sleep 200" );
		fx.Write( "b.vtex", "hidden a.vtex" );

		var report = fx.Build( maxJobs: 2 );

		CollectionAssert.AreEqual( new[] { "a.vtex" }, report.Failed.ToArray(), report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "b.vtex" }, report.Blocked.ToArray(), report.Log.ToString() );
		Assert.IsFalse( fx.Exists( "b.vtex_c" ) );
	}

	[TestMethod]
	public void ConcurrencyIsBounded()
	{
		using var fx = new Fixture();
		for ( var i = 0; i < 6; i++ ) fx.Write( $"t{i}.vtex", "sleep 150" );

		var report = fx.Build( maxJobs: 2 );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( 2, fx.Compiler.MaxConcurrent );
	}

	[TestMethod]
	public void SelectionBuildsWhatItReadsAndNothingElse()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "b.vmat", "reads a.vtex" );
		fx.Write( "c.vtex", "unrelated" );
		Assert.IsTrue( fx.Build().Success );

		fx.Write( "a.vtex", "blue" );
		fx.Write( "c.vtex", "edited" );
		var report = fx.Build( select: ["b.vmat"] );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "a.vtex", "b.vmat" }, report.Jobs.ToArray() );
		StringAssert.Contains( fx.Read( "b.vmat_c" ), "blue" );
	}

	[TestMethod]
	public void UnchangedPrerequisiteOutputDoesNotRebuildDependents()
	{
		using var fx = new Fixture();
		fx.Write( "m.vmat", "red", "child m_child.vtex_c one" );
		fx.Write( "model.vmdl", "reads m.vmat" );
		Assert.IsTrue( fx.Build().Success );

		// Rebuilt without its output changing: the model's read is still current
		File.SetLastWriteTimeUtc( Path.Combine( fx.Root, "m.vmat" ), DateTime.UtcNow.AddMinutes( 1 ) );
		fx.Write( "m.vmat", "red", "child m_child.vtex_c two" );
		AssertJobs( fx.Build(), "m.vmat" );

		fx.Write( "m.vmat", "blue", "child m_child.vtex_c two" );
		AssertJobs( fx.Build(), "m.vmat", "model.vmdl" );
		StringAssert.Contains( fx.Read( "model.vmdl_c" ), "blue" );
	}

	[TestMethod]
	public void CompilerVersionChangeRebuildsWhatUsesIt()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "version" );
		fx.Write( "b.vtex", "plain" );
		Assert.IsTrue( fx.Build().Success );

		fx.Compiler.Versions["version"] = 2;

		AssertJobs( fx.Build(), "a.vtex" );
		AssertJobs( fx.Build() );
	}

	[TestMethod]
	public void SharedOutputMustMatch()
	{
		using var fx = new Fixture();
		fx.Write( "a.vmat", "child shared.vtex_c same" );
		fx.Write( "b.vmat", "child shared.vtex_c same" );
		Assert.IsTrue( fx.Build().Success );

		// Deleting one owner keeps what the other still owns
		File.Delete( Path.Combine( fx.Root, "a.vmat" ) );
		var report = fx.Build();
		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.IsTrue( fx.Exists( "shared.vtex_c" ) );
		Assert.IsFalse( fx.Exists( "a.vmat_c" ) );

		fx.Write( "c.vmat", "child shared.vtex_c different" );
		report = fx.Build();
		CollectionAssert.AreEqual( new[] { "c.vmat" }, report.Failed.ToArray(), report.Log.ToString() );
		StringAssert.Contains( report.Log.ToString(), "different contents" );
		Assert.AreEqual( "same", fx.Read( "shared.vtex_c" ) );
	}

	[TestMethod]
	public void FilesNotBuiltHereAreNeverDeleted()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "shipped.sound_c", "not ours" );
		Assert.IsTrue( fx.Build().Success );

		File.Delete( Path.Combine( fx.Root, "a.vtex" ) );
		var report = fx.Build();

		CollectionAssert.AreEqual( new[] { "a.vtex" }, report.Removed.ToArray() );
		Assert.IsFalse( fx.Exists( "a.vtex_c" ) );
		Assert.IsTrue( fx.Exists( "shipped.sound_c" ) );
	}

	[TestMethod]
	public void PublicationInterruptedBeforeCommitIsUndone()
	{
		using var fx = new Fixture();
		fx.Write( "a.vmat", "red", "child a_child.vtex_c one" );
		Assert.IsTrue( fx.Build().Success );
		var output = fx.Read( "a.vmat_c" );

		fx.Write( "a.vmat", "blue", "child a_child.vtex_c two", "child extra.vtex_c new" );
		Assert.ThrowsException<IOException>( () => fx.Build( beforeCommit: _ => throw new IOException( "crash" ) ) );
		StringAssert.Contains( fx.Read( "a.vmat_c" ), "blue" );

		fx.Build( select: [] );
		Assert.AreEqual( output, fx.Read( "a.vmat_c" ) );
		Assert.AreEqual( "one", fx.Read( "a_child.vtex_c" ) );
		Assert.IsFalse( fx.Exists( "extra.vtex_c" ) );

		AssertJobs( fx.Build(), "a.vmat" );
		StringAssert.Contains( fx.Read( "a.vmat_c" ), "blue" );
	}

	[TestMethod]
	public void PublicationInterruptedAfterCommitIsKept()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		Assert.IsTrue( fx.Build().Success );
		var record = Directory.GetFiles( Path.Combine( fx.State, "records" ) ).Single();
		var job = JsonDocument.Parse( File.ReadAllText( record ) ).RootElement.GetProperty( "Job" ).GetString();

		// The record names this job, so it committed; only cleanup was left
		var target = Path.Combine( fx.Root, "a.vtex_c" );
		var backup = Path.Combine( fx.State, "backup", job, "a.vtex_c" );
		Directory.CreateDirectory( Path.GetDirectoryName( backup ) );
		File.WriteAllText( backup, "old" );
		File.WriteAllText( Path.Combine( fx.State, "journal", $"{job}.json" ),
			JsonSerializer.Serialize( new { Job = job, Source = "a.vtex", Remove = false, Moves = new[] { new { Target = target, Staged = "staged", Existed = true } } } ) );
		var published = fx.Read( "a.vtex_c" );

		var report = fx.Build();

		AssertJobs( report );
		Assert.AreEqual( published, fx.Read( "a.vtex_c" ) );
		Assert.IsFalse( File.Exists( backup ) );
		Assert.AreEqual( 0, Directory.GetFiles( Path.Combine( fx.State, "journal" ) ).Length );
	}

	[TestMethod]
	public void JournalCutShortWhileWritingIsIgnored()
	{
		// Written before any output moved, so there is nothing to undo
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		Assert.IsTrue( fx.Build().Success );
		File.WriteAllText( Path.Combine( fx.State, "journal", "interrupted.json.tmp" ), "{\"Job\":\"interr" );

		AssertJobs( fx.Build() );
		Assert.AreEqual( 0, Directory.GetFiles( Path.Combine( fx.State, "journal" ) ).Length );
	}

	[TestMethod]
	public void UnreadableJournalIsNotGuessedAt()
	{
		// A whole journal that can't be read may have moved outputs: fail rather than lose or keep them blindly
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		Assert.IsTrue( fx.Build().Success );
		var journal = Path.Combine( fx.State, "journal", "unreadable.json" );
		File.WriteAllText( journal, "not json" );

		Assert.ThrowsException<JsonException>( () => fx.Build() );
		Assert.IsTrue( File.Exists( journal ) );
		Assert.IsTrue( fx.Exists( "a.vtex_c" ) );
	}

	[TestMethod]
	public void SourceTheOtherHostBuildsIsNotDeleted()
	{
		// The editor builds game resources into the same engine roots SboxBuild builds
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "s.sound", "sound" );
		Assert.IsTrue( fx.Build( compiler: new ScriptCompiler { BuildsSounds = true } ).Success );
		var built = fx.Read( "s.sound_c" );

		var report = fx.Build();

		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( 0, report.Removed.Count, report.Log.ToString() );
		Assert.AreEqual( built, fx.Read( "s.sound_c" ) );
		AssertJobs( fx.Build( compiler: new ScriptCompiler { BuildsSounds = true } ) );
	}

	[TestMethod]
	public void OneBuildOfARootAtATime()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red", "sleep 600" );
		var first = Task.Run( () => fx.Build() );
		Thread.Sleep( 200 );

		var second = fx.Build( lockTimeout: TimeSpan.FromMilliseconds( 100 ) );

		Assert.IsFalse( second.Success );
		StringAssert.Contains( second.Error, "running" );
		Assert.IsTrue( first.Result.Success, first.Result.Log.ToString() );
		AssertJobs( fx.Build() );
	}

	[TestMethod]
	public void FailedDescriptionFailsTheAssetAndBlocksWhatReadsIt()
	{
		using var fx = new Fixture();
		fx.Write( "m.vmat", "red" );
		fx.Write( "model.vmdl", "reads m.vmat" );
		fx.Compiler.Describer = job => job.Source.EndsWith( "m.vmat" ) ? new CompileResult( false, "crashed", [], [], [], [], [] ) : null;

		var report = fx.Build();

		CollectionAssert.AreEqual( new[] { "m.vmat" }, report.Failed.ToArray(), report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "model.vmdl" }, report.Blocked.ToArray(), report.Log.ToString() );
		AssertJobs( report );
	}

	[TestMethod]
	public void MalformedBuildReportIsAFailure()
	{
		Assert.IsFalse( BuildReportParser.Parse( [], true, "" ).Success, "empty" );
		Assert.IsFalse( BuildReportParser.Parse( ["buildreport\t1", "input\t2\t00000000\tc:/a.tga"], true, "" ).Success, "truncated" );
		Assert.IsFalse( BuildReportParser.Parse( ["buildreport\t2", "result\t1"], true, "" ).Success, "unknown version" );
		Assert.IsFalse( BuildReportParser.Parse( ["buildreport\t1", "input\t2", "result\t1"], true, "" ).Success, "short line" );
		Assert.IsFalse( BuildReportParser.Parse( ["buildreport\t1", "result\t0"], true, "" ).Success, "failed compile" );

		Assert.IsFalse( BuildReportParser.Parse( ["buildreport\t1", "prereq\tc:/m.vmat\tc:/m.vmat_c\tbogus", "result\t1"], true, "" ).Success, "unknown prereq flag" );

		var ok = BuildReportParser.Parse( ["buildreport\t1", "prereq\t\t", "prereq\tc:/m.vmat\tc:/m.vmat_c", "prereq\tc:/n.vmat\t\torder", "result\t1"], true, "" );
		Assert.IsTrue( ok.Success );
		Assert.AreEqual( 2, ok.Prerequisites.Count, "a read with no source is skipped" );
		Assert.IsFalse( ok.Prerequisites[0].OrderOnly );
		Assert.IsTrue( ok.Prerequisites[1].OrderOnly );
	}

	[TestMethod]
	public void CancelledWhilePlanningReportsCancelled()
	{
		using var fx = new Fixture();
		fx.Write( "m.vmat", "red" );
		using var cancel = new CancellationTokenSource();
		fx.Compiler.Describer = job => { cancel.Cancel(); return null; };

		var report = fx.Build( token: cancel.Token );

		Assert.IsTrue( report.Cancelled );
		AssertJobs( report );
	}

	[TestMethod]
	public void InputEditedWhileCompilingIsRebuiltNextTime()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Compiler.During = job => { Thread.Sleep( 20 ); File.WriteAllText( job.Source, "blue\n" ); };
		Assert.IsTrue( fx.Build().Success );
		fx.Compiler.During = null;

		var report = fx.Build();

		AssertJobs( report, "a.vtex" );
		StringAssert.Contains( fx.Read( "a.vtex_c" ), "blue" );
		AssertJobs( fx.Build() );
	}

	[TestMethod]
	public void ProducerFoundWhileCompilingIsPlannedBeforeItRuns()
	{
		// The model's capture finds a material the description didn't, which itself reads a texture
		using var fx = new Fixture();
		fx.Write( "t.vtex", "red" );
		fx.Write( "m.vmat", "reads t.vtex" );
		fx.Write( "model.vmdl", "hidden m.vmat" );

		var report = fx.Build( select: ["model.vmdl"], maxJobs: 1 );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( 1, report.DescribeMisses, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "model.vmdl", "t.vtex", "m.vmat", "model.vmdl" }, report.Jobs.ToArray() );
		StringAssert.Contains( fx.Read( "model.vmdl_c" ), "red" );
	}

	[TestMethod]
	public void CurrentOutputsWithoutARecordAreAdoptedNotRebuilt()
	{
		// Built before this builder kept records (contentbuilder, the old editor loop, a shipped install)
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "m.vmat", "reads a.vtex" );
		fx.Write( "stale.vtex", "old" );
		Assert.IsTrue( fx.Build().Success );
		Directory.Delete( Path.Combine( fx.State, "records" ), true );
		fx.Write( "stale.vtex", "new" );

		var report = fx.Build();

		Assert.IsTrue( report.Success, report.Log.ToString() );
		// The material's output records the texture it read, which is still that: adopted with the edge
		CollectionAssert.AreEquivalent( new[] { "a.vtex", "m.vmat" }, report.Adopted.ToArray() );
		AssertJobs( report, "stale.vtex" );
		Assert.AreEqual( 0, fx.Build().Jobs.Count );

		fx.Write( "a.vtex", "blue" );
		AssertJobs( fx.Build(), "a.vtex", "m.vmat" );
	}

	[TestMethod]
	public void MaterialThatReadsNoCompiledOutputIsAdoptedAfterDescribing()
	{
		using var fx = new Fixture();
		fx.Write( "m.vmat", "red" );
		Assert.IsTrue( fx.Build().Success );
		Directory.Delete( Path.Combine( fx.State, "records" ), true );

		var report = fx.Build();

		CollectionAssert.AreEqual( new[] { "m.vmat" }, report.Described.ToArray() );
		CollectionAssert.AreEqual( new[] { "m.vmat" }, report.Adopted.ToArray() );
		AssertJobs( report );
		AssertJobs( fx.Build() );
	}

	[TestMethod]
	public void OutputInAnotherRootIsNotAdopted()
	{
		// Its edit info lists a child that resolves to another root's file: compile, never take that file
		using var fx = new Fixture();
		File.WriteAllText( Path.Combine( fx.Dir, "other.vtex_c" ), "someone else's" );
		fx.Write( "a.vtex", "red", "foreign other.vtex_c" );
		Assert.IsTrue( fx.Build().Success );
		Directory.Delete( Path.Combine( fx.State, "records" ), true );

		var report = fx.Build();

		Assert.AreEqual( 0, report.Adopted.Count, report.Log.ToString() );
		AssertJobs( report, "a.vtex" );
		File.Delete( Path.Combine( fx.Root, "a.vtex" ) );
		fx.Build();
		Assert.AreEqual( "someone else's", File.ReadAllText( Path.Combine( fx.Dir, "other.vtex_c" ) ) );
	}

	[TestMethod]
	public void GeneratedChildFoundInAnotherRootIsAdoptedAsItsOwnCopy()
	{
		// Shared default textures: the compiler finds another root's copy first, the asset has its own as compiling writes it
		using var fx = new Fixture();
		WriteIn( fx, "r1/m.vmat", "child t.generated.vtex_c pixels" );
		WriteIn( fx, "r2/n.vmat", "child t.generated.vtex_c pixels", "foreign r1/t.generated.vtex_c" );
		Assert.IsTrue( BuildRoots( fx, ["r1", "r2"] ).Success );
		Directory.Delete( Path.Combine( fx.Dir, "state-r2", "records" ), true );

		var report = BuildRoots( fx, ["r1", "r2"] );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "r2:n.vmat" }, report.Adopted.ToArray(), report.Log.ToString() );
		AssertJobs( report );

		// Its own copy isn't the one the compiler vouched for (older pixels under the same recipe name): compiled
		File.WriteAllText( Path.Combine( fx.Dir, "r2", "t.generated.vtex_c" ), "older pixels" );
		Directory.Delete( Path.Combine( fx.Dir, "state-r2", "records" ), true );
		report = BuildRoots( fx, ["r1", "r2"] );
		Assert.AreEqual( 0, report.Adopted.Count, report.Log.ToString() );
		AssertJobs( report, "r2:n.vmat" );
	}

	[TestMethod]
	public void RecordedOutputOutsideItsRootIsNeverTouched()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		Assert.IsTrue( fx.Build().Success );
		var outside = Path.Combine( fx.Dir, "outside.txt" );
		File.WriteAllText( outside, "not ours" );

		// A foreign or hand-edited record claiming a file outside the root
		var file = Directory.GetFiles( Path.Combine( fx.State, "records" ) ).Single();
		File.WriteAllText( file, File.ReadAllText( file ).Replace( "\"a.vtex.src_c\"", "\"../outside.txt\"" ) );
		StringAssert.Contains( File.ReadAllText( file ), "outside.txt" );

		File.Delete( Path.Combine( fx.Root, "a.vtex" ) );
		Assert.IsTrue( fx.Build().Success );

		Assert.AreEqual( "not ours", File.ReadAllText( outside ) );
	}

	[TestMethod]
	public void SharedGeneratedChildIsOwnedByEveryAssetThatMakesIt()
	{
		// Generated children are named after their recipe, so two assets with the same embedded image
		// make the same file, and one asset can make it twice
		using var fx = new Fixture();
		fx.Write( "d1.vtex", "red", "generates gen/t.vtex_c", "generates gen/t.vtex_c" );
		fx.Write( "d2.vtex", "blue", "generates gen/t.vtex_c" );
		var child = Path.Combine( fx.Dir, "p", "gen", "t.vtex_c" );

		var report = fx.Build( transient: "p" );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.IsTrue( File.Exists( child ) );

		File.Delete( Path.Combine( fx.Root, "d1.vtex" ) );
		Assert.IsTrue( fx.Build( transient: "p" ).Success );
		Assert.IsTrue( File.Exists( child ), "d2 still makes it" );

		// Nothing here makes it any more, but another root might: the editor's orphan collector decides
		File.Delete( Path.Combine( fx.Root, "d2.vtex" ) );
		Assert.IsTrue( fx.Build( transient: "p" ).Success );
		Assert.IsTrue( File.Exists( child ) );
	}

	[TestMethod]
	public void GeneratedChildWithDifferentBytesKeepsTheCurrentOwnersCopy()
	{
		using var fx = new Fixture();
		fx.Write( "d1.vtex", "child gen/t_1a.generated.vtex_c one" );
		fx.Write( "d2.vtex", "child gen/t_1a.generated.vtex_c two" );
		var child = Path.Combine( fx.Root, "gen", "t_1a.generated.vtex_c" );

		var report = fx.Build();
		Assert.IsTrue( report.Success, report.Log.ToString() );
		var kept = File.ReadAllText( child );

		AssertJobs( fx.Build() );

		File.AppendAllText( Path.Combine( fx.Root, "d2.vtex" ), "\nedited" );
		report = fx.Build();
		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( kept, File.ReadAllText( child ) );
		AssertJobs( fx.Build() );
	}

	[TestMethod]
	public void GeneratedChildSharedWithAnotherRootIsNeitherComparedNorDeleted()
	{
		// Two roots, two record stores, one transient folder: core's decal and the project's copy of it
		using var fx = new Fixture();
		fx.Write( "d.vtex", "red", "generates gen/t.vtex_c" );
		var other = Path.Combine( fx.Dir, "other" );
		Directory.CreateDirectory( other );
		File.WriteAllLines( Path.Combine( other, "e.vtex" ), ["blue", "generates gen/t.vtex_c"] );
		var transient = Path.Combine( fx.Dir, "p" );
		BuildReport BuildOther() => ContentBuilder.Build( new BuildRequest { Roots = [new ContentRoot( "other", other, Path.Combine( fx.Dir, "otherstate" ) )], OutputRoots = new Dictionary<string, string> { ["transient"] = transient } }, new ScriptCompiler { Transient = transient } );
		var child = Path.Combine( transient, "gen", "t.vtex_c" );

		Assert.IsTrue( fx.Build( transient: "p" ).Success );
		Assert.IsTrue( BuildOther().Success );

		// Each root rewrote the shared file; neither rebuilds because of the other
		AssertJobs( fx.Build( transient: "p" ) );
		AssertJobs( BuildOther() );

		// Removing one root's asset leaves the file the other still uses
		File.Delete( Path.Combine( fx.Root, "d.vtex" ) );
		CollectionAssert.AreEqual( new[] { "d.vtex" }, fx.Build( transient: "p" ).Removed.ToArray() );
		Assert.IsTrue( File.Exists( child ) );
		AssertJobs( BuildOther() );

		// If it does go, whoever still makes it rebuilds
		File.Delete( child );
		AssertJobs( BuildOther(), "e.vtex" );
		Assert.IsTrue( File.Exists( child ) );
	}

	[TestMethod]
	public void GeneratedOutputsBelongToWhicheverProjectMountsTransient()
	{
		// An engine decal generates its texture into the open project's transient folder
		using var fx = new Fixture();
		fx.Write( "d.vtex", "red", "generates gen/t.vtex_c" );
		var inA = Path.Combine( fx.Dir, "a", "gen", "t.vtex_c" );
		var inB = Path.Combine( fx.Dir, "b", "gen", "t.vtex_c" );

		var first = fx.Build( transient: "a" );
		Assert.IsTrue( first.Success, first.Log.ToString() );
		Assert.IsTrue( File.Exists( inA ) );
		AssertJobs( fx.Build( transient: "a" ) );

		// Another project has no copy yet, and building one leaves the first project's alone
		AssertJobs( fx.Build( transient: "b" ), "d.vtex" );
		Assert.IsTrue( File.Exists( inB ) && File.Exists( inA ) );
		AssertJobs( fx.Build( transient: "a" ) );

		// SboxBuild mounts no transient folder: it can't check that output, so it doesn't rebuild for it
		AssertJobs( fx.Build() );

		// Generated files stay for the editor's orphan collector, in every project
		File.Delete( Path.Combine( fx.Root, "d.vtex" ) );
		var report = fx.Build( transient: "b" );
		CollectionAssert.AreEqual( new[] { "d.vtex" }, report.Removed.ToArray() );
		Assert.IsFalse( fx.Exists( "d.vtex_c" ) );
		Assert.IsTrue( File.Exists( inB ) && File.Exists( inA ) );
	}

	[TestMethod]
	public void OutputsGeneratedForAnotherProjectAreNotAdopted()
	{
		using var fx = new Fixture();
		fx.Write( "d.vtex", "red", "generates gen/t.vtex_c" );
		Assert.IsTrue( fx.Build( transient: "a" ).Success );
		var records = Path.Combine( fx.State, "records" );

		Directory.Delete( records, true );
		var report = fx.Build( transient: "a" );
		CollectionAssert.AreEqual( new[] { "d.vtex" }, report.Adopted.ToArray(), report.Log.ToString() );
		AssertJobs( report );

		// Project b has no copy of the generated texture, so the output isn't current there
		Directory.Delete( records, true );
		report = fx.Build( transient: "b" );
		Assert.AreEqual( 0, report.Adopted.Count, report.Log.ToString() );
		AssertJobs( report, "d.vtex" );
		Assert.IsTrue( File.Exists( Path.Combine( fx.Dir, "b", "gen", "t.vtex_c" ) ) );
	}

	/// <summary>
	/// Roots r1..rN under the fixture, in mount order, each with its own state.
	/// </summary>
	static BuildReport BuildRoots( Fixture fx, string[] mods, IReadOnlyList<string> select = null, TimeSpan? lockTimeout = null ) => ContentBuilder.Build( new BuildRequest
	{
		Roots = mods.Select( x => new ContentRoot( x, Path.Combine( fx.Dir, x ), Path.Combine( fx.Dir, "state-" + x ) ) ).ToList(),
		Select = select?.Select( x => Path.Combine( fx.Dir, x ) ).ToList(),
		LockTimeout = lockTimeout ?? TimeSpan.FromSeconds( 30 ),
	}, fx.Compiler );

	static void WriteIn( Fixture fx, string rel, params string[] lines )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( Path.Combine( fx.Dir, rel ) ) );
		File.WriteAllLines( Path.Combine( fx.Dir, rel ), lines );
	}

	[TestMethod]
	public void ChainAcrossRootsBuildsInOrderWhateverTheMountOrder()
	{
		// r1 is mounted last, but reads r2, which reads r3
		using var fx = new Fixture();
		WriteIn( fx, "r1/a.vmat", "reads r2:b.vmat" );
		WriteIn( fx, "r2/b.vmat", "reads r3:c.vtex" );
		WriteIn( fx, "r3/c.vtex", "red" );
		string[] mounts = ["r1", "r2", "r3"];

		var report = BuildRoots( fx, mounts );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "r3:c.vtex", "r2:b.vmat", "r1:a.vmat" }, report.Jobs.ToArray() );
		Assert.AreEqual( 0, report.DescribeMisses );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Dir, "r1/a.vmat_c" ) ), "red" );

		WriteIn( fx, "r3/c.vtex", "blue" );
		report = BuildRoots( fx, mounts );
		CollectionAssert.AreEqual( new[] { "r3:c.vtex", "r2:b.vmat", "r1:a.vmat" }, report.Jobs.ToArray(), report.Log.ToString() );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Dir, "r1/a.vmat_c" ) ), "blue" );

		AssertJobs( BuildRoots( fx, mounts ) );
	}

	[TestMethod]
	public void FailedProducerInAnotherRootBlocksItsReaders()
	{
		using var fx = new Fixture();
		WriteIn( fx, "r1/a.vmat", "reads r2:b.vtex" );
		WriteIn( fx, "r2/b.vtex", "red" );
		Assert.IsTrue( BuildRoots( fx, ["r2", "r1"] ).Success );
		var good = File.ReadAllText( Path.Combine( fx.Dir, "r1/a.vmat_c" ) );

		WriteIn( fx, "r2/b.vtex", "blue", "fail" );
		var report = BuildRoots( fx, ["r2", "r1"] );

		CollectionAssert.AreEqual( new[] { "r2:b.vtex" }, report.Failed.ToArray(), report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "r1:a.vmat" }, report.Blocked.ToArray(), report.Log.ToString() );
		Assert.AreEqual( good, File.ReadAllText( Path.Combine( fx.Dir, "r1/a.vmat_c" ) ) );

		WriteIn( fx, "r2/b.vtex", "blue" );
		AssertJobs( BuildRoots( fx, ["r2", "r1"] ), "r2:b.vtex", "r1:a.vmat" );
	}

	[TestMethod]
	public void CycleAcrossRootsFailsWithoutHanging()
	{
		using var fx = new Fixture();
		WriteIn( fx, "r1/a.vmat", "reads r2:b.vmat" );
		WriteIn( fx, "r2/b.vmat", "reads r1:a.vmat" );

		var report = BuildRoots( fx, ["r2", "r1"] );

		CollectionAssert.AreEquivalent( new[] { "r1:a.vmat", "r2:b.vmat" }, report.Failed.ToArray() );
		StringAssert.Contains( report.Log.ToString(), "cycle" );
	}

	[TestMethod]
	public void ShadowedSourceIsNeitherBuiltNorRead()
	{
		// Both roots have m.vtex; r2 is mounted later, so its copy is the only one anything sees
		using var fx = new Fixture();
		WriteIn( fx, "r1/m.vtex", "red" );
		WriteIn( fx, "r2/m.vtex", "blue" );
		WriteIn( fx, "r2/a.vmat", "reads *m.vtex" );

		var report = BuildRoots( fx, ["r1", "r2"] );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "r2:m.vtex", "r2:a.vmat" }, report.Jobs.ToArray() );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Dir, "r2/a.vmat_c" ) ), "blue" );

		WriteIn( fx, "r1/m.vtex", "green" );
		AssertJobs( BuildRoots( fx, ["r1", "r2"] ) );
	}

	[TestMethod]
	public void SourceIsHiddenByALaterRootsSourceForTheSameOutput()
	{
		// Different source formats, one compiled name: the later root's mp3 is the s.vsnd_c anything finds
		using var fx = new Fixture();
		WriteIn( fx, "r1/s.wav", "wave" );
		WriteIn( fx, "r2/s.mp3", "mpeg" );

		var report = BuildRoots( fx, ["r1", "r2"] );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "r2:s.mp3" }, report.Jobs.ToArray() );
		Assert.IsFalse( File.Exists( Path.Combine( fx.Dir, "r1/s.vsnd_c" ) ) );
	}

	[TestMethod]
	public void SelectionPullsInWhatItReadsFromAnotherRoot()
	{
		using var fx = new Fixture();
		WriteIn( fx, "r1/a.vmat", "reads r2:b.vtex" );
		WriteIn( fx, "r2/b.vtex", "red" );
		WriteIn( fx, "r2/unrelated.vtex", "red" );

		var report = BuildRoots( fx, ["r2", "r1"], select: ["r1/a.vmat"] );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "r2:b.vtex", "r1:a.vmat" }, report.Jobs.ToArray() );

		// A folder selects every source in it
		AssertJobs( BuildRoots( fx, ["r2", "r1"], select: ["r2"] ), "r2:unrelated.vtex" );
	}

	[TestMethod]
	public void RecordedReadOfAnotherRootFindsItsProducerOnceThatRootIsBuiltToo()
	{
		// Built alone, r1 could only record r2's output as an outside file; built together, that file is r2's
		using var fx = new Fixture();
		WriteIn( fx, "r2/b.vtex", "red" );
		Assert.IsTrue( BuildRoots( fx, ["r2"] ).Success );
		WriteIn( fx, "r1/a.vmat", "reads ../r2/b.vtex" );
		Assert.IsTrue( BuildRoots( fx, ["r1"] ).Success );

		// r2's source changes and fails: r1's unchanged record must still wait for it, and be blocked
		WriteIn( fx, "r2/b.vtex", "blue", "fail" );
		var report = BuildRoots( fx, ["r2", "r1"] );

		CollectionAssert.AreEqual( new[] { "r1:a.vmat" }, report.Blocked.ToArray(), report.Log.ToString() );
	}

	[TestMethod]
	public void NewShadowOfWhatWasReadRebuildsTheReader()
	{
		// a.vmat reads m.vtex through the mounts: core's until the later root gets its own
		using var fx = new Fixture();
		WriteIn( fx, "r1/m.vtex", "red" );
		WriteIn( fx, "r2/a.vmat", "reads *m.vtex" );
		Assert.IsTrue( BuildRoots( fx, ["r1", "r2"] ).Success );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Dir, "r2/a.vmat_c" ) ), "red" );

		WriteIn( fx, "r2/m.vtex", "blue" );
		var report = BuildRoots( fx, ["r1", "r2"] );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.Contains( report.Described.ToArray(), "r2:a.vmat" );
		CollectionAssert.AreEqual( new[] { "r2:m.vtex", "r2:a.vmat" }, report.Jobs.ToArray(), report.Log.ToString() );
		Assert.AreEqual( 0, report.DescribeMisses );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Dir, "r2/a.vmat_c" ) ), "blue" );
		AssertJobs( BuildRoots( fx, ["r1", "r2"] ) );

		// Removing the shadow goes back to the first root's
		File.Delete( Path.Combine( fx.Dir, "r2/m.vtex" ) );
		report = BuildRoots( fx, ["r1", "r2"] );
		AssertJobs( report, "r2:a.vmat" );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Dir, "r2/a.vmat_c" ) ), "red" );
	}

	[TestMethod]
	public void CancelledWhileWaitingForARootIsCancelledNotAnError()
	{
		using var fx = new Fixture();
		WriteIn( fx, "r1/a.vtex", "red", "sleep 800" );
		var first = Task.Run( () => BuildRoots( fx, ["r1"] ) );
		Thread.Sleep( 200 );

		using var cancel = new CancellationTokenSource( 200 );
		var waiting = ContentBuilder.Build( new BuildRequest { Roots = [new ContentRoot( "r1", Path.Combine( fx.Dir, "r1" ), Path.Combine( fx.Dir, "state-r1" ) )] }, fx.Compiler, cancel.Token );

		Assert.IsTrue( waiting.Cancelled );
		Assert.IsNull( waiting.Error );
		Assert.IsTrue( first.Result.Success );
	}

	[TestMethod]
	public void FileThatCantBeReplacedFailsOnlyItsAsset()
	{
		// Something has the old output open without sharing delete: that job is undone, the rest carries on
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		fx.Write( "m.vmat", "reads a.vtex" );
		fx.Write( "b.vtex", "red" );
		Assert.IsTrue( fx.Build().Success );
		fx.Write( "a.vtex", "blue" );
		fx.Write( "b.vtex", "blue" );
		var output = fx.Read( "a.vtex_c" );
		var marker = fx.Read( "a.vtex.src_c" );

		BuildReport report;
		using ( new FileStream( Path.Combine( fx.Root, "a.vtex_c" ), FileMode.Open, FileAccess.Read, FileShare.Read ) )
			report = fx.Build();

		CollectionAssert.AreEqual( new[] { "a.vtex" }, report.Failed.ToArray(), report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "m.vmat" }, report.Blocked.ToArray(), report.Log.ToString() );
		Assert.AreEqual( output, fx.Read( "a.vtex_c" ) );
		Assert.AreEqual( marker, fx.Read( "a.vtex.src_c" ), "everything that job had moved is put back" );
		Assert.AreEqual( 0, Directory.GetFiles( Path.Combine( fx.State, "journal" ) ).Length );
		Assert.IsFalse( Directory.Exists( Path.Combine( fx.State, "backup" ) ) && Directory.EnumerateFileSystemEntries( Path.Combine( fx.State, "backup" ) ).Any() );
		CollectionAssert.Contains( report.Changed, Path.GetFullPath( Path.Combine( fx.Root, "b.vtex_c" ) ) );

		AssertJobs( fx.Build(), "a.vtex", "m.vmat" );
		StringAssert.Contains( fx.Read( "m.vmat_c" ), "blue" );
	}

	[TestMethod]
	public void BuildWaitsForAnotherBuildOfItsRoot()
	{
		using var fx = new Fixture();
		fx.Write( "a.vtex", "red" );
		Directory.CreateDirectory( fx.State );
		var waited = 0;

		using var held = new FileStream( Path.Combine( fx.State, "lock" ), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None );
		var release = Task.Delay( 1500 ).ContinueWith( _ => held.Dispose() );
		var report = ContentBuilder.Build( new BuildRequest { Roots = [new ContentRoot( "unit", fx.Root, fx.State )], Waiting = _ => waited++ }, fx.Compiler );
		release.Wait();

		Assert.IsTrue( report.Success, report.Error ?? report.Log.ToString() );
		Assert.IsTrue( waited > 0 );
		AssertJobs( report, "a.vtex" );
	}

	[TestMethod]
	public void ReadOfAnotherProjectsCopyIsRebuiltWithoutThatProject()
	{
		// An engine material built while a project shadowed the texture it reads, then built without that project
		using var fx = new Fixture();
		WriteIn( fx, "r1/m.vtex", "red" );
		WriteIn( fx, "r1/a.vmat", "reads *m.vtex" );
		WriteIn( fx, "r2/m.vtex", "blue" );
		Assert.IsTrue( BuildRoots( fx, ["r1", "r2"] ).Success );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Dir, "r1/a.vmat_c" ) ), "blue" );

		// r1's own texture was hidden until now, so it's built first
		var report = BuildRoots( fx, ["r1"] );
		AssertJobs( report, "m.vtex", "a.vmat" );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Dir, "r1/a.vmat_c" ) ), "red" );
		AssertJobs( BuildRoots( fx, ["r1"] ) );

		report = BuildRoots( fx, ["r1", "r2"] );
		AssertJobs( report, "r1:a.vmat" );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Dir, "r1/a.vmat_c" ) ), "blue" );
		AssertJobs( BuildRoots( fx, ["r1", "r2"] ) );
	}

	[TestMethod]
	public void OrderOnlyReadOfAnotherProjectsCopyIsRebuiltWithoutThatProject()
	{
		// Which copy an order only read finds still matters, only edits to it don't
		using var fx = new Fixture();
		WriteIn( fx, "r1/m.vmat", "red" );
		WriteIn( fx, "r1/a.vmdl", "orders *m.vmat" );
		WriteIn( fx, "r2/m.vmat", "blue" );
		Assert.IsTrue( BuildRoots( fx, ["r1", "r2"] ).Success );

		AssertJobs( BuildRoots( fx, ["r1"] ), "m.vmat", "a.vmdl" );
		AssertJobs( BuildRoots( fx, ["r1"] ) );

		AssertJobs( BuildRoots( fx, ["r1", "r2"] ), "r1:a.vmdl" );
		WriteIn( fx, "r2/m.vmat", "green" );
		AssertJobs( BuildRoots( fx, ["r1", "r2"] ), "r2:m.vmat" );
	}

	[TestMethod]
	public void OlderRecordOfAReadOutsideTheBuildIsRebuilt()
	{
		// Written before reads named their output: the file alone can't say which root it belongs to
		using var fx = new Fixture();
		WriteIn( fx, "r1/m.vtex", "red" );
		WriteIn( fx, "r1/a.vmat", "reads *m.vtex" );
		WriteIn( fx, "r2/m.vtex", "blue" );
		Assert.IsTrue( BuildRoots( fx, ["r1", "r2"] ).Success );
		foreach ( var file in Directory.GetFiles( Path.Combine( fx.Dir, "state-r1", "records" ) ) )
			File.WriteAllText( file, Regex.Replace( File.ReadAllText( file ), @",\s*""Output"": ""[^""]*""", "" ) );

		var report = BuildRoots( fx, ["r1"] );

		AssertJobs( report, "m.vtex", "a.vmat" );
		StringAssert.Contains( File.ReadAllText( Path.Combine( fx.Dir, "r1/a.vmat_c" ) ), "red" );
	}

	[TestMethod]
	public void BuildsOfOverlappingRootsTakeTurnsInEitherOrder()
	{
		using var fx = new Fixture();
		WriteIn( fx, "r1/a.vtex", "red", "sleep 400" );
		WriteIn( fx, "r2/b.vtex", "red", "sleep 400" );

		var first = Task.Run( () => BuildRoots( fx, ["r1", "r2"] ) );
		var second = Task.Run( () => BuildRoots( fx, ["r2", "r1"] ) );

		Assert.IsTrue( Task.WaitAll( [first, second], TimeSpan.FromSeconds( 20 ) ), "deadlocked" );
		Assert.IsTrue( first.Result.Success && second.Result.Success, first.Result.Log.ToString() + second.Result.Log );
		Assert.AreEqual( 2, first.Result.Jobs.Count + second.Result.Jobs.Count, "one built, the other found it current" );
	}

	[TestMethod]
	public void RemovalStaysInItsRoot()
	{
		using var fx = new Fixture();
		WriteIn( fx, "r1/a.vmat", "reads r2:b.vtex" );
		WriteIn( fx, "r2/b.vtex", "red" );
		Assert.IsTrue( BuildRoots( fx, ["r2", "r1"] ).Success );

		File.Delete( Path.Combine( fx.Dir, "r2/b.vtex" ) );
		var report = BuildRoots( fx, ["r2", "r1"] );

		CollectionAssert.AreEqual( new[] { "r2:b.vtex" }, report.Removed.ToArray() );
		Assert.IsFalse( File.Exists( Path.Combine( fx.Dir, "r2/b.vtex_c" ) ) );
		Assert.IsTrue( File.Exists( Path.Combine( fx.Dir, "r1/a.vmat_c" ) ) );
		AssertJobs( report, "r1:a.vmat" );
	}

	[TestMethod]
	public void WritingIntoAnUnmountedRootFails()
	{
		using var fx = new Fixture();
		fx.Write( "d.vtex", "red", "generates gen/t.vtex_c" );

		var report = fx.Build();

		CollectionAssert.AreEqual( new[] { "d.vtex" }, report.Failed.ToArray() );
		StringAssert.Contains( report.Log.ToString(), "doesn't own" );
		Assert.IsFalse( fx.Exists( "d.vtex_c" ) );
	}

	[TestMethod]
	public void InputChecksumMatchesResourceCompiler()
	{
		// From a resourcecompiler build report: materials/a.vmat with this content had checksum b9258695
		var file = Path.GetTempFileName();
		File.WriteAllText( file, "Layer0\r\n{\r\n\tshader \"shaders/complex.shader\"\r\n\tTextureColor \"materials/a_color.tga\"\r\n}\r\n" );
		try { Assert.AreEqual( 0xb9258695u, Hashing.Checksum( file ) ); }
		finally { File.Delete( file ); }
	}
}
