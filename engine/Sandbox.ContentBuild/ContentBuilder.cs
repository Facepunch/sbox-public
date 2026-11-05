using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Sandbox.ContentBuild;

/// <param name="Mod">Mount name; compiled outputs are staged under it.</param>
/// <param name="Path">Content root; compiled outputs are published beside their sources.</param>
/// <param name="StateDirectory">Records, journal and staging. Outside every root, on the same volume as this one.</param>
public sealed record ContentRoot( string Mod, string Path, string StateDirectory );

public sealed class BuildRequest
{
	/// <summary>
	/// Every root the build mounts, in mount order: a root shadows the ones before it. One graph covers
	/// them all, so a compile that reads another root's output waits for it, and fails with it.
	/// </summary>
	public IReadOnlyList<ContentRoot> Roots { get; init; } = [];

	/// <summary>
	/// Shipped content available to compilers, mounted before the writable roots. Not inventoried, compiled,
	/// locked or recorded by this build: installed editors consume engine content and build their projects.
	/// </summary>
	public IReadOnlyList<(string Mod, string Path)> ReadOnlyMounts { get; init; } = [];

	/// <summary>
	/// Other writable roots a compile may write into, by mod name: the editor's transient folder ("transient"),
	/// where game resources put the textures they generate. Files there are named after their recipe and
	/// shared by every root's assets that make them, so they are recorded by mod (a missing one rebuilds its
	/// asset, in whichever project mounts the folder) but never compared or deleted here: the editor's orphan
	/// collector removes the ones nothing uses.
	/// </summary>
	public IReadOnlyDictionary<string, string> OutputRoots { get; init; } = new Dictionary<string, string>();

	/// <summary>
	/// Where else a file of an output root may already be, as the editor mounts it after that root (the engine's
	/// shipped transients): a recorded or adopted file found there is present. Nothing is written or deleted there.
	/// </summary>
	public IReadOnlyDictionary<string, string> OutputFallbacks { get; init; } = new Dictionary<string, string>();

	/// <summary>
	/// What a compile mounts, by mod name, each shadowing the ones before: each output root after its fallback,
	/// so what is written there wins over what was shipped, then the roots. The editor mounts transients last,
	/// so a root's own outputs win over a same-named file there (the menu's shipped cloud assets).
	/// </summary>
	public IReadOnlyList<(string Mod, string Path)> Mounts =>
	[
		.. OutputRoots.SelectMany( x => OutputFallbacks.TryGetValue( x.Key, out var fallback ) ? [(x.Key, fallback), (x.Key, x.Value)] : new[] { (x.Key, x.Value) } ),
		.. ReadOnlyMounts,
		.. Roots.Select( x => (x.Mod, x.Path) ),
	];

	/// <summary>
	/// Absolute paths of sources, or folders of them, to build with whatever they read; null builds every
	/// root and removes outputs of sources that are gone.
	/// </summary>
	public IReadOnlyList<string> Select { get; init; }

	public bool Force { get; init; }
	public IReadOnlyList<string> CompilerArgs { get; init; } = [];

	/// <summary>
	/// Compiles at once. 1 hands them out one at a time, from the calling thread.
	/// </summary>
	public int MaxJobs { get; init; } = Environment.ProcessorCount;
	public Action<string> JobFinished { get; init; }

	/// <summary>
	/// The files a publication just replaced, added or deleted, as soon as it has, on the thread that settles jobs.
	/// </summary>
	public Action<IReadOnlyList<string>> Published { get; init; }

	/// <summary>
	/// How long to wait for another build of any of the roots (the editor or SboxBuild) to finish.
	/// </summary>
	public TimeSpan LockTimeout { get; init; } = TimeSpan.FromMinutes( 30 );

	/// <summary>
	/// Called with the root's path every so often while another build holds it.
	/// </summary>
	public Action<string> Waiting { get; init; }

	/// <summary>
	/// Called after a job's files are in place and before its record is written. Tests throw here to
	/// interrupt a publication.
	/// </summary>
	internal Action<string> BeforeCommit { get; init; }
}

/// <summary>
/// Assets are named root-relative when the build has one root, "mod:path" when it has several.
/// </summary>
public sealed class BuildReport
{
	/// <summary>
	/// Every compile started, in order. An asset appears twice only after a describe miss.
	/// </summary>
	public List<string> Jobs { get; } = [];
	public List<string> Described { get; } = [];

	/// <summary>
	/// Wall time of each phase of the build, in order.
	/// </summary>
	public List<(string Phase, TimeSpan Time)> Phases { get; } = [];

	/// <summary>
	/// Compiler jobs by source extension and mode (".vmdl compile"): how many, and their time added up.
	/// </summary>
	public Dictionary<string, (int Jobs, TimeSpan Time)> JobTime { get; } = [];

	/// <summary>
	/// Outputs built without this build (before it had a record of them, or by the editor compiling one asset
	/// by itself) and still current: recorded, not compiled.
	/// </summary>
	public List<string> Adopted { get; } = [];
	public List<string> Failed { get; } = [];
	public List<string> Blocked { get; } = [];
	public List<string> Unsupported { get; } = [];
	public List<string> Removed { get; } = [];

	/// <summary>
	/// Compiles that read another asset's output their description didn't list, so they ran before it
	/// and had to run again. Zero for every supported case; anything else is a describe bug.
	/// </summary>
	public int DescribeMisses { get; set; }
	public bool Cancelled { get; set; }

	/// <summary>
	/// Why the build couldn't run at all, e.g. another build holds a root.
	/// </summary>
	public string Error { get; set; }

	/// <summary>
	/// Absolute paths of outputs this build wrote, deleted or adopted, for whoever caches what's on disk.
	/// </summary>
	public List<string> Changed { get; } = [];

	/// <summary>
	/// Absolute paths of the sources whose outputs are current after the build (a hidden source when the one hiding
	/// it is). Why the rest aren't is in <see cref="Log"/>.
	/// </summary>
	public HashSet<string> Current { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Absolute paths of the files in other output roots (<see cref="BuildRequest.OutputRoots"/>) that a record
	/// of the build's roots lists after the build. Whoever removes unused files there has to leave these.
	/// </summary>
	public HashSet<string> SharedOutputs { get; } = new( StringComparer.OrdinalIgnoreCase );
	public StringBuilder Log { get; } = new();

	public bool Success => Error is null && !Cancelled && Failed.Count == 0 && Blocked.Count == 0 && Unsupported.Count == 0;
}

/// <summary>
/// Builds content roots. Each source asset is a node; an edge means its compile reads another asset's
/// compiled output, in any root of the build. Edges come from describing the current source of every
/// asset whose inputs changed (or that was never built), and from the last successful build of the rest,
/// whose inputs didn't change. Compiles run once everything they read is current, and report what they
/// actually read, which checks the description. An asset whose record still holds isn't compiled.
/// Records, publication and ownership stay per root.
/// </summary>
public static class ContentBuilder
{
	enum State { Waiting, Queued, Running, Clean, Committed, Failed, Blocked }

