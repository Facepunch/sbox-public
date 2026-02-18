using NativeEngine;

namespace Sandbox;

/// <summary>
/// A <a href="https://en.wikipedia.org/wiki/Shader">shader</a> is a specialized and complex computer program that use
/// world geometry, materials and textures to render graphics.
/// </summary>
public partial class Shader : Resource
{
	internal CVfx native;

	public override bool IsValid => native.IsValid;

	private Shader( CVfx native, string name )
	{
		if ( native.IsNull ) throw new Exception( "Shader pointer cannot be null!" );

		this.native = native;

		RegisterWeakResourceId( name );
	}

	internal Shader()
	{
		native = CVfx.Create( "__debugShader" );
	}

	~Shader()
	{
		var n = native;
		native = default;

		MainThread.Queue( () => n.DestroyStrongHandle() );
	}

	const int VFX_CHECK_MD5_AGAINST_SOURCE = (1 << 0);
	const int VFX_LOAD_STATIC_COMBO_DATA = (1 << 1);
	const int VFX_LOAD_FEATURES_ONLY = (1 << 2);
	const int VFX_COMPUTE_MD5_CHECKSUMS = (1 << 4);

	/// <summary>
	/// Loads from the compiled resource, unless it's out of date by comparing the md5
	/// in which case it just returns false 
	/// </summary>
	internal bool LoadFromCompiledUnlessOutOfDate( string filename )
	{
		return native.CreateFromResourceFile( filename, NativeEngine.VfxCompileTarget_t.SM_6_0_VULKAN, VFX_CHECK_MD5_AGAINST_SOURCE, true );
	}


	internal bool LoadFromCompiled( string filename )
	{
		return native.CreateFromResourceFile( filename, NativeEngine.VfxCompileTarget_t.SM_6_0_VULKAN, VFX_CHECK_MD5_AGAINST_SOURCE | VFX_LOAD_STATIC_COMBO_DATA, true );
	}

	/// <summary>
	/// Loads all shader programs from the shader source file, except those that are already loaded (from above, assumably)
	/// </summary>
	internal bool LoadFromSourceChecksums( string filename )
	{
		return native.CreateFromShaderFile( filename, NativeEngine.VfxCompileTarget_t.SM_6_0_VULKAN, VFX_COMPUTE_MD5_CHECKSUMS );
	}

	/// <summary>
	/// Loads all shader programs from the shader source file, except those that are already loaded (from above, assumably)
	/// </summary>
	internal bool LoadFromSource( string filename )
	{
		return native.CreateFromShaderFile( filename, NativeEngine.VfxCompileTarget_t.SM_6_0_VULKAN, VFX_COMPUTE_MD5_CHECKSUMS );
	}

	internal ShaderProgram GetProgram( ShaderProgramType program )
	{
		return new ShaderProgram( this, program );
	}

	internal bool HasProgram( ShaderProgramType program )
	{
		return native.HasShaderProgram( program );
	}

	//internal byte[] Serialize()
	//{
	//	native.Serialize();
	//}
}
