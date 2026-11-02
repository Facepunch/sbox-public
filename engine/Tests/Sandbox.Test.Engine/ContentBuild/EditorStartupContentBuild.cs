using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sandbox.ContentBuild;

namespace ContentBuild;

/// <summary>
/// Editor startup builds every mounted root with the content builder before the project opens, so nothing it
/// built or kept compiles on demand when loaded afterwards (an asset that failed still can, as before).
/// Opt-in, because it opens real editors through sbox-cli on a throwaway project: set SBOX_EDITOR_TESTS=1.
/// Engine roots are built too, so run SboxBuild build-content first or the first open compiles what engine
/// content needs.
/// </summary>
[TestClass, DoNotParallelize]
public class EditorStartupContentBuild
{
	const string Blood = "core/decals/blood/blood_splatter_01.decal";
	const string BloodColor = "decals/blood/decal_blood_splatter_01_color.png";

	[TestMethod, TestCategory( "Slow" )]
	public void ProjectBuildReadsShippedMaterialWithoutItsSourcesOrBuildRecords()
	{
		using var shipped = new ContentFixture();
		shipped.CopyShipped( Sources.ImageA, "materials/color.tga" );
		shipped.Write( "materials/shipped.vmat", Sources.Material( "TextureColor", "materials/color.tga" ) );
		Assert.IsTrue( shipped.Build( new ManagedBuilder() ).Result.Success );
		shipped.Delete( "materials/color.tga" );
		shipped.Delete( "materials/shipped.vmat" );
		Directory.Delete( Path.Combine( shipped.Parent, "state" ), true );
		shipped.Write( "materials/unused.vmat", "invalid leftover source" );
		var before = shipped.Outputs();

		using var project = new ContentFixture();
		project.Write( "models/box.vmdl", Sources.Box( "materials/shipped.vmat" ) );
		var request = new BuildRequest
		{
			Roots = [new ContentRoot( project.Mod, project.Root, Path.Combine( project.Parent, "state" ) )],
			ReadOnlyMounts = [(shipped.Mod, shipped.Root)],
			MaxJobs = 1
		};
		var cold = ManagedBuilder.Build( request );
		Assert.IsTrue( cold.Success, cold.Error + cold.Log.ToString() );
		CollectionAssert.AreEqual( new[] { "models/box.vmdl" }, cold.Jobs.ToArray() );
		Assert.AreEqual( 0, cold.Adopted.Count );
		var warm = ManagedBuilder.Build( request );
		Assert.IsTrue( warm.Success, warm.Error + warm.Log.ToString() );
		Assert.AreEqual( 0, warm.Jobs.Count );
		Assert.IsFalse( Directory.Exists( Path.Combine( shipped.Parent, "state" ) ) );
		CollectionAssert.AreEquivalent( before.ToArray(), shipped.Outputs().ToArray() );
	}

	[TestMethod, TestCategory( "Slow" )]
	[DataRow( false )]
	[DataRow( true )]
	public void EditorContinuesRunningWhileAnIncrementalCompilerWaits( bool closeWhileCompiling )
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_EDITOR_TESTS" ) is null )
			Assert.Inconclusive( "Opens an editor: set SBOX_EDITOR_TESTS=1 to run" );

		using var project = new EditorProject();
		var entered = Path.Combine( project.Dir, "entered" );
		var release = Path.Combine( project.Dir, "release" );
		var timedOut = Path.Combine( project.Dir, "timed-out" );
		var output = Path.Combine( project.Assets, "graphs", "a.lvg_c" );
		var code = LiveGraphSource.Replace( "protected override Task<bool> Compile()", "protected override async Task<bool> Compile()" )
			.Replace( "return Task.FromResult( true );", $$"""
				if ( !Context.Describing && json["Wait"] is not null )
				{
					System.IO.File.WriteAllText( @"{{entered}}", "entered" );
					var timer = System.Diagnostics.Stopwatch.StartNew();
					while ( !System.IO.File.Exists( @"{{release}}" ) && timer.Elapsed.TotalSeconds < 15 )
						await Task.Delay( 25 );
					if ( !System.IO.File.Exists( @"{{release}}" ) )
					{
						System.IO.File.WriteAllText( @"{{timedOut}}", "timed out" );
						throw new System.Exception( "Editor did not tick while compiling" );
					}
				}
				return true;
				""" );
		Directory.CreateDirectory( Path.Combine( project.Dir, "Editor" ) );
		File.WriteAllText( Path.Combine( project.Dir, "Editor", "LiveGraph.cs" ), code );
		project.Write( "graphs/a.lvg", "{ \"Name\": \"original\" }" );