	sealed class Node
	{
		public RootState Root;
		public string Source, Output, Name;
		public AssetKind Kind;
		public AssetRecord Record;
		public HashSet<Node> Prerequisites = [];
		public bool Planned;
		public State State;
		public long Started, Committed;
		public DateTime Launched;
		public int Misses;
		public bool Final => State is State.Clean or State.Committed or State.Failed or State.Blocked;
	}

	/// <summary>
	/// Builds with a compiler that may keep workers for the next build: whoever made it calls <see cref="IAssetCompiler.Finished"/>.
	/// </summary>
	public static BuildReport Build( BuildRequest request, IAssetCompiler compiler, CancellationToken token = default )
	{
		using var run = new Run( request, compiler, token, false );
		return run.Execute().GetAwaiter().GetResult();
	}

	/// <summary>
	/// The same build, yielding while compilers run. Planning, publication and callbacks resume on the caller's
	/// synchronization context, so the editor can keep ticking without moving engine calls to worker threads.
	/// </summary>
	public static async Task<BuildReport> BuildAsync( BuildRequest request, IAssetCompiler compiler, CancellationToken token = default )
	{
		using var run = new Run( request, compiler, token, true );
		return await run.Execute();
	}

	/// <summary>
	/// The file a build holds exclusively, from any process, while it builds the root.
	/// </summary>
	public static string LockFile( ContentRoot root ) => Path.Combine( root.StateDirectory, "lock" );

	/// <summary>
	/// One root of the build: its sources, its records, and where its files go.
	/// </summary>
	sealed class RootState( ContentRoot def, IReadOnlyDictionary<string, string> outputRoots, IReadOnlyDictionary<string, string> fallbacks )
	{
		public readonly ContentRoot Def = def;
		public readonly string Directory = Path.GetFullPath( def.Path ).TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar;
		public RecordStore Store;
		public readonly Dictionary<string, (string Output, AssetKind Kind)> Inventory = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, string> SourceOf = new( StringComparer.OrdinalIgnoreCase );

		public string Relative( string path ) => Path.GetRelativePath( Directory, path ).Replace( '\\', '/' );

		public string InRoot( string path )
		{
			if ( string.IsNullOrEmpty( path ) ) return null;
			var full = Path.GetFullPath( path );
			return full.StartsWith( Directory, StringComparison.OrdinalIgnoreCase ) ? Relative( full ) : null;
		}

		/// <summary>
		/// Where a staged file is published, as an output key: root-relative for this root, "mod:path" for
		/// another output root, null for anywhere else.
		/// </summary>
		public string OutputKey( string staged, string staging )
		{
			var parts = Path.GetRelativePath( staging, staged ).Replace( '\\', '/' ).Split( '/', 2 );
			if ( parts.Length < 2 || parts[0] == ".." ) return null;
			if ( string.Equals( parts[0], Def.Mod, StringComparison.OrdinalIgnoreCase ) ) return parts[1];
			return outputRoots.Keys.FirstOrDefault( x => string.Equals( x, parts[0], StringComparison.OrdinalIgnoreCase ) ) is { } mod ? $"{mod}:{parts[1]}" : null;
		}

		/// <summary>
		/// The output key of a file on disk, or null if it isn't somewhere this root's assets write.
		/// </summary>
		public string Key( string path )
		{
			if ( InRoot( path ) is { } rel ) return rel;

			var full = Path.GetFullPath( path );
			foreach ( var (mod, dir) in outputRoots.Concat( fallbacks ) )
			{
				var root = Path.GetFullPath( dir ).TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar;
				if ( full.StartsWith( root, StringComparison.OrdinalIgnoreCase ) ) return $"{mod}:{Path.GetRelativePath( root, full ).Replace( '\\', '/' )}";
			}
			return null;
		}

		/// <summary>
		/// Where an output key's file is now: where it is written, else, for another output root, its fallback.
		/// Null if it is in neither.
		/// </summary>
		public string Locate( string key )
		{
			if ( Resolve( key ) is { } path && File.Exists( path ) ) return path;

			var colon = key.IndexOf( ':' );
			if ( colon < 0 || fallbacks.FirstOrDefault( x => string.Equals( x.Key, key[..colon], StringComparison.OrdinalIgnoreCase ) ).Value is not { } dir ) return null;

			var root = Path.GetFullPath( dir ).TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar;
			var fallback = Path.GetFullPath( Path.Combine( root, key[(colon + 1)..] ) );
			return fallback.StartsWith( root, StringComparison.OrdinalIgnoreCase ) && File.Exists( fallback ) ? fallback : null;
		}

		/// <summary>
		/// Where an output key is on disk, or null if its root isn't mounted in this build. A key that would
		/// leave its root (a hand-edited or foreign record) resolves nowhere, so it is never touched.
		/// </summary>
		public string Resolve( string key )
		{
			var colon = key.IndexOf( ':' );
			var dir = colon < 0 ? Def.Path : outputRoots.FirstOrDefault( x => string.Equals( x.Key, key[..colon], StringComparison.OrdinalIgnoreCase ) ).Value;
			if ( dir is null ) return null;

			var root = Path.GetFullPath( dir ).TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar;
			var path = Path.GetFullPath( Path.Combine( root, key[(colon + 1)..] ) );
			return path.StartsWith( root, StringComparison.OrdinalIgnoreCase ) ? path : null;
		}
	}

	sealed class Run( BuildRequest request, IAssetCompiler compiler, CancellationToken token, bool asynchronous ) : IDisposable
	{
		readonly BuildReport report = new();

		// Cancels the compiles still running when the build stops on an error; the caller's token cancels them too
		readonly CancellationTokenSource abort = CancellationTokenSource.CreateLinkedTokenSource( token );

		public void Dispose() => abort.Dispose();
		readonly List<RootState> roots = request.Roots.Select( x => new RootState( x, request.OutputRoots, request.OutputFallbacks ) ).ToList();
		readonly Dictionary<(RootState, string), Node> nodes = new( new NodeKey() );
		readonly Dictionary<SpecialDependency, uint?> fingerprints = [];
		readonly IReadOnlyList<(string Mod, string Path)> mounts = request.Mounts;
		readonly Dictionary<string, string> arguments = ParseArguments( request.CompilerArgs );
		readonly List<(string Source, string Visible)> hidden = [];
		long clock;
		int jobs;

