using System;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private void BuildSkybox3D( BspFile bsp, GameObject world )
	{
		var camera = bsp.SkyCamera;
		if ( camera is null )
			return;
		var path = $"mount://{Host.Ident}/{FullPath}_skybox.scene";
		var scene = SceneFile.Load( path ) ?? throw new InvalidOperationException( $"{FullPath}: could not load skybox scene '{path}'." );
		var cameraScale = camera.Integer( "scale" );
		var scale = cameraScale > 0 ? cameraScale : 1;
		var skybox = new GameObject( true, "3d skybox" )
		{
			Parent = world,
			LocalPosition = -camera.Vector( "origin" ) * scale,
			LocalScale = new Vector3( scale )
		};
		var component = skybox.AddComponent<Skybox3D>( false );
		component.FogOverride = CreateFog( skybox, camera, bsp.Hdr, enabled: false );
		component.SkyboxScene = scene;
		component.Enabled = true;
	}

	private void BuildSkyboxScene( BspFile bsp )
	{
		if ( bsp.SkyCamera is null )
			return;
		var content = new GameObject( true, "3d skybox content" )
		{
			IsStatic = true
		};
		content.Tags.Add( "skybox" );
		var (surfaces, aggregates) = BuildWorldModel( bsp, skybox: true );
		if ( surfaces is not null )
			content.AddComponent<ModelRenderer>().Model = surfaces;
		foreach ( var aggregate in aggregates )
		{
			var child = new GameObject( true, aggregate.Model.ResourceName )
			{
				Parent = content,
				IsStatic = true
			};
			child.Tags.Add( "world" );
			var renderer = child.AddComponent<AggregateRenderer>( false );
			renderer.Fragments = aggregate.Fragments;
			renderer.Model = aggregate.Model;
			renderer.Enabled = true;
		}

		BuildWater( content, skybox: true );
		BuildWorldPhysics( bsp, content, skybox: true );
		BuildEntities( bsp, content, skybox: true );
		BuildLightProbes( bsp, content, skybox: true );
	}
}
