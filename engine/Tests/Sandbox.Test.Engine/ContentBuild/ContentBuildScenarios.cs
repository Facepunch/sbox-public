using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Sandbox.ContentBuild;

namespace ContentBuild;

/// <summary>
/// What a content build must get right, as small real-content scenarios. Each derived class runs every
/// scenario against one way of running the builder; every scenario must pass.
/// </summary>
public abstract class ContentBuildScenarios
{
	protected abstract IContentBuilder Builder { get; }

	protected static void Scenario( Action<ContentFixture> body )
	{
		using var fixture = new ContentFixture();
		body( fixture );
	}

	protected BuildRun Build( ContentFixture fixture, BuildOptions options = null, CancellationToken token = default ) => fixture.Build( Builder, options, token );

	protected static void AssertSucceeded( BuildRun run )
	{
		Assert.IsTrue( run.Success, $"Build failed: {run}\n{run.Result.Log}" );
		Assert.AreEqual( 0, run.Result.Reruns, $"Compiled before what it reads was known: {run}\n{run.Result.Log}" );
		AssertValid( run );
	}

	static void AssertValid( BuildRun run ) => Assert.AreEqual( 0, run.Invalid.Length, $"Incomplete outputs: {run}" );

	protected static void AssertWrote( BuildRun run, params string[] outputs )
	{
		foreach ( var output in outputs )
			Assert.IsTrue( run.Wrote( output ), $"Expected {output} to be built: {run}" );
	}

	static void AssertNotWritten( BuildRun run, params string[] outputs )
	{
		foreach ( var output in outputs )
			Assert.IsFalse( run.Wrote( output ), $"Expected {output} to be left alone: {run}" );
	}

	protected static void AssertWroteExactly( BuildRun run, params string[] outputs ) =>
		CollectionAssert.AreEquivalent( outputs.Select( x => x.ToLowerInvariant() ).ToArray(), run.Written.Select( x => x.ToLowerInvariant() ).ToArray(), $"Wrong outputs built: {run}" );

	protected static void MaterialWithTexture( ContentFixture fixture, string material, string image, string shippedImage )
	{
		fixture.CopyShipped( shippedImage, image );
		fixture.Write( material, Sources.Material( "TextureColor", image ) );
	}

	static bool OutputMentions( ContentFixture fixture, string output, string text ) =>
		Encoding.ASCII.GetString( fixture.Read( output ) ).Contains( text, StringComparison.OrdinalIgnoreCase );

	//
	// Asset types
	//

