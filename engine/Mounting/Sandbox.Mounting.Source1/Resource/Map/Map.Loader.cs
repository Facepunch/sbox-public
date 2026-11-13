using Sandbox.Mounting;
using System;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal sealed partial class MapLoader : SceneLoader<BaseMount>
{
	internal string FullPath { get; }
	private bool skybox;

	private string ContentRoot => BaseMount.GetMapPakRoot( FullPath );
	private string ResourcePrefix => $"source1/{Host.Ident}/{FullPath}{(skybox ? "_skybox" : "")}";

	private readonly List<Model> _worldModels = [];
	private readonly List<PhysicsGroupDescription> _worldPhysics = [];
	private readonly Dictionary<(Material Material, int Page, bool NoDecals), Material> _worldMaterials = [];
	private readonly Dictionary<Material, Material> _vertexLitMaterials = [];
	private FaceSurface[] _faceSurfaces;
	private SourceMaterialOverrides _bakedShadowBindings;
	private BspLightingContext _lighting;

	protected override Texture GetThumbnail() =>
		Texture.Load( $"mount://{Host.Ident}/maps/thumb/{Name.WithExtension( "png" )}.vtex", false )
		?? Texture.Load( $"mount://{Host.Ident}/maps/{Name.WithExtension( "png" )}.vtex", false );

	public MapLoader( string fileName, bool skybox = false )
	{
		FullPath = fileName;
		this.skybox = skybox;

		// hide skyboxes
		Flags = Flags.WithFlag( ResourceFlags.DeveloperOnly, skybox );
	}

	protected override void BuildScene()
	{
		OnSceneUnloaded();
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

	protected override void OnSceneUnloaded()
	{
		// A live scene still renders the probes through these exact wrappers, so leave those to the GC
		if ( _probeTextures is not null && !LiveScenes.Any( scene => scene.GetComponentsInChildren<LightProbeVolume>( true ).Any( UsesProbeTextures ) ) )
			_probeTextures.Dispose();
		_probeTextures = null;
		_worldModels.Clear();
		_propModels.Clear();
		_waterModels.Clear();
		_worldPhysics.Clear();
		_worldMaterials.Clear();
		_vertexLitMaterials.Clear();
		_lightmapPages.Clear();
		_faceSurfaces = null;
	}

	private bool UsesProbeTextures( LightProbeVolume volume ) =>
		_probeTextures.Contains( volume.Texture ) || _probeTextures.Contains( volume.IndicesTexture ) || _probeTextures.Contains( volume.ScalarsTexture );

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
