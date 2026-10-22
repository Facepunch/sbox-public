using System;
using System.IO;
using System.Linq;
using System.Text;
using Sandbox;
using Sandbox.Engine.Shaders;

namespace EngineTests;

/// <summary>
/// Outdated package shaders are found by their headers alone, the same checks the native loaders make
/// </summary>
[TestClass]
public class ShaderUpgradeTest
{
	static readonly ShaderUpgrade.Versions Current = new( Resource: 4, MinVcs: 65, Vcs: 68, Abi: 2 );

	/// <summary>
	/// A .shader_c with a VS and PS, each program starting with its VCS header
	/// </summary>
	static byte[] Shader( int resource = 4, int vcs = 68, int abi = 2, string formatId = "vcs2" )
	{
		var programCount = (int)ShaderProgramType.VFX_PROGRAM_MAX;
		var headers = new byte[programCount * 8];

		byte[] program = [.. Encoding.ASCII.GetBytes( formatId ), .. BitConverter.GetBytes( vcs ), .. BitConverter.GetBytes( abi ), 1, 2, 3, 4];

		var vs = headers.Length;
		var ps = vs + program.Length;
		BitConverter.GetBytes( vs ).CopyTo( headers, (int)ShaderProgramType.VFX_PROGRAM_VS * 8 );
		BitConverter.GetBytes( program.Length ).CopyTo( headers, (int)ShaderProgramType.VFX_PROGRAM_VS * 8 + 4 );
		BitConverter.GetBytes( ps ).CopyTo( headers, (int)ShaderProgramType.VFX_PROGRAM_PS * 8 );
		BitConverter.GetBytes( program.Length ).CopyTo( headers, (int)ShaderProgramType.VFX_PROGRAM_PS * 8 + 4 );

		byte[] spirv = [.. headers, .. program, .. program];
		byte[] payload = [.. BitConverter.GetBytes( spirv.Length ), .. spirv, .. BitConverter.GetBytes( 0 )];

		var missing = Path.Combine( Path.GetTempPath(), $"{Guid.NewGuid()}.shader" );
		return ShaderSource.WriteResourceFile( missing, payload, resource );
	}

	static ShaderUpgrade.State State( byte[] file ) => ShaderUpgrade.GetState( new MemoryStream( file ), Current );

	[TestMethod]
	public void CurrentShaderIsLeftAlone()
	{
		Assert.AreEqual( ShaderUpgrade.State.Current, State( Shader() ) );
	}

	[TestMethod]
	public void OlderResourceVersionIsOutdated()
	{
		Assert.AreEqual( ShaderUpgrade.State.Outdated, State( Shader( resource: 3 ) ) );
	}

	[TestMethod]
	public void OtherAbiIsOutdated()
	{
		Assert.AreEqual( ShaderUpgrade.State.Outdated, State( Shader( abi: 1 ) ) );
	}

	[TestMethod]
	public void OlderSupportedVcsVersionStillLoads()
	{
		Assert.AreEqual( ShaderUpgrade.State.Current, State( Shader( vcs: 65 ) ) );
	}

	[TestMethod]
	public void VcsVersionsTheEngineCantReadAreUnreadable()
	{
		Assert.AreEqual( ShaderUpgrade.State.Unreadable, State( Shader( vcs: 64 ) ), "Too old to read, even to recompile" );
		Assert.AreEqual( ShaderUpgrade.State.Unreadable, State( Shader( vcs: 69 ) ), "From a newer engine" );
	}

	[TestMethod]
	public void NotAShaderIsUnreadable()
	{
		Assert.AreEqual( ShaderUpgrade.State.Unreadable, State( Shader( formatId: "nope" ) ) );
		Assert.AreEqual( ShaderUpgrade.State.Unreadable, State( [1, 2, 3] ) );
		Assert.AreEqual( ShaderUpgrade.State.Unreadable, State( [.. Enumerable.Repeat( (byte)0xff, 64 )] ) );
	}

	[TestMethod]
	public void TruncatedShaderIsUnreadable()
	{
		var file = Shader();
		Assert.AreEqual( ShaderUpgrade.State.Unreadable, State( file[..(file.Length / 2)] ) );
	}

	[TestMethod]
	public void CacheNameHasTheCrcAndVersions()
	{
		Assert.AreEqual( "materials/my_shader.1a2b.r4v68a2.shader_c", ShaderUpgrade.CacheName( "/materials/my-shader.shader_c", 0x1a2b, Current ) );
	}

	[TestMethod]
	public void FirstErrorSkipsThePreprocessorsWarnings()
	{
		string[] output =
		[
			"Error while running preprocessor over shader code",
			"shaders/class/system.slang:13:9: warning: 'StaticCombo' macro redefined [-Wmacro-redefined]",
			"#define StaticCombo(comboName, range, sys) ;",
			"        ^",
			"shaders/class/system.slang:40:2: fatal error: 'missing.hlsl' file not found",
		];

		Assert.AreEqual( "shaders/class/system.slang:40:2: fatal error: 'missing.hlsl' file not found", ShaderUpgrade.FirstError( output ) );
	}

	[TestMethod]
	public void FirstErrorPointsAtWhereTheParserStopped()
	{
		string[] output =
		[
			"hlslParser.Parse err: ANTLR - Mismatched Token Exception: 58:1",
			"#line 11 \"shaders/decal_generic.shader\"",
			"====>  Translucent();",
			"HLSL parser encountered 1 error",
		];

		Assert.AreEqual( "hlslParser.Parse err: ANTLR - Mismatched Token Exception: 58:1 at Translucent();", ShaderUpgrade.FirstError( output ) );
	}

	[TestMethod]
	public void FirstErrorWithoutAnErrorIsTheFirstLine()
	{
		Assert.AreEqual( "something went wrong", ShaderUpgrade.FirstError( ["", "something went wrong", "and more"] ) );
		Assert.IsNull( ShaderUpgrade.FirstError( [] ) );
	}
}