		var live = project.Open( $$"""
			System.IO.File.WriteAllText( @"{{project.Assets}}\graphs\a.lvg", "{ \"Name\": \"edited\", \"Wait\": true }" );
			var timer = System.Diagnostics.Stopwatch.StartNew();
			while ( !System.IO.File.Exists( @"{{entered}}" ) && timer.Elapsed.TotalSeconds < 90 ) await Task.Delay( 25 );
			if ( !System.IO.File.Exists( @"{{entered}}" ) ) return "compiler never entered";
			if ( {{(closeWhileCompiling ? "true" : "false")}} ) return "closing during compile";
			// This continuation needs the editor's main thread, while the worker is waiting for us.
			System.IO.File.WriteAllText( @"{{release}}", "released by editor" );
			while ( timer.Elapsed.TotalSeconds < 100 )
			{
				if ( System.Text.Encoding.UTF8.GetString( System.IO.File.ReadAllBytes( @"{{output}}" ) ).Contains( "edited" ) ) return "editor released compiler and output updated";
				await Task.Delay( 25 );
			}
			return "output did not update";
			""" );
		StringAssert.Contains( live.Live, closeWhileCompiling ? "closing during compile" : "editor released compiler and output updated", live.ToString() );
		Assert.IsFalse( File.Exists( timedOut ), "Closing the editor cancels its active compile instead of waiting for the compiler timeout" );
		if ( closeWhileCompiling ) StringAssert.Contains( System.Text.Encoding.UTF8.GetString( File.ReadAllBytes( output ) ), "original" );
	}

	[TestMethod, TestCategory( "Slow" )]
	public void StartupBuildsWhatTheProjectLoads()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_EDITOR_TESTS" ) is null )
			Assert.Inconclusive( "Opens an editor: set SBOX_EDITOR_TESTS=1 to run" );

		using var project = new EditorProject();
		project.Copy( Sources.ImageA, "materials/shared.tga" );
		project.Write( "materials/a.vmat", Sources.Material( "TextureColor", "materials/shared.tga" ) );
		project.Write( "models/box.vmdl", Sources.Box( "materials/a.vmat" ) );
		project.Write( "textures/broken.vtex", Sources.Broken );
		project.Copy( Sources.ImageB, "textures/t.tga" );
		project.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );

		// A library, mounted before the project, whose material a project model uses
		project.Copy( Sources.ImageA, "materials/libm_color.tga", EditorProject.Library );
		project.Write( "materials/libm.vmat", Sources.Material( "TextureColor", "materials/libm_color.tga" ), EditorProject.Library );
		project.Write( "models/libbox.vmdl", Sources.Box( "materials/libm.vmat" ) );

		// Generated textures: a standalone generator copying another texture's compiled pixels, a copy of a
		// core decal (so its generated textures are shared with core's), a decal whose color embeds the same
		// texture, and one whose image doesn't exist yet
		project.Write( "textures/copy.vtex", """{ "$compiler": "texture", "$source": "imagefile", "data": { "FilePath": "textures/t.vtex", "CacheToDisk": true } }""" );
		foreach ( var image in new[] { "color", "normal", "RMO", "height" } )
			project.Copy( $"core/decals/blood/decal_blood_splatter_01_{image}.png", $"decals/blood/decal_blood_splatter_01_{image}.png" );
		project.Copy( Blood, "decals/blood.decal" );
		var decal = File.ReadAllText( Path.Combine( ContentFixture.Engine, Blood ) );
		project.Write( "decals/copy.decal", decal.Replace( BloodColor, "textures/t.vtex" ) );
		project.Write( "decals/later.decal", decal.Replace( BloodColor, "decals/later.png" ) );

		var cold = project.Open();
		cold.Assert( compiles: 8, failed: 1, library: 1 );
		cold.AssertCopiedPixels();

		// Unchanged: only the broken texture is tried again, and fails describing
		project.Open().Assert( compiles: 0, failed: 1 );

		// The copied texture's source: the texture, then both copies of its pixels
		project.Copy( Sources.ImageA, "textures/t.tga" );
		var edited = project.Open();
		edited.Assert( compiles: 3, failed: 1 );
		edited.AssertCopiedPixels();

		// The library material's shader changes its signature: the project model follows in the same startup
		project.Write( "materials/libm.vmat", Sources.Material( "shaders/unlit.shader", ["TextureColor", "materials/libm_color.tga"] ), EditorProject.Library );
		project.Open().Assert( compiles: 1, failed: 1, library: 1 );

		// The missing image turns up
		project.Copy( $"core/{BloodColor}", "decals/later.png" );
		project.Open().Assert( compiles: 1, failed: 1 );

		// The project's copy goes; core's decal still has the generated textures they shared
		File.Delete( Path.Combine( project.Assets, "decals/blood.decal" ) );
		var removed = project.Open();
		removed.Assert( compiles: 0, failed: 1, removed: 1 );
		StringAssert.Contains( removed.Loads, "core decal True", removed.ToString() );
		project.Open().Assert( compiles: 0, failed: 1 );

		// Another build holds core while this editor starts: it waits for it rather than skip the build
		var lockFile = Path.Combine( ContentFixture.Engine, ".source2", "contentbuild", "core", "lock" );
		using ( var held = new FileStream( lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None ) )
		{
			var release = Task.Delay( TimeSpan.FromSeconds( 70 ) ).ContinueWith( _ => held.Dispose() );
			var waited = project.Open();
			release.Wait();
			waited.Assert( compiles: 0, failed: 1 );
		}

		// A sound's .meta is an input of its compile even before it exists: writing one while the editor is open
		// rebuilds the sound, after a startup that left the .meta files registration writes current
		project.Copy( "core/sounds/ambience/stings/sting-frog-1.wav", "sounds/frog.wav" );
		var live = project.Open( EditMetaAndWait( project, "{ \"visemes\": [] }" ) );
		live.Assert( compiles: 1, failed: 1 );
		StringAssert.Contains( live.Live, "rebuilt in", live.ToString() );
		StringAssert.Contains( live.Live, "Content build: 1 compiles", live.ToString() );
	}

	/// <summary>
	/// A sound's .meta edited twice in an open editor: each edit is one build that compiles the sound again.
	/// </summary>
	[TestMethod, TestCategory( "Slow" )]
	public void SoundMetaEditedWhileOpenRebuildsTheSound()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_EDITOR_TESTS" ) is null )
			Assert.Inconclusive( "Opens an editor: set SBOX_EDITOR_TESTS=1 to run" );

		using var project = new EditorProject();
		project.Copy( "core/sounds/ambience/stings/sting-frog-1.wav", "sounds/frog.wav" );
		var live = project.Open( EditMetaAndWait( project, "{ \"visemes\": [] }", "{ \"visemes\": [], \"subtitle\": \"frog\" }" ) );

		StringAssert.Contains( live.Summary, $"Content local.{EditorProject.Ident}: 1 compiles, 0 failed", live.ToString() );
		Assert.AreEqual( 2, Regex.Matches( live.Live, "rebuilt in" ).Count, live.ToString() );
		Assert.AreEqual( 2, Regex.Matches( live.Live, @"Content build: 1 compiles, 0 adopted, 0 failed" ).Count, live.ToString() );
		Assert.AreEqual( 2, Regex.Matches( live.Live, @"Content build: \d+ compiles" ).Count, live.ToString() );
	}

	/// <summary>
	/// A project's own compiler, its code edited in an open editor: what it compiled stays as it is, and a source written
	/// after the hotload compiles with the new code (the build's workers have it). An unchanged broken texture
	/// isn't retried just because code hotloaded; the next asset edit still retries it.
	/// </summary>
	[TestMethod, TestCategory( "Slow" )]
	public void ProjectCompilerEditedWhileOpenCompilesNewSourcesWithTheNewCode()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_EDITOR_TESTS" ) is null )
			Assert.Inconclusive( "Opens an editor: set SBOX_EDITOR_TESTS=1 to run" );

		using var project = new EditorProject();
		var code = Path.Combine( project.Dir, "Editor", "LiveGraph.cs" );
		Directory.CreateDirectory( Path.GetDirectoryName( code ) );
		File.WriteAllText( code, LiveGraphSource.Replace( "MARKER", "compiler 1" ) );
		project.Write( "graphs/a.lvg", """{ "Name": "a" }""" );
		project.Write( "textures/broken.vtex", "invalid texture" );
		var a = Path.Combine( project.Assets, "graphs", "a.lvg_c" );
		var b = Path.Combine( project.Assets, "graphs", "b.lvg_c" );

		var live = project.Open( $$"""
			bool Marked( string path, string marker ) => System.IO.File.Exists( path ) && System.Text.Encoding.UTF8.GetString( System.IO.File.ReadAllBytes( path ) ).Contains( marker );
			var before = Marked( @"{{a}}", "compiler 1" );
			var written = System.IO.File.GetLastWriteTimeUtc( @"{{a}}" );
			var contentBuild = typeof( Editor.AssetSystem ).Assembly.GetType( "Editor.EditorContentBuild" );
			int BuildState( string name ) => (int)contentBuild.GetField( name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic ).GetValue( null );
			var generation = BuildState( "codeGeneration" );
			var builtEpoch = BuildState( "builtEpoch" );

			// Wait for the actual hotload, then check it did not schedule another content pass before editing an asset.
			System.IO.File.WriteAllText( @"{{code}}", {{System.Text.Json.JsonSerializer.Serialize( LiveGraphSource.Replace( "MARKER", "compiler 2" ) )}} );
			var hotload = System.Diagnostics.Stopwatch.StartNew();
			while ( BuildState( "codeGeneration" ) == generation && hotload.ElapsedMilliseconds < 90000 ) await Task.Delay( 25 );
			if ( BuildState( "codeGeneration" ) == generation ) return "code did not hotload";
			if ( BuildState( "builtEpoch" ) != builtEpoch ) return "hotload scheduled a content rebuild";

			System.IO.File.WriteAllText( @"{{project.Assets}}\graphs\b.lvg", "{ \"Name\": \"b\" }" );
			var timer = System.Diagnostics.Stopwatch.StartNew();
			while ( !Marked( @"{{b}}", "compiler 2" ) && timer.ElapsedMilliseconds < 120000 )
				await Task.Delay( 500 );
			await Task.Delay( 1000 );
			var kept = System.IO.File.GetLastWriteTimeUtc( @"{{a}}" ) == written && Marked( @"{{a}}", "compiler 1" );
			return $"built with 1 {before}, kept {kept}, new source with 2 {Marked( @"{{b}}", "compiler 2" )}";
			""" );

		StringAssert.Contains( live.Summary, $"Content local.{EditorProject.Ident}: 2 compiles, 1 failed", live.ToString() );
		StringAssert.Contains( live.Live, "built with 1 True, kept True, new source with 2 True", live.ToString() );
		Assert.AreEqual( 1, Regex.Matches( live.Live, @"Content build: \d+ compiles" ).Count, live.ToString() );
		Assert.AreEqual( 1, Regex.Matches( live.Live, @"Content build: 2 compiles, 0 adopted, 1 failed" ).Count, live.ToString() );
	}

	/// <summary>
	/// A project's own compiler, its code edited while the startup build runs (the compiler edits it itself, then two
	/// jobs keep the build going while the editor hotloads it): once startup is done, a source written then compiles with
	/// the new code.
	/// </summary>
	[TestMethod, TestCategory( "Slow" )]
	public void ProjectCompilerEditedDuringStartupCompilesNewSourcesWithTheNewCode()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_EDITOR_TESTS" ) is null )
			Assert.Inconclusive( "Opens an editor: set SBOX_EDITOR_TESTS=1 to run" );

		using var project = new EditorProject();
		var code = Path.Combine( project.Dir, "Editor", "LiveGraph.cs" );
		Directory.CreateDirectory( Path.GetDirectoryName( code ) );
		File.WriteAllText( code, SelfEditingGraphSource.Replace( "CODE", code ).Replace( "MARKER", "compiler 1" ) );
		project.Write( "graphs/a.lvg", """{ "Name": "a", "Edit": true, "Delay": 20000 }""" );
		project.Write( "graphs/b.lvg", """{ "Name": "b", "Delay": 50000 }""" );
		var c = Path.Combine( project.Assets, "graphs", "c.lvg_c" );

		var live = project.Open( $$"""
			bool Marked( string path ) => System.IO.File.Exists( path ) && System.Text.Encoding.UTF8.GetString( System.IO.File.ReadAllBytes( path ) ).Contains( "compiler 2" );
			await Task.Delay( 20000 );
			System.IO.File.WriteAllText( @"{{project.Assets}}\graphs\c.lvg", "{ \"Name\": \"c\" }" );
			var timer = System.Diagnostics.Stopwatch.StartNew();
			while ( !Marked( @"{{c}}" ) && timer.ElapsedMilliseconds < 120000 )
				await Task.Delay( 500 );
			return $"new source with 2 {Marked( @"{{c}}" )} after {timer.ElapsedMilliseconds} ms";
			""" );

		StringAssert.Contains( live.Summary, $"Content local.{EditorProject.Ident}: 2 compiles, 0 failed", live.ToString() );
		StringAssert.Contains( File.ReadAllText( code ), "compiler 2", "the startup compile didn't edit the compiler" );
		StringAssert.Contains( live.Live, "new source with 2 True", live.ToString() );
	}

	/// <summary>
	/// LiveGraph's compiler, whose code (at CODE) is "compiler 1" until its first compile of an asset with "Edit" makes it
	/// "compiler 2". Under compiler 1 each compile takes the asset's "Delay".
	/// </summary>
	const string SelfEditingGraphSource = """
		using Sandbox;
		using Sandbox.Resources;
		using System.Threading.Tasks;

		[AssetType( Name = "Live Graph", Extension = "lvg" )]
		public class LiveGraph : GameResource
		{
		}

		[ResourceIdentity( "lvg" )]
		public class LiveGraphCompiler : ResourceCompiler
		{
			protected override async Task<bool> Compile()
			{
				var json = Context.ReadSourceAsJson();
				json["Marker"] = "MARKER";
				Context.Data.Write( Context.ScanJson( json.ToJsonString() ) );

				if ( "MARKER".EndsWith( "1" ) )
				{
					if ( json["Edit"] is not null )
						System.IO.File.WriteAllText( @"CODE", System.IO.File.ReadAllText( @"CODE" ).Replace( "compiler " + "1", "compiler " + "2" ) );
					await Task.Delay( json["Delay"].GetValue<int>() );
				}
				return true;
			}
		}
		""";

	const string LiveGraphSource = """
		using Sandbox;
		using Sandbox.Resources;
		using System.Threading.Tasks;

		[AssetType( Name = "Live Graph", Extension = "lvg" )]
		public class LiveGraph : GameResource
		{
		}

		[ResourceIdentity( "lvg" )]
		public class LiveGraphCompiler : ResourceCompiler
		{
			protected override Task<bool> Compile()
			{
				var json = Context.ReadSourceAsJson();
				json["Marker"] = "MARKER";
				Context.Data.Write( Context.ScanJson( json.ToJsonString() ) );
				return Task.FromResult( true );
			}
		}
		""";

	/// <summary>
	/// Writes each text as the sound's .meta in turn, waiting (up to 30s) for the build to publish the sound again.
	/// </summary>
	static string EditMetaAndWait( EditorProject project, params string[] texts )
	{
		var meta = Path.Combine( project.Assets, "sounds", "frog.wav.meta" );
		var output = Path.Combine( project.Assets, "sounds", "frog.vsnd_c" );
		var edits = string.Join( "\n", texts.Select( x => $"results.Add( await Edit( {System.Text.Json.JsonSerializer.Serialize( x )} ) );" ) );
		return $$"""
			var results = new System.Collections.Generic.List<string>();
			async Task<string> Edit( string text )
			{
				var before = System.IO.File.GetLastWriteTimeUtc( @"{{output}}" );
				var timer = System.Diagnostics.Stopwatch.StartNew();
				System.IO.File.WriteAllText( @"{{meta}}", text );
				while ( System.IO.File.GetLastWriteTimeUtc( @"{{output}}" ) == before && timer.ElapsedMilliseconds < 30000 )
					await Task.Delay( 250 );
				return System.IO.File.GetLastWriteTimeUtc( @"{{output}}" ) == before ? "not rebuilt" : $"rebuilt in {timer.ElapsedMilliseconds} ms";
			}
			{{edits}}
			await Task.Delay( 1000 );
			return string.Join( ", ", results );
			""";
	}

	/// <summary>
	/// A texture copying another's compiled pixels, both loaded in an open editor: editing the source rebuilds the
	/// copy from the new pixels, not from the old ones still loaded.
	/// </summary>
	[TestMethod, TestCategory( "Slow" )]
	public void LiveEditRebuildsACopyFromTheNewPixels()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_EDITOR_TESTS" ) is null )
			Assert.Inconclusive( "Opens an editor: set SBOX_EDITOR_TESTS=1 to run" );

		using var project = new EditorProject();
		// Images of the same size: a loaded texture that changes size reloads as a fallback, which isn't this
		project.Copy( Sources.ImageA, "textures/t.tga" );
		project.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );
		project.Write( "textures/copy.vtex", """{ "$compiler": "texture", "$source": "imagefile", "data": { "FilePath": "textures/t.vtex", "CacheToDisk": true } }""" );
		// Edited while the project's root is locked, so the asset system can't compile the loaded texture by itself
		// first: one build compiles both, the copy right after the texture it reads
		var image = Path.Combine( ContentFixture.Engine, Sources.ImageC );
		var source = Path.Combine( project.Assets, "textures", "t.tga" );
		var lockFile = Path.Combine( project.Dir, ".sbox", "build", "lock" );
		var live = project.Open( $$"""
			// Both loaded, as a scene using them would have them
			var copy = Texture.Load( "textures/copy.vtex" );
			var old = Texture.Load( "textures/t.vtex" ).GetPixels();
			using ( var held = new System.IO.FileStream( @"{{lockFile}}", System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None ) )
			{
				System.IO.File.Copy( @"{{image}}", @"{{source}}", true );
				await Task.Delay( 3000 );
			}
			// The copy reloads some frames after it's built; one built from the old pixels never matches
			Texture now = null, copied = null;
			bool Done()
			{
				now = Texture.Load( "textures/t.vtex" );
				copied = Texture.Load( "textures/copy.vtex" );
				return !now.GetPixels().SequenceEqual( old ) && copied.Width == now.Width && copied.GetPixels().SequenceEqual( now.GetPixels() );
			}
			for ( var waited = 0; waited < 20000 && !Done(); waited += 500 )
				await Task.Delay( 500 );
			return $"source changed {!now.GetPixels().SequenceEqual( old )} copy current {copied.Width == now.Width && copied.GetPixels().SequenceEqual( now.GetPixels() )} ({copied.Width}x{copied.Height} copy, {now.Width}x{now.Height} source)";
			""" );

		StringAssert.Contains( live.Live, "Content build: 2 compiles", live.ToString() );
		StringAssert.Contains( live.Live, "source changed True", live.ToString() );
		StringAssert.Contains( live.Live, "copy current True", live.ToString() );
	}

	/// <summary>
	/// A native load or reload must not compile inside the resource loader. The normal content build must still
	/// compile and publish the edit afterwards, including when that compile reads an animated model's base model.
	/// </summary>
	[TestMethod, TestCategory( "Slow" )]
	[DataRow( false )]
	[DataRow( true )]
	public void LoadingAModelWithABaseModelDefersCompilation( bool reload )
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_EDITOR_TESTS" ) is null )
			Assert.Inconclusive( "Opens an editor: set SBOX_EDITOR_TESTS=1 to run" );

		using var project = new EditorProject();
		project.Copy( Sources.ImageA, "materials/a.tga" );
		project.Copy( Sources.ImageB, "materials/b.tga" );
		project.Write( "materials/a.vmat", Sources.Material( "TextureColor", "materials/a.tga" ) );
		project.Write( "materials/b.vmat", Sources.Material( "TextureColor", "materials/b.tga" ) );
		project.Copy( $"{Sources.BonnetFolder}/easter_bonnet.fbx", "models/bonnet.fbx" );
		project.Copy( $"{Sources.BonnetFolder}/easter_bonnet_citizen_idle_anim.fbx", "models/idle.fbx" );
		project.Write( "models/base.vmdl", Sources.Mesh( "models/bonnet.fbx", "materials/a.vmat" ) );
		project.Write( "models/derived.vmdl", Sources.MeshAnimated( "models/bonnet.fbx", "materials/a.vmat", "models/base.vmdl", "models/idle.fbx" ) );
		var source = Path.Combine( project.Assets, "models", "derived.vmdl" );
		var output = source + "_c";

		var live = project.Open( $$"""
			var reload = {{(reload ? "true" : "false")}};
			if ( reload )
			{
				var model = Model.Load( "models/derived.vmdl" );
				if ( model is null || model.IsError ) return "initial model failed";
			}
			var before = System.IO.File.GetLastWriteTimeUtc( @"{{output}}" );
			var text = System.IO.File.ReadAllText( @"{{source}}" );
			System.IO.File.WriteAllText( @"{{source}}", text.Replace( "materials/a.vmat", "materials/b.vmat" ) );
			Editor.AssetSystem.RegisterFile( @"{{source}}" );
			// Exercise the native load before yielding to the debounced content build.
			if ( reload )
			{
				var resourceSystem = typeof( Model ).Assembly.GetType( "NativeEngine.g_pResourceSystem", true );
				resourceSystem.GetMethod( "ReloadResource", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic )
					.Invoke( null, new object[] { "models/derived.vmdl" } );
			}
			else
			{
				Model.Load( "models/derived.vmdl" );
			}
			if ( System.IO.File.GetLastWriteTimeUtc( @"{{output}}" ) != before ) return "compiled inside the resource load";
			var timer = System.Diagnostics.Stopwatch.StartNew();
			while ( timer.Elapsed.TotalSeconds < 60 )
			{
				if ( System.IO.File.GetLastWriteTimeUtc( @"{{output}}" ) != before ) return "deferred compile published";
				await Task.Delay( 50 );
			}
			return "deferred compile did not publish";
			""" );

		StringAssert.Contains( live.Summary, $"Content local.{EditorProject.Ident}: 4 compiles, 0 failed", live.ToString() );
		StringAssert.Contains( live.Live, "deferred compile published", live.ToString() );
		StringAssert.Contains( live.Live, "Content build: 1 compiles", live.ToString() );
	}

	/// <summary>
	/// A game resource whose source stops compiling still opens as it last compiled, so its editor can fix it, as on
	/// master; building it still fails and publishes nothing.
	/// </summary>
	[TestMethod, TestCategory( "Slow" )]
	public void GameResourceThatFailsToCompileOpensAsItLastCompiled()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_EDITOR_TESTS" ) is null )
			Assert.Inconclusive( "Opens an editor: set SBOX_EDITOR_TESTS=1 to run" );

		using var project = new EditorProject();
		foreach ( var image in new[] { "color", "normal", "RMO", "height" } )
			project.Copy( $"core/decals/blood/decal_blood_splatter_01_{image}.png", $"decals/blood/decal_blood_splatter_01_{image}.png" );
		project.Copy( Blood, "decals/blood.decal" );

		// Its colour texture as loaded, with where that comes from
		const string open = """
			var registered = ResourceLibrary.TryGet<DecalDefinition>( "decals/blood.decal", out _ );
			var opened = Editor.AssetSystem.FindByPath( "decals/blood.decal" ).TryLoadResource<DecalDefinition>( out var decal );
			await Task.Delay( 1000 );
			var texture = decal?.ColorTexture;
			var compiled = texture?.EmbeddedResource?.CompiledPath;
			var exists = compiled is not null && FileSystem.Mounted.FileExists( compiled + "_c" );
			return $"opened {opened} color {texture?.IsValid() == true} texture {texture?.ResourcePath} compiled {compiled} exists {exists} registered before {registered}";
			""";

		var built = project.Open( open );
		StringAssert.Contains( built.Summary, $"Content local.{EditorProject.Ident}: 1 compiles, 0 failed", built.ToString() );
		var compiled = Path.Combine( project.Assets, "decals", "blood.decal_c" );
		var lastGood = File.ReadAllBytes( compiled );

		// Its embedded textures name a generator that doesn't exist
		var source = Path.Combine( project.Assets, "decals", "blood.decal" );
		project.Write( "decals/blood.decal", File.ReadAllText( source ).Replace( "\"imagefile\"", "\"nosuchgenerator\"" ) );

		var broken = project.Open( open );
		// Describing it fails already (no such generator), so it fails without a compile job
		StringAssert.Contains( broken.Summary, $"Content local.{EditorProject.Ident}: 0 compiles, 1 failed", broken.ToString() );
		CollectionAssert.AreEqual( lastGood, File.ReadAllBytes( compiled ), "the failed compile published" );
		StringAssert.Contains( broken.Live, "opened True color True", $"{broken}\nwhen it compiled: {built.Live}" );
	}

	sealed class EditorProject : IDisposable
	{
		public readonly string Dir = Directory.CreateTempSubdirectory( "sbox-editor-startup" ).FullName;
		public const string Ident = "cb_editor_startup";

		public const string Library = "cb_editor_startup_lib";

		public string Assets => Path.Combine( Dir, "Assets" );

		public EditorProject()
		{
			Directory.CreateDirectory( Assets );
			File.WriteAllText( Path.Combine( Dir, $"{Ident}.sbproj" ), $$"""
				{ "Title": "Editor Startup Test", "Type": "game", "Org": "local", "Ident": "{{Ident}}", "Schema": 1, "AI": { "EnableMcpCommunication": true } }
				""" );

			var library = Path.Combine( Dir, "Libraries", Library );
			Directory.CreateDirectory( Path.Combine( library, "Assets" ) );
			File.WriteAllText( Path.Combine( library, $"{Library}.sbproj" ), $$"""
				{ "Title": "Editor Startup Test Library", "Type": "library", "Org": "local", "Ident": "{{Library}}", "Schema": 1 }
				""" );
		}

		string Root( string library ) => library is null ? Assets : Path.Combine( Dir, "Libraries", library, "Assets" );

		public void Dispose()
		{
			try { Directory.Delete( Dir, true ); } catch ( IOException ) { } catch ( UnauthorizedAccessException ) { }
		}

		public void Write( string rel, string text, string library = null )
		{
			Directory.CreateDirectory( Path.GetDirectoryName( Path.Combine( Root( library ), rel ) ) );
			File.WriteAllText( Path.Combine( Root( library ), rel ), text.ReplaceLineEndings( "\r\n" ) );
		}

		public void Copy( string engineRel, string rel, string library = null )
		{
			Directory.CreateDirectory( Path.GetDirectoryName( Path.Combine( Root( library ), rel ) ) );
			File.Copy( Path.Combine( ContentFixture.Engine, engineRel ), Path.Combine( Root( library ), rel ), true );
		}

		/// <summary>
		/// Opens the project in an editor of its own, waits for startup, reads what happened and closes it.
		/// </summary>
		public Startup Open( string live = null )
		{
			using var editor = new McpSession( Path.Combine( ContentFixture.Engine, "bin", "managed", "sbox-cli.exe" ), Path.Combine( Dir, $"{Ident}.sbproj" ) );

			var summary = editor.Tool( "read_console", new { limit = 50, minimumLevel = "Trace", filter = "Content " } );

			// Load what was built, the way a session would, then see what the asset system still thinks is stale
			var loads = editor.Tool( "run_csharp", new
			{
				code = """
					var model = Model.Load( "models/box.vmdl" );
					var material = Material.Load( "materials/a.vmat" );
					var libbox = Model.Load( "models/libbox.vmdl" );
					var copy = ResourceLibrary.Get<DecalDefinition>( "decals/copy.decal" );
					var core = ResourceLibrary.Get<DecalDefinition>( "decals/blood/blood_splatter_01.decal" );
					var source = Texture.Load( "textures/t.vtex" );
					var standalone = Texture.Load( "textures/copy.vtex" );
					await Task.Delay( 1000 );
					bool Same( Texture t ) => t is not null && t.Width == source.Width && t.GetPixels().SequenceEqual( source.GetPixels() );
					var copied = Same( copy?.ColorTexture ) && Same( standalone );
					var stale = Editor.AssetSystem.All.Where( x => x.Path.StartsWith( "models/" ) || x.Path.StartsWith( "materials/" ) || x.Path.StartsWith( "decals/" ) )
						.Where( x => !x.IsTrivialChild && x.CanRecompile && !x.IsCompiledAndUpToDate ).Select( x => x.Path );
					return $"model error {model?.IsError} material valid {material?.IsValid()} library model error {libbox?.IsError} copied pixels {copied} core decal {core?.ColorTexture?.IsValid() == true} stale [{string.Join( ", ", stale )}]";
					"""
			} );
			var onDemand = editor.Tool( "read_console", new { limit = 50, minimumLevel = "Trace", filter = "On-demand recompile" } );

			if ( live is null ) return new Startup( summary, loads, onDemand, null );

			var ran = editor.Tool( "run_csharp", new { code = live } );
			return new Startup( summary, loads, onDemand, $"{ran}\n{editor.Tool( "read_console", new { limit = 50, minimumLevel = "Trace", filter = "Content build: " } )}" );
		}
	}

	sealed record Startup( string Summary, string Loads, string OnDemand, string Live )
	{
		/// <summary>
		/// What the project's and the library's roots compiled, failed and removed at startup, with no describe
		/// misses, nothing compiled on demand afterwards and nothing left stale.
		/// </summary>
		public void Assert( int compiles, int failed, int removed = 0, int library = 0 )
		{
			var project = Regex.Match( Summary, $@"Content local\.{EditorProject.Ident}: (\d+) compiles, (\d+) failed, \d+ blocked, \d+ adopted, \d+ described, (\d+) removed" );
			var lib = Regex.Match( Summary, $@"Content local\.{EditorProject.Library}: (\d+) compiles" );
			var misses = Regex.Match( Summary, @"Content build: (\d+) describe misses" );
			Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue( project.Success && lib.Success && misses.Success, $"No content build at startup:\n{this}" );
			Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual( (compiles, failed, removed, library, 0), (Count( project, 1 ), Count( project, 2 ), Count( project, 3 ), Count( lib, 1 ), Count( misses, 1 )), $"compiles, failed, removed, library compiles, misses:\n{this}" );

			StringAssert.Contains( Loads, "model error False material valid True library model error False", ToString() );
			StringAssert.Contains( Loads, "stale []", ToString() );
			StringAssert.Contains( OnDemand, "No matching console output", ToString() );
		}

		public void AssertCopiedPixels() => StringAssert.Contains( Loads, "copied pixels True", ToString() );

		static int Count( Match match, int group ) => int.Parse( match.Groups[group].Value );

		public override string ToString() => $"{Summary}\n{Loads}\n{OnDemand}\n{Live}";
	}

	/// <summary>
	/// sbox-cli --project as an MCP server over stdio: it launches an editor it owns and closes it with stdin.
	/// </summary>
	sealed class McpSession : IDisposable
	{
		readonly Process process;
		int id;

		public McpSession( string cli, string project )
		{
			var info = new ProcessStartInfo( cli ) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
			foreach ( var arg in new[] { "--project", project, "--startup-timeout", "3000", "--request-timeout", "600" } ) info.ArgumentList.Add( arg );
			process = Process.Start( info );

			Call( "initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "contentbuild-editor-startup", version = "1" } } );
			Send( new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" } );
		}

		public string Tool( string name, object arguments )
		{
			var result = Call( "tools/call", new { name, arguments } );
			var text = new List<string>();
			foreach ( var item in result["result"]?["content"]?.AsArray() ?? [] ) text.Add( item?["text"]?.GetValue<string>() ?? "" );
			return string.Join( "\n", text );
		}

		JsonNode Call( string method, object parameters )
		{
			var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = ++id, ["method"] = method, ["params"] = JsonSerializer.SerializeToNode( parameters ) };
			Send( request );
			while ( true )
			{
				var line = process.StandardOutput.ReadLine() ?? throw new InvalidOperationException( $"sbox-cli closed: {process.StandardError.ReadToEnd()}" );
				var reply = JsonNode.Parse( line );
				if ( reply?["id"]?.GetValue<int>() == id ) return reply;
			}
		}

		void Send( JsonNode message )
		{
			process.StandardInput.WriteLine( message.ToJsonString() );
			process.StandardInput.Flush();
		}

		public void Dispose()
		{
			process.StandardInput.Close();
			if ( !process.WaitForExit( TimeSpan.FromMinutes( 2 ) ) ) process.Kill( true );
			process.Dispose();
		}
	}
}
