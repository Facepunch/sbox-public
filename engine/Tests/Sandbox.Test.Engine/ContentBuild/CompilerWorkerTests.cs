using Sandbox.ContentBuild;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ContentBuild;

/// <summary>
/// Content workers take one job after another; each job must come out as it would from a fresh process.
/// Every test runs a sequence through one long-lived worker and compares each job with the same job run alone in a
/// new process: the same files with the same bytes, and the same reported facts.
/// </summary>
[TestClass, DoNotParallelize]
public class CompilerWorkerTests
{
	internal static ResourceCompiler Compiler( IReadOnlyList<ContentRoot> mounts, string compilers = null ) => new( ManagedBuilder.Worker, ManagedBuilder.Types, Mounts( mounts ), compilers );

	static List<(string Mod, string Path)> Mounts( IReadOnlyList<ContentRoot> roots ) => roots.Select( x => (x.Mod, x.Path) ).ToList();

	static ContentRoot Root( ContentFixture fx ) => new( fx.Mod, fx.Root, Path.Combine( fx.Parent, "state" ) );

	/// <summary>
	/// A shader's version, which every material using it records, asked twice of one worker (as the editor keeps its
	/// workers): the second answer is the shader as it is then, not as the worker first saw it (missing).
	/// </summary>
	[TestMethod, TestCategory( "Slow" )]
	public void ShaderVersionIsReadAgainEachTimeItIsAsked()
	{
		using var fx = new ContentFixture();
		var root = Root( fx );
		var compiler = Compiler( [root] );
		try
		{
			static SpecialDependency Shader( string name ) => new( "CompileMaterial", 0, name, 0 );
			var missing = compiler.Fingerprints( [Shader( "shaders/cbt_fixture.shader" )] )[0];
			var generic = compiler.Fingerprints( [Shader( "shaders/generic.shader" )] )[0];
			Assert.AreNotEqual( missing, generic, "a missing shader has no version of its own" );

			fx.CopyShipped( "core/shaders/generic.shader_c", "shaders/cbt_fixture.shader_c" );
			Assert.AreEqual( generic, compiler.Fingerprints( [Shader( "shaders/cbt_fixture.shader" )] )[0], "the shader compiled since it was first asked about" );
		}
		finally { compiler.Finished(); }
	}

	/// <summary>
	/// A job's outputs (path under its staging, bytes) and facts, with its staging written as a placeholder.
	/// </summary>
	internal sealed record Outcome( bool Success, string Facts, Dictionary<string, byte[]> Files, string Log );

	internal static Outcome Run( ResourceCompiler compiler, ContentRoot root, IReadOnlyList<ContentRoot> mounts, string rel, JobMode mode = JobMode.Compile, CancellationToken token = default )
	{
		var staging = Directory.CreateTempSubdirectory( "sbox-worker-test" ).FullName;
		try
		{
			var result = compiler.Compile( new CompileJob( root, Mounts( mounts ), Path.Combine( root.Path, rel ), staging, [], mode ), token );
			string Clean( string s ) => s.Replace( '\\', '/' ).Replace( staging.Replace( '\\', '/' ), "<staging>", StringComparison.OrdinalIgnoreCase );
			string[] lines =
			[
				.. result.Outputs.Select( x => $"output {Clean( x )}" ).Order(),
				.. result.Inputs.Select( x => $"input {Clean( x.Path )} {x.Exists} {x.Checksum:x8} {x.Game}" ).Order(),
				.. result.Special.Select( x => $"special {x.Compiler} {x.Name} {x.Data} {x.Fingerprint:x8}" ).Order(),
				.. result.Arguments.Select( x => $"arg {x}" ).Order(),
				.. result.Prerequisites.Select( x => $"prereq {Clean( x.Source )} {Clean( x.Compiled ?? "" )}" ).Order(),
			];
			var facts = string.Join( "\n", lines );
			var files = Directory.EnumerateFiles( staging, "*", SearchOption.AllDirectories )
				.ToDictionary( x => Path.GetRelativePath( staging, x ).Replace( '\\', '/' ), File.ReadAllBytes );
			return new Outcome( result.Success, facts, files, result.Log );
		}
		finally
		{
			Directory.Delete( staging, true );
		}
	}

