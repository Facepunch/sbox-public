using System;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	Material LoadWorldMaterial( BspFile bsp, int texDataIdx, int page, Material defaultMaterial, bool noDecals = false )
	{
		Material material = null;
		var matName = bsp.GetTexDataString( texDataIdx );
		if ( !string.IsNullOrEmpty( matName ) )
		{
			var path = VmtFile.NormalizePath( matName, ".vmt" );
			if ( Host.GetContentPath( path, ContentRoot ) is null )
				path = $"{GetBaseMaterialPath( path[..^4] )}.vmt";
			if ( Host.GetContentPath( path, ContentRoot ) is not null )
				material = Host.LoadContentMaterial( path, ContentRoot );
			else
				Log.Warning( $"{FullPath}: world material '{matName}' is missing; using the engine default material." );
		}

		material ??= defaultMaterial;
		var sourceSurface = SourceMaterialBuilder.IsSurfaceShader( material );
		var lightmapped = page >= 0 && SourceMaterialBuilder.IsLitSurfaceShader( material );
		if ( sourceSurface )
		{
			page = lightmapped ? page : -1;
			var key = (material, page, noDecals);
			if ( _worldMaterials.TryGetValue( key, out var cached ) )
				return cached;

			var variantName = $"{ResourcePrefix}_lightmap_{texDataIdx}_{page}{(noDecals ? "_nodecal" : "")}.vmat";
			var bindings = lightmapped ? _bakedShadowBindings.Snapshot() : new SourceMaterialOverrides { MapSurface = true };
			if ( noDecals )
				bindings.Flags["g_flSourceReceiveDecals"] = false;
			if ( lightmapped )
			{
				bindings.Features["F_SOURCE_LIGHTMAP"] = 1;
				bindings.Features["F_SOURCE_LIGHTMAP_HDR"] = bsp.Hdr ? 1 : 0;
				BindLightmap( bindings, page );
			}

			material = Host.CreateMaterialVariant( material, variantName, bindings );
			_worldMaterials.Add( key, material );
		}

		return material;
	}

	static string GetBaseMaterialPath( string path )
	{
		const string mapsPrefix = "materials/maps/";
		if ( !path.StartsWith( mapsPrefix, StringComparison.Ordinal ) )
			return path;
		var slash = path.IndexOf( '/', mapsPrefix.Length );
		if ( slash < 0 )
			return path;
		var realPath = path[(slash + 1)..];
		var last = realPath.LastIndexOf( '_' );
		var second = last > 0 ? realPath.LastIndexOf( '_', last - 1 ) : -1;
		var first = second > 0 ? realPath.LastIndexOf( '_', second - 1 ) : -1;
		if ( first > 0 && int.TryParse( realPath.AsSpan( first + 1, second - first - 1 ), out _ ) && int.TryParse( realPath.AsSpan( second + 1, last - second - 1 ), out _ ) && int.TryParse( realPath.AsSpan( last + 1 ), out _ ) )
			realPath = realPath[..first];
		return $"materials/{realPath}";
	}
}
