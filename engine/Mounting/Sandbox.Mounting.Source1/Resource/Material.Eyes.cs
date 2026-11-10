using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private Material CreateEye( bool refract, VmtParameters parameters, string resourceName )
	{
		var name = resourceName ?? $"{FullPath}.vmat";
		var material = CreateShaderMaterial( $"shaders/source1/{shaderName}.shader", resourceName );
		material.SetFeature( "F_RENDER_BACKFACES", parameters.Boolean( "$nocull" ) ? 1 : 0 );
		if ( !refract )
			Bind( "$basetexture", "$frame", "g_tSourceSclera" );
		Bind( "$iris", "$irisframe", "g_tSourceIris" );
		material.Set( "g_flSourceDilation", Math.Clamp( parameters.Float( "$dilation", 0.5f ), 0, 1 ) );
		var tint = refract
			? parameters.Vector( "$color", Vector3.One ) * parameters.Vector( "$color2", Vector3.One )
			: Vector3.One;
		material.Set( "g_vSourceEyeTint", tint );
		if ( parameters.Boolean( "$halflambert" ) )
			Log.Trace( $"{name}: native s&box lighting replaces Source Half-Lambert." );
		return material;

		void Bind( string key, string frameKey, string input )
		{
			var path = parameters.String( key );
			if ( string.IsNullOrWhiteSpace( path ) )
				throw new InvalidDataException( $"{name}: eye material is missing {key}." );
			LoadSurfaceTexture( path, parameters.Integer( frameKey ) ).Bind( material, input, clamp: key == "$iris" );
		}
	}
}
