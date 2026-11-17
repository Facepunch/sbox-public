using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.ContentBuild;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ContentBuild;

[TestClass]
public class CompilerWorkerTests
{
	static string Dotnet => Environment.GetEnvironmentVariable( "DOTNET_HOST_PATH" ) ?? "dotnet";

	/// <summary>
	/// A console program compiled from source, to run with dotnet.
	/// </summary>
	static string Emit( string directory, string source )
	{
		var references = ((string)AppContext.GetData( "TRUSTED_PLATFORM_ASSEMBLIES" )).Split( Path.PathSeparator )
			.Select( x => MetadataReference.CreateFromFile( x ) );
		var compilation = CSharpCompilation.Create( "worker", [CSharpSyntaxTree.ParseText( source )], references, new CSharpCompilationOptions( OutputKind.ConsoleApplication ) );
		var assembly = Path.Combine( directory, "worker.dll" );
		var emitted = compilation.Emit( assembly );
		Assert.IsTrue( emitted.Success, string.Join( "\n", emitted.Diagnostics ) );
		File.WriteAllText( Path.Combine( directory, "worker.runtimeconfig.json" ), System.Text.Json.JsonSerializer.Serialize( new
		{
			runtimeOptions = new { tfm = $"net{Environment.Version.Major}.0", framework = new { name = "Microsoft.NETCore.App", version = $"{Environment.Version.Major}.{Environment.Version.Minor}.0" } }
		} ) );
		return assembly;
	}

