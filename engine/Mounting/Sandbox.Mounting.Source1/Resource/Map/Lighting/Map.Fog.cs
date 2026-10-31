using System;
using Sandbox.Rendering;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private void BuildFog( BspFile bsp, GameObject world )
	{
		var controller = SelectFogController( bsp.Entities );
		if ( controller is null )
			return;

		CreateFog( world, controller, bsp.Hdr );
	}

	internal static MapEntity SelectFogController( IEnumerable<MapEntity> entities )
	{
		MapEntity selected = null;
		foreach ( var entity in entities )
		{
			if ( !string.Equals( entity.Get( "classname" ), "env_fog_controller", StringComparison.OrdinalIgnoreCase ) )
				continue;
			if ( selected is null || (entity.Integer( "spawnflags" ) & 1) != 0 )
				selected = entity;
		}
		return selected;
	}

	internal static Fog CreateFog( GameObject world, MapEntity entity, bool hdr, bool enabled = true )
	{
		var scale = hdr ? entity.Number( "HDRColorScale", 1 ) : 1;
		if ( scale <= 0 )
			scale = 1;
		var fogObject = new GameObject( false, entity.Get( "targetname", "fog" ) )
		{
			Parent = world,
			IsStatic = true
		};
		var fog = fogObject.AddComponent<Fog>();
		fog.Enabled = enabled;
		fog.FogEnabled = entity.Integer( "fogenable" ) != 0;
		fog.Mode = FogMode.Distance;
		fog.DistanceMetric = FogDistanceMetric.ViewDepth;
		var start = entity.Number( "fogstart" );
		var end = entity.Number( "fogend" );
		// Source uses an inverse range of one when both endpoints are equal.
		fog.DistanceRange = new Vector2( start, end == start ? start + 1 : end );
		fog.FalloffExponent = 2;
		// min(ramp, density)^2 == min(ramp^2, density^2) for a saturated ramp.
		var density = entity.Number( "fogmaxdensity", 1 ).Clamp( 0, 1 );
		fog.MaximumOpacity = density * density;
		fog.ColorSource = FogColorSource.Constant;
		var hdrColors = hdr && entity.Vector( "fogcolor_hdr" ).x > 0;
		fog.DirectionalColorEnabled = entity.Integer( "fogblend" ) != 0;
		fog.Color = ReadFogColor( entity, "fogcolor", hdrColors, scale );
		if ( fog.DirectionalColorEnabled )
		{
			fog.SecondaryColor = ReadFogColor( entity, "fogcolor2", hdrColors, scale );
			fog.ColorDirection = entity.Vector( "fogdir" );
		}
		fog.SunLightingEnabled = false;
		fog.SkyAffect = 0;
		fogObject.Enabled = true;
		return fog;
	}

	static Color ReadFogColor( MapEntity entity, string key, bool hdr, float scale )
	{
		var color = entity.Vector( hdr ? $"{key}_hdr" : key ).Clamp( 0, 255 );
		color = (new Vector3( (byte)color.x, (byte)color.y, (byte)color.z ) * scale).Clamp( 0, 255 );
		return new Color32( (byte)color.x, (byte)color.y, (byte)color.z ).ToColor( true );
	}
}
