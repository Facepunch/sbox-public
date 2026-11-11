using Editor;
using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;

namespace ResourceTests;

[TestClass]
public partial class AnimationGraphEditorTests
{
	static PackageLoader _loader;
	static PackageLoader.Enroller _enroller;
	static Assembly _assembly;
	static Sandbox.Internal.TypeLibrary _previousEditorTypes;
	string _assetsPath;
	string _folder;

	[ClassInitialize]
	public static async Task InitializeEditor( TestContext context )
	{
		_previousEditorTypes = GlobalToolsNamespace.EditorTypeLibrary;
		ToolsDll.CreateEditorTypeLibrary();
		_loader = new PackageLoader( "ag2_guid_editor_tests", typeof( AnimationGraphEditorTests ).Assembly ) { ToolsMode = true };
		_enroller = _loader.CreateEnroller( "ag2_guid_editor_tests" );
		Project.Clear();
		Project.AddFromFile( "addons/tools/.sbproj" );
		var project = Project.AddFromFile( "editor/AnimGraph/.sbproj" );
		await Project.SyncWithPackageManager();
		Assert.IsTrue( await Project.CompileAsync(), "Editor projects failed to compile" );
		Assert.IsTrue( _enroller.LoadPackage( project.Package.FullIdent, true ) );
		_loader.Tick();
		foreach ( var loaded in _enroller.GetLoadedAssemblies() )
			GlobalToolsNamespace.EditorTypeLibrary.AddAssembly( loaded.Assembly, true );
		_assembly = _enroller.GetLoadedAssemblies().Select( loaded => loaded.Assembly )
			.Single( assembly => assembly.GetType( "Editor.AnimGraph.Data" ) is not null );
	}

	[ClassCleanup]
	public static void CleanupEditor()
	{
		_enroller?.Dispose();
		_loader?.Dispose();
		GlobalToolsNamespace.EditorTypeLibrary = _previousEditorTypes;
		Sandbox.Internal.TypeLibrary.Editor = _previousEditorTypes;
		Project.Clear();
	}

	[TestInitialize]
	public void InitializeFiles()
	{
		var project = Project.AddFromFile( "unittest/addons/resources/.sbproj" );
		_assetsPath = project.GetAssetsPath();
		_folder = $"ag2_editor_guid_{Guid.NewGuid():N}";
		Directory.CreateDirectory( Path.Combine( _assetsPath, _folder ) );
		NativeEngine.FullFileSystem.AddProjectPath( "unittest", _assetsPath.ToLowerInvariant() );
	}

	[TestCleanup]
	public void CleanupFiles()
	{
		foreach ( var resource in _importResources.Distinct() )
		{
			Game.Resources.Unregister( resource );
			resource.Destroy();
		}
		Directory.Delete( Path.Combine( _assetsPath, _folder ), recursive: true );
	}

	Type EditorType( string name ) => _assembly.GetType( $"Editor.AnimGraph.{name}", throwOnError: true );

	static object Call( object target, string name, params object[] arguments )
		=> target.GetType().GetMethod( name, arguments.Select( argument => argument.GetType() ).ToArray() ).Invoke( target, arguments );

	object Load( string type, string file )
		=> EditorType( type ).GetMethod( "Load", BindingFlags.Public | BindingFlags.Static ).Invoke( null, [file] );

	JsonObject ReadDocument( string file )
		=> (JsonObject)EditorType( "Kv3Document" ).GetMethod( "Read", BindingFlags.Public | BindingFlags.Static ).Invoke( null, [file] );

	ResourceId WriteReference( string name )
	{
		var id = new ResourceId { Guid = Guid.NewGuid(), Path = $"{_folder}/{name}" };
		var file = Path.Combine( _assetsPath, id.Path );
		File.WriteAllText( file, "{}" );
		File.WriteAllText( file + ".meta", $$"""{"guid":"{{id.Guid}}"}""" );
		NativeEngine.g_pResourceSystem.NotifyMetaFileChanged( id.Path, true );
		return id;
	}

	string MoveReference( ResourceId id )
	{
		var moved = $"{_folder}/moved_{Path.GetFileName( id.Path )}";
		File.Move( Path.Combine( _assetsPath, id.Path ), Path.Combine( _assetsPath, moved ) );
		File.Move( Path.Combine( _assetsPath, id.Path + ".meta" ), Path.Combine( _assetsPath, moved + ".meta" ) );
		NativeEngine.g_pResourceSystem.NotifyMetaFileChanged( id.Path, false );
		NativeEngine.g_pResourceSystem.NotifyMetaFileChanged( moved, true );
		return moved;
	}

