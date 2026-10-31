using Sandbox.Mounting;
using System;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal sealed partial class MapLoader( string fileName, bool skybox = false ) : SceneLoader<BaseMount>
{
	internal string FullPath { get; } = fileName;
	private string ContentRoot => BaseMount.GetMapPakRoot( FullPath );
	private string ResourcePrefix => $"source1/{Host.Ident}/{FullPath}{(skybox ? "_skybox" : "")}";

	private readonly List<Model> _worldModels = [];
	private readonly List<PhysicsGroupDescription> _worldPhysics = [];
	private readonly Dictionary<(Material Material, int Page, bool NoDecals), Material> _worldMaterials = [];
	private readonly Dictionary<Material, Material> _vertexLitMaterials = [];
	private FaceSurface[] _faceSurfaces;
	private SourceMaterialOverrides _bakedShadowBindings;
	private BspLightingContext _lighting;
	protected override void BuildScene()
	{
		_waterModels.Clear();
		_worldPhysics.Clear();
		_worldMaterials.Clear();
		_vertexLitMaterials.Clear();
		using var import = Host.BeginMapImport( FullPath );
		var bsp = import.Data;
		if ( skybox && bsp.SkyCamera is null )
			return;
		_lighting = new BspLightingContext();
		try
		{
			BuildMapScene( bsp );
		}
		finally
		{
			_lighting = null;
		}
	}

	private void BuildMapScene( BspFile bsp )
	{
		_bakedShadowBindings = new MapLightingBuilder( bsp, this, _lighting ).CreateBakedShadowBindings( skybox );
		_faceSurfaces = BuildLightmaps( bsp, skybox );
		if ( skybox )
		{
			BuildSkyboxScene( bsp );
			return;
		}

		var (surfaces, aggregates) = BuildWorldModel( bsp );
		var world = new GameObject( true, "worldspawn" );
		var geometry = new GameObject( true, "world" )
		{
			Parent = world,
			IsStatic = true
		};
		geometry.Tags.Add( "world" );
		if ( surfaces is not null )
			geometry.AddComponent<ModelRenderer>().Model = surfaces;
		foreach ( var aggregate in aggregates )
		{
			var child = new GameObject( true, aggregate.Model.ResourceName );
			child.Parent = geometry;
			child.IsStatic = true;
			child.Tags.Add( "world" );
			var renderer = child.AddComponent<AggregateRenderer>( false );
			renderer.Fragments = aggregate.Fragments;
			renderer.Model = aggregate.Model;
			renderer.Enabled = true;
		}

		BuildWater( geometry );
		BuildWorldPhysics( bsp, geometry );
		AddShadowBlocker( BuildShadowModel( bsp, 0 ), geometry );
		BuildEntities( bsp, world );
		BuildFog( bsp, world );
		BuildLightProbes( bsp, world );
		BuildSkybox3D( bsp, world );
	}
}
