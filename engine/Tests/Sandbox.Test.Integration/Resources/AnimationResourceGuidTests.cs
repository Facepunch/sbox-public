using Sandbox.Animation;
using Sandbox.Engine;
using Sandbox.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace ResourceTests;

[TestClass]
public class AnimationResourceGuidTests
{
	string _assetsPath;
	string _folder;
	readonly List<Resource> _resources = [];

	[TestInitialize]
	public void Initialize()
	{
		Project.Clear();
		var project = Project.AddFromFile( "unittest/addons/resources/.sbproj" );
		_assetsPath = project.GetAssetsPath();
		_folder = $"ag2_guid_{Guid.NewGuid():N}";
		Directory.CreateDirectory( Path.Combine( _assetsPath, _folder ) );
		NativeEngine.FullFileSystem.AddProjectPath( "unittest", _assetsPath.ToLowerInvariant() );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var resource in _resources.Distinct().Reverse() )
		{
			Game.Resources.Unregister( resource );
			if ( resource is Model model && !model.native.IsNull )
				NativeResourceCache.Remove( model.native.GetBindingPtr().ToInt64() );
			resource.Destroy();
		}
		Project.Clear();
		Directory.Delete( Path.Combine( _assetsPath, _folder ), recursive: true );
	}

	T Keep<T>( T resource ) where T : Resource
	{
		Assert.IsNotNull( resource );
		Assert.IsTrue( resource.IsValid );
		_resources.Add( resource );
		return resource;
	}

	string AssetPath( string name ) => $"{_folder}/{name}";

	internal static byte[] CompileData( JsonObject data )
	{
		var kv = NativeEngine.EngineGlue.JsonToKeyValues3( data.ToJsonString() );
		try
		{
			using var buffer = kv.Save();
			return buffer.ToArray();
		}
		finally
		{
			kv.DeleteThis();
		}
	}

	void WriteResource( string path, Guid guid, byte[] data )
	{
		var file = Path.Combine( _assetsPath, path );
		var writer = new ResourceWriter { ResourceVersion = path.EndsWith( ".ag", StringComparison.Ordinal ) ? (ushort)1 : (ushort)0 };
		if ( guid != Guid.Empty )
		{
			File.WriteAllText( file, "{}" );
			File.WriteAllText( file + ".meta", $$"""{"guid":"{{guid}}"}""" );
			writer.RegisterAdditionalBlock( 0x44495547, guid.ToByteArray() );
		}
		writer.SetDataBlock( data );
		File.WriteAllBytes( file + "_c", writer.ToArray() );
		if ( guid != Guid.Empty )
			NativeEngine.g_pResourceSystem.NotifyMetaFileChanged( path, true );
		Game.Resources.RegisterPath( path );
	}

	internal static JsonObject SkeletonData() => JsonNode.Parse( """
		{
			"m_boneIDs":["root"],
			"m_parentIndices":[-1],
			"m_parentSpaceReferencePose":[[0,0,0,1,0,0,0,1]]
		}
		""" ).AsObject();

	internal static JsonObject ClipData( JsonNode skeleton ) => new()
	{
		["m_skeleton"] = skeleton,
		["m_nNumFrames"] = 2,
		["m_flDuration"] = 1,
		["m_compressedPoseData"] = new JsonArray( 0 ),
		["m_compressedPoseOffsets"] = new JsonArray( 0, 0 ),
		["m_rootMotion"] = JsonNode.Parse( """{"m_nNumFrames":2,"m_transforms":[[0,0,0,1,0,0,0,1]]}""" ),
		["m_trackCompressionSettings"] = JsonNode.Parse( """
			[{
				"m_bIsRotationStatic":true,
				"m_bIsTranslationStatic":true,
				"m_bIsScaleStatic":true,
				"m_translationRangeX":{"m_flRangeStart":7},
				"m_scaleRange":{"m_flRangeStart":1}
			}]
			""" )
	};

	[TestMethod]
	public void ColdGuidLoadsResolveMovedSkeletonClipAndNestedGraph()
	{
		var skeletonGuid = Guid.NewGuid();
		var clipGuid = Guid.NewGuid();
		var graphGuid = Guid.NewGuid();
		var skeletonPath = AssetPath( "moved.skel" );
		var clipPath = AssetPath( "moved.anim" );
		var graphPath = AssetPath( "moved.ag" );
		// Only the new files exist: no resident resource or old-path fallback can hide a broken GUID lookup.
		WriteResource( skeletonPath, skeletonGuid, CompileData( SkeletonData() ) );
		WriteResource( clipPath, clipGuid, CompileData( ClipData( Json.ToNode( new ResourceId { Guid = skeletonGuid, Path = AssetPath( "old.skel" ) } ) ) ) );

		var builder = Graph.Builder;
		builder.SkeletonReference = new ResourceId { Guid = skeletonGuid };
		var clip = builder.Clip( new ResourceId { Guid = clipGuid, Path = AssetPath( "old.anim" ) } );
		WriteResource( graphPath, graphGuid, builder.Compile( clip, null, "Default" ) );

		var skeleton = Keep( Skeleton.Load( new ResourceId { Guid = skeletonGuid } ) );
		Assert.AreEqual( skeletonPath, skeleton.Name );
		Assert.AreEqual( 1, skeleton.BoneCount );
		var animation = Keep( Clip.Load( new ResourceId { Guid = clipGuid, Path = "wrong-extension.vmdl" } ) );
		Assert.AreEqual( clipPath, animation.Name );
		Assert.IsTrue( animation.IsBound );
		Assert.AreSame( skeleton, Keep( animation.Skeleton ) );
		Assert.AreEqual( 7f, animation.GetLocalTransform( 0, 0 ).Position.x );

		var graph = Keep( Graph.Load( new ResourceId { Guid = graphGuid } ) );
		Assert.AreEqual( graphPath, graph.Name );
		Assert.AreEqual( clipPath, graph.GetAnimationSlotName( 0 ) );

		var parent = Graph.Builder;
		parent.SkeletonReference = new ResourceId { Guid = skeletonGuid };
		using var nested = parent.Create( parent.ReferencedGraph( new ResourceId { Guid = graphGuid, Path = AssetPath( "old.ag" ) } ) );
		Assert.IsNotNull( nested );
		Assert.IsTrue( nested.IsValid );

		Game.Resources.Unregister( graph );
		graph.Destroy();
		var fresh = Keep( Graph.Load( new ResourceId { Guid = graphGuid } ) );
		Assert.AreNotSame( graph, fresh );
		Assert.AreEqual( graphPath, fresh.Name );
		Assert.AreEqual( clipPath, fresh.GetAnimationSlotName( 0 ) );
	}

	[TestMethod]
	public void LegacyCompiledReferencesStillBindAndSample()
	{
		var skeletonPath = AssetPath( "legacy.skel" );
		WriteResource( skeletonPath, Guid.NewGuid(), CompileData( SkeletonData() ) );
		var clipPath = AssetPath( "legacy.anim" );
		WriteResource( clipPath, Guid.NewGuid(), CompileData( ClipData( JsonValue.Create( skeletonPath ) ) ) );
		var clip = Keep( Clip.Load( clipPath ) );
		Assert.IsTrue( clip.IsBound );
		Assert.AreEqual( skeletonPath, Keep( clip.Skeleton ).Name );
		Assert.AreEqual( 7f, clip.GetLocalTransform( .5f, 0 ).Position.x );
	}

	[TestMethod]
	public void NamedVariationResolvesTheMovedOwnerInsteadOfLoadingItsDefaultGraph()
	{
		var skeletonPath = AssetPath( "variation.skel" );
		WriteResource( skeletonPath, Guid.NewGuid(), CompileData( SkeletonData() ) );
		var clipPath = AssetPath( "variation.anim" );
		WriteResource( clipPath, Guid.NewGuid(), CompileData( ClipData( JsonValue.Create( skeletonPath ) ) ) );
		var ownerPath = AssetPath( "renamed.ag" );
		var ownerGuid = Guid.NewGuid();
		var child = Graph.Builder;
		child.Skeleton = skeletonPath;
		WriteResource( ownerPath, ownerGuid, child.Compile( child.ReferencePose(), null, "Default" ) );
		var variation = Graph.Builder;
		variation.Skeleton = skeletonPath;
		WriteResource( ownerPath + "+Fast.ag", Guid.Empty, variation.Compile(
			variation.Clip( clipPath, new GraphBuilder.ClipSettings { GraphEvents = ["guid_named_variation"] } ), null, "Fast" ) );
		var parent = Graph.Builder;
		parent.Skeleton = skeletonPath;
		var root = parent.ReferencedGraph( new ResourceId { Guid = ownerGuid, Path = AssetPath( "old.ag" ) }, variation: "Fast" );
		var parentPath = AssetPath( "parent.ag" );
		WriteResource( parentPath, Guid.NewGuid(), parent.Compile( root, null, "Default" ) );
		var graph = Keep( Graph.Load( parentPath ) );
		var model = Keep( Model.Builder.AddBone( "root", Vector3.Zero, Rotation.Identity ).Create() );
		var world = new SceneWorld();
		try
		{
			var actor = new SceneModel( world, model, Transform.Zero ) { UseAnimGraph = true };
			actor.Animation.Graph = graph;
			actor.OnSoundEvent = _ => { };
			actor.Update( .016f );
			Assert.IsTrue( actor.Animation.IsValid );
			Assert.IsTrue( actor.Animation.Debug.TryGetNode( root.Index, out var info ) );
			Assert.IsTrue( info.IsValid );
			Assert.AreEqual( 1f, info.Duration );
			Assert.IsTrue( actor.Animation.IsTagActive( "guid_named_variation" ) );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void GraphOverTaskLimitFallsBackToReferencePoseAndRecovers()
	{
		var skeletonPath = AssetPath( "tasklimit.skel" );
		WriteResource( skeletonPath, Guid.NewGuid(), CompileData( SkeletonData() ) );
		var clipPath = AssetPath( "tasklimit.anim" );
		WriteResource( clipPath, Guid.NewGuid(), CompileData( ClipData( JsonValue.Create( skeletonPath ) ) ) );
		var builder = Graph.Builder;
		builder.Skeleton = skeletonPath;
		var depth = builder.FloatParameter( "depth" );
		var depth14 = builder.Clip( clipPath );
		for ( var i = 0; i < 14; i++ )
			depth14 = builder.Blend( depth14, depth14, .5f );
		var depth15 = builder.Blend( depth14, depth14, .5f );
		var root = builder.BlendSpace( depth, looping: false )
			.Add( 0f, builder.Clip( clipPath ) )
			.Add( 1f, depth14 )
			.Add( 2f, depth15 );
		using var graph = builder.Create( root );
		var mesh = new Mesh( Material.Load( "materials/default/white.vmat" ) );
		mesh.CreateVertexBuffer( 3, new Vertex[3].AsSpan() );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		mesh.Bounds = new BBox( new Vector3( -16 ), new Vector3( 16 ) );
		var model = Keep( Model.Builder.AddBone( "root", Vector3.Zero, Rotation.Identity ).AddMesh( mesh ).Create() );
		var world = new SceneWorld();
		try
		{
			var actor = new SceneModel( world, model, Transform.Zero ) { UseAnimGraph = true };
			actor.Animation.Graph = graph;

			float Evaluate( float value )
			{
				Assert.IsTrue( actor.Animation.Parameters.TrySet( "depth", value ) );
				actor.Update( .016f );
				return actor.GetParentSpaceBone( 0 ).Position.x;
			}

			Assert.AreEqual( 7f, Evaluate( 0f ), .001f );
			Assert.AreEqual( 7f, Evaluate( 1f ), .001f, "Exactly MaxTasks still evaluates" );
			Assert.AreEqual( 0f, Evaluate( 2f ), .001f, "Over MaxTasks falls back to the reference pose" );
			Assert.AreEqual( 7f, Evaluate( 0f ), .001f, "Recovers once under the limit" );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void SlotIdentityDoesNotMergeReusedPathsOrMixedLegacyReferences()
	{
		var skeletonPath = AssetPath( "slots.skel" );
		WriteResource( skeletonPath, Guid.NewGuid(), CompileData( SkeletonData() ) );
		var firstPath = AssetPath( "first.anim" );
		var secondPath = AssetPath( "second.anim" );
		var firstGuid = Guid.NewGuid();
		var secondGuid = Guid.NewGuid();
		WriteResource( firstPath, firstGuid, CompileData( ClipData( JsonValue.Create( skeletonPath ) ) ) );
		WriteResource( secondPath, secondGuid, CompileData( ClipData( JsonValue.Create( skeletonPath ) ) ) );

		var builder = Graph.Builder;
		builder.Skeleton = skeletonPath;
		var first = builder.Clip( new ResourceId { Guid = firstGuid, Path = secondPath } );
		builder.Clip( new ResourceId { Guid = firstGuid, Path = "another-stale.anim" } );
		builder.Clip( new ResourceId { Guid = secondGuid, Path = secondPath } );
		builder.Clip( secondPath );
		builder.Clip( new ResourceId { Guid = Guid.Empty, Path = secondPath } );
		builder.Clip( new ResourceId { Guid = Guid.Empty, Path = firstPath } );
		using var graph = builder.Create( first );
		Assert.IsNotNull( graph );
		Assert.AreEqual( 4, graph.AnimationSlotCount );
		CollectionAssert.AreEqual( new[] { firstPath, secondPath, secondPath, firstPath },
			Enumerable.Range( 0, graph.AnimationSlotCount ).Select( graph.GetAnimationSlotName ).ToArray() );
	}

	[TestMethod]
	public void NativeCacheHitsAreRegisteredInTheCurrentResourceLibrary()
	{
		var path = AssetPath( "context.skel" );
		var guid = Guid.NewGuid();
		WriteResource( path, guid, CompileData( SkeletonData() ) );
		var skeleton = Keep( Skeleton.Load( path ) );
		var previous = GlobalContext.Current;
		var context = new GlobalContext { TypeLibrary = previous.TypeLibrary, FileMount = previous.FileMount };
		using ( new GlobalContext.GlobalContextScope( context ) )
		{
			Assert.AreSame( skeleton, Skeleton.Load( path ) );
			Assert.IsTrue( Game.Resources.TryGet<Skeleton>( guid, out var indexed ) );
			Assert.AreSame( skeleton, indexed );
			Game.Resources.Unregister( skeleton );
		}
	}

	[TestMethod]
	public void UnavailableGuidOnlyChildDoesNotDiscardItsFallbackGraph()
	{
		var skeletonPath = AssetPath( "fallback.skel" );
		WriteResource( skeletonPath, Guid.NewGuid(), CompileData( SkeletonData() ) );
		var builder = Graph.Builder;
		builder.Skeleton = skeletonPath;
		var root = builder.ReferencedGraph( new ResourceId { Guid = Guid.NewGuid() }, builder.ReferencePose() );
		using var graph = builder.Create( root );
		Assert.IsNotNull( graph );
		Assert.IsTrue( graph.IsValid );
	}

	[TestMethod]
	public void ClipReloadRebindsItsGuidDependencyAndInvalidatesTheCachedSkeleton()
	{
		var firstPath = AssetPath( "before.skel" );
		var secondPath = AssetPath( "after.skel" );
		var firstGuid = Guid.NewGuid();
		var secondGuid = Guid.NewGuid();
		WriteResource( firstPath, firstGuid, CompileData( SkeletonData() ) );
		WriteResource( secondPath, secondGuid, CompileData( SkeletonData() ) );
		var clipPath = AssetPath( "reload.anim" );
		var clipGuid = Guid.NewGuid();
		WriteResource( clipPath, clipGuid, CompileData( ClipData( Json.ToNode( new ResourceId { Guid = firstGuid } ) ) ) );
		var clip = Keep( Clip.Load( clipPath ) );
		var first = Keep( clip.Skeleton );
		Assert.AreEqual( firstGuid, ResourceId.Get( first ).Guid );
		WriteResource( clipPath, clipGuid, CompileData( ClipData( Json.ToNode( new ResourceId { Guid = secondGuid, Path = "stale.skel" } ) ) ) );
		NativeEngine.g_pResourceSystem.ReloadResource( clipPath );
		Assert.AreSame( clip, Clip.Load( new ResourceId { Guid = clipGuid } ) );
		var second = Keep( clip.Skeleton );
		Assert.AreNotSame( first, second );
		Assert.AreEqual( secondGuid, ResourceId.Get( second ).Guid );
		Assert.IsTrue( clip.IsBound );
		Assert.AreEqual( 7f, clip.GetLocalTransform( .5f, 0 ).Position.x );
	}

	[TestMethod]
	public void WrongTypeGuidDependenciesFailWithoutBindingAnIncompatibleHandle()
	{
		var skeletonPath = AssetPath( "type.skel" );
		var skeletonGuid = Guid.NewGuid();
		WriteResource( skeletonPath, skeletonGuid, CompileData( SkeletonData() ) );
		var clipPath = AssetPath( "type.anim" );
		var clipGuid = Guid.NewGuid();
		WriteResource( clipPath, clipGuid, CompileData( ClipData( JsonValue.Create( skeletonPath ) ) ) );
		Keep( Skeleton.Load( skeletonPath ) );
		Keep( Clip.Load( clipPath ) );
		var invalid = AssetPath( "invalid.anim" );
		WriteResource( invalid, Guid.NewGuid(), CompileData( ClipData( Json.ToNode( new ResourceId { Guid = clipGuid, Path = skeletonPath } ) ) ) );
		Assert.IsNull( Clip.Load( invalid ) );

		var data = SkeletonData();
		data["m_secondarySkeletons"] = new JsonArray( new JsonObject
		{
			["m_attachToBoneID"] = "root",
			["m_skeleton"] = Json.ToNode( new ResourceId { Guid = clipGuid, Path = skeletonPath } )
		} );
		var invalidSkeleton = AssetPath( "invalid.skel" );
		WriteResource( invalidSkeleton, Guid.NewGuid(), CompileData( data ) );
		Assert.IsNull( Skeleton.Load( invalidSkeleton ) );

		var invalidSecondary = ClipData( JsonValue.Create( skeletonPath ) );
		invalidSecondary["m_secondaryAnimations"] = new JsonArray( Json.ToNode( new ResourceId { Guid = skeletonGuid, Path = clipPath } ) );
		var invalidSecondaryPath = AssetPath( "invalid_secondary.anim" );
		WriteResource( invalidSecondaryPath, Guid.NewGuid(), CompileData( invalidSecondary ) );
		Assert.IsNull( Clip.Load( invalidSecondaryPath ) );

		var builder = Graph.Builder;
		builder.Skeleton = skeletonPath;
		var wrongClip = builder.Clip( new ResourceId { Guid = skeletonGuid, Path = clipPath } );
		using var invalidGraph = Graph.Create( "wrong clip type", builder.Compile( wrongClip, null, "Default" ) );
		Assert.IsNull( invalidGraph );
	}
}
