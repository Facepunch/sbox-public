using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Sandbox.ContentBuild;

namespace ContentBuild;

/// <summary>
/// Sandbox.ContentBuild with content workers, its records and staging under the fixture's parent.
/// Several jobs at once by default, so ordering mistakes show up as races; or one at a time.
/// </summary>
public sealed class ManagedBuilder : IContentBuilder
{
	public string Name => MaxJobs == 1 ? "managed-inline" : "managed";
	public int MaxJobs { get; init; } = 4;

	internal Action<string> BeforeCommit { get; init; }

	internal static readonly AssetTypeTable Types = new( Path.Combine( ContentFixture.Engine, "bin", "assettypes.txt" ) );

	internal static string Worker => Path.Combine( ContentFixture.Engine, "bin", "managed", "contentworker.exe" );

	/// <summary>
	/// A build with workers of its own, stopped when it's done. They have no render device, as on a build machine.
	/// </summary>
	internal static BuildReport Build( BuildRequest request, string compilers = null, CancellationToken token = default )
	{
		var compiler = new ResourceCompiler( Worker, Types, request.Mounts, compilers )
		{
			MaxWorkers = Math.Max( 1, request.MaxJobs ),
		};
		try { return ContentBuilder.Build( request, compiler, token ); }
		finally { compiler.Finished(); }
	}

	static string State( ContentFixture fixture ) => Path.Combine( fixture.Parent, "state" );

	public BuildResult Build( ContentFixture fixture, BuildOptions options, CancellationToken token )
	{
		var request = new BuildRequest
		{
			Roots = [new ContentRoot( fixture.Mod, fixture.Root, State( fixture ) )],

			// Where game resources generate their textures, as SboxBuild's builds and the editor's have one
			OutputRoots = new Dictionary<string, string> { ["transient"] = Path.Combine( fixture.Parent, "transient" ) },
			Select = options.Select?.Select( fixture.Path ).ToList(),
			Force = options.Force,
			CompilerArgs = options.CompilerArgs,
			MaxJobs = MaxJobs,
			JobFinished = options.JobFinished,
			BeforeCommit = BeforeCommit,
		};

		// Incremental checks share workers, as successive editor builds do. Each fixture owns its pool.
		fixture.Compiler ??= new ResourceCompiler( Worker, Types, request.Mounts, null ) { MaxWorkers = Math.Max( 1, MaxJobs ) };
		var report = ContentBuilder.Build( request, fixture.Compiler, token );

		var failed = new List<string>( report.Failed );
		failed.AddRange( report.Blocked );
		failed.AddRange( report.Unsupported );
		return new BuildResult( report.Success, failed, report.Jobs, report.Log.ToString(), report.DescribeMisses );
	}

	public void Clean( ContentFixture fixture )
	{
		// Clean-build comparisons also start with fresh compiler processes.
		fixture.Compiler?.Finished();
		fixture.Compiler = null;
		if ( Directory.Exists( State( fixture ) ) ) Directory.Delete( State( fixture ), true );
	}
}
