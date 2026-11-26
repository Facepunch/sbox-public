using Borealis.Source2.Materials;
using System;
using System.IO;

namespace ResourceTests;

/// <summary>
/// A material's shader is the one code gave it, else the one its compiled file names, without asking the material system.
/// </summary>
[TestClass]
public class MaterialShaderNameTests
{
	static string GameFolder => Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" );

	/// <summary>The shader a material names: its compiled file's, or what code gave it.</summary>
	[TestMethod]
	public void MaterialsNameTheirShaderFromTheirFileOrCode()
	{
		foreach ( var (path, shader) in new[] { ("materials/default.vmat", "shaders/standard.shader"), ("materials/dev/reflectivity_30.vmat", "shaders/standard.shader"), ("materials/skybox/skybox_dark_01.vmat", "shaders/sky.shader") } )
		{
			var material = Material.Load( path );
			Assert.IsNotNull( material, path );
			Assert.IsNull( material.ShaderNameFromCode, path );

			var file = Path.Combine( GameFolder, "core", path + "_c" );
			Assert.IsTrue( File.Exists( file ), file );
			Assert.AreEqual( shader, MaterialFile.Read( File.ReadAllBytes( file ) ).Shader, path );
			Assert.AreEqual( shader, material.ShaderName, path );

			var copy = material.CreateCopy();
			Assert.IsNull( copy.ShaderNameFromCode, $"{path} copy" );
			Assert.AreEqual( shader, copy.ShaderName, $"{path} copy" );
			copy.Shader = Shader.Load( "shaders/glass.shader" );
			Assert.AreEqual( "shaders/glass.shader", copy.ShaderNameFromCode, $"{path} copy with its shader set" );
			Assert.AreEqual( "shaders/glass.shader", copy.ShaderName, $"{path} copy with its shader set" );
			Assert.AreEqual( "shaders/glass.shader", copy.CreateCopy().ShaderName, $"{path} copy of a copy" );
		}

		var made = Material.Create( "test_made_from_shader", "shaders/complex.shader" );
		Assert.AreEqual( "shaders/complex.shader", made.ShaderNameFromCode );
		Assert.AreEqual( "shaders/complex.shader", made.ShaderName );
	}
}
