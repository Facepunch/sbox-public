using Editor;
using Sandbox.Engine;
using Sandbox.Resources;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace ResourceTests;

public partial class AnimationGraphEditorTests
{
	readonly List<Resource> _importResources = [];

	static T Get<T>( object target, string name ) => (T)target.GetType().GetProperty( name ).GetValue( target );
	static void Set( object target, string name, object value ) => target.GetType().GetProperty( name ).SetValue( target, value );
	static IList Entries( object batch ) => Get<IList>( batch, "Entries" );

	string WriteSource( string name, string content = "animation source" )
	{
		var file = Path.Combine( _assetsPath, _folder, name );
		Directory.CreateDirectory( Path.GetDirectoryName( file ) );
		File.WriteAllText( file, content );
		return file;
	}

	object CreateImportBatch()
	{
		var path = WriteSource( "rig.skel", "{}" );
		var guid = Guid.NewGuid();
		File.WriteAllText( path + ".meta", $$"""{"guid":"{{guid}}"}""" );
		var writer = new ResourceWriter();
		writer.RegisterAdditionalBlock( 0x44495547, guid.ToByteArray() );
		writer.SetDataBlock( AnimationResourceGuidTests.CompileData( AnimationResourceGuidTests.SkeletonData() ) );
		File.WriteAllBytes( path + "_c", writer.ToArray() );
		var relative = $"{_folder}/rig.skel";
		NativeEngine.g_pResourceSystem.NotifyMetaFileChanged( relative, true );
		var skeleton = Sandbox.Animation.Skeleton.Load( relative );
		Assert.IsNotNull( skeleton );
		_importResources.Add( skeleton );
		return Activator.CreateInstance( EditorType( "AnimationImportBatch" ), _assetsPath, Path.Combine( _assetsPath, _folder ), relative );
	}

	static void AddSources( object batch, params string[] sources ) => batch.GetType().GetMethod( "AddSources" ).Invoke( batch, [sources] );

	[TestMethod]
	public void ImportQueueDeduplicatesSourcesAndRequiresExplicitConflictResolution()
	{
		var batch = CreateImportBatch();
		var first = WriteSource( "first/walk.smd" );
		var second = WriteSource( "second/WALK.smd" );
		AddSources( batch, first, first, second );
		Assert.AreEqual( 2, Entries( batch ).Count );
		var a = Entries( batch )[0];
		var b = Entries( batch )[1];
		Assert.IsNotNull( Call( batch, "ValidateEntry", a ) );
		Assert.IsNotNull( Call( batch, "ValidateEntry", b ) );
		Set( b, "Included", false );
		Assert.IsNull( Call( batch, "ValidateEntry", a ) );
		Set( b, "Included", true );
		Set( b, "Name", "run" );
		Assert.IsNull( Call( batch, "ValidateEntry", a ) );
		Assert.IsNull( Call( batch, "ValidateEntry", b ) );

		foreach ( var suffix in new[] { "", "_c", ".meta" } )
		{
			var existing = WriteSource( "walk.anim" + suffix, "existing asset" );
			Assert.IsNotNull( Call( batch, "ValidateEntry", a ) );
			Assert.AreEqual( "existing asset", File.ReadAllText( existing ) );
			File.Delete( existing );
		}
		Set( a, "Name", "../outside" );
		Assert.IsNotNull( Call( batch, "ValidateEntry", a ) );
		Set( batch, "OutputDirectory", Path.Combine( _assetsPath, ".." ) );
		Assert.IsNotNull( Call( batch, "ValidateSettings" ) );
	}