	internal static Outcome Fresh( ContentRoot root, IReadOnlyList<ContentRoot> mounts, string rel, JobMode mode = JobMode.Compile, string compilers = null )
	{
		var compiler = Compiler( mounts, compilers: compilers );
		try { return Run( compiler, root, mounts, rel, mode ); }
		finally { compiler.Finished(); }
	}

	internal static void AssertSame( Outcome fresh, Outcome reused, string what )
	{
		Assert.IsTrue( fresh.Success, $"{what}: {fresh.Log}" );
		Assert.AreEqual( fresh.Success, reused.Success, $"{what}: {reused.Log}" );
		Assert.AreEqual( fresh.Facts, reused.Facts, what );
		CollectionAssert.AreEquivalent( fresh.Files.Keys.ToArray(), reused.Files.Keys.ToArray(), what );
		foreach ( var (file, bytes) in fresh.Files )
			CollectionAssert.AreEqual( bytes, reused.Files[file], $"{what}: {file}" );
	}

	static void Build( ContentFixture fx, ContentRoot root )
	{
		var compiler = Compiler( [root] );
		try
		{
			var report = ContentBuilder.Build( new BuildRequest { Roots = [root], MaxJobs = 4 }, compiler );
			Assert.IsTrue( report.Success, report.Log.ToString() );
		}
		finally { compiler.Finished(); }
	}

	static void WriteMaterial( ContentFixture fx, string material, string image, string shader = "shaders/complex.shader" )
	{
		fx.CopyShipped( Sources.ImageA, image );
		fx.Write( material, Sources.Material( shader, ["TextureColor", image] ) );
	}

	[TestMethod, TestCategory( "Slow" )]
	public void MixedJobsInOneWorkerMatchFreshProcesses()
	{
		using var fx = new ContentFixture();
		var root = Root( fx );
		Sources.WriteTga( fx, "textures/t.tga", 6, 5 );
		fx.Write( "textures/t.vtex", Sources.PaddedTexture( "textures/t.tga" ) );
		WriteMaterial( fx, "materials/a.vmat", "materials/a_color.tga", "shaders/unlit.shader" );
		WriteMaterial( fx, "materials/b.vmat", "materials/b_color.tga" );
		Sources.WriteWav( fx, "sounds/s.wav", 440 );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/b.vmat" ) );
		fx.CopyShipped( $"{Sources.BonnetFolder}/easter_bonnet.fbx", "models/bonnet.fbx" );
		fx.CopyShipped( $"{Sources.BonnetFolder}/easter_bonnet_citizen_idle_anim.fbx", "models/bonnet_idle.fbx" );
		fx.Write( "models/bonnet.vmdl", Sources.Mesh( "models/bonnet.fbx", "materials/a.vmat" ) );
		fx.Write( "models/idle.vmdl", Sources.Animated( "models/bonnet.vmdl", "models/bonnet_idle.fbx" ) );
		Build( fx, root );

		var jobs = new (string Rel, JobMode Mode)[]
		{
			("models/idle.vmdl", JobMode.Compile), ("textures/t.vtex", JobMode.Compile), ("materials/b.vmat", JobMode.Describe),
			("materials/b.vmat", JobMode.Compile), ("sounds/s.wav", JobMode.Compile), ("models/box.vmdl", JobMode.Compile),
			("materials/a.vmat", JobMode.Compile), ("models/bonnet.vmdl", JobMode.Compile), ("models/box.vmdl", JobMode.Adopt),
			("models/idle.vmdl", JobMode.Compile),
		};

		var reused = Compiler( [root] );
		try
		{
			foreach ( var (rel, mode) in jobs )
				AssertSame( Fresh( root, [root], rel, mode ), Run( reused, root, [root], rel, mode ), $"{mode} {rel}" );

			Assert.AreEqual( 1, reused.Launches, string.Join( "; ", reused.Ended ) );
			Assert.AreEqual( 0, reused.Ended.Count, string.Join( "; ", reused.Ended ) );
		}
		finally { reused.Finished(); }
	}