		public async Task<BuildReport> Execute()
		{
			foreach ( var root in roots )
			{
				if ( roots.Any( x => x.InRoot( root.Def.StateDirectory ) is not null ) )
					throw new ArgumentException( $"The state directory of {root.Def.Mod} can't be inside a content root" );
			}

			// One build of a root at a time, from any process: recovery and publication rewrite its files.
			// Every root is held for the whole build, taken in one order so two builds can't deadlock.
			var locks = new List<FileStream>();
			var phase = Stopwatch.StartNew();
			void Phase( string name )
			{
				report.Phases.Add( (name, phase.Elapsed) );
				phase.Restart();
			}

			try
			{
				foreach ( var root in roots.OrderBy( x => Path.GetFullPath( x.Def.StateDirectory ), StringComparer.OrdinalIgnoreCase ) )
				{
					if ( !Lock( root.Def, locks ) )
					{
						if ( token.IsCancellationRequested ) report.Cancelled = true;
						else report.Error = $"another content build of {root.Def.Path} is running";
						return report;
					}
				}

				foreach ( var root in roots )
				{
					root.Store = new RecordStore( root.Resolve, root.Def.StateDirectory );
					DeleteStaging( Path.Combine( root.Def.StateDirectory, "staging" ) );
					Inventory( root );
					if ( request.Select is null ) RemoveDeletedSources( root );
				}

				// A source whose output a later root also builds is hidden (sounds/x.wav under sounds/x.mp3 too):
				// the compiler and the runtime only ever find the later one. It isn't built, and what it built
				// before is left alone.
				for ( var i = 0; i < roots.Count; i++ )
				{
					var root = roots[i];
					foreach ( var source in root.Inventory.Keys.Where( x => roots.Skip( i + 1 ).Any( r => r.SourceOf.ContainsKey( root.Inventory[x].Output ) ) ).ToList() )
					{
						var output = root.Inventory[source].Output;
						var visible = roots.Last( r => r.SourceOf.ContainsKey( output ) );
						hidden.Add( (Absolute( root, source ), Absolute( visible, visible.SourceOf[output] )) );
						root.SourceOf.Remove( output );
						root.Inventory.Remove( source );
					}
				}

				foreach ( var selected in request.Select ?? roots.Select( x => x.Def.Path ).ToList() ) Select( selected );
				Phase( "inventory" );

				await Plan();
				Phase( "describe and adopt" );
				if ( token.IsCancellationRequested ) { report.Cancelled = true; return report; }

				await Schedule();
				Settle();
				Phase( "compile and publish" );
				return report;
			}
			finally
			{
				foreach ( var held in locks ) held.Dispose();
			}
		}

		/// <summary>
		/// What the build ended up knowing: which sources are current, and the shared files records own. A hidden
		/// source is as current as the one hiding it, which is what the runtime finds.
		/// </summary>
		void Settle()
		{
			foreach ( var node in nodes.Values.Where( x => x.State is State.Clean or State.Committed ) )
				report.Current.Add( Absolute( node.Root, node.Source ) );

			foreach ( var (source, visible) in hidden.Where( x => report.Current.Contains( x.Visible ) ) )
				report.Current.Add( source );

			foreach ( var root in roots )
			{
				foreach ( var key in root.Store.All.Values.SelectMany( x => x.Outputs.Keys ).Where( Shared ) )
				{
					if ( root.Resolve( key ) is { } path ) report.SharedOutputs.Add( path );
				}
			}
		}

		static string Absolute( RootState root, string source ) => Path.GetFullPath( Path.Combine( root.Def.Path, source ) );

		bool Lock( ContentRoot root, List<FileStream> held )
		{
			Directory.CreateDirectory( root.StateDirectory );
			var path = LockFile( root );
			var until = DateTime.UtcNow + request.LockTimeout;
			while ( true )
			{
				try { held.Add( new FileStream( path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None ) ); return true; }
				catch ( IOException ) when ( DateTime.UtcNow < until && !token.IsCancellationRequested )
				{
					request.Waiting?.Invoke( root.Path );
					Thread.Sleep( 100 );
				}
				catch ( IOException ) { return false; }
			}
		}

		string Name( RootState root, string source ) => roots.Count == 1 ? source : $"{root.Def.Mod}:{source}";

		void Inventory( RootState root )
		{
			foreach ( var file in Directory.EnumerateFiles( root.Def.Path, "*", SearchOption.AllDirectories ) )
			{
				var source = root.Relative( file );
				if ( source.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) continue;

				var kind = compiler.Classify( Path.GetExtension( source ), out var outputExtension );
				if ( kind == AssetKind.None ) continue;
				if ( kind == AssetKind.Unsupported ) { report.Unsupported.Add( Name( root, source ) ); continue; }

				var output = Path.ChangeExtension( source, outputExtension );
				if ( root.SourceOf.TryGetValue( output, out var other ) )
				{
					report.Failed.AddRange( [Name( root, source ), Name( root, other )] );
					report.Log.AppendLine( $"{Name( root, source )} and {other} both compile to {output}" );
					root.Inventory.Remove( other );
					continue;
				}

				root.SourceOf[output] = source;
				root.Inventory[source] = (output, kind);
			}
		}

		/// <summary>
		/// Adds a selected source, or every source under a selected folder.
		/// </summary>
		void Select( string path )
		{
			var full = Path.GetFullPath( path ).TrimEnd( Path.DirectorySeparatorChar );
			var found = false;
			foreach ( var root in roots )
			{
				if ( File.Exists( full ) && root.InRoot( full ) is { } rel && root.Inventory.ContainsKey( rel ) ) { Add( root, rel ); found = true; }
				if ( !Directory.Exists( full ) ) continue;

				var folder = full + Path.DirectorySeparatorChar;
				foreach ( var source in root.Inventory.Keys.Where( x => Path.GetFullPath( Path.Combine( root.Def.Path, x ) ).StartsWith( folder, StringComparison.OrdinalIgnoreCase ) ).ToList() )
				{
					Add( root, source );
					found = true;
				}
			}

			if ( !found && !Directory.Exists( full ) ) report.Failed.Add( path );
		}