	[TestMethod]
	public void SavingBatchClipsAppliesSharedSettingsAndPreservesPerFileTrimming()
	{
		var batch = CreateImportBatch();
		AddSources( batch, WriteSource( "pose.smd" ), WriteSource( "motion.smd" ) );
		var pose = Get<object>( Entries( batch )[0], "Document" );
		Set( pose, "StartFrame", 0 );
		Set( pose, "EndFrame", 0 );
		Set( Get<object>( batch, "Settings" ), "ImportScale", 2f );
		foreach ( var entry in Entries( batch ) )
		{
			Call( batch, "SaveEntry", entry );
			Assert.AreEqual( 2f, ReadDocument( Get<string>( entry, "OutputPath" ) )["m_flImportScale"].GetValue<float>() );
		}
		var first = ReadDocument( Get<string>( Entries( batch )[0], "OutputPath" ) );
		var second = ReadDocument( Get<string>( Entries( batch )[1], "OutputPath" ) );
		Assert.AreEqual( 0, first["m_nStartFrame"].GetValue<int>() );
		Assert.AreEqual( 0, first["m_nEndFrame"].GetValue<int>() );
		Assert.AreEqual( -1, second["m_nStartFrame"].GetValue<int>() );
		Assert.AreEqual( -1, second["m_nEndFrame"].GetValue<int>() );
	}

	[TestMethod]
	public void RetryingSavedClipsReusesExternalSourcesAndPreservesOtherOutputs()
	{
		var batch = CreateImportBatch();
		var external = Path.Combine( Path.GetTempPath(), $"ag2_import_{Guid.NewGuid():N}.smd" );
		try
		{
			File.WriteAllText( external, "external source" );
			AddSources( batch, external, WriteSource( "other.smd" ) );
			var entry = Entries( batch )[0];
			var other = Entries( batch )[1];
			Call( batch, "SaveEntry", entry );
			Call( batch, "SaveEntry", other );
			var otherPath = Get<string>( other, "OutputPath" );
			var writeTime = File.GetLastWriteTimeUtc( otherPath );
			var content = File.ReadAllText( otherPath );
			var output = Get<string>( entry, "OutputPath" );
			var source = ReadDocument( output )["m_sourceFilename"].GetValue<string>();
			Set( Get<object>( entry, "Document" ), "DurationOverride", 2f );
			Call( batch, "SaveEntry", entry );
			Assert.AreEqual( source, ReadDocument( output )["m_sourceFilename"].GetValue<string>() );
			Assert.AreEqual( 1, Directory.GetFiles( Path.Combine( _assetsPath, _folder, "source" ) ).Length );
			Assert.AreEqual( writeTime, File.GetLastWriteTimeUtc( otherPath ) );
			Assert.AreEqual( content, File.ReadAllText( otherPath ) );

			File.WriteAllText( external, "corrected external source" );
			Call( batch, "SaveEntry", entry );
			var corrected = ReadDocument( output )["m_sourceFilename"].GetValue<string>();
			Assert.AreNotEqual( source, corrected );
			Assert.AreEqual( "corrected external source", File.ReadAllText( Path.Combine( _assetsPath, corrected ) ) );
		}
		finally
		{
			File.Delete( external );
		}
	}

	[TestMethod]
	public async Task CancellingBeforeAClipStartsLeavesNoOutputsAndResetsRunningState()
	{
		var batch = CreateImportBatch();
		AddSources( batch, WriteSource( "one.smd" ), WriteSource( "two.smd" ) );
		Action<object> changed = _ => Set( batch, "CancelRequested", true );
		var imported = await (Task<IReadOnlyList<Asset>>)batch.GetType().GetMethod( "ImportAsync" ).Invoke( batch, [changed] );
		Assert.AreEqual( 0, imported.Count );
		Assert.IsFalse( Get<bool>( batch, "IsRunning" ) );
		foreach ( var entry in Entries( batch ) )
		{
			Assert.IsFalse( Get<bool>( entry, "IsImporting" ) );
			Assert.IsNull( Get<string>( entry, "Error" ) );
			Assert.IsFalse( File.Exists( (string)Call( batch, "GetOutputPath", entry ) ) );
		}
	}
}
