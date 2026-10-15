using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private Material CreateTeeth( VmtParameters parameters, string resourceName )
	{
		var name = resourceName ?? $"{FullPath}.vmat";
		if ( !name.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "Teeth material resource names must end in .vmat.", nameof( name ) );
		var exponent = parameters.Float( "$phongexponent", 100 );
		if ( exponent < 0 )
			throw new InvalidDataException( $"{name}: $phongexponent must not be negative." );
		var illumination = parameters.Float( "$illumfactor", 1 );
		var bump = !string.IsNullOrWhiteSpace( parameters.String( "$bumpmap" ) );
		var material = CreateShaderMaterial( "shaders/source1/teeth.shader", resourceName );
		material.SetFeature( "F_SOURCE_TEETH_BUMP", bump ? 1 : 0 );
		material.SetFeature( "F_RENDER_BACKFACES", parameters.Boolean( "$nocull" ) ? 1 : 0 );
		material.Set( "g_flSourceIllumFactor", illumination );
		var forward = parameters.Vector( "$forward", Vector3.Forward );
		material.Set( "g_vSourceTeethForward", forward );
		material.Set( "g_flSourcePhongExponent", exponent );
		Bind( "$basetexture", "g_tSourceTeeth", parameters.Integer( "$frame" ) );
		if ( bump )
			Bind( "$bumpmap", "g_tSourceTeethNormal" );
		if ( parameters.Boolean( "$intro" ) || parameters.Float( "$warpparam" ) != 0 )
			Log.Trace( $"{name}: Teeth INTRO/vortwarp deformation is not supported; using the undeformed model." );
		return material;

		void Bind( string key, string input, int frame = 0 )
		{
			var path = parameters.String( key );
			if ( string.IsNullOrWhiteSpace( path ) )
				throw new InvalidDataException( $"{name}: teeth material is missing {key}." );
			LoadSurfaceTexture( path, frame ).Bind( material, input );
		}
	}
}
