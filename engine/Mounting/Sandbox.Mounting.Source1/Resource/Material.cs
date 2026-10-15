using Sandbox.Mounting;
using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class MaterialLoader( string fullPath ) : ResourceLoader<BaseMount>
{
	protected override object Load()
	{
		try
		{
			var source = Host.GetSourceMaterial( fullPath );
			var material = source.CreateMaterial( null );
			Host.TrackMaterialSource( material, source );
			return material;
		}
		catch ( Exception e ) when ( e is FileNotFoundException or InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"{fullPath}: material was not imported: {e.Message} Using the error material." );
			var material = Material.Load( "materials/error.vmat" );
			if ( material == null || !material.IsValid || material.IsError )
				throw new InvalidOperationException( $"{fullPath}: the engine error material could not be loaded.", e );
			return material;
		}
	}
}

internal sealed partial class SourceMaterialBuilder
{
	private BaseMount Host { get; }
	private string FullPath { get; }
	private readonly VmtParameters parameters;
	private readonly string shaderName;
	private readonly TextureTransforms transforms;
	private readonly MaterialAlpha alpha;
	private readonly TextureAnimation? animation;
	private readonly VmtNode proxies;

	internal SourceMaterialBuilder( BaseMount mount, string path, VmtFile vmt )
	{
		Host = mount;
		FullPath = path;
		parameters = new VmtParameters( VmtFile.SelectParameters( vmt ), FullPath );
		shaderName = VmtFile.NormalizeShaderName( vmt.ShaderName );
		proxies = vmt.Root.GetBlock( "proxies" );
		(transforms, alpha, animation) = GetMaterialProxies( vmt, shaderName, parameters );
	}

	internal Material CreateMaterial( string resourceName )
	{
		var material = shaderName switch
		{
			"eyes" or "eyerefract" => CreateEye( shaderName == "eyerefract", parameters, resourceName ),
			"unlittwotexture" => CreateTwoTexture( parameters, transforms, animation, resourceName ),
			"modulate" => CreateModulate( parameters, transforms, alpha, animation, resourceName ),
			"teeth" => CreateTeeth( parameters, resourceName ),
			"refract" or "core" => CreateRefract( shaderName == "core", resourceName ),
			"aftershock" => CreateRefract( false, resourceName, aftershock: true ),
			"volumeclouds" => CreateVolumeClouds( resourceName ),
			"monitorscreen" => CreateMonitor( resourceName ),
			"vertexlitgeneric" or "lightmappedgeneric" or "unlitgeneric" or "vortwarp"
				=> CreateSurface( shaderName, parameters, transforms, alpha, animation, resourceName ),
			_ => throw new NotSupportedException( $"{FullPath}: unsupported Source material shader '{shaderName}'." )
		};
		material.Attributes.Set( "PhysicsSurfaceProperties", Host.GetSurfaceBindingName( parameters.String( "$surfaceprop", "default" ), FullPath ) );
		return material;
	}

	private Material CreateShaderMaterial( string shader, string resourceName = null )
	{
		var name = resourceName ?? $"source1/{Host.Ident}/{System.IO.Path.ChangeExtension( FullPath, ".vmat" )}";
		return Material.Create( name, shader, anonymous: false )
			?? throw new InvalidOperationException( $"Could not create '{FullPath}' using {shader}." );
	}
}
