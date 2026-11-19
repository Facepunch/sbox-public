using Borealis.Source2.Materials;
using System;
using System.IO;

namespace ResourceTests;

/// <summary>
/// Borealis.Gpu takes the shader a material names from code or from its compiled file, not from the material system:
/// it must be the material system's.
/// </summary>
[TestClass]
public class MaterialShaderNameTests
{
	static string GameFolder => Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" );

	/// <summary>The shader a material names: its compiled file's, or what code gave it, as the material system has it.</summary>
	[TestMethod]
	public void MaterialsNameTheirShaderAsTheMaterialSystemDoes()
	{
		foreach ( var path in new[] { "materials/default.vmat", "materials/dev/reflectivity_30.vmat", "materials/skybox/skybox_dark_01.vmat" } )
		{
			var material = Material.Load( path );
			Assert.IsNotNull( material, path );
			Assert.IsNull( material.ShaderNameFromCode, path );

			var file = Path.Combine( GameFolder, "core", path + "_c" );
			Assert.IsTrue( File.Exists( file ), file );
			Assert.AreEqual( material.native.GetString( "shader", "" ), MaterialFile.Read( File.ReadAllBytes( file ) ).Shader, path );

			var copy = material.CreateCopy();
			Assert.IsNull( copy.ShaderNameFromCode, $"{path} copy" );
			copy.Shader = Shader.Load( "shaders/complex.shader" );
			Assert.AreEqual( copy.native.GetString( "shader", "" ), copy.ShaderNameFromCode, $"{path} copy with its shader set" );
			Assert.AreEqual( copy.native.GetString( "shader", "" ), copy.CreateCopy().ShaderNameFromCode, $"{path} copy of a copy" );
		}

		var made = Material.Create( "test_made_from_shader", "shaders/complex.shader" );
		Assert.AreEqual( made.native.GetString( "shader", "" ), made.ShaderNameFromCode );
	}
}
