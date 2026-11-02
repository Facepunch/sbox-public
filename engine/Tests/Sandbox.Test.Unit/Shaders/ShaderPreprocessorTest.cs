using Sandbox;
using Sandbox.Engine.Shaders;

namespace EngineTests;

/// <summary>
/// Games compile shaders from their own files, so includes are only expanded from inside the filesystem they gave us
/// </summary>
[TestClass]
public class ShaderPreprocessorTest
{
	static string Preprocess( string source, string shaderPath = "shaders/test.shader" )
	{
		var fs = FileSystem.CreateMemoryFileSystem();
		fs.CreateDirectory( "shaders" );
		fs.WriteAllText( "shaders/tint.hlsl", "float4 Tint;" );
		fs.WriteAllText( "secret.hlsl", "float4 Secret;" );
		fs.CreateDirectory( "materials/shaders" );
		fs.WriteAllText( "materials/shaders/infection.hlsl", "float4 Infection;" );

		var preprocessor = new ShaderPreprocessor( new ShaderPreprocessorOptions { ExpandIncludes = true, IgnoreCoreIncludes = true, FileSystem = fs } );
		return preprocessor.Preprocess( source, null, shaderPath, addHeader: false );
	}

	[TestMethod]
	public void ExpandsRelativeIncludes()
	{
		Assert.IsTrue( Preprocess( "#include \"tint.hlsl\"" ).Contains( "float4 Tint;" ) );
	}

	/// <summary>
	/// A path from the root, which native would find on GAME, has to be expanded here too or the embedded source
	/// can't recompile without the project
	/// </summary>
	[TestMethod]
	public void ExpandsRootIncludes()
	{
		var output = Preprocess( "#include \"materials/shaders/infection.hlsl\"", "materials/shaders/test.shader" );

		Assert.IsTrue( output.Contains( "float4 Infection;" ) );
		Assert.IsFalse( output.Contains( "#include \"materials/shaders/infection.hlsl\"" ) );
	}

	[TestMethod]
	public void LeavesParentDirectoryIncludes()
	{
		var output = Preprocess( "#include \"../secret.hlsl\"" );

		Assert.IsFalse( output.Contains( "float4 Secret;" ) );
		Assert.IsTrue( output.Contains( "#include \"../secret.hlsl\"" ), "Left for the native include handler to refuse" );
	}

	[TestMethod]
	public void NoHeaderForWholeFiles()
	{
		Assert.IsFalse( Preprocess( "PS {}" ).Contains( "#line" ) );
	}
}