	[TestMethod]
	public void OpenLegacyClipRetainsCapturedGuidsAcrossMovesAndPreservesExternalEdits()
	{
		var skeleton = WriteReference( "original.skel" );
		var preview = WriteReference( "preview.vmdl" );
		var secondary = WriteReference( "secondary.anim" );
		var file = Path.Combine( _assetsPath, _folder, "editable.anim" );
		var source = new JsonObject
		{
			["m_animationSkeletonName"] = skeleton.Path,
			["m_previewModel"] = preview.Path,
			["m_secondaryAnimations"] = new JsonArray( secondary.Path ),
			["m_sourceFilename"] = "",
			["unknown_import_option"] = 42
		};
		File.WriteAllText( file, source.ToJsonString() );
		var document = Load( "ClipDocument", file );
		Assert.IsNotNull( document );
		var movedSkeleton = MoveReference( skeleton );
		var movedPreview = MoveReference( preview );
		var movedSecondary = MoveReference( secondary );
		source["external_edit"] = "keep";
		File.WriteAllText( file, source.ToJsonString() );

		Assert.IsTrue( (bool)Call( document, "Save" ) );
		var saved = ReadDocument( file );
		foreach ( var (node, id, path) in new[]
		{
			(saved["m_animationSkeletonName"], skeleton, movedSkeleton),
			(saved["m_previewModel"], preview, movedPreview),
			(saved["m_secondaryAnimations"][0], secondary, movedSecondary)
		} )
		{
			var actual = Json.FromNode<ResourceId>( node );
			Assert.AreEqual( id.Guid, actual.Guid );
			Assert.AreEqual( path, actual.Path );
		}
		Assert.AreEqual( 42, saved["unknown_import_option"].GetValue<int>() );
		Assert.AreEqual( "keep", saved["external_edit"].GetValue<string>() );

		var replacement = WriteReference( "replacement.skel" );
		saved["m_animationSkeletonName"] = replacement.Path;
		File.WriteAllText( file, saved.ToJsonString() );
		Assert.IsTrue( (bool)Call( document, "Save" ) );
		Assert.AreEqual( replacement.Guid, Json.FromNode<ResourceId>( ReadDocument( file )["m_animationSkeletonName"] ).Guid );

		var copy = Path.Combine( _assetsPath, _folder, "copy.anim" );
		Assert.IsTrue( (bool)Call( document, "SaveAs", copy ) );
		Assert.AreEqual( skeleton.Guid, Json.FromNode<ResourceId>( ReadDocument( copy )["m_animationSkeletonName"] ).Guid );
	}

	[TestMethod]
	public void ClipReferencesCanBeClearedAfterLoadingAnUnavailableGuidOnlyIdentity()
	{
		var file = Path.Combine( _assetsPath, _folder, "clear.anim" );
		File.WriteAllText( file, new JsonObject
		{
			["m_animationSkeletonName"] = Json.ToNode( new ResourceId { Guid = Guid.NewGuid(), Path = "" } ),
			["m_previewModel"] = Json.ToNode( new ResourceId { Guid = Guid.NewGuid(), Path = "" } )
		}.ToJsonString() );
		var document = Load( "ClipDocument", file );
		Assert.IsNotNull( document );
		foreach ( var property in new[] { "SkeletonName", "PreviewModel" } )
			document.GetType().GetProperty( property ).SetValue( document, "" );
		Assert.IsTrue( (bool)Call( document, "Save" ) );
		var saved = ReadDocument( file );
		Assert.IsTrue( Json.FromNode<ResourceId>( saved["m_animationSkeletonName"] ).IsEmpty );
		Assert.IsTrue( Json.FromNode<ResourceId>( saved["m_previewModel"] ).IsEmpty );
	}

	[TestMethod]
	public void SkeletonUndoAndSaveKeepUnavailableGuidOnlyPreviewsAndUnknownFields()
	{
		var preview = new ResourceId { Guid = Guid.NewGuid(), Path = "" };
		var file = Path.Combine( _assetsPath, _folder, "preview.skel" );
		File.WriteAllText( file, new JsonObject { ["m_previewModel"] = Json.ToNode( preview ), ["future_option"] = 17 }.ToJsonString() );
		var document = Load( "SkeletonDocument", file );
		Assert.IsNotNull( document );
		var snapshot = (string)Call( document, "Snapshot" );
		document.GetType().GetProperty( "PreviewModel" ).SetValue( document, "" );
		Call( document, "Restore", snapshot );
		Assert.IsTrue( (bool)Call( document, "Save" ) );
		var saved = ReadDocument( file );
		Assert.AreEqual( preview.Guid, Json.FromNode<ResourceId>( saved["m_previewModel"] ).Guid );
		Assert.AreEqual( 17, saved["future_option"].GetValue<int>() );
	}