		Node Add( RootState root, string source )
		{
			if ( nodes.TryGetValue( (root, source), out var node ) ) return node;

			var (output, kind) = root.Inventory[source];
			node = new Node { Root = root, Source = source, Output = output, Name = Name( root, source ), Kind = kind, Record = root.Store.All.GetValueOrDefault( source ) };
			nodes[(root, source)] = node;
			return node;
		}

		/// <summary>
		/// The node whose source the compiler resolved, in whichever root it is.
		/// </summary>
		Node Producer( string source )
		{
			foreach ( var root in roots )
			{
				if ( root.InRoot( source ) is { } rel && root.Inventory.ContainsKey( rel ) ) return Add( root, rel );
			}
			return null;
		}

		/// <summary>
		/// The node that publishes this compiled file, in whichever root it is.
		/// </summary>
		Node ProducerOfOutput( string file )
		{
			foreach ( var root in roots )
			{
				if ( root.InRoot( file ) is { } rel && root.SourceOf.TryGetValue( rel, out var source ) && root.Inventory.ContainsKey( source ) ) return Add( root, source );
			}
			return null;
		}

		/// <summary>
		/// Finds what every node reads: from a fresh description if its own inputs changed, otherwise from
		/// its record. Producers found are added and planned too, so a selection pulls in what it needs.
		/// </summary>
		async Task Plan()
		{
			for ( var pending = nodes.Values.ToList(); pending.Count > 0; pending = nodes.Values.Where( x => !x.Planned ).ToList() )
			{
				LoadFingerprints( pending );

				var describe = pending.Where( x => x.Kind == AssetKind.Described && (SelfDirty( x ) ?? Shadowed( x )) is not null ).ToList();
				var results = await RunJobs( describe, node => Compile( Job( node, JobMode.Describe ) ) );
				var described = new Dictionary<Node, IReadOnlyList<PrerequisiteRead>>();
				for ( var i = 0; i < describe.Count; i++ )
				{
					report.Described.Add( describe[i].Name );

					// Without a complete description nothing is known about what it reads
					if ( results[i].Success ) described[describe[i]] = results[i].Prerequisites;
					else Fail( describe[i], "describe failed", results[i].Log );
				}

				await Adopt( pending, described );

				foreach ( var node in pending )
				{
					node.Planned = true;
					if ( described.TryGetValue( node, out var reads ) )
					{
						foreach ( var read in reads )
						{
							var producer = Producer( read.Source );
							if ( producer is not null && producer != node ) node.Prerequisites.Add( producer );
						}
						continue;
					}

					// A recorded read is the compiled file that was read, so it finds its producer in any root of
					// this build, including one that wasn't part of the build that recorded it
					foreach ( var prerequisite in node.Record?.Prerequisites ?? [] )
					{
						var producer = ProducerOfOutput( prerequisite.File );
						if ( producer is not null && producer != node ) node.Prerequisites.Add( producer );
					}
				}
			}
		}

