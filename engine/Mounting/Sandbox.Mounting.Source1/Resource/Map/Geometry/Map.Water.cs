using System;
using System.Collections.Generic;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private readonly List<(Model Model, bool Skybox)> _waterModels = [];
	private void BuildWater( GameObject geometry, bool skybox = false )
	{
		foreach ( var water in _waterModels )
		{
			if ( water.Skybox != skybox )
				continue;
			var child = new GameObject( true, "water" )
			{
				Parent = geometry,
				IsStatic = true
			};
			child.Tags.Add( "world" );
			child.Tags.Add( "source1_water" );
			child.AddComponent<ModelRenderer>().Model = water.Model;
		}
		if ( _waterModels.Any( water => water.Skybox == skybox ) )
			Log.Warning( $"{FullPath}: water uses native shader refraction and cubemap reflections; the engine has no component for Source planar water views." );
	}

	private void BuildWaterModels( Dictionary<(int Texture, float Height), MeshGroup> groups, bool skybox )
	{
		foreach ( var (key, group) in groups )
		{
			var builder = Model.Builder;
			var (bounds, _) = BuildWorldMesh( builder, group.Material, [group] );
			var model = CreateWorldModel( builder.WithViewBounds( bounds ), $"{(skybox ? "skybox_" : "")}water_{key.Texture}_{_waterModels.Count}.vmdl" );
			_waterModels.Add( (model, skybox) );
		}
	}
}
