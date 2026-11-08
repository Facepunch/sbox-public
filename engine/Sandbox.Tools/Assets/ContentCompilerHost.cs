using NativeEngine;
using Sandbox.Engine;
using System.Reflection;

namespace Editor;

/// <summary>
/// The editor's resource compilers in a content build worker: game resources and managed compilers compile there
/// the way they do in the editor, through the same type libraries and compile context, without its windows or its
/// asset registry (a compile's references are found in the mounts, see ResourceCompileContextImp.ScanJson).
/// </summary>
internal static class ContentCompilerHost
{
	/// <summary>
	/// Before the native asset system loads: the resource compiler reaches managed compilers through it.
	/// </summary>
	internal static void InitializeInterop()
	{
		Managed.SourceAssetSytem.NativeInterop.Initialize();
	}

	/// <summary>
	/// The type libraries with the compiled tool and project assemblies the build's compilers and game resource types
	/// come from, and the mounts compiles read.
	/// </summary>
	internal static void InitializeCompilers( IReadOnlyList<Assembly> assemblies, IEnumerable<(string Mod, string Path)> mounts )
	{
		// The build's roots are the content, where the editor has its project's. Generated resources (an embedded
		// texture) go to the build's "transient" output root, mounted under the roots, where the editor has its project's
		// transient folder. Everything is mounted, as the editor mounts it for a compile to read: the engine's core
		// content first, under everything else, as the resource compiler has it.
		var content = new AggregateFileSystem();
		var mounted = new AggregateFileSystem();
		content.Mount( EngineFileSystem.CoreContent );
		mounted.Mount( EngineFileSystem.CoreContent );
		foreach ( var (mod, path) in mounts )
		{
			var folder = new LocalFileSystem( path );
			if ( mod == "transient" ) FileSystem.Transient = folder;
			else content.Mount( folder );
			mounted.Mount( folder );
		}
		FileSystem.Content = content;
		GlobalContext.Current.FileMount = mounted;

		ToolsDll.CreateEditorTypeLibrary();

		// Compiled resources a compile loads are made with the game's type library, as in the editor
		Game.TypeLibrary = new Sandbox.Internal.TypeLibrary();
		Game.TypeLibrary.AddIntrinsicTypes();
		Game.TypeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( EngineLoop ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( Facepunch.ActionGraphs.ActionGraph ).Assembly, false );

		foreach ( var assembly in assemblies )
		{
			EditorTypeLibrary.AddAssembly( assembly, true );
			Game.TypeLibrary.AddAssembly( assembly, true );
		}
	}

	/// <summary>
	/// Says it's ready, then answers jobs from standard input until it ends, one per line:
	/// <c>id \t compile|describe|adopt|fingerprints|types \t source \t staging \t report</c>. Once the job's report is
	/// written, one line answers it: <c>\x01done \t id \t result 1|0 \t takes another 1|0 [\t why not]</c>. Everything
	/// else printed before it is the job's log. Stderr ends the job with <c>\x01done \t id</c> too, so the parent
	/// can drain both pipes before reusing the worker.
	/// </summary>
	internal static int Serve()
	{
		static void Reply( string id, string result )
		{
			System.Console.Error.Write( $"\u0001done\t{id}\n" );
			System.Console.Out.Write( $"\u0001done\t{id}\t{result}\n" );
		}

		System.Console.Out.Write( "\u0001ready\n" );

		while ( System.Console.In.ReadLine() is { } line )
		{
			var fields = line.Split( '\t' );
			if ( fields.Length != 5 )
			{
				Reply( "?", "0\t0\tmalformed request" );
				return 1;
			}

			var (id, mode, source, staging, report) = (fields[0], fields[1], fields[2], fields[3], fields[4]);
			if ( mode is "types" or "fingerprints" )
			{
				System.IO.File.WriteAllLines( report, Answer( mode, System.IO.File.ReadAllLines( source ) ) );
				Reply( id, "1\t1" );
				continue;
			}

			var ok = IResourceCompilerSystem.RunBuildJob( mode, source, staging, report ) ? 1 : 0;

			// Something it read is still held, so the next job could see that as it was: this worker stops here
			var held = IResourceCompilerSystem.GetLoadedAfterBuildJob();
			if ( !string.IsNullOrEmpty( held ) )
			{
				Reply( id, $"{ok}\t0\t{held} is still loaded after the job" );
				return 0;
			}

			Reply( id, $"{ok}\t1" );
		}
		return 0;
	}

	/// <summary>
	/// A question about the compilers rather than a compile, of a worker or of the editor, which has the same ones:
	/// "types", each source type managed code takes part in (see <see cref="AssetSystem.ManagedCompilerTypes"/>), or
	/// "fingerprints" of the special dependencies given.
	/// </summary>
	internal static IReadOnlyList<string> Answer( string mode, IEnumerable<string> input ) => mode == "types" ? AssetSystem.ManagedCompilerTypes().ToList() : input.Select( Fingerprint ).ToList();

	/// <summary>
	/// "compiler \t userdata \t name" to its current fingerprint in hex, or "-" if it can't be calculated here.
	/// </summary>
	static string Fingerprint( string line )
	{
		var parts = line.Split( '\t' );
		var fingerprint = parts.Length == 3 && uint.TryParse( parts[1], out var data ) ? IResourceCompilerSystem.SpecialDependencyFingerprint( parts[0], parts[2], data ) : -1;
		return fingerprint < 0 ? "-" : ((uint)fingerprint).ToString( "x8" );
	}

	/// <summary>
	/// Compiles one asset where it is, as the editor's asset system does when it compiles one by itself.
	/// </summary>
	internal static bool Compile( string path ) => IResourceCompilerSystem.CompileFile( path );
}