		/// <summary>
		/// Records outputs this build didn't make (contentbuilder, the old editor loop or a shipped install built
		/// them, or the editor compiled the asset by itself after something it was built from changed) when they are
		/// provably current: the compiler
		/// confirms every input, compiler version and argument their edit info lists is unchanged, every output is
		/// present in a root this build writes, and every other asset's compiled output the asset reads (from its
		/// fresh description, or its record) is one of those inputs, so it was read as it is now. Anything else
		/// compiles once, which records what it read.
		/// </summary>
		async Task Adopt( List<Node> pending, Dictionary<Node, IReadOnlyList<PrerequisiteRead>> described )
		{
			var candidates = request.Force ? [] : pending.Where( x => x.State == State.Waiting && (x.Record is null || RebuiltOutside( x )) && File.Exists( x.Root.Resolve( x.Output ) ) && (x.Kind != AssetKind.Described || described.ContainsKey( x ) || x.Record is not null) ).ToList();
			var results = await RunJobs( candidates, node => Compile( Job( node, JobMode.Adopt ) ) );

			var adopted = new List<Node>();
			for ( var i = 0; i < candidates.Count; i++ )
			{
				var (node, result) = (candidates[i], results[i]);
				if ( !result.Success ) continue;

				// Owned outputs only: a child found in another root or mount isn't this asset's to adopt
				var root = node.Root;
				var outputs = result.Outputs.Select( root.Key ).Distinct( StringComparer.OrdinalIgnoreCase ).ToList();
				if ( outputs.Any( x => x is null || root.Locate( x ) is null ) || !outputs.Contains( node.Output, StringComparer.OrdinalIgnoreCase ) ) continue;

				// An output only it owned that this build of it doesn't make is a compile's to remove
				if ( node.Record is not null && Unshared( root, node.Record.Outputs.Keys.Where( x => !outputs.Contains( x, StringComparer.OrdinalIgnoreCase ) ), node.Source ).Any() ) continue;

				// Its own outputs (its generated textures) aren't reads
				var reads = (described.TryGetValue( node, out var fresh ) ? fresh.Select( Read ).Where( x => x.File is not null && x.Producer != node ).ToList() : (node.Record?.Prerequisites ?? []).Select( x => (Producer: ProducerOfOutput( x.File ), File: Path.GetFullPath( x.File ), x.OrderOnly) ).ToList())
					.Where( x => !outputs.Contains( root.Key( x.File ), StringComparer.OrdinalIgnoreCase ) ).ToList();
				var checkedReads = result.Inputs.Where( x => x.Game ).Select( x => Path.GetFullPath( x.Path ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );
				if ( reads.Any( x => !x.OrderOnly && !checkedReads.Contains( x.File ) ) ) continue;

				// Whatever else the output says it read (more than its description lists) is a read too, built first
				reads.AddRange( checkedReads.Where( x => !reads.Any( r => string.Equals( r.File, x, StringComparison.OrdinalIgnoreCase ) ) && !outputs.Contains( root.Key( x ), StringComparer.OrdinalIgnoreCase ) ).Select( x => (ProducerOfOutput( x ), x, false) ) );
				foreach ( var producer in reads.Select( x => x.Producer ).Where( x => x is not null && x != node ) ) node.Prerequisites.Add( producer );

				var prerequisites = reads.Select( x => new RecordedPrerequisite( x.File, File.Exists( x.File ) ? Hashing.File( x.File ) : "0", ReadOutput( x.Producer, x.File ), x.OrderOnly ) ).ToList();
				var record = NewRecord( node, result, outputs.ToDictionary( x => x, x => Hashing.Stamp( root.Locate( x ) ), StringComparer.OrdinalIgnoreCase ), prerequisites );

				try { root.Store.Publish( node.Source, record.Job, record, new Dictionary<string, string>(), [] ); }
				catch ( PublishFailedException ) { continue; }
				node.Record = record;
				adopted.Add( node );
				report.Adopted.Add( node.Name );
				Changed( outputs.Select( root.Locate ) );
			}

			LoadFingerprints( adopted );
		}

		/// <summary>
		/// The compiled file a described read finds: its producer's output in this build, or whatever the compiler found.
		/// </summary>
		(Node Producer, string File, bool OrderOnly) Read( PrerequisiteRead read ) => Producer( read.Source ) is { } producer
			? (producer, Path.GetFullPath( producer.Root.Resolve( producer.Output ) ), read.OrderOnly)
			: (null, read.Compiled is null ? null : Path.GetFullPath( read.Compiled ), read.OrderOnly);

		string ReadOutput( Node producer, string file ) => producer?.Output ?? request.ReadOnlyMounts
			.Select( x => Path.GetRelativePath( x.Path, file ).Replace( '\\', '/' ) )
			.FirstOrDefault( x => !Path.IsPathRooted( x ) && x != ".." && !x.StartsWith( "../" ) ) ?? "";

		/// <summary>
		/// An output it owns isn't what its record says this build published, and something it was built from
		/// changed since, so another compile may have rebuilt it. Changed bytes with nothing else changed are damage,
		/// which the record's hash catches and a compile repairs.
		/// </summary>
		bool RebuiltOutside( Node node ) => node.Record.Outputs.Any( x =>
		{
			if ( Shared( x.Key ) || node.Root.Resolve( x.Key ) is not { } path ) return false;

			var file = new FileInfo( path );
			return file.Exists && (file.Length != x.Value.Size || file.LastWriteTimeUtc.Ticks != x.Value.Time) && Hashing.File( path ) != x.Value.Hash;
		} ) && (SelfDirty( node ) ?? Shadowed( node ) ?? ReadChanged( node )) is not null;

		/// <summary>
		/// A record of a compile: what it read, stamped as it is now, and the outputs it published.
		/// </summary>
		AssetRecord NewRecord( Node node, CompileResult result, Dictionary<string, FileStamp> outputs, List<RecordedPrerequisite> prerequisites ) => new()
		{
			Source = node.Source,
			Job = NewJob(),
			Outputs = outputs,
			Inputs = result.Inputs.Where( x => !x.Game ).DistinctBy( x => x.Path, StringComparer.OrdinalIgnoreCase ).Select( StampInput ).ToList(),
			ChangedWhileCompiling = node.Launched != default && result.Inputs.Any( x => File.Exists( x.Path ) && File.GetLastWriteTimeUtc( x.Path ) >= node.Launched ),
			Special = result.Special.Distinct().ToList(),
			Arguments = result.Arguments.Distinct().ToDictionary( x => x, x => arguments.GetValueOrDefault( x ) ),
			Prerequisites = prerequisites,
		};

		/// <summary>
		/// Removes what was built from sources that no longer exist. A source this build doesn't compile (a
		/// shader, a map) still exists, and its outputs stay.
		/// </summary>
		void RemoveDeletedSources( RootState root )
		{
			foreach ( var record in root.Store.All.Values.Where( x => !File.Exists( Path.Combine( root.Def.Path, x.Source ) ) ).ToList() )
			{
				var outputs = Unshared( root, record.Outputs.Keys, record.Source ).ToList();
				try
				{
					Changed( root.Store.Publish( record.Source, NewJob(), null, new Dictionary<string, string>(), outputs, request.BeforeCommit ) );
					report.Removed.Add( Name( root, record.Source ) );
				}
				catch ( PublishFailedException e )
				{
					report.Failed.Add( Name( root, record.Source ) );
					report.Log.AppendLine( $"{Name( root, record.Source )}: {e.Message}" );
				}
			}
		}

		void Changed( IEnumerable<string> paths )
		{
			var list = paths.Where( x => x is not null ).ToList();
			report.Changed.AddRange( list );
			if ( list.Count > 0 ) request.Published?.Invoke( list );
		}

		static IEnumerable<string> Unshared( RootState root, IEnumerable<string> outputs, string owner ) =>
			outputs.Where( x => !Shared( x ) && !root.Store.Owners( x ).Any( o => !string.Equals( o, owner, StringComparison.OrdinalIgnoreCase ) ) ).ToList();

		/// <summary>
		/// In another output root, shared with assets of other roots (see <see cref="BuildRequest.OutputRoots"/>).
		/// </summary>
		static bool Shared( string key ) => key.Contains( ':' );

		void LoadFingerprints( IEnumerable<Node> pending )
		{
			var missing = pending.SelectMany( x => x.Record?.Special ?? [] ).Select( x => x with { Fingerprint = 0 } ).Distinct().Where( x => !fingerprints.ContainsKey( x ) ).ToList();
			if ( missing.Count == 0 ) return;

			var current = compiler.Fingerprints( missing );
			for ( var i = 0; i < missing.Count; i++ ) fingerprints[missing[i]] = current[i];
		}

		async Task Schedule()
		{
			var running = new Dictionary<Task<CompileResult>, (Node Node, CompileJob Job)>();
			try
			{
				await ScheduleUntilDone( running );
			}
			catch
			{
				// Settling a job went wrong: the compiles still running stop before the build lets go of its roots
				abort.Cancel();
				try
				{
					if ( asynchronous ) await Task.WhenAll( running.Keys );
					else Task.WaitAll( [.. running.Keys] );
				}
				catch ( Exception ) { }
				foreach ( var (_, job) in running.Values ) DeleteStaging( job.Staging );
				throw;
			}
		}

		async Task ScheduleUntilDone( Dictionary<Task<CompileResult>, (Node Node, CompileJob Job)> running )
		{
			var queue = new List<Node>();
			while ( true )
			{
				// Producers a compile turned out to read are planned like the rest before they run
				if ( nodes.Values.Any( x => !x.Planned ) ) await Plan();

				// Everything whose prerequisites are settled is current, blocked or queued; settling one can
				// make others ready, so repeat until nothing changes
				for ( var ready = Ready(); ready.Count > 0; ready = Ready() )
				{
					foreach ( var node in ready )
					{
						var blocker = node.Prerequisites.FirstOrDefault( p => p.State is State.Failed or State.Blocked );
						if ( blocker is not null )
						{
							Block( node, $"blocked by {blocker.Name}" );
							continue;
						}

						var reason = Dirty( node );
						if ( reason is null ) { node.State = State.Clean; continue; }

						report.Log.AppendLine( $"{node.Name}: {reason}" );
						node.State = State.Queued;
						queue.Add( node );
					}
				}

				// What should take longest starts first, so it doesn't end the build alone; ties stay in name order
				queue = [.. queue.OrderByDescending( Estimate )];

				while ( running.Count < Math.Max( 1, request.MaxJobs ) && queue.Count > 0 && !token.IsCancellationRequested )
				{
					var node = queue[0];
					queue.RemoveAt( 0 );
					node.State = State.Running;
					node.Started = ++clock;
					node.Launched = DateTime.UtcNow;
					report.Jobs.Add( node.Name );

					var job = Job( node, JobMode.Compile );
					var task = !asynchronous && request.MaxJobs <= 1 ? Task.FromResult( Compile( job, keep: true ) ) : Task.Run( () => Compile( job, keep: true ) );
					running[task] = (node, job);
				}

				if ( running.Count == 0 )
				{
					if ( token.IsCancellationRequested ) { report.Cancelled = true; return; }

					var stuck = nodes.Values.Where( x => !x.Final ).ToList();
					if ( stuck.Count == 0 ) return;

					// Nothing can run: what's left waits on itself
					foreach ( var node in stuck )
						Fail( node, $"prerequisite cycle through {string.Join( ", ", node.Prerequisites.Where( p => !p.Final || p.State == State.Failed ).Select( p => p.Name ) )}", "" );
					continue;
				}

				var next = Task.WhenAny( running.Keys );
				var done = asynchronous ? await next : next.GetAwaiter().GetResult();
				var (finished, finishedJob) = running[done];
				running.Remove( done );

				try { Finish( finished, done.Result, finishedJob.Staging ); }
				finally { DeleteStaging( finishedJob.Staging ); }

				request.JobFinished?.Invoke( finished.Name );
			}
		}

		/// <summary>
		/// Rough seconds a compile of each source type took on the engine's own content, for sources nothing in the
		/// build has timed yet. Sizes vary a lot within a type (a 4K sky material takes 100 s, most materials 4 s).
		/// </summary>
		static readonly Dictionary<string, double> TypicalSeconds = new( StringComparer.OrdinalIgnoreCase )
		{
			[".vmat"] = 3.9,
			[".ogg"] = 4,
			[".mp3"] = 2,
			[".vtex"] = 1.4,
			[".vmdl"] = 0.8,
			[".vanmgrph"] = 0.6,
			[".wav"] = 0.2,
		};

		Dictionary<string, double> recordedSeconds;

		/// <summary>
		/// How long a compile should take: its own last compile's time (launch to settle, so an estimate), else the
		/// average of its type's recorded times in this build, else the typical time for its type.
		/// </summary>
		double Estimate( Node node )
		{
			if ( node.Record?.Seconds > 0 ) return node.Record.Seconds;

			recordedSeconds ??= nodes.Values.Where( x => x.Record?.Seconds > 0 )
				.GroupBy( x => Path.GetExtension( x.Source ), StringComparer.OrdinalIgnoreCase )
				.ToDictionary( x => x.Key, x => x.Average( n => n.Record.Seconds ), StringComparer.OrdinalIgnoreCase );

			var type = Path.GetExtension( node.Source );
			return recordedSeconds.TryGetValue( type, out var seconds ) ? seconds : TypicalSeconds.GetValueOrDefault( type, 1 );
		}

		List<Node> Ready() => nodes.Values
			.Where( x => x.State == State.Waiting && x.Prerequisites.All( p => p.Final ) )
			.OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
			.ToList();

		CompileJob Job( Node node, JobMode mode )
		{
			// Short: compiled paths under it have to fit the native MAX_PATH. Unique while the root is locked.
			var staging = Path.Combine( node.Root.Def.StateDirectory, "staging", $"{Interlocked.Increment( ref jobs ):x}" );
			Directory.CreateDirectory( staging );
			return new CompileJob( node.Root.Def, mounts, Path.Combine( node.Root.Def.Path, node.Source ), staging, request.CompilerArgs, mode );
		}

		/// <summary>
		/// Runs a compile; a compiler that throws fails the job rather than the build. Unless kept, the
		/// staging directory goes when it's done.
		/// </summary>
		CompileResult Compile( CompileJob job, bool keep = false )
		{
			var time = Stopwatch.StartNew();
			try { return compiler.Compile( job, abort.Token ); }
			catch ( Exception e ) { return new CompileResult( false, e.ToString(), [], [], [], [], [] ); }
			finally
			{
				if ( !keep ) DeleteStaging( job.Staging );

				var key = $"{Path.GetExtension( job.Source ).ToLowerInvariant()} {job.Mode.ToString().ToLowerInvariant()}";
				lock ( report.JobTime )
				{
					var (jobs, total) = report.JobTime.GetValueOrDefault( key );
					report.JobTime[key] = (jobs + 1, total + time.Elapsed);
				}
			}
		}

		Task<List<CompileResult>> RunJobs( List<Node> items, Func<Node, CompileResult> run ) =>
			asynchronous && items.Count > 0 ? Task.Run( () => ForEach( items, run ) ) : Task.FromResult( ForEach( items, run ) );

		List<CompileResult> ForEach( List<Node> items, Func<Node, CompileResult> run )
		{
			var results = new CompileResult[items.Count];
			void One( int i ) => results[i] = token.IsCancellationRequested ? new CompileResult( false, "cancelled", [], [], [], [], [] ) : run( items[i] );

			if ( request.MaxJobs <= 1 ) for ( var i = 0; i < items.Count; i++ ) One( i );
			// One at a time: ranges handed out in chunks queue slow neighbours (the citizen models) behind each other
			else Parallel.ForEach( Partitioner.Create( Enumerable.Range( 0, items.Count ), EnumerablePartitionerOptions.NoBuffering ), new ParallelOptions { MaxDegreeOfParallelism = request.MaxJobs }, One );
			return results.ToList();
		}

		void Finish( Node node, CompileResult result, string staging )
		{
			if ( token.IsCancellationRequested )
			{
				node.State = State.Failed;
				report.Cancelled = true;
				return;
			}

			var root = node.Root;
			var reads = Reads( node, result, result.Outputs.Select( x => root.OutputKey( x, staging ) ).ToHashSet( StringComparer.OrdinalIgnoreCase ), file => File.Exists( file ) ? Hashing.File( file ) : "0" );

			// Read something not yet built this run: its description missed it. Build it first and rerun,
			// once; a compile that misses twice fails rather than loop.
			var late = reads.Where( x => x.Node is not null && (!x.Node.Final || x.Node.Committed > node.Started) ).Select( x => x.Node ).ToList();
			if ( late.Count > 0 )
			{
				report.Log.AppendLine( $"{node.Name}: read {string.Join( ", ", late.Select( x => x.Name ) )}, which its description didn't list" );
				if ( node.Misses++ > 0 ) { Fail( node, "described wrongly twice", result.Log ); return; }

				node.Prerequisites.UnionWith( late );
				node.State = State.Waiting;
				report.DescribeMisses++;
				return;
			}

			if ( Misread( node, result ) is { } shadowed ) { Fail( node, shadowed, result.Log ); return; }

			var blocker = reads.FirstOrDefault( x => x.Node?.State is State.Failed or State.Blocked ).Node;
			if ( blocker is not null ) { Block( node, $"read {blocker.Name}, which failed" ); return; }

			if ( result.Success ) KeepPublishedGenerated( node, result, staging );
			var error = result.Success ? Validate( node, result, staging ) : "compile failed";
			if ( error is not null ) { Fail( node, error, result.Log ); return; }

			var staged = result.Outputs.DistinctBy( x => root.OutputKey( x, staging ), StringComparer.OrdinalIgnoreCase ).ToDictionary( x => root.OutputKey( x, staging ), x => x, StringComparer.OrdinalIgnoreCase );
			var record = NewRecord( node, result, staged.ToDictionary( x => x.Key, x => Hashing.Stamp( x.Value ), StringComparer.OrdinalIgnoreCase ), reads.Select( x => x.Recorded ).ToList() );
			record.Seconds = Math.Round( (DateTime.UtcNow - node.Launched).TotalSeconds, 2 );

			var obsolete = node.Record is null ? [] : Unshared( root, node.Record.Outputs.Keys.Where( x => !staged.ContainsKey( x ) ), node.Source );
			try { Changed( root.Store.Publish( node.Source, record.Job, record, staged, obsolete, request.BeforeCommit ) ); }
			catch ( PublishFailedException e ) { Fail( node, e.Message, "" ); return; }

			node.Record = record;
			node.State = State.Committed;
			node.Committed = ++clock;
		}

		void Fail( Node node, string error, string log )
		{
			node.State = State.Failed;
			report.Failed.Add( node.Name );
			report.Log.AppendLine( $"{node.Name}: {error}\n{log}" );
		}

		void Block( Node node, string error )
		{
			node.State = State.Blocked;
			report.Blocked.Add( node.Name );
			report.Log.AppendLine( $"{node.Name}: {error}" );
		}

		/// <summary>
		/// The compiled outputs a compile read, with their producer in the build if there is one and the
		/// hash of the file that was there to read. Its own outputs (by key) don't count.
		/// </summary>
		List<(Node Node, RecordedPrerequisite Recorded)> Reads( Node node, CompileResult result, HashSet<string> own, Func<string, string> hashOf )
		{
			var reads = new List<(Node, RecordedPrerequisite)>();
			// Order only if every read of it was
			foreach ( var read in result.Prerequisites.GroupBy( x => x.Source, StringComparer.OrdinalIgnoreCase ).Select( x => x.First() with { OrderOnly = x.All( r => r.OrderOnly ) } ) )
			{
				var producer = Producer( read.Source );
				if ( producer == node ) continue;

				var file = Path.GetFullPath( producer is not null ? producer.Root.Resolve( producer.Output ) : read.Compiled ?? read.Source + "_c" );
				if ( producer is null && node.Root.Key( file ) is { } key && own.Contains( key ) ) continue;

				reads.Add( (producer, new RecordedPrerequisite( file, hashOf( file ), ReadOutput( producer, file ), read.OrderOnly )) );
			}
			return reads;
		}

		/// <summary>
		/// Why a compile's reads can't be trusted: what it read wasn't what the producer publishes, but a same-named
		/// file from another mount. Null if they can.
		/// </summary>
		string Misread( Node node, CompileResult result )
		{
			var shadowed = result.Prerequisites.FirstOrDefault( x => x.Compiled is not null && Producer( x.Source ) is { } p && p != node && !string.Equals( Path.GetFullPath( x.Compiled ), p.Root.Resolve( p.Output ), StringComparison.OrdinalIgnoreCase ) );
			return shadowed is null ? null : $"read {shadowed.Compiled} instead of what {Producer( shadowed.Source ).Name} builds";
		}

		// Generated children are named after their recipe; GPU compression isn't bit exact, so keep a current owner's copy
		void KeepPublishedGenerated( Node node, CompileResult result, string staging )
		{
			var root = node.Root;
			foreach ( var output in result.Outputs )
			{
				var rel = root.OutputKey( output, staging );
				if ( rel is null || Shared( rel ) || !Path.GetFileName( rel ).Contains( ".generated.", StringComparison.OrdinalIgnoreCase ) ) continue;
				if ( root.Resolve( rel ) is not { } published || !File.Exists( published ) ) continue;

				var hash = Hashing.File( published );
				var current = root.Store.Owners( rel ).Any( owner => !string.Equals( owner, node.Source, StringComparison.OrdinalIgnoreCase )
					&& nodes.TryGetValue( (root, owner), out var other ) && other.State is State.Committed or State.Clean
					&& root.Store.All[owner].Outputs[rel].Hash == hash );
				if ( current ) File.Copy( published, output, true );
			}
		}

		/// <summary>
		/// Checks a successful compile before anything is published: it wrote only where its root's assets
		/// write, produced its primary output, and agrees with other owners of any output.
		/// </summary>
		string Validate( Node node, CompileResult result, string staging )
		{
			var root = node.Root;
			var declared = result.Outputs.Select( Path.GetFullPath ).ToHashSet( StringComparer.OrdinalIgnoreCase );

			var foreign = Directory.EnumerateFiles( staging, "*", SearchOption.AllDirectories ).Where( x => root.OutputKey( x, staging ) is null || !declared.Contains( Path.GetFullPath( x ) ) ).ToList();
			if ( foreign.Count > 0 ) return $"wrote files it doesn't own: {string.Join( ", ", foreign )}";
			if ( !declared.Any( x => string.Equals( root.OutputKey( x, staging ), node.Output, StringComparison.OrdinalIgnoreCase ) ) ) return $"didn't write {node.Output}";

			foreach ( var output in declared )
			{
				var rel = root.OutputKey( output, staging );
				if ( Shared( rel ) ) continue;

				foreach ( var owner in root.Store.Owners( rel ).Where( x => !string.Equals( x, node.Source, StringComparison.OrdinalIgnoreCase ) ) )
				{
					// An owner still to build this run will compare against this; one outside it goes stale and rebuilds next time
					if ( nodes.TryGetValue( (root, owner), out var other ) && (other.State is State.Committed or State.Clean) && root.Store.All[owner].Outputs[rel].Hash != Hashing.File( output ) )
						return $"{rel} is also built by {owner}, with different contents";
				}
			}

			return null;
		}

		/// <summary>
		/// Why a node's own inputs, compiler or settings no longer match its record, or null.
		/// </summary>
		string SelfDirty( Node node )
		{
			var record = node.Record;
			if ( record is null ) return "never built";
			if ( record.ChangedWhileCompiling ) return "an input changed while it compiled";

			foreach ( var input in record.Inputs )
			{
				var file = new FileInfo( input.Path );
				if ( file.Exists != input.Exists ) return $"{input.Path} {(file.Exists ? "appeared" : "is gone")}";
				if ( file.Exists && (file.Length != input.Size || file.LastWriteTimeUtc.Ticks != input.Time) && Hashing.Checksum( file.FullName ) != input.Checksum ) return $"{input.Path} changed";
			}

			foreach ( var dependency in record.Special )
			{
				if ( fingerprints.GetValueOrDefault( dependency with { Fingerprint = 0 } ) != dependency.Fingerprint ) return $"{dependency.Name} changed";
			}

			foreach ( var (name, value) in record.Arguments )
			{
				if ( arguments.GetValueOrDefault( name ) != value ) return $"-{name} changed";
			}

			return null;
		}

		/// <summary>
		/// Why what a node read last time may no longer be what it would read: the output it read now comes
		/// from another root, because a root mounted later shadows it or the root it came from isn't part of
		/// this build (another project's). Null if every read still resolves to the file that was read.
		/// </summary>
		string Shadowed( Node node )
		{
			foreach ( var prerequisite in node.Record?.Prerequisites ?? [] )
			{
				// Older records only have the file: one outside every root here can't be checked, so read again
				var output = prerequisite.Output ?? roots.Select( x => x.InRoot( prerequisite.File ) ).FirstOrDefault( x => x is not null );
				if ( output is null ) return $"{prerequisite.File} is outside every root of this build";
				if ( output.Length == 0 ) continue;

				var resolved = roots.LastOrDefault( x => x.SourceOf.ContainsKey( output ) )?.Resolve( output )
					?? mounts.Reverse().Select( x => Path.GetFullPath( Path.Combine( x.Path, output ) ) ).FirstOrDefault( File.Exists );
				if ( !string.Equals( resolved, Path.GetFullPath( prerequisite.File ), StringComparison.OrdinalIgnoreCase ) ) return $"{output} now resolves to {resolved ?? "nothing"}";
			}
			return null;
		}

		/// <summary>
		/// Why a node must compile, or null if its record still holds.
		/// </summary>
		string Dirty( Node node )
		{
			if ( request.Force ) return "forced";
			if ( (SelfDirty( node ) ?? Shadowed( node )) is { } reason ) return reason;

			foreach ( var (key, stamp) in node.Record.Outputs )
			{
				// An output in a root this build doesn't mount can't be checked or rebuilt here
				if ( node.Root.Resolve( key ) is not { } path ) continue;

				if ( node.Root.Locate( key ) is null ) return $"{key} is missing";
				if ( Shared( key ) ) continue;

				var file = new FileInfo( path );
				if ( (file.Length != stamp.Size || file.LastWriteTimeUtc.Ticks != stamp.Time) && Hashing.File( file.FullName ) != stamp.Hash ) return $"{key} was modified";
			}

			return ReadChanged( node );
		}

		/// <summary>
		/// Why a compiled file a node read is no longer what it read, or null.
		/// </summary>
		string ReadChanged( Node node )
		{
			foreach ( var prerequisite in node.Record.Prerequisites )
			{
				var file = new FileInfo( prerequisite.File );
				if ( prerequisite.OrderOnly )
				{
					if ( file.Exists != (prerequisite.Hash != "0") ) return $"{prerequisite.File} {(file.Exists ? "appeared" : "is gone")}";
					continue;
				}

				var producer = ProducerOfOutput( prerequisite.File );
				var stamp = producer?.Record?.Outputs.GetValueOrDefault( producer.Output );
				// The producer already records these bytes. Reuse that hash only while the file still matches
				// its stamp; external edits and reads without a producer still check the actual contents.
				var current = !file.Exists ? "0" : stamp is not null && file.Length == stamp.Size && file.LastWriteTimeUtc.Ticks == stamp.Time
					? stamp.Hash : Hashing.File( prerequisite.File );
				if ( current != prerequisite.Hash ) return $"{prerequisite.File} changed";
			}

			return null;
		}

		/// <summary>
		/// Size and time to skip hashing next time, if the file is still what the compile saw.
		/// </summary>
		static InputStamp StampInput( InputFact input )
		{
			var file = new FileInfo( input.Path );
			var current = file.Exists && input.Exists && Hashing.Checksum( file.FullName ) == input.Checksum;
			return new InputStamp( input.Path, input.Exists, input.Checksum, current ? file.Length : -1, current ? file.LastWriteTimeUtc.Ticks : -1 );
		}

		static string NewJob() => Guid.NewGuid().ToString( "N" );

		/// <summary>
		/// Staging is scratch: something briefly holding a file there (a scanner, a closing process) leaves it
		/// for the next build to clear, rather than failing this one.
		/// </summary>
		static void DeleteStaging( string dir )
		{
			try { if ( Directory.Exists( dir ) ) Directory.Delete( dir, true ); }
			catch ( IOException ) { }
			catch ( UnauthorizedAccessException ) { }
		}

		sealed class NodeKey : IEqualityComparer<(RootState Root, string Source)>
		{
			public bool Equals( (RootState Root, string Source) a, (RootState Root, string Source) b ) => a.Root == b.Root && string.Equals( a.Source, b.Source, StringComparison.OrdinalIgnoreCase );
			public int GetHashCode( (RootState Root, string Source) x ) => HashCode.Combine( x.Root, StringComparer.OrdinalIgnoreCase.GetHashCode( x.Source ) );
		}
	}

	/// <summary>
	/// "-name value" pairs; a bare "-flag" has an empty value.
	/// </summary>
	static Dictionary<string, string> ParseArguments( IReadOnlyList<string> args )
	{
		var parsed = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		for ( var i = 0; i < args.Count; i++ )
		{
			if ( !args[i].StartsWith( '-' ) ) continue;
			var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith( '-' );
			parsed[args[i][1..]] = hasValue ? args[++i] : "";
		}
		return parsed;
	}
}
