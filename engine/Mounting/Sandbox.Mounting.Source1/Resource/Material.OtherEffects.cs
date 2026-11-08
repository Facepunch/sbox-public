using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private Material CreateMonitor( string resourceName, bool mapSurface = false )
	{
		var material = CreateSurface( "unlitgeneric", parameters, transforms, alpha, animation, resourceName, mapSurface );
		var second = parameters.String( "$texture2" );
		if ( !string.IsNullOrWhiteSpace( second ) )
		{
			var texture = LoadSurfaceTexture( second, parameters.Integer( "$frame2" ) );
			if ( texture.IsCube )
				throw new InvalidDataException( $"{FullPath}: $texture2 requires a 2D texture." );
			texture.Bind( material, "g_tTexture2" );
		}
		material.Set( "g_flSourceMonitor", true );
		material.Set( "g_vSourceMonitorContrast", parameters.Vector( "$contrast", Vector3.Zero ) );
		material.Set( "g_vSourceMonitorSaturation", parameters.Vector( "$saturation", Vector3.One ) );
		material.Set( "g_vSourceMonitorTint", parameters.Vector( "$tint", Vector3.One ) );
		transforms.Get( "$texture2transform" ).Bind( material, "Texture2" );
		return material;
	}

	private Material CreateVolumeClouds( string resourceName )
	{
		var material = CreateShaderMaterial( "shaders/source1/volumeclouds.shader", resourceName );
		material.SetFeature( "F_RENDER_BACKFACES", parameters.Boolean( "$nocull" ) ? 1 : 0 );
		string[] keys = ["$basetexture", "$basetexture2", "$basetexture3"];
		string[] inputs = ["g_tColor", "g_tTexture2", "g_tTexture3"];
		for ( var i = 0; i < keys.Length; i++ )
		{
			var path = parameters.String( keys[i] );
			if ( string.IsNullOrWhiteSpace( path ) )
				throw new InvalidDataException( $"{FullPath}: VolumeClouds requires {keys[i]}." );
			var texture = LoadSurfaceTexture( path );
			if ( texture.IsCube )
				throw new InvalidDataException( $"{FullPath}: {keys[i]} requires a 2D texture." );
			texture.Bind( material, inputs[i], clamp: true );
		}
		material.Set( "g_vSourceCloudSpeed", new Vector3( parameters.Float( "$basetexturespeed", 4 ),
			parameters.Float( "$basetexture2speed", 2 ), parameters.Float( "$basetexture3speed", 1 ) ) );
		material.Set( "g_flSourceEffectTime", parameters.Float( "$time" ) );
		material.Set( "g_flSourceEffectRuntime", UsesCurrentTime() || parameters.Float( "$time" ) <= 0 );
		return material;
	}
}