	[TestMethod, TestCategory( "Slow" )]
	public void ReadsRebuiltBetweenJobsAreReadAgain()
	{
		// The animation reads its base model's compiled skeleton through the resident resource cache, and the model reads
		// its material order only; both are rebuilt and published by another process between two jobs of the same worker
		using var fx = new ContentFixture();
		var root = Root( fx );
		WriteMaterial( fx, "materials/a.vmat", "materials/a_color.tga" );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/a.vmat" ) );
		fx.CopyShipped( $"{Sources.BonnetFolder}/easter_bonnet.fbx", "models/bonnet.fbx" );
		fx.CopyShipped( $"{Sources.BonnetFolder}/easter_bonnet_citizen_idle_anim.fbx", "models/bonnet_idle.fbx" );
		fx.Write( "models/bonnet.vmdl", Sources.Mesh( "models/bonnet.fbx", "materials/a.vmat" ) );
		fx.Write( "models/idle.vmdl", Sources.Animated( "models/bonnet.vmdl", "models/bonnet_idle.fbx" ) );
		Build( fx, root );

		var reused = Compiler( [root] );
		try
		{
			Run( reused, root, [root], "models/idle.vmdl" );
			Assert.IsTrue( Run( reused, root, [root], "models/box.vmdl", JobMode.Adopt ).Success, "current output adopted" );

			fx.Write( "models/bonnet.vmdl", Sources.Mesh( "models/bonnet.fbx", "materials/default.vmat" ) );
			fx.Write( "materials/a.vmat", Sources.Material( "shaders/unlit.shader", ["TextureColor", "materials/a_color.tga"] ) );
			Build( fx, root );

			AssertSame( Fresh( root, [root], "models/idle.vmdl" ), Run( reused, root, [root], "models/idle.vmdl" ), "animation after its base model changed" );
			AssertSame( Fresh( root, [root], "models/box.vmdl" ), Run( reused, root, [root], "models/box.vmdl" ), "model after its material changed" );

			// Nothing it read stayed loaded, so the worker never had to stop
			Assert.AreEqual( 1, reused.Launches );
			Assert.AreEqual( 0, reused.Ended.Count, string.Join( "; ", reused.Ended ) );
		}
		finally { reused.Finished(); }
	}

	[TestMethod, TestCategory( "Slow" )]
	public void OldFormatModelDescribedThenCompiledMatchesFresh()
	{
		// Describing it compiles it, children included, into staging; the compile after it must not see those
		using var fx = new ContentFixture();
		var root = Root( fx );
		fx.CopyShipped( "core/models/editor/scripted_sequence.vmdl", "models/legacy.vmdl" );
		WriteMaterial( fx, "materials/models/editor/scripted_sequence.vmat", "materials/legacy_color.tga" );
		Build( fx, root );

		var reused = Compiler( [root] );
		try
		{
			AssertSame( Fresh( root, [root], "models/legacy.vmdl", JobMode.Describe ), Run( reused, root, [root], "models/legacy.vmdl", JobMode.Describe ), "describe" );
			AssertSame( Fresh( root, [root], "models/legacy.vmdl" ), Run( reused, root, [root], "models/legacy.vmdl" ), "compile after describe" );

			// Its collision takes the material's surface property, which a model compile reads once per material name
			fx.Write( "materials/models/editor/scripted_sequence.vmat", Sources.Material( "shaders/complex.shader", ["TextureColor", "materials/legacy_color.tga", "PhysicsSurfaceProperties", "wood"] ) );
			Build( fx, root );
			AssertSame( Fresh( root, [root], "models/legacy.vmdl" ), Run( reused, root, [root], "models/legacy.vmdl" ), "compile after the material's surface changed" );
			Assert.AreEqual( 1, reused.Launches );
		}
		finally { reused.Finished(); }
	}

