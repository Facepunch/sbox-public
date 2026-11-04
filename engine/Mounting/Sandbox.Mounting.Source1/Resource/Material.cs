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
	private readonly string contentRoot;
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
		contentRoot = BaseMount.GetContentRoot( path, "materials" );
		parameters = new VmtParameters( VmtFile.SelectParameters( vmt ), FullPath );
		shaderName = VmtFile.NormalizeShaderName( vmt.ShaderName );
		proxies = vmt.Root.GetBlock( "proxies" );
		(transforms, alpha, animation) = GetMaterialProxies( vmt, shaderName, parameters );
	}

	internal Material CreateMaterial( string resourceName, bool mapSurface = false )
	{
		proxyProgram?.ResetInstructions();
		var material = shaderName switch
		{
			"eyes" or "eyerefract" => CreateEye( shaderName == "eyerefract", parameters, resourceName ),
			"unlittwotexture" => CreateTwoTexture( parameters, transforms, animation, resourceName ),
			"modulate" => CreateModulate( parameters, transforms, alpha, animation, resourceName ),
			"teeth" => CreateTeeth( parameters, resourceName ),
			"refract" or "core" => CreateRefract( shaderName == "core", resourceName ),
			"aftershock" => CreateRefract( false, resourceName, aftershock: true ),
			"volumeclouds" => CreateVolumeClouds( resourceName ),
			"monitorscreen" => CreateMonitor( resourceName, mapSurface ),
			"water" => CreateEffect( true, parameters, transforms, alpha, animation, resourceName ),
			"worldvertextransition" => CreateSurface( "lightmappedgeneric", parameters, transforms, alpha, animation, resourceName, mapSurface: true ),
			"vertexlitgeneric" or "lightmappedgeneric" or "unlitgeneric" or "vortwarp" or "worldtwotextureblend"
				=> CreateSurface( shaderName, parameters, transforms, alpha, animation, resourceName, mapSurface ),
			_ => throw new NotSupportedException( $"{FullPath}: unsupported Source material shader '{shaderName}'." )
		};
		BindProxyProgram( material );
		material.Set( "g_bFogEnabled", !parameters.Boolean( "$nofog" ) );
		material.Attributes.Set( "PhysicsSurfaceProperties", Host.GetSurfaceBindingName( parameters.String( "$surfaceprop", "default" ), FullPath ) );
		UpdateProbeUsage( material );
		return material;
	}

	private Material CreateShaderMaterial( string shader, string resourceName = null )
	{
		var name = resourceName ?? $"source1/{Host.Ident}/{System.IO.Path.ChangeExtension( FullPath, ".vmat" )}";
		return Material.Create( name, shader, anonymous: false )
			?? throw new InvalidOperationException( $"Could not create '{FullPath}' using {shader}." );
	}

	internal static void UpdateProbeUsage( Material material )
	{
		if ( !IsSurfaceShader( material ) )
			return;

		material.Attributes.Set( "UsesBakedLightingFromProbe", IsLitSurfaceShader( material )
			&& material.GetFeature( "F_SOURCE_LIGHTMAP" ) == 0
			&& material.GetFeature( "F_SOURCE_PROP_LIGHTING" ) == 0 );
	}

	internal static bool IsSurfaceShader( Material material ) => material.ShaderName is
		"shaders/source1/vertexlitgeneric.shader" or "shaders/source1/lightmappedgeneric.shader"
		or "shaders/source1/unlitgeneric.shader" or "shaders/source1/worldvertextransition.shader"
		or "shaders/source1/vortwarp.shader" or "shaders/source1/monitorscreen.shader"
		or "shaders/source1/worldtwotextureblend.shader";

	internal static bool IsLitSurfaceShader( Material material ) => IsSurfaceShader( material )
		&& material.ShaderName is not ("shaders/source1/unlitgeneric.shader" or "shaders/source1/monitorscreen.shader")
		&& (material.ShaderName != "shaders/source1/vortwarp.shader" || material.GetFeature( "F_SOURCE_UNLIT" ) == 0);
}