	[TestMethod]
	public void GraphClonePreservesNodeVariationAndAttachmentIdentities()
	{
		var model = Json.ToNode( new ResourceId { Guid = Guid.NewGuid(), Path = "" } );
		var skeleton = Json.ToNode( new ResourceId { Guid = Guid.NewGuid(), Path = "" } );
		var animation = WriteReference( "walk.anim" );
		var child = WriteReference( "child.ag" );
		var nodes = new JsonArray();
		foreach ( var name in new[] { "AnimationClipNode", "AnimationPoseNode", "TimeControlledAnimationClipNode" } )
		{
			nodes.Add( new JsonObject
			{
				["_class"] = name,
				["Identifier"] = Guid.NewGuid().ToString(),
				["DefaultVariationData"] = new JsonObject { ["AnimationName"] = animation.Path },
				["Overrides"] = new JsonArray( new JsonObject
				{
					["VariationID"] = "Alternate",
					["VariationData"] = new JsonObject { ["AnimationName"] = Json.ToNode( new ResourceId { Guid = Guid.NewGuid(), Path = "" } ) }
				} )
			} );
		}
		nodes.Add( new JsonObject
		{
			["_class"] = "InternalReferencedGraphNode",
			["Identifier"] = Guid.NewGuid().ToString(),
			["DefaultVariationData"] = new JsonObject { ["GraphPath"] = child.Path, ["GraphVariation"] = "Fast" }
		} );
		var source = new JsonObject
		{
			["Model"] = model,
			["Skeleton"] = skeleton,
			["PreviewAttachments"] = new JsonArray( new JsonObject { ["Model"] = model.DeepClone() } ),
			["variations"] = new JsonArray( new JsonObject
			{
				["ID"] = "Alternate",
				["Model"] = model.DeepClone(),
				["Skeleton"] = skeleton.DeepClone()
			} ),
			["nodes"] = nodes
		};
		var graph = Activator.CreateInstance( EditorType( "Data" ) );
		Call( graph, "Deserialize", source.ToJsonString() );
		var movedAnimation = MoveReference( animation );
		var movedChild = MoveReference( child );
		var clone = Call( graph, "Clone", true );
		var saved = JsonNode.Parse( (string)Call( clone, "Serialize", false ) );
		Assert.IsTrue( JsonNode.DeepEquals( model, saved["Model"] ) );
		Assert.IsTrue( JsonNode.DeepEquals( model, saved["PreviewAttachments"][0]["Model"] ) );
		Assert.IsTrue( JsonNode.DeepEquals( skeleton, saved["variations"][0]["Skeleton"] ) );
		for ( var i = 0; i < 3; i++ )
		{
			var node = saved["nodes"][i];
			var actual = Json.FromNode<ResourceId>( node["DefaultVariationData"]["AnimationName"] );
			Assert.AreEqual( animation.Guid, actual.Guid );
			Assert.AreEqual( movedAnimation, actual.Path );
			Assert.IsTrue( JsonNode.DeepEquals( nodes[i]["Overrides"][0]["VariationData"]["AnimationName"], node["Overrides"][0]["VariationData"]["AnimationName"] ) );
		}
		Assert.AreEqual( child.Guid, Json.FromNode<ResourceId>( saved["nodes"][3]["DefaultVariationData"]["GraphPath"] ).Guid );
		Assert.AreEqual( movedChild, Json.FromNode<ResourceId>( saved["nodes"][3]["DefaultVariationData"]["GraphPath"] ).Path );
		Assert.AreEqual( "Fast", saved["nodes"][3]["DefaultVariationData"]["GraphVariation"].GetValue<string>() );

		var defaultVariation = clone.GetType().GetProperty( "DefaultVariation" ).GetValue( clone );
		defaultVariation.GetType().GetProperty( "TargetModel" ).SetValue( defaultVariation, null );
		var attachments = (System.Collections.IList)defaultVariation.GetType().GetProperty( "PreviewAttachments" ).GetValue( defaultVariation );
		attachments[0].GetType().GetProperty( "Model" ).SetValue( attachments[0], null );
		var cleared = JsonNode.Parse( (string)Call( clone, "Serialize", false ) );
		Assert.IsTrue( Json.FromNode<ResourceId>( cleared["Model"] ).IsEmpty );
		Assert.IsNull( cleared["PreviewAttachments"] );
	}
}