	/// <summary>
	/// The pool starts its worker as a program with arguments of its own.
	/// </summary>
	static string Launcher( string directory, string assembly )
	{
		if ( OperatingSystem.IsWindows() )
		{
			var cmd = Path.Combine( directory, "worker.cmd" );
			File.WriteAllText( cmd, $"@\"{Dotnet}\" \"{assembly}\" %*" );
			return cmd;
		}

		var sh = Path.Combine( directory, "worker.sh" );
		File.WriteAllText( sh, $"#!/bin/sh\nexec \"{Dotnet}\" \"{assembly}\" \"$@\"\n" );
		File.SetUnixFileMode( sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
		return sh;
	}

	/// <summary>
	/// A worker that answers each job right away, or exits during it if its source has a ".crash" file beside it.
	/// </summary>
	const string CrashingWorker = """
		using System;
		using System.IO;
		Console.WriteLine("\u0001ready");
		while (Console.ReadLine() is {} line)
		{
			var fields = line.Split('\t');
			if (File.Exists(fields[2] + ".crash")) Environment.Exit(1);
			File.WriteAllLines(fields[4], ["buildreport\t1", "result\t1"]);
			Console.WriteLine($"\u0001done\t{fields[0]}\tsucceeded");
		}
		""";

	/// <summary>
	/// The editor's builds, with a worker and this process both. At startup, questions about the compilers are answered
	/// here without starting a worker, and compiles run in one. A source that lost every worker it ran on is lost, and the
	/// next build's compiler, which shares that, compiles it in a worker too, everything else here, until it compiles.
	/// </summary>
	[TestMethod]
	public void LostSourcesCompileInAWorkerUntilOneSucceeds()
	{
		var directory = Directory.CreateTempSubdirectory( "worker-lost-test" ).FullName;
		try
		{
			var assembly = Emit( directory, CrashingWorker );
			var types = Path.Combine( directory, "assettypes.txt" );
			File.WriteAllText( types, "" );

			var lost = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>( StringComparer.OrdinalIgnoreCase );
			var here = new System.Collections.Concurrent.ConcurrentQueue<string>();
			ResourceCompiler Compiler( bool startup ) => new( Launcher( directory, assembly ), new AssetTypeTable( types ), [], null )
			{
				InProcess = ( mode, source, staging, report, args ) =>
				{
					here.Enqueue( mode == "compile" ? Path.GetFileName( source ) : mode );
					File.WriteAllLines( report, mode == "types" ? [".gres\t.gres_c"] : ["buildreport\t1", "result\t1"] );
					return true;
				},
				InWorker = ( mode, source ) => mode == "compile" && (startup || lost.ContainsKey( source )),
				Lost = lost,
			};

			var root = new ContentRoot( "test", directory, directory );
			CompileResult Compile( ResourceCompiler compiler, string name )
			{
				var staging = Directory.CreateDirectory( Path.Combine( directory, $"staging_{Guid.NewGuid():N}" ) ).FullName;
				return compiler.Compile( new CompileJob( root, [], Path.Combine( directory, name ), staging, [] ), default );
			}

			File.WriteAllText( Path.Combine( directory, "crash.gres.crash" ), "" );
			var startup = Compiler( true );
			try
			{
				Assert.AreEqual( AssetKind.Described, startup.Classify( ".gres", out var output ) );
				Assert.AreEqual( ".gres_c", output );
				Assert.AreEqual( 0, startup.Launches );

				Assert.IsTrue( Compile( startup, "a.gres" ).Success );
				Assert.IsFalse( Compile( startup, "crash.gres" ).Success );
			}
			finally { startup.Finished(); }
			CollectionAssert.AreEqual( new[] { "types" }, here.ToArray() );
			CollectionAssert.AreEqual( new[] { Path.Combine( directory, "crash.gres" ) }, lost.Keys.ToArray() );

			var next = Compiler( false );
			try
			{
				Assert.IsFalse( Compile( next, "crash.gres" ).Success );
				Assert.IsTrue( Compile( next, "a.gres" ).Success );
				CollectionAssert.AreEqual( new[] { "types", "a.gres" }, here.ToArray() );
				Assert.AreEqual( 3, next.Launches );

				// Fixed, it compiles in a worker once more, then here
				File.Delete( Path.Combine( directory, "crash.gres.crash" ) );
				Assert.IsTrue( Compile( next, "crash.gres" ).Success );
				Assert.AreEqual( 0, lost.Count );
				Assert.IsTrue( Compile( next, "crash.gres" ).Success );
				CollectionAssert.AreEqual( new[] { "types", "a.gres", "crash.gres" }, here.ToArray() );
			}
			finally { next.Finished(); }
		}
		finally { Directory.Delete( directory, true ); }
	}

	/// <summary>
	/// A worker that exits before it's ready (no render device, compilers that don't load) fails the job, which isn't
	/// lost: the next build doesn't send it to a worker that can't start either.
	/// </summary>
	[TestMethod]
	public void AWorkerThatNeverGetsReadyLosesNoSource()
	{
		var directory = Directory.CreateTempSubdirectory( "worker-unready-test" ).FullName;
		try
		{
			var assembly = Emit( directory, "System.Environment.Exit(1);" );
			var types = Path.Combine( directory, "assettypes.txt" );
			File.WriteAllText( types, "" );
			var compiler = new ResourceCompiler( Launcher( directory, assembly ), new AssetTypeTable( types ), [], null )
			{
				InProcess = ( mode, source, staging, report, args ) => true,
				InWorker = ( mode, source ) => true,
			};
			try
			{
				var root = new ContentRoot( "test", directory, directory );
				var staging = Directory.CreateDirectory( Path.Combine( directory, "staging" ) ).FullName;
				Assert.IsFalse( compiler.Compile( new CompileJob( root, [], Path.Combine( directory, "a.vtex" ), staging, [] ), default ).Success );
				Assert.AreEqual( 0, compiler.Lost.Count );
			}
			finally { compiler.Finished(); }
		}
		finally { Directory.Delete( directory, true ); }
	}

	/// <summary>
	/// The last build's cleanup, run late (the editor stops a build's workers in the background), stops idle workers only:
	/// a job the next build started on one finishes there.
	/// </summary>
	[TestMethod]
	public async Task LateCleanupLeavesTheWorkerOfARunningJob()
	{
		var directory = Directory.CreateTempSubdirectory( "worker-cleanup-test" ).FullName;
		try
		{
			var assembly = Emit( directory, """
				using System;
				using System.IO;
				using System.Threading;
				var output = new object();
				Console.WriteLine("\u0001ready");
				while (Console.ReadLine() is {} line)
				{
					var fields = line.Split('\t');
					new Thread(() =>
					{
						File.WriteAllText(fields[2] + ".waiting", "");
						while (!File.Exists(fields[2] + ".release")) Thread.Sleep(10);
						File.WriteAllLines(fields[4], ["buildreport\t1", "result\t1"]);
						lock (output) Console.WriteLine($"\u0001done\t{fields[0]}\tsucceeded");
					}) { IsBackground = true }.Start();
				}
				""" );
			var types = Path.Combine( directory, "assettypes.txt" );
			File.WriteAllText( types, "" );
			var compiler = new ResourceCompiler( Launcher( directory, assembly ), new AssetTypeTable( types ), [], null );
			try
			{
				var root = new ContentRoot( "test", directory, directory );
				var source = Path.Combine( directory, "a.vtex" );
				var staging = Directory.CreateDirectory( Path.Combine( directory, "staging" ) ).FullName;
				var job = Task.Run( () => compiler.Compile( new CompileJob( root, [], source, staging, [] ), default ) );
				using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 30 ) );
				while ( !File.Exists( $"{source}.waiting" ) ) await Task.Delay( 10, timeout.Token );

				compiler.Finished();
				File.WriteAllText( $"{source}.release", "" );
				var result = await job;
				Assert.IsTrue( result.Success, result.Log );
				Assert.AreEqual( 1, compiler.Launches );
				Assert.AreEqual( 0, compiler.Ended.Count );
			}
			finally { compiler.Finished(); }
		}
		finally { Directory.Delete( directory, true ); }
	}

	/// <summary>
	/// A job that crashes every worker it runs on fails, and the jobs that ran beside it don't: they died with it twice,
	/// so their last run is on a worker of their own.
	/// </summary>
	[TestMethod, TestCategory( "Slow" )]
	public void AJobThatCrashesItsWorkersFailsAlone()
	{
		var directory = Directory.CreateTempSubdirectory( "worker-crash-test" ).FullName;
		try
		{
			// "crash" ends the process soon after it's asked for; the others take a while, so they're still running then
			var assembly = Emit( directory, """
				using System;
				using System.IO;
				using System.Threading;
				var output = new object();
				Console.WriteLine("\u0001ready");
				while (Console.ReadLine() is {} line)
				{
					var fields = line.Split('\t');
					new Thread(() =>
					{
						if (fields[2].EndsWith("crash")) { Thread.Sleep(200); Environment.Exit(1); }
						Thread.Sleep(1000);
						File.WriteAllLines(fields[4], ["buildreport\t1", "result\t1"]);
						lock (output) Console.WriteLine($"\u0001done\t{fields[0]}\tsucceeded");
					}).Start();
				}
				""" );

			var types = Path.Combine( directory, "assettypes.txt" );
			File.WriteAllText( types, "" );
			var compiler = new ResourceCompiler( Launcher( directory, assembly ), new AssetTypeTable( types ), [], null );
			try
			{
				var root = new ContentRoot( "test", directory, directory );
				var jobs = new[] { "crash", "a", "b", "c" }.Select( name => Task.Run( () =>
				{
					var staging = Directory.CreateDirectory( Path.Combine( directory, $"staging_{name}" ) ).FullName;
					return compiler.Compile( new CompileJob( root, [], Path.Combine( directory, name ), staging, [] ), default );
				} ) ).ToArray();
				Task.WaitAll( jobs );

				Assert.IsFalse( jobs[0].Result.Success );
				foreach ( var job in jobs[1..] ) Assert.IsTrue( job.Result.Success, job.Result.Log );
			}
			finally { compiler.Finished(); }
		}
		finally { Directory.Delete( directory, true ); }
	}
	sealed class WorkerFixture : IDisposable
	{
		public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory( "worker-protocol-test" ).FullName;
		public CompilerWorker Worker { get; }
		public string Waiting => Path.Combine( Directory, "waiting" );
		public string Release => Path.Combine( Directory, "release" );

		// A worker that runs each job on a thread of its own, as contentworker does: "first..." waits for the release file
		// (or exits the process), the others answer right away, after a partial line of output
		public WorkerFixture( string end )
		{
			var assembly = Emit( Directory, """
				using System;
				using System.IO;
				using System.Threading;
				var output = new object();
				Console.WriteLine("\u0001ready");
				while (Console.ReadLine() is {} line)
				{
					var fields = line.Split('\t');
					var id = fields[0];
					var name = fields[2];
					new Thread(() =>
					{
						lock (output) Console.WriteLine($"output {name}");
						if (name.StartsWith("first"))
						{
							File.WriteAllText("waiting", "ready");
							if (args[0] == "exit") Environment.Exit(1);
							while (!File.Exists("release")) Thread.Sleep(10);
						}
						lock (output)
						{
							Console.Write($"partial {name}");
							Console.WriteLine($"\u0001done\t{id}\tsucceeded");
						}
					}).Start();
				}
				""" );
			Worker = new CompilerWorker( Dotnet, "test", [assembly, end], Directory );
		}

		public Task<CompilerWorker.Answer> Run( string name, CancellationToken token = default, TimeSpan? timeout = null ) =>
			Task.Run( () => Worker.Run( "compile", name, Directory, "report", [], timeout ?? TimeSpan.FromSeconds( 15 ), token ) );

		public async Task WaitForFirst()
		{
			using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
			while ( !File.Exists( Waiting ) ) await Task.Delay( 10, timeout.Token );
		}

		public void Dispose()
		{
			File.WriteAllText( Release, "release" );
			Worker.Stop();
			System.IO.Directory.Delete( Directory, true );
		}
	}

	[TestMethod]
	public async Task JobsAreAnsweredByIdWhileOthersRun()
	{
		using var fx = new WorkerFixture( "wait" );
		var first = fx.Run( "first" );
		await fx.WaitForFirst();

		var second = await fx.Run( "second" );
		Assert.AreEqual( CompilerWorker.Outcome.Succeeded, second.Outcome, second.Reason );
		Assert.IsFalse( first.IsCompleted, "Another job's answer must not finish this one" );

		File.WriteAllText( fx.Release, "release" );
		var answer = await first;
		Assert.AreEqual( CompilerWorker.Outcome.Succeeded, answer.Outcome, answer.Reason );
	}

	/// <summary>
	/// A job that times out ends the process: the job running beside it is interrupted, which isn't its loss.
	/// </summary>
	[TestMethod]
	public async Task JobsBesideATimedOutJobAreInterrupted()
	{
		using var fx = new WorkerFixture( "wait" );
		var beside = fx.Run( "first beside" );
		await fx.WaitForFirst();

		var timedOut = await fx.Run( "first", timeout: TimeSpan.FromMilliseconds( 500 ) );
		Assert.AreEqual( CompilerWorker.Outcome.TimedOut, timedOut.Outcome, timedOut.Reason );
		var answer = await beside;
		Assert.AreEqual( CompilerWorker.Outcome.Interrupted, answer.Outcome, answer.Reason );
	}

	[TestMethod]
	[DataRow( "exit" )]
	[DataRow( "cancel" )]
	public async Task UnansweredJobsEndWithTheProcess( string end )
	{
		using var fx = new WorkerFixture( end );
		using var cancel = new CancellationTokenSource();
		var job = fx.Run( "first", cancel.Token );
		await fx.WaitForFirst();
		if ( end == "cancel" ) cancel.Cancel();
		var answer = await job;
		Assert.AreEqual( end == "cancel" ? CompilerWorker.Outcome.Cancelled : CompilerWorker.Outcome.Lost, answer.Outcome );
		Assert.AreEqual( end == "cancel" ? "cancelled" : "exited during a job", answer.Reason );
		StringAssert.Contains( answer.Log, "output first" );
	}
}