	[TestMethod]
	public void MaterialGeneratesTexture() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );

		var run = Build( fx );

		AssertSucceeded( run );
		var child = fx.Child( "materials/a_color.tga" );
		Assert.IsNotNull( child, $"No texture generated: {run}" );
		AssertWrote( run, "materials/a.vmat_c", child );
	} );

	[TestMethod]
	public void StandaloneTexture() => Scenario( fx =>
	{
		fx.CopyShipped( Sources.ImageB, "textures/t.tga" );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );

		var run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "textures/t.vtex_c" );
	} );

	[TestMethod]
	public void Sound() => Scenario( fx =>
	{
		fx.CopyShipped( Sources.SoundA, "sounds/s.wav" );

		var run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "sounds/s.vsnd_c" );

		fx.CopyShipped( Sources.SoundB, "sounds/s.wav" );
		run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "sounds/s.vsnd_c" );
		CollectionAssert.Contains( run.Changed, "sounds/s.vsnd_c" );
	} );

	[TestMethod]
	public void ModelImportsMeshWithRemappedMaterial() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/m.vmat", "materials/m_color.tga", Sources.ImageA );
		MaterialWithTexture( fx, "materials/n.vmat", "materials/n_color.tga", Sources.ImageB );
		fx.CopyShipped( Sources.MeshA, "models/mesh.dmx" );
		fx.Write( "models/mesh.vmdl", Sources.Mesh( "models/mesh.dmx", "materials/m.vmat" ) );

		var run = Build( fx );

		AssertSucceeded( run );
		AssertWrote( run, "models/mesh.vmdl_c", "materials/m.vmat_c" );
		Assert.IsTrue( OutputMentions( fx, "models/mesh.vmdl_c", "materials/m.vmat" ), "Model doesn't reference its remapped material" );

		// A mesh edit rebuilds the model only
		fx.CopyShipped( Sources.MeshB, "models/mesh.dmx" );
		run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "models/mesh.vmdl_c" );

		// So does pointing the remap somewhere else
		fx.Write( "models/mesh.vmdl", Sources.Mesh( "models/mesh.dmx", "materials/n.vmat" ) );
		run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "models/mesh.vmdl_c" );
		Assert.IsTrue( OutputMentions( fx, "models/mesh.vmdl_c", "materials/n.vmat" ), "Model doesn't reference its new material" );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	//
	// Incremental builds
	//

	[TestMethod]
	public void UnchangedBuildWritesNothing() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		fx.CopyShipped( Sources.SoundA, "sounds/s.wav" );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/a.vmat" ) );
		AssertSucceeded( Build( fx ) );

		var run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run );
	} );

	[TestMethod]
	public void UnchangedBuildCompilesNothing() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		fx.CopyShipped( Sources.SoundA, "sounds/s.wav" );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/a.vmat" ) );
		AssertSucceeded( Build( fx ) );

		var run = Build( fx );

		AssertSucceeded( run );
		Assert.AreEqual( 0, run.Result.Jobs.Count, $"Nothing changed, nothing to compile: {run}" );
	} );

	[TestMethod]
	public void ForcedBuildIsByteIdentical() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		fx.CopyShipped( Sources.SoundA, "sounds/s.wav" );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/a.vmat" ) );
		var first = Build( fx );
		AssertSucceeded( first );

		var run = Build( fx, new BuildOptions { Force = true } );

		AssertSucceeded( run );
		CollectionAssert.IsSubsetOf( first.Result.Jobs.ToArray(), run.Result.Jobs.ToArray(), $"Not everything was compiled: {run}" );
		Assert.AreEqual( 0, run.Changed.Length, $"Forced rebuild changed bytes: {string.Join( ", ", run.Changed )}" );
	} );

	[TestMethod]
	public void SourceEditRebuildsOnlyThatAsset() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		MaterialWithTexture( fx, "materials/b.vmat", "materials/b_color.tga", Sources.ImageB );
		AssertSucceeded( Build( fx ) );

		fx.Write( "materials/a.vmat", Sources.Material( "TextureColor", "materials/a_color.tga", "g_flMetalness", "0.5" ) );
		var run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "materials/a.vmat_c" );
	} );

	[TestMethod]
	public void SharedInputRebuildsEveryUser() => Scenario( fx =>
	{
		fx.CopyShipped( Sources.ImageA, "materials/shared.tga" );
		fx.Write( "materials/a.vmat", Sources.Material( "TextureColor", "materials/shared.tga" ) );
		fx.Write( "materials/b.vmat", Sources.Material( "TextureColor", "materials/shared.tga", "g_flMetalness", "0.5" ) );
		AssertSucceeded( Build( fx ) );
		var child = fx.Child( "materials/shared.tga" );
		Assert.IsNotNull( child, "Both materials should share one generated texture" );

		fx.CopyShipped( Sources.ImageB, "materials/shared.tga" );
		var run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "materials/a.vmat_c", "materials/b.vmat_c", child );
		CollectionAssert.Contains( run.Changed, child );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	[TestMethod]
	public void SidecarEditRebuildsUsers() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		fx.Write( "materials/a_color.tga.meta", """{ "guid": "00000000-0000-0000-0000-000000000001" }""" );
		AssertSucceeded( Build( fx ) );

		fx.Write( "materials/a_color.tga.meta", """{ "guid": "00000000-0000-0000-0000-000000000002" }""" );
		var run = Build( fx );

		AssertSucceeded( run );
		AssertWrote( run, "materials/a.vmat_c", fx.Child( "materials/a_color.tga" ) );
	} );

	[TestMethod]
	public void GlobalSettingRebuildsAffectedAssets() => Scenario( fx =>
	{
		fx.CopyShipped( Sources.ImageA, "textures/t.tga" );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );
		fx.CopyShipped( Sources.SoundA, "sounds/s.wav" );
		AssertSucceeded( Build( fx ) );
		var small = new BuildOptions { CompilerArgs = ["-maxcompiledtextureres", "4"] };

		var run = Build( fx, small );

		AssertSucceeded( run );
		AssertWroteExactly( run, "textures/t.vtex_c" );
		CollectionAssert.Contains( run.Changed, "textures/t.vtex_c" );

		AssertWroteExactly( Build( fx, small ) );
		AssertWroteExactly( Build( fx ), "textures/t.vtex_c" );
	} );

	//
	// Missing files
	//

	[TestMethod]
	public void MissingOutputIsRebuilt() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		AssertSucceeded( Build( fx ) );

		fx.Delete( "materials/a.vmat_c" );
		var run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "materials/a.vmat_c" );
	} );

	[TestMethod]
	public void MissingGeneratedChildIsRebuilt() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		AssertSucceeded( Build( fx ) );
		var child = fx.Child( "materials/a_color.tga" );

		fx.Delete( child );
		var run = Build( fx );

		AssertSucceeded( run );
		AssertWrote( run, child );
	} );

	[TestMethod]
	public void BrokenSourceFailsOnlyThatAsset() => Scenario( fx =>
	{
		fx.Write( "textures/broken.vtex", Sources.Broken );
		fx.CopyShipped( Sources.ImageA, "textures/t.tga" );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );

		var run = Build( fx );

		Assert.IsFalse( run.Success, $"Build should fail: {run}" );
		CollectionAssert.AreEquivalent( new[] { "textures/broken.vtex" }, run.Result.Failed.ToArray() );
		AssertWroteExactly( run, "textures/t.vtex_c" );
		AssertValid( run );
	} );

	[TestMethod]
	public void MaterialPicksUpImageThatAppearsLater() => Scenario( fx =>
	{
		// Fails while the image is missing
		fx.Write( "materials/a.vmat", Sources.Material( "TextureColor", "materials/later.tga" ) );
		AssertValid( Build( fx ) );

		fx.CopyShipped( Sources.ImageA, "materials/later.tga" );
		var run = Build( fx );

		AssertSucceeded( run );
		AssertWrote( run, "materials/a.vmat_c" );
		Assert.IsNotNull( fx.Child( "materials/later.tga" ), $"Texture not generated once its input exists: {run}" );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	[TestMethod]
	public void TexturePicksUpImageThatAppearsLater() => Scenario( fx =>
	{
		// Compiles a placeholder while the image is missing
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/later.tga" ) );
		AssertValid( Build( fx ) );

		fx.CopyShipped( Sources.ImageA, "textures/later.tga" );
		var run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "textures/t.vtex_c" );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	//
	// Failure and publication
	//

	[TestMethod]
	public void FailureKeepsLastGoodOutputAndRecovers() => Scenario( fx =>
	{
		fx.CopyShipped( Sources.ImageA, "textures/t.tga" );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );
		AssertSucceeded( Build( fx ) );
		var good = fx.Read( "textures/t.vtex_c" );

		fx.Write( "textures/t.vtex", Sources.Broken );
		var failed = Build( fx );

		Assert.IsFalse( failed.Success, $"Build should fail: {failed}" );
		AssertValid( failed );
		CollectionAssert.AreEqual( good, fx.Read( "textures/t.vtex_c" ), "A failed compile replaced the last good output" );

		// Nothing changed, but a failed asset has no successful build to reuse, so it is tried again
		var retried = Build( fx );
		Assert.IsFalse( retried.Success, $"Build should still fail: {retried}" );
		CollectionAssert.Contains( retried.Result.Failed.ToArray(), "textures/t.vtex" );

		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );
		AssertSucceeded( Build( fx ) );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	[TestMethod]
	public void FailedAssetPublishesNoOutputs() => Scenario( fx =>
	{
		fx.CopyShipped( Sources.ImageA, "materials/a_color.tga" );
		fx.Write( "materials/a.vmat", Sources.Material( "TextureColor", "materials/a_color.tga", "g_tNormal", "materials/missing.vtex_c" ) );

		var run = Build( fx );

		Assert.IsFalse( run.Success, $"Build should fail: {run}" );
		AssertWroteExactly( run );
	} );

	[TestMethod]
	public void CancelledBuildLeavesNoPartialOutputs() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		MaterialWithTexture( fx, "materials/b.vmat", "materials/b_color.tga", Sources.ImageB );
		fx.CopyShipped( Sources.SoundA, "sounds/s.wav" );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/a.vmat" ) );

		using var cancel = new CancellationTokenSource();
		var run = Build( fx, new BuildOptions { JobFinished = _ => cancel.Cancel() }, cancel.Token );

		Assert.IsFalse( run.Success, $"A cancelled build isn't a success: {run}" );
		AssertValid( run );

		AssertSucceeded( Build( fx ) );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	//
	// Graph shape
	//

	[TestMethod]
	public void TwoModelsShareAMaterial() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/m.vmat", "materials/m_color.tga", Sources.ImageA );
		fx.Write( "models/one.vmdl", Sources.Box( "materials/m.vmat" ) );
		fx.Write( "models/two.vmdl", Sources.Box( "materials/m.vmat" ) );

		var run = Build( fx );

		AssertSucceeded( run );
		AssertWrote( run, "models/one.vmdl_c", "models/two.vmdl_c", "materials/m.vmat_c", fx.Child( "materials/m_color.tga" ) );
		Assert.IsTrue( run.Result.Jobs.Count( x => x == "materials/m.vmat" ) <= 1, $"Material compiled more than once: {run}" );
	} );

	[TestMethod]
	public void MaterialEditLeavesTheModelsThatUseItAlone() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/m.vmat", "materials/m_color.tga", Sources.ImageA );
		fx.Write( "models/one.vmdl", Sources.Box( "materials/m.vmat" ) );
		fx.Write( "models/two.vmdl", Sources.Box( "materials/m.vmat" ) );
		AssertSucceeded( Build( fx ) );

		// Models read their material's output order only: built after it, not rebuilt when it changes
		fx.Write( "materials/m.vmat", Sources.Material( "TextureColor", "materials/m_color.tga", "g_flMetalness", "0.5" ) );
		var run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "materials/m.vmat_c" );

		fx.Write( "models/one.vmdl", Sources.Box( "materials/m.vmat" ).Replace( "50.0, 50.0, 50.0", "60.0, 60.0, 60.0" ) );
		run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "models/one.vmdl_c" );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	[TestMethod]
	public void RemapToANewMaterialBuildsItFirst()
	{
		Scenario( fx =>
		{
			MaterialWithTexture( fx, "materials/m.vmat", "materials/m_color.tga", Sources.ImageA );
			fx.CopyShipped( Sources.MeshA, "models/mesh.dmx" );
			fx.Write( "models/mesh.vmdl", Sources.Mesh( "models/mesh.dmx", "materials/m.vmat" ) );
			AssertSucceeded( Build( fx ) );

			// The model now uses a material that was never built
			MaterialWithTexture( fx, "materials/new.vmat", "materials/new_color.tga", Sources.ImageB );
			fx.Write( "models/mesh.vmdl", Sources.Mesh( "models/mesh.dmx", "materials/new.vmat" ) );
			var run = Build( fx );

			AssertSucceeded( run );
			AssertWrote( run, "materials/new.vmat_c", "models/mesh.vmdl_c" );
			fx.AssertMatchesCleanBuild( Builder );
		} );
	}

	[TestMethod]
	public void SelectedModelBuildsItsNewMaterial() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/m.vmat", "materials/m_color.tga", Sources.ImageA );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/m.vmat" ) );
		AssertSucceeded( Build( fx ) );

		MaterialWithTexture( fx, "materials/new.vmat", "materials/new_color.tga", Sources.ImageB );
		fx.CopyShipped( Sources.ImageC, "materials/unrelated_color.tga" );
		fx.Write( "materials/unrelated.vmat", Sources.Material( "TextureColor", "materials/unrelated_color.tga" ) );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/new.vmat" ) );
		var run = Build( fx, new BuildOptions { Select = ["models/box.vmdl"] } );

		AssertSucceeded( run );
		AssertWrote( run, "materials/new.vmat_c", "models/box.vmdl_c" );
		AssertNotWritten( run, "materials/unrelated.vmat_c" );
	} );

	[TestMethod]
	public void CompiledOutputReadIsABuildEdge() => Scenario( fx =>
	{
		// The material points straight at another asset's compiled texture and reads it while compiling
		// (its display rect sets the texture coordinate scale), so the texture has to be built first,
		// and rebuilding it has to rebuild the material.
		Sources.WriteTga( fx, "textures/t.tga", 6, 5 );
		fx.Write( "textures/t.vtex", Sources.PaddedTexture( "textures/t.tga" ) );
		fx.Write( "materials/r.vmat", Sources.Material( "shaders/unlit.shader", ["g_tColor", "textures/t.vtex_c"] ) );

		var run = Build( fx );

		AssertSucceeded( run );
		AssertWrote( run, "textures/t.vtex_c", "materials/r.vmat_c" );

		Sources.WriteTga( fx, "textures/t.tga", 8, 8 );
		run = Build( fx );

		AssertSucceeded( run );
		CollectionAssert.IsSubsetOf( new[] { "textures/t.vtex_c", "materials/r.vmat_c" }, run.Changed, $"Material not rebuilt for its texture's new display rect: {run}" );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	[TestMethod]
	public void SelectedMaterialBuildsTheTextureItReads() => Scenario( fx =>
	{
		Sources.WriteTga( fx, "textures/t.tga", 6, 5 );
		fx.Write( "textures/t.vtex", Sources.PaddedTexture( "textures/t.tga" ) );
		fx.Write( "materials/r.vmat", Sources.Material( "shaders/unlit.shader", ["g_tColor", "textures/t.vtex_c"] ) );
		fx.CopyShipped( Sources.ImageA, "textures/unrelated.tga" );
		fx.Write( "textures/unrelated.vtex", Sources.Texture( "textures/unrelated.tga" ) );

		var run = Build( fx, new BuildOptions { Select = ["materials/r.vmat"] } );

		AssertSucceeded( run );
		AssertWrote( run, "textures/t.vtex_c", "materials/r.vmat_c" );
		AssertNotWritten( run, "textures/unrelated.vtex_c" );
	} );

	[TestMethod]
	public void NewDependencyIsTracked() => Scenario( fx =>
	{
		fx.CopyShipped( Sources.ImageA, "textures/a.tga" );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/a.tga" ) );
		AssertSucceeded( Build( fx ) );

		fx.CopyShipped( Sources.ImageB, "textures/b.tga" );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/a.tga", "textures/b.tga" ) );
		AssertWroteExactly( Build( fx ), "textures/t.vtex_c" );

		fx.CopyShipped( Sources.ImageC, "textures/b.tga" );
		var run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, "textures/t.vtex_c" );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	[TestMethod]
	public void ReplacedInputRemovesItsGeneratedTexture() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		AssertSucceeded( Build( fx ) );
		var old = fx.Child( "materials/a_color.tga" );

		fx.CopyShipped( Sources.ImageB, "materials/a_other.tga" );
		fx.Write( "materials/a.vmat", Sources.Material( "TextureColor", "materials/a_other.tga" ) );
		var run = Build( fx );

		AssertSucceeded( run );
		CollectionAssert.Contains( run.Removed, old, $"The texture a.vmat no longer generates remains: {run}" );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	//
	// Files moving around
	//

	[TestMethod]
	public void DeletedSourceRemovesItsOutputs() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		fx.CopyShipped( Sources.ImageA, "materials/shared.tga" );
		fx.Write( "materials/b.vmat", Sources.Material( "TextureNormal", "materials/shared.tga" ) );
		fx.Write( "materials/c.vmat", Sources.Material( "TextureNormal", "materials/shared.tga" ) );
		AssertSucceeded( Build( fx ) );
		var child = fx.Child( "materials/a_color.tga" );
		var shared = fx.Child( "materials/shared.tga" );

		// Never built here, so not this builder's to delete
		File.Copy( fx.Path( "materials/c.vmat_c" ), fx.Path( "materials/manual.vmat_c" ) );

		fx.Delete( "materials/a.vmat" );
		fx.Delete( "materials/b.vmat" );
		var run = Build( fx );

		AssertSucceeded( run );
		CollectionAssert.IsSubsetOf( new[] { "materials/a.vmat_c", child, "materials/b.vmat_c" }, run.Removed, $"Outputs of deleted sources remain: {run}" );
		Assert.IsTrue( fx.Exists( shared ), "A generated texture still used by c.vmat was removed" );
		Assert.IsTrue( fx.Exists( "materials/manual.vmat_c" ), "A compiled file the builder never made was removed" );
		File.Delete( fx.Path( "materials/manual.vmat_c" ) );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	[TestMethod]
	public void CompiledFileWithoutSourceIsKept() => Scenario( fx =>
	{
		// Shipped compiled, without its source
		fx.CopyShipped( "core/sounds/core.voip.sound_c", "sounds/core.voip.sound_c" );
		var shipped = fx.Read( "sounds/core.voip.sound_c" );
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );

		AssertSucceeded( Build( fx ) );
		var run = Build( fx );

		AssertSucceeded( run );
		CollectionAssert.AreEqual( shipped, fx.Read( "sounds/core.voip.sound_c" ), "A shipped compiled file was touched" );
	} );

	[TestMethod]
	public void RenamedSourceMovesItsOutputs() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		AssertSucceeded( Build( fx ) );

		fx.Move( "materials/a.vmat", "materials/renamed/b.vmat" );
		var run = Build( fx );

		AssertSucceeded( run );
		AssertWrote( run, "materials/renamed/b.vmat_c" );
		CollectionAssert.Contains( run.Removed, "materials/a.vmat_c" );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	[TestMethod]
	public void PathCaseDoesNotSplitAnAsset() => Scenario( fx =>
	{
		// Mixed-case file, referenced in lower case
		MaterialWithTexture( fx, "materials/W_Case.vmat", "materials/W_Case_Color.tga", Sources.ImageA );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/w_case.vmat" ) );

		var run = Build( fx );

		AssertSucceeded( run );
		Assert.AreEqual( 1, run.Result.Jobs.Count( x => x.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase ) ), $"One material, one compile: {run}" );
		AssertWroteExactly( Build( fx ) );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	[TestMethod]
	public void RootShadowsCoreAsset() => Scenario( fx =>
	{
		// Same path as a core material: the root's copy wins and is built into the root, and core is
		// left alone
		MaterialWithTexture( fx, "materials/dev/primary_white.vmat", "materials/dev/fixture_color.tga", Sources.ImageA );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/dev/primary_white.vmat" ) );

		var run = Build( fx );

		AssertSucceeded( run );
		AssertWrote( run, "materials/dev/primary_white.vmat_c", "models/box.vmdl_c" );
		Assert.IsNotNull( fx.Child( "materials/dev/fixture_color.tga" ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( fx.Parent, "core" ) ), "Something was built for core" );
		AssertWroteExactly( Build( fx ) );
	} );

	[TestMethod]
	public void SelectedAssetBuildsOnlyWhatItNeeds() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		MaterialWithTexture( fx, "materials/b.vmat", "materials/b_color.tga", Sources.ImageB );
		fx.CopyShipped( Sources.ImageC, "textures/t.tga" );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );

		var run = Build( fx, new BuildOptions { Select = ["materials/a.vmat"] } );

		AssertSucceeded( run );
		AssertWrote( run, "materials/a.vmat_c", fx.Child( "materials/a_color.tga" ) );
		AssertNotWritten( run, "materials/b.vmat_c", "textures/t.vtex_c" );

		// The rest is still out of date afterwards
		run = Build( fx );
		AssertSucceeded( run );
		AssertWrote( run, "materials/b.vmat_c", "textures/t.vtex_c" );
		AssertNotWritten( run, "materials/a.vmat_c" );
	} );

	//
	// Shipped content
	//

	static void CopyCrate( ContentFixture fixture, string folder )
	{
		foreach ( var file in Sources.Crate )
		{
			fixture.CopyShipped( $"{Sources.CrateFolder}/{file}", $"{folder}/{file}" );
			fixture.CopyShipped( $"{Sources.CrateFolder}/{file}.meta", $"{folder}/{file}.meta" );
			if ( file.EndsWith( ".fbx" ) || file.EndsWith( ".png" ) ) continue;
			fixture.Write( $"{folder}/{file}", File.ReadAllText( fixture.Path( $"{folder}/{file}" ) ).Replace( "models/citizen_props/", $"{folder}/" ) );
		}
	}

	[TestMethod, TestCategory( "Slow" )]
	public void ShippedPropWithBreakPieces() => Scenario( fx =>
	{
		const string folder = "models/fixture_props";
		CopyCrate( fx, folder );

		var run = Build( fx );

		AssertSucceeded( run );
		var color = fx.Child( $"{folder}/crate01a_color.png" );
		AssertWrote( run, $"{folder}/crate01.vmdl_c", $"{folder}/crate01a.vmat_c", color );
		var pieces = run.Written.Where( x => x.StartsWith( $"{folder}/crate01/", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		Assert.IsTrue( pieces.Length > 0, $"No break piece models: {run}" );
		AssertWroteExactly( Build( fx ) );

		// A texture edit rebuilds the material and that texture; the model and its pieces read the material order only
		fx.CopyShipped( $"{Sources.CrateFolder}/crate01a_ao.png", $"{folder}/crate01a_color.png" );
		run = Build( fx );

		AssertSucceeded( run );
		AssertWroteExactly( run, $"{folder}/crate01a.vmat_c", color );

		// Break pieces belong to the model: a missing one rebuilds it
		fx.Delete( pieces[0] );
		run = Build( fx );

		AssertSucceeded( run );
		AssertWrote( run, pieces[0] );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	[TestMethod, TestCategory( "Slow" )]
	public void RootShadowsMountedPropWithSidecars() => Scenario( fx =>
	{
		// The same prop at the same path as citizen's copy, sidecars included: the compiler rejects a
		// shadowed file whose sidecar would still resolve to citizen's, which is a content error
		CopyCrate( fx, "models/citizen_props" );
		for ( var i = 0; i <= 8; i++ )
		{
			var meta = $"models/citizen_props/crate01/piece{(i == 0 ? "" : i)}.vmdl.meta";
			fx.CopyShipped( $"addons/citizen/Assets/{meta}", meta );
		}

		var run = Build( fx );

		AssertSucceeded( run );
		AssertWrote( run, "models/citizen_props/crate01.vmdl_c" );
	} );
}

/// <summary>
/// Sandbox.ContentBuild with several compiles at once, as SboxBuild runs it.
/// The full incremental/clean-build matrix runs on master; focused worker and managed compiler checks also run on PRs.
/// </summary>
[TestClass, DoNotParallelize, TestCategory( "Slow" )]
public class ManagedContentBuild : ContentBuildScenarios
{
	protected override IContentBuilder Builder { get; } = new ManagedBuilder();

	[TestMethod]
	public void InterruptedPublicationIsRolledBack()
	{
		using var fx = new ContentFixture();
		fx.CopyShipped( Sources.ImageA, "textures/t.tga" );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );
		Assert.IsTrue( fx.Build( Builder ).Success );
		var good = fx.Read( "textures/t.vtex_c" );

		// New output moved in, record not yet written: as if the process died there
		fx.CopyShipped( Sources.ImageB, "textures/t.tga" );
		var crashing = new ManagedBuilder { BeforeCommit = _ => throw new IOException( "simulated crash" ) };
		Assert.ThrowsException<IOException>( () => crashing.Build( fx, new BuildOptions(), default ) );
		CollectionAssert.AreNotEqual( good, fx.Read( "textures/t.vtex_c" ), "The crash should have come after the new output was moved in" );

		// Any next build first puts the last committed outputs back
		fx.Build( Builder, new BuildOptions { Select = [] } );
		CollectionAssert.AreEqual( good, fx.Read( "textures/t.vtex_c" ), "Interrupted publication wasn't rolled back" );

		var run = fx.Build( Builder );
		Assert.IsTrue( run.Success, run.ToString() );
		Assert.IsTrue( run.Wrote( "textures/t.vtex_c" ), run.ToString() );
		fx.AssertMatchesCleanBuild( Builder );
	}

	[TestMethod]
	public void OutputsBuiltWithoutRecordsAreAdoptedWithWhatTheyRead()
	{
		// Built before there were records. Current outputs are recorded, not compiled again: the material's
		// edit info lists the texture output it read and the model's description its material, so they are adopted
		// with those edges, and a rebuilt texture still rebuilds the material.
		using var fx = new ContentFixture();
		Sources.WriteTga( fx, "textures/t.tga", 6, 5 );
		fx.Write( "textures/t.vtex", Sources.PaddedTexture( "textures/t.tga" ) );
		fx.Write( "materials/r.vmat", Sources.Material( "shaders/unlit.shader", ["g_tColor", "textures/t.vtex_c"] ) );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/r.vmat" ) );
		fx.CopyShipped( Sources.ImageA, "textures/stale.tga" );
		fx.Write( "textures/stale.vtex", Sources.Texture( "textures/stale.tga" ) );
		AssertSucceeded( fx.Build( Builder ) );
		Builder.Clean( fx );
		fx.CopyShipped( Sources.ImageB, "textures/stale.tga" );

		var run = fx.Build( Builder );

		AssertSucceeded( run );
		CollectionAssert.AreEquivalent( new[] { "textures/stale.vtex" }, run.Result.Jobs.ToArray(), run.Result.Log );
		CollectionAssert.AreEqual( new[] { "textures/stale.vtex_c" }, run.Changed, run.ToString() );
		AssertWroteExactly( fx.Build( Builder ) );

		Sources.WriteTga( fx, "textures/t.tga", 8, 8 );
		run = fx.Build( Builder );

		AssertSucceeded( run );
		CollectionAssert.IsSubsetOf( new[] { "textures/t.vtex_c", "materials/r.vmat_c" }, run.Changed, run.ToString() );
		fx.AssertMatchesCleanBuild( Builder );
	}

	/// <summary>
	/// What the editor's asset system does to compile one asset by itself: the resource compiler, with the editor's
	/// managed compilers, straight into the root, compiling what it needs first, with no build report and no record.
	/// </summary>
	static string CompileInPlace( ContentFixture fx, string rel )
	{
		var info = new System.Diagnostics.ProcessStartInfo( ManagedBuilder.Worker )
		{
			WorkingDirectory = fx.Root,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		foreach ( var arg in new[] { "-testmode", "-ignorecontentasserts", "-searchpaths", $"{fx.Mod}|{fx.Root}", "-compile", fx.Path( rel ) } )
			info.ArgumentList.Add( arg );

		using var process = System.Diagnostics.Process.Start( info );
		var log = process.StandardOutput.ReadToEndAsync();
		var errors = process.StandardError.ReadToEndAsync();
		if ( !process.WaitForExit( TimeSpan.FromMinutes( 2 ) ) )
		{
			process.Kill( true );
			Assert.Fail( $"Compiling {rel} didn't finish" );
		}
		Assert.AreEqual( 0, process.ExitCode, $"{log.Result}\n{errors.Result}" );
		return log.Result;
	}

	[TestMethod]
	public void AssetCompiledByTheEditorAloneIsAdoptedNotCompiledAgain() => Scenario( fx =>
	{
		Sources.WriteTga( fx, "textures/t.tga", 6, 5 );
		fx.Write( "textures/t.vtex", Sources.PaddedTexture( "textures/t.tga" ) );
		fx.Write( "materials/r.vmat", Sources.Material( "shaders/complex.shader", ["g_tColor", "textures/t.vtex_c"] ) );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/r.vmat" ) );
		AssertSucceeded( Build( fx ) );

		// The editor compiles the edited material by itself
		fx.Write( "materials/r.vmat", Sources.Material( "shaders/complex.shader", ["g_tColor", "textures/t.vtex_c", "g_flMetalness", "0.5"] ) );
		CompileInPlace( fx, "materials/r.vmat" );
		var run = Build( fx );

		// It is adopted, the model that reads it order only is left alone, and the result is what a clean build makes
		AssertSucceeded( run );
		Assert.AreEqual( 0, run.Result.Jobs.Count, run.Result.Log );
		AssertWroteExactly( Build( fx ) );
		fx.AssertMatchesCleanBuild( Builder );

		// What it read is still checked: a new texture rebuilds it
		Sources.WriteTga( fx, "textures/t.tga", 8, 8 );
		run = Build( fx );
		AssertSucceeded( run );
		CollectionAssert.IsSubsetOf( new[] { "textures/t.vtex_c", "materials/r.vmat_c" }, run.Changed, run.ToString() );
	} );

	[TestMethod]
	public void EditorCompilingAModelLeavesItsEditedMaterialToTheBuild() => Scenario( fx =>
	{
		// Only the model's material changed: the model is current, so compiling it by itself does nothing and the
		// build rebuilds just the material
		Sources.WriteTga( fx, "textures/t.tga", 6, 5 );
		fx.Write( "textures/t.vtex", Sources.PaddedTexture( "textures/t.tga" ) );
		fx.Write( "materials/r.vmat", Sources.Material( "shaders/complex.shader", ["g_tColor", "textures/t.vtex_c"] ) );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/r.vmat" ) );
		AssertSucceeded( Build( fx ) );
		var before = fx.Outputs();

		fx.Write( "materials/r.vmat", Sources.Material( "shaders/complex.shader", ["g_tColor", "textures/t.vtex_c", "g_flMetalness", "0.5"] ) );
		CompileInPlace( fx, "models/box.vmdl" );
		AssertSucceeded( Build( fx ) );

		var after = fx.Outputs();
		Assert.AreNotEqual( before["materials/r.vmat_c"].Hash, after["materials/r.vmat_c"].Hash, "Material not rebuilt" );
		Assert.AreEqual( before["models/box.vmdl_c"].Hash, after["models/box.vmdl_c"].Hash, "Model rebuilt for a material edit" );
		AssertWroteExactly( Build( fx ) );
		fx.AssertMatchesCleanBuild( Builder );
	} );

	/// <summary>
	/// A DXT5 texture two materials generate from the same normal map, built from nothing: one set of bytes whichever
	/// parent makes it, in a build (several jobs at once or one worker for all) or compiled by itself as the editor
	/// does, then nothing to do. DXT5 is compressed on the CPU, so it doesn't depend on a GPU or on build order.
	/// </summary>
	[TestMethod]
	public void SharedGeneratedDxt5TextureIsTheSameWhoeverMakesIt() => Scenario( fx =>
	{
		fx.CopyShipped( "core/materials/default/default_normal.tga", "materials/n.tga" );
		fx.CopyShipped( Sources.ImageA, "materials/c.tga" );
		// The generic shader's normal map is DXT5 (the complex shader's is BC7), as core's dev materials have it
		fx.Write( "materials/a.vmat", Sources.Material( "shaders/generic.shader", ["TextureNormal", "materials/n.tga"] ) );
		fx.Write( "materials/b.vmat", Sources.Material( "shaders/generic.shader", ["TextureNormal", "materials/n.tga", "TextureColor", "materials/c.tga"] ) );

		var run = Build( fx );
		AssertSucceeded( run );
		var child = fx.Child( "materials/n.tga" );
		Assert.IsNotNull( child, run.ToString() );
		var built = fx.Read( child );
		Assert.AreEqual( (2, 4, 4), TextureFormat( built ), "a 4x4 DXT5 (BC3) texture" );
		AssertWroteExactly( Build( fx ) );

		foreach ( var parent in new[] { "materials/b.vmat", "materials/a.vmat" } )
		{
			fx.Delete( child );
			fx.Delete( parent + "_c" );
			CompileInPlace( fx, parent );
			CollectionAssert.AreEqual( built, fx.Read( child ), $"{parent} compiled by itself" );
		}

		fx.AssertMatchesCleanBuild( Builder );
		fx.AssertMatchesCleanBuild( new ManagedBuilder { MaxJobs = 1 } );
	} );

	/// <summary>
	/// A compiled texture's format (VTEX_Format_t) and size, from the header in its DATA block.
	/// </summary>
	static (int Format, int Width, int Height) TextureFormat( byte[] file )
	{
		int Offset( int field ) => field + BitConverter.ToInt32( file, field );
		var blocks = Offset( 8 );
		for ( var i = 0; i < BitConverter.ToInt32( file, 12 ); i++ )
		{
			var block = blocks + i * 12;
			if ( BitConverter.ToUInt32( file, block ) != 0x41544144 ) continue; // DATA

			var header = Offset( block + 4 );
			return (file[header + 26], BitConverter.ToUInt16( file, header + 20 ), BitConverter.ToUInt16( file, header + 22 ));
		}
		return default;
	}

	[TestMethod]
	public void AssetTheEditorCompilesWithNothingChangedStaysCurrent() => Scenario( fx =>
	{
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/a.vmat" ) );
		AssertSucceeded( Build( fx ) );
		var built = fx.Outputs();

		CompileInPlace( fx, "models/box.vmdl" );

		CollectionAssert.AreEquivalent( built.ToList(), fx.Outputs().ToList(), "The editor's compile found it current" );
		AssertWroteExactly( Build( fx ) );
	} );

	[TestMethod]
	public void EditorCompiledMaterialWithAGeneratedTextureIsAdopted() => Scenario( fx =>
	{
		// Rebuilding a material reads its own generated texture's previous build, which isn't something it
		// depends on: the editor's compile doesn't record it, and the build doesn't expect it
		MaterialWithTexture( fx, "materials/a.vmat", "materials/a_color.tga", Sources.ImageA );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/a.vmat" ) );
		AssertSucceeded( Build( fx ) );
		fx.CopyShipped( Sources.ImageB, "materials/a_color.tga" );
		CompileInPlace( fx, "materials/a.vmat" );
		Builder.Clean( fx );

		var run = Build( fx );

		AssertSucceeded( run );
		CollectionAssert.DoesNotContain( run.Result.Jobs.ToArray(), "materials/a.vmat", run.Result.Log );
		AssertWroteExactly( Build( fx ) );
	} );

	[TestMethod]
	public void ModelsThatReadEachOtherCompileInPlaceAndFinish()
	{
		// Each is the other's base model, so each output records the other's: compiling one brings the other up to
		// date first, which finds the first one already being compiled instead of starting it again
		using var fx = new ContentFixture();
		MaterialWithTexture( fx, "materials/m.vmat", "materials/m_color.tga", Sources.ImageA );
		fx.CopyShipped( $"{Sources.BonnetFolder}/easter_bonnet.fbx", "models/bonnet.fbx" );
		fx.CopyShipped( $"{Sources.BonnetFolder}/easter_bonnet_citizen_idle_anim.fbx", "models/bonnet_idle.fbx" );
		fx.Write( "models/a.vmdl", Sources.Mesh( "models/bonnet.fbx", "materials/m.vmat" ) );
		fx.Write( "models/b.vmdl", Sources.MeshAnimated( "models/bonnet.fbx", "materials/m.vmat", "models/a.vmdl", "models/bonnet_idle.fbx" ) );
		AssertSucceeded( fx.Build( Builder ) );

		fx.Write( "models/a.vmdl", Sources.MeshAnimated( "models/bonnet.fbx", "materials/m.vmat", "models/b.vmdl", "models/bonnet_idle.fbx" ) );
		CompileInPlace( fx, "models/a.vmdl" );

		var log = CompileInPlace( fx, "models/b.vmdl" );
		StringAssert.Contains( log, "read each other's compiled output", log );
	}

	[TestMethod]
	public void OldFormatModelIsDescribedByCompilingIt()
	{
		// No describe pass exists for the pre-model-document format: its compile runs in describe mode and
		// reports what it actually reads. Its mesh names core's material; the root's own copy of that
		// material, never built yet, is the one it reads.
		using var fx = new ContentFixture();
		fx.CopyShipped( "core/models/editor/scripted_sequence.vmdl", "models/legacy.vmdl" );
		MaterialWithTexture( fx, "materials/models/editor/scripted_sequence.vmat", "materials/legacy_color.tga", Sources.ImageA );

		var run = fx.Build( Builder );

		AssertSucceeded( run );
		var jobs = run.Result.Jobs.ToList();
		Assert.IsTrue( jobs.IndexOf( "materials/models/editor/scripted_sequence.vmat" ) < jobs.IndexOf( "models/legacy.vmdl" ), run.Result.Log );
		AssertWroteExactly( fx.Build( Builder ) );

		// It reads the material order only, so editing the material leaves it alone
		fx.Write( "materials/models/editor/scripted_sequence.vmat", Sources.Material( "TextureColor", "materials/legacy_color.tga", "g_flMetalness", "0.5" ) );
		run = fx.Build( Builder );

		AssertSucceeded( run );
		CollectionAssert.AreEqual( new[] { "materials/models/editor/scripted_sequence.vmat" }, run.Result.Jobs.ToArray(), run.Result.Log );
	}

	[TestMethod]
	public void ModelReadsAMaterialFromAnotherRoot()
	{
		// A library root mounted before the project, built as one graph: the compiler sees both
		using var fx = new ContentFixture();
		var lib = new ContentRoot( fx.Mod + "lib", Path.Combine( fx.Parent, "lib" ), Path.Combine( fx.Parent, "libstate" ) );
		var roots = new[] { lib, new ContentRoot( fx.Mod, fx.Root, Path.Combine( fx.Parent, "state" ) ) };
		void WriteLib( string rel, string text )
		{
			Directory.CreateDirectory( Path.GetDirectoryName( Path.Combine( lib.Path, rel ) ) );
			File.WriteAllText( Path.Combine( lib.Path, rel ), text.ReplaceLineEndings( "\r\n" ) );
		}
		BuildReport Build() => ManagedBuilder.Build( new BuildRequest { Roots = roots, MaxJobs = 4 } );

		Directory.CreateDirectory( Path.Combine( lib.Path, "materials" ) );
		File.Copy( Path.Combine( ContentFixture.Engine, Sources.ImageA ), Path.Combine( lib.Path, "materials/libm_color.tga" ) );
		WriteLib( "materials/libm.vmat", Sources.Material( "TextureColor", "materials/libm_color.tga" ) );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/libm.vmat" ) );
		string LibM = $"{lib.Mod}:materials/libm.vmat", Box = $"{fx.Mod}:models/box.vmdl";

		var report = Build();
		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( 0, report.DescribeMisses, report.Log.ToString() );
		Assert.IsTrue( report.Jobs.IndexOf( LibM ) < report.Jobs.IndexOf( Box ), report.Log.ToString() );
		Assert.AreEqual( 0, Build().Jobs.Count );

		// The model reads it order only: a material edit rebuilds just the material
		WriteLib( "materials/libm.vmat", Sources.Material( "TextureColor", "materials/libm_color.tga", "g_flMetalness", "0.5" ) );
		report = Build();
		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { LibM }, report.Jobs.ToArray(), report.Log.ToString() );

		WriteLib( "materials/libm.vmat", Sources.Broken );
		report = Build();
		CollectionAssert.AreEqual( new[] { LibM }, report.Failed.ToArray(), report.Log.ToString() );
	}

	static ContentFixture BuiltTexture( IContentBuilder builder, out byte[] good, out int nonStreaming )
	{
		var fx = new ContentFixture();
		fx.CopyShipped( Sources.ImageC, "textures/t.tga" );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );
		AssertSucceeded( fx.Build( builder ) );
		good = fx.Read( "textures/t.vtex_c" );
		nonStreaming = (int)BitConverter.ToUInt32( good, 0 );
		return fx;
	}

	static ContentFixture BuiltTextureWithoutRecord( IContentBuilder builder, out byte[] good, out int nonStreaming )
	{
		var fx = BuiltTexture( builder, out good, out nonStreaming );
		builder.Clean( fx );
		return fx;
	}

	[TestMethod]
	public void RecordedOutputCutShortInItsStreamedDataIsRebuilt()
	{
		// Its edit info still validates, so only the record's hash tells
		using var fx = BuiltTexture( Builder, out var good, out var nonStreaming );
		Assert.IsTrue( nonStreaming < good.Length, "The texture has streamed data" );
		File.WriteAllBytes( fx.Path( "textures/t.vtex_c" ), good[..((nonStreaming + good.Length) / 2)] );

		var run = fx.Build( Builder );

		AssertSucceeded( run );
		CollectionAssert.AreEqual( new[] { "textures/t.vtex" }, run.Result.Jobs.ToArray(), run.Result.Log );
		CollectionAssert.AreEqual( good, fx.Read( "textures/t.vtex_c" ) );
	}

	[TestMethod]
	public void OutputCutShortInsideItsEditInfoIsNotAdopted()
	{
		using var fx = BuiltTextureWithoutRecord( Builder, out var good, out var nonStreaming );
		File.WriteAllBytes( fx.Path( "textures/t.vtex_c" ), good[..(nonStreaming - 1)] );

		var run = fx.Build( Builder );

		AssertSucceeded( run );
		CollectionAssert.AreEqual( new[] { "textures/t.vtex" }, run.Result.Jobs.ToArray(), run.Result.Log );
		CollectionAssert.AreEqual( good, fx.Read( "textures/t.vtex_c" ) );
	}

	[TestMethod]
	public void ProjectMaterialShadowingALibraryOneIsWhatTheModelReads()
	{
		// The compiler mounts the project after the library, so the project's copy of a material wins, and adding it
		// rebuilds a model that read the library's; editing either copy doesn't, the model reads it order only
		using var fx = new ContentFixture();
		var lib = new ContentRoot( fx.Mod + "lib", Path.Combine( fx.Parent, "lib" ), Path.Combine( fx.Parent, "libstate" ) );
		var roots = new[] { lib, new ContentRoot( fx.Mod, fx.Root, Path.Combine( fx.Parent, "state" ) ) };
		BuildReport Build() => ManagedBuilder.Build( new BuildRequest { Roots = roots, MaxJobs = 4 } );
		void WriteLib( string text ) => File.WriteAllText( Path.Combine( lib.Path, "materials/m.vmat" ), text.ReplaceLineEndings( "\r\n" ) );
		string ProjM = $"{fx.Mod}:materials/m.vmat", Box = $"{fx.Mod}:models/box.vmdl";

		Directory.CreateDirectory( Path.Combine( lib.Path, "materials" ) );
		File.Copy( Path.Combine( ContentFixture.Engine, Sources.ImageA ), Path.Combine( lib.Path, "materials/m_color.tga" ) );
		WriteLib( Sources.Material( "TextureColor", "materials/m_color.tga" ) );
		fx.Write( "models/box.vmdl", Sources.Box( "materials/m.vmat" ) );
		Assert.IsTrue( Build().Success );

		MaterialWithTexture( fx, "materials/m.vmat", "materials/m_color.tga", Sources.ImageB );
		var report = Build();
		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( 0, report.DescribeMisses, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { ProjM, Box }, report.Jobs.ToArray(), report.Log.ToString() );

		// The library's copy is hidden now: it isn't built
		WriteLib( Sources.Material( "shaders/unlit.shader", ["TextureColor", "materials/m_color.tga"] ) );
		report = Build();
		Assert.IsTrue( report.Success, report.Log.ToString() );
		Assert.AreEqual( 0, report.Jobs.Count, report.Log.ToString() );

		fx.Write( "materials/m.vmat", Sources.Material( "shaders/unlit.shader", ["TextureColor", "materials/m_color.tga"] ) );
		report = Build();
		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { ProjM }, report.Jobs.ToArray(), report.Log.ToString() );
	}

	[TestMethod]
	public void SoundIsHiddenByALaterRootsSoundOfAnotherFormat()
	{
		// sounds/s.wav in the library and sounds/s.mp3 in the project both make sounds/s.vsnd_c; the compiler only
		// ever finds the project's, so the library's isn't built
		using var fx = new ContentFixture();
		var lib = new ContentRoot( fx.Mod + "lib", Path.Combine( fx.Parent, "lib" ), Path.Combine( fx.Parent, "libstate" ) );
		Directory.CreateDirectory( Path.Combine( lib.Path, "sounds" ) );
		File.Copy( Path.Combine( ContentFixture.Engine, Sources.SoundA ), Path.Combine( lib.Path, "sounds/s.wav" ) );
		fx.CopyShipped( "addons/menu/Assets/sounds/box_open.mp3", "sounds/s.mp3" );

		var report = ManagedBuilder.Build( new BuildRequest { Roots = [lib, new ContentRoot( fx.Mod, fx.Root, Path.Combine( fx.Parent, "state" ) )], MaxJobs = 4 } );

		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.AreEqual( new[] { $"{fx.Mod}:sounds/s.mp3" }, report.Jobs.ToArray(), report.Log.ToString() );
		Assert.IsTrue( fx.Exists( "sounds/s.vsnd_c" ) );
		Assert.IsFalse( File.Exists( Path.Combine( lib.Path, "sounds/s.vsnd_c" ) ) );
	}

	[TestMethod]
	public void EngineModelBuiltWithAProjectsMaterialIsRebuiltWithoutThatProject()
	{
		// The fixture root plays the engine root; while a project shadows its material, its model is built against
		// the project's. Built without the project, it must match a clean engine-only build, and back again.
		using var fx = new ContentFixture();
		var engine = new ContentRoot( fx.Mod, fx.Root, Path.Combine( fx.Parent, "state" ) );
		var project = new ContentRoot( fx.Mod + "proj", Path.Combine( fx.Parent, "proj" ), Path.Combine( fx.Parent, "projstate" ) );
		static BuildReport Build( params ContentRoot[] roots ) => ManagedBuilder.Build( new BuildRequest { Roots = roots, MaxJobs = 4 } );

		MaterialWithTexture( fx, "materials/x.vmat", "materials/x_color.tga", Sources.ImageA );
		fx.Write( "models/m.vmdl", Sources.Box( "materials/x.vmat" ) );
		Directory.CreateDirectory( Path.Combine( project.Path, "materials" ) );
		File.Copy( Path.Combine( ContentFixture.Engine, Sources.ImageB ), Path.Combine( project.Path, "materials/x_color.tga" ) );
		File.WriteAllText( Path.Combine( project.Path, "materials/x.vmat" ), Sources.Material( "shaders/unlit.shader", ["TextureColor", "materials/x_color.tga"] ).ReplaceLineEndings( "\r\n" ) );

		var report = Build( engine, project );
		Assert.IsTrue( report.Success, report.Log.ToString() );
		var withProject = fx.Read( "models/m.vmdl_c" );

		report = Build( engine );
		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.Contains( report.Jobs.ToArray(), "models/m.vmdl", report.Log.ToString() );
		CollectionAssert.AreNotEqual( withProject, fx.Read( "models/m.vmdl_c" ) );
		fx.AssertMatchesCleanBuild( Builder );

		report = Build( engine, project );
		Assert.IsTrue( report.Success, report.Log.ToString() );
		CollectionAssert.Contains( report.Jobs.ToArray(), $"{fx.Mod}:models/m.vmdl", report.Log.ToString() );
		CollectionAssert.AreEqual( withProject, fx.Read( "models/m.vmdl_c" ) );
		Assert.AreEqual( 0, Build( engine, project ).Jobs.Count );
	}

	[TestMethod]
	public void MapsAreLeftToHammer()
	{
		using var fx = new ContentFixture();
		fx.CopyShipped( "core/maps/templates/empty_box.vmap", "maps/box.vmap" );
		fx.CopyShipped( "core/maps/templates/empty_box.vmap.meta", "maps/box.vmap.meta" );
		fx.Write( "maps/box.vpk", "built by hammer" );
		fx.CopyShipped( Sources.ImageA, "textures/t.tga" );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );

		var run = fx.Build( Builder );

		AssertSucceeded( run );
		CollectionAssert.AreEqual( new[] { "textures/t.vtex_c" }, run.Written, run.ToString() );
		Assert.AreEqual( "built by hammer", File.ReadAllText( fx.Path( "maps/box.vpk" ) ) );
	}

	[TestMethod]
	public void AnimgraphReadsOnlyItsSources()
	{
		// The graph names its model, but that's a reference: it compiles without it
		using var fx = new ContentFixture();
		fx.CopyShipped( $"{Sources.BonnetFolder}/easter_bonnet_anim.vanmgrph", "models/bonnet.vanmgrph" );

		var run = fx.Build( Builder );

		AssertSucceeded( run );
		CollectionAssert.AreEqual( new[] { "models/bonnet.vanmgrph_c" }, run.Written, run.ToString() );
		Assert.AreEqual( 0, fx.Build( Builder ).Result.Jobs.Count );
	}

	[TestMethod, TestCategory( "Slow" )]
	public void BaseModelIsBuiltBeforeTheModelsAnimatedWithIt()
	{
		using var fx = new ContentFixture();
		MaterialWithTexture( fx, "materials/m.vmat", "materials/m_color.tga", Sources.ImageA );
		fx.CopyShipped( $"{Sources.BonnetFolder}/easter_bonnet.fbx", "models/bonnet.fbx" );
		fx.CopyShipped( $"{Sources.BonnetFolder}/easter_bonnet_citizen_idle_anim.fbx", "models/bonnet_idle.fbx" );
		fx.Write( "models/bonnet.vmdl", Sources.Mesh( "models/bonnet.fbx", "materials/m.vmat" ) );
		fx.Write( "models/bonnet_idle.vmdl", Sources.Animated( "models/bonnet.vmdl", "models/bonnet_idle.fbx" ) );

		var run = fx.Build( Builder );

		AssertSucceeded( run );
		AssertWrote( run, "models/bonnet.vmdl_c", "models/bonnet_idle.vmdl_c" );
		var order = run.Result.Jobs.ToList();
		Assert.IsTrue( order.IndexOf( "models/bonnet.vmdl" ) < order.IndexOf( "models/bonnet_idle.vmdl" ), run.Result.Log );

		// A new base model rebuilds what is animated with it
		fx.Write( "models/bonnet.vmdl", Sources.Mesh( "models/bonnet.fbx", "materials/default.vmat" ) );
		run = fx.Build( Builder );

		AssertSucceeded( run );
		CollectionAssert.IsSubsetOf( new[] { "models/bonnet.vmdl_c" }, run.Changed, run.ToString() );
		CollectionAssert.Contains( run.Result.Jobs.ToArray(), "models/bonnet_idle.vmdl", run.Result.Log );
		AssertWroteExactly( fx.Build( Builder ) );
	}

	/// <summary>
	/// A prepared copy of a shipped root: the menu's sources and compiled outputs, built once with the current compilers,
	/// then its build records removed. The build that finds those outputs without records adopts every one as it is,
	/// writing nothing, and the build after that has nothing to do. (Outputs the repository has built with older compiler
	/// code are the preparing build's to bring up to date, not the adoption's.)
	/// </summary>
	[TestMethod, TestCategory( "Slow" )]
	public void PreparedShippedRootIsAdoptedUnchanged()
	{
		// Its outputs name its mod (a scene's shared generated textures), so the copy keeps it
		using var fx = new ContentFixture( "menu" );
		var shipped = Path.Combine( ContentFixture.Engine, "addons", "menu", "Assets" );
		foreach ( var file in Directory.EnumerateFiles( shipped, "*", SearchOption.AllDirectories ) )
		{
			var rel = Path.GetRelativePath( shipped, file );
			Directory.CreateDirectory( Path.GetDirectoryName( fx.Path( rel ) ) );
			File.Copy( file, fx.Path( rel ) );
			File.SetLastWriteTimeUtc( fx.Path( rel ), File.GetLastWriteTimeUtc( file ) );
		}

		// With the textures its scenes generated, which the content build put in the menu's transient folder
		var shippedTransients = Path.Combine( ContentFixture.Engine, "addons", "menu", "Transients" );
		foreach ( var file in Directory.Exists( shippedTransients ) ? Directory.EnumerateFiles( shippedTransients, "*", SearchOption.AllDirectories ) : [] )
		{
			var copy = Path.Combine( fx.Parent, "transient", Path.GetRelativePath( shippedTransients, file ) );
			Directory.CreateDirectory( Path.GetDirectoryName( copy ) );
			File.Copy( file, copy );
			File.SetLastWriteTimeUtc( copy, File.GetLastWriteTimeUtc( file ) );
		}

		// Preparing: the current compilers bring what the repository has up to date
		AssertSucceeded( fx.Build( Builder ) );
		var transient = Path.Combine( fx.Parent, "transient" );
		Dictionary<string, string> Transients() => Directory.Exists( transient )
			? Directory.EnumerateFiles( transient, "*", SearchOption.AllDirectories ).ToDictionary( x => x, x => Convert.ToHexString( System.Security.Cryptography.SHA1.HashData( File.ReadAllBytes( x ) ) ) )
			: [];
		var transients = Transients();

		// Adopting: the same outputs, with no records
		Builder.Clean( fx );
		var run = fx.Build( Builder );

		AssertSucceeded( run );
		Assert.AreEqual( 0, run.Written.Length + run.Changed.Length + run.Removed.Length, $"Adoption rewrote outputs:\n{run}\n{run.Result.Log}" );
		CollectionAssert.AreEquivalent( transients.ToList(), Transients().ToList(), "Adoption rewrote generated textures" );

		Assert.AreEqual( 0, fx.Build( Builder ).Result.Jobs.Count );
	}
}

/// <summary>
/// Sandbox.ContentBuild one job at a time (MaxJobs 1), still in content workers. The live editor is tested by
/// EditorStartupContentBuild.
/// </summary>
[TestClass, DoNotParallelize, TestCategory( "Slow" )]
public class ManagedInlineContentBuild : ContentBuildScenarios
{
	protected override IContentBuilder Builder { get; } = new ManagedBuilder { MaxJobs = 1 };
}
