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

			// The pool starts its worker as a program with arguments of its own
			string launcher;
			if ( OperatingSystem.IsWindows() )
			{
				launcher = Path.Combine( directory, "worker.cmd" );
				File.WriteAllText( launcher, $"@\"{Dotnet}\" \"{assembly}\" %*" );
			}
			else
			{
				launcher = Path.Combine( directory, "worker.sh" );
				File.WriteAllText( launcher, $"#!/bin/sh\nexec \"{Dotnet}\" \"{assembly}\" \"$@\"\n" );
				File.SetUnixFileMode( launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
			}

			var types = Path.Combine( directory, "assettypes.txt" );
			File.WriteAllText( types, "" );
			var compiler = new ResourceCompiler( launcher, new AssetTypeTable( types ), [], null );
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
