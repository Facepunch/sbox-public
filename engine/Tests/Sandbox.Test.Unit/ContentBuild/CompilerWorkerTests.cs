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
	sealed class WorkerFixture : IDisposable
	{
		public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory( "worker-protocol-test" ).FullName;
		public CompilerWorker Worker { get; }
		public string Waiting => Path.Combine( Directory, "waiting" );
		public string Release => Path.Combine( Directory, "release" );

		public WorkerFixture( string order )
		{
			var references = ((string)AppContext.GetData( "TRUSTED_PLATFORM_ASSEMBLIES" )).Split( Path.PathSeparator )
				.Select( x => MetadataReference.CreateFromFile( x ) );
			var compilation = CSharpCompilation.Create( "worker", [CSharpSyntaxTree.ParseText( """
				using System;
				using System.IO;
				using System.Threading;
				Console.WriteLine("\u0001ready");
				while (Console.ReadLine() is {} line)
				{
					var fields = line.Split('\t');
					var id = fields[0];
					var name = fields[2];
					var stdoutFirst = args[0] != "stderr-first";
					void Stdout() { Console.WriteLine($"output {name}"); Console.WriteLine($"\u0001done\t{id}\t1\t1"); }
					void Stderr() { Console.Error.WriteLine($"error {name}"); Console.Error.WriteLine($"\u0001done\t{id}"); }
					if (stdoutFirst) Stdout(); else Stderr();
					if (name == "first")
					{
						File.WriteAllText("waiting", "ready");
						if (args[0] == "exit") return;
						while (!File.Exists("release")) Thread.Sleep(10);
					}
					if (stdoutFirst) Stderr(); else Stdout();
				}
				""" )], references, new CSharpCompilationOptions( OutputKind.ConsoleApplication ) );
			var assembly = Path.Combine( Directory, "worker.dll" );
			var emitted = compilation.Emit( assembly );
			Assert.IsTrue( emitted.Success, string.Join( "\n", emitted.Diagnostics ) );
			File.WriteAllText( Path.Combine( Directory, "worker.runtimeconfig.json" ), System.Text.Json.JsonSerializer.Serialize( new
			{
				runtimeOptions = new { tfm = $"net{Environment.Version.Major}.0", framework = new { name = "Microsoft.NETCore.App", version = $"{Environment.Version.Major}.{Environment.Version.Minor}.0" } }
			} ) );
			Worker = new CompilerWorker( Environment.GetEnvironmentVariable( "DOTNET_HOST_PATH" ) ?? "dotnet", "test", [assembly, order], Directory );
		}

		public Task<CompilerWorker.Answer> Run( string name, CancellationToken token = default ) =>
			Task.Run( () => Worker.Run( "compile", name, Directory, "report", TimeSpan.FromSeconds( 15 ), token ) );

		public async Task WaitForFirstPipe()
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
	[DataRow( "stdout-first" )]
	[DataRow( "stderr-first" )]
	public async Task BothPipesFinishBeforeTheWorkerIsReused( string order )
	{
		using var fx = new WorkerFixture( order );
		var first = fx.Run( "first" );
		await fx.WaitForFirstPipe();
		Assert.AreNotSame( first, await Task.WhenAny( first, Task.Delay( 100 ) ), "One pipe's completion must not finish the job" );
		File.WriteAllText( fx.Release, "release" );
		var answer = await first;
		Assert.IsTrue( answer.Answered, answer.Reason );
		StringAssert.Contains( answer.Log, "error first" );
		StringAssert.Contains( answer.Log, "output first" );
		var second = await fx.Run( "second" );
		Assert.IsTrue( second.Answered, second.Reason );
		StringAssert.Contains( second.Log, "error second" );
		Assert.IsFalse( second.Log.Contains( "first" ), second.Log );
	}

	[TestMethod]
	[DataRow( "exit" )]
	[DataRow( "cancel" )]
	public async Task IncompleteDiagnosticsCannotReportSuccess( string end )
	{
		using var fx = new WorkerFixture( end );
		using var cancel = new CancellationTokenSource();
		var job = fx.Run( "first", cancel.Token );
		await fx.WaitForFirstPipe();
		if ( end == "cancel" ) cancel.Cancel();
		var answer = await job;
		Assert.IsFalse( answer.Answered );
		Assert.AreEqual( end == "cancel" ? "cancelled" : "exited during a job", answer.Reason );
		StringAssert.Contains( answer.Log, "output first" );
	}
}