	[TestMethod, TestCategory( "Slow" )]
	public void ModelsWithMorphsInOneWorkerMatchFreshProcesses()
	{
		// Flex controller names are interned case-insensitively for one model compile, shared by its morph sets: one
		// model's names never take another's casing, in whatever order a worker compiles them
		var citizen = ContentRoots.Engine( ContentFixture.Engine );
		var root = citizen.Single( x => x.Mod == "citizen" );
		const string a = "models/citizen_clothes/hair/hair_tight_ponytail/Models/hair_tight_ponytail_brown.vmdl";
		const string b = "models/citizen_clothes/hat/Beanie/Models/beanie.vmdl";

		var reused = Compiler( citizen );
		try
		{
			var freshA = Fresh( root, citizen, a );
			AssertSame( freshA, Run( reused, root, citizen, a ), "first model with morphs" );
			AssertSame( Fresh( root, citizen, b ), Run( reused, root, citizen, b ), "second model with morphs" );
			AssertSame( freshA, Run( reused, root, citizen, a ), "first model again" );
			Assert.AreEqual( 1, reused.Launches );
		}
		finally { reused.Finished(); }
	}

	[TestMethod, TestCategory( "Slow" )]
	public void LostWorkerIsReplacedAndItsJobRunsAgain()
	{
		using var fx = new ContentFixture();
		var root = Root( fx );
		Sources.WriteWav( fx, "sounds/a.wav", 440 );
		Sources.WriteWav( fx, "sounds/b.wav", 550 );

		var reused = Compiler( [root] );
		try
		{
			Run( reused, root, [root], "sounds/a.wav" );
			foreach ( var id in reused.IdleProcessIds ) Process.GetProcessById( id ).Kill();

			AssertSame( Fresh( root, [root], "sounds/b.wav" ), Run( reused, root, [root], "sounds/b.wav" ), "after its worker died" );
			Assert.AreEqual( 2, reused.Launches );
			Assert.AreEqual( 1, reused.Ended.Count, string.Join( "; ", reused.Ended ) );
		}
		finally { reused.Finished(); }
	}

	[TestMethod, TestCategory( "Slow" )]
	public void TimedOutOrCancelledJobFailsAloneAndTheNextGetsANewWorker()
	{
		using var fx = new ContentFixture();
		var root = Root( fx );
		Sources.WriteWav( fx, "sounds/a.wav", 440 );

		var reused = new ResourceCompiler( ManagedBuilder.Worker, ManagedBuilder.Types, Mounts( [root] ), null ) { Timeout = TimeSpan.FromMilliseconds( 1 ) };
		try
		{
			var timedOut = Run( reused, root, [root], "sounds/a.wav" );
			Assert.IsFalse( timedOut.Success );
			StringAssert.StartsWith( reused.Ended.Single(), "timed out" );
		}
		finally { reused.Finished(); }

		var cancelled = Compiler( [root] );
		try
		{
			using var cancel = new CancellationTokenSource();
			cancel.Cancel();
			Assert.IsFalse( Run( cancelled, root, [root], "sounds/a.wav", token: cancel.Token ).Success );
			Assert.IsTrue( Run( cancelled, root, [root], "sounds/a.wav" ).Success );
		}
		finally { cancelled.Finished(); }
	}

	[TestMethod]
	public void WorkersThatCantStartFailTheirJobs()
	{
		using var fx = new ContentFixture();
		var root = Root( fx );
		Sources.WriteWav( fx, "sounds/a.wav", 440 );

		var missing = new ResourceCompiler( Path.Combine( fx.Parent, "missing.exe" ), ManagedBuilder.Types, Mounts( [root] ), null );
		var outcomes = new System.Collections.Concurrent.ConcurrentBag<bool>();
		Parallel.For( 0, 6, _ =>
		{
			try { outcomes.Add( Run( missing, root, [root], "sounds/a.wav" ).Success ); }
			catch ( System.ComponentModel.Win32Exception ) { outcomes.Add( false ); }
		} );

		Assert.AreEqual( 6, outcomes.Count );
		Assert.IsTrue( outcomes.All( x => !x ) );
		missing.Finished();
	}

	[TestMethod, TestCategory( "Slow" )]
	public void WorkerWithoutARenderDeviceCompilesWithTheEmptyRenderSystem()
	{
		// As on a build machine with no GPU: not a device that failed to start, the empty render system, which the
		// worker was asked for. Native compiles need nothing more.
		using var fx = new ContentFixture();
		var root = Root( fx );
		Sources.WriteTga( fx, "textures/t.tga", 8, 8 );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );
		Sources.WriteWav( fx, "sounds/a.wav", 440 );

		var compiler = Compiler( [root] );
		try
		{
			Assert.IsTrue( Run( compiler, root, [root], "textures/t.vtex" ).Success );
			Assert.IsTrue( Run( compiler, root, [root], "sounds/a.wav" ).Success );

			using var worker = Process.GetProcessById( compiler.IdleProcessIds.Single() );
			var modules = worker.Modules.Cast<ProcessModule>().Select( x => x.ModuleName.ToLowerInvariant() ).ToList();
			Assert.IsTrue( modules.Any( x => x.StartsWith( "rendersystemempty" ) ), string.Join( ", ", modules ) );
			Assert.IsFalse( modules.Any( x => x.StartsWith( "rendersystemvulkan" ) ), string.Join( ", ", modules ) );
		}
		finally { compiler.Finished(); }
	}

	[TestMethod, TestCategory( "Slow" )]
	public void RootsOfABuildShareOneWorker()
	{
		// One worker in all: both roots' jobs go to it, as a fresh process of their own root would build them, from six
		// threads at once; a job with other mounts (the roots the other way round, as another project mounts them) gets
		// a worker of its own, and the idle one goes
		using var fx = new ContentFixture();
		var a = Root( fx );
		var b = new ContentRoot( fx.Mod + "b", Path.Combine( fx.Parent, "b" ), Path.Combine( fx.Parent, "bstate" ) );
		Sources.WriteWav( fx, "sounds/a.wav", 440 );
		Directory.CreateDirectory( Path.Combine( b.Path, "sounds" ) );
		File.Copy( fx.Path( "sounds/a.wav" ), Path.Combine( b.Path, "sounds/b.wav" ) );
		Directory.CreateDirectory( Path.Combine( b.Path, "materials" ) );
		Directory.CreateDirectory( Path.Combine( b.Path, "models" ) );
		File.Copy( Path.Combine( ContentFixture.Engine, Sources.ImageA ), Path.Combine( b.Path, "materials/m_color.tga" ) );
		File.WriteAllText( Path.Combine( b.Path, "materials/m.vmat" ), Sources.Material( "shaders/complex.shader", ["TextureColor", "materials/m_color.tga"] ) );
		File.WriteAllText( Path.Combine( b.Path, "models/box.vmdl" ), Sources.Box( "materials/m.vmat" ) );

		var reused = Compiler( [a, b] );
		try
		{
			var results = new System.Collections.Concurrent.ConcurrentBag<Outcome>();
			Parallel.For( 0, 6, i => results.Add( i % 2 == 0 ? Run( reused, a, [a, b], "sounds/a.wav" ) : Run( reused, b, [a, b], "sounds/b.wav" ) ) );

			Assert.IsTrue( results.All( x => x.Success ), string.Join( "\n---\n", results.Where( x => !x.Success ).Select( x => x.Log ) ) );
			Assert.AreEqual( 1, reused.Launches );
			AssertSame( Fresh( b, [a, b], "sounds/b.wav" ), Run( reused, b, [a, b], "sounds/b.wav" ), "another root's job in a shared worker" );
			AssertSame( Fresh( b, [a, b], "materials/m.vmat" ), Run( reused, b, [a, b], "materials/m.vmat" ), "another root's material in a shared worker" );
			AssertSame( Fresh( b, [a, b], "models/box.vmdl" ), Run( reused, b, [a, b], "models/box.vmdl" ), "another root's model in a shared worker" );
			Assert.AreEqual( 1, reused.Launches );
			Assert.AreEqual( 0, reused.Ended.Count, string.Join( "; ", reused.Ended ) );

			// The same name in both roots, mounted the other way round
			fx.CopyShipped( Sources.ImageB, "materials/m_color.tga" );
			AssertSame( Fresh( b, [b, a], "materials/m.vmat" ), Run( reused, b, [b, a], "materials/m.vmat" ), "a job with the roots the other way round" );
			Assert.AreEqual( 2, reused.Launches );
			Assert.AreEqual( 1, reused.Evicted );
		}
		finally { reused.Finished(); }
	}
}
