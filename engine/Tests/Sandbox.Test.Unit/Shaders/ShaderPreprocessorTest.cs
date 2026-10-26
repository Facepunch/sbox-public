using Sandbox;
using Sandbox.Engine.Shaders;

namespace EngineTests;

/// <summary>
/// Games compile shaders from their own files, so includes are only expanded from inside the filesystem they gave us
/// </summary>
[TestClass]
public class ShaderPreprocessorTest
{
	static string Preprocess( string source )
	{
		var fs = FileSystem.CreateMemoryFileSystem();
		fs.CreateDirectory( "shaders" );
		fs.WriteAllText( "shaders/tint.hlsl", "float4 Tint;" );
		fs.WriteAllText( "secret.hlsl", "float4 Secret;" );

		var preprocessor = new ShaderPreprocessor( new ShaderPreprocessorOptions { ExpandIncludes = true, IgnoreCoreIncludes = true, FileSystem = fs } );
		return preprocessor.Preprocess( source, null, "shaders/test.shader", addHeader: false );
	}

	[TestMethod]
	public void ExpandsRelativeIncludes()
	{
		Assert.IsTrue( Preprocess( "#include \"tint.hlsl\"" ).Contains( "float4 Tint;" ) );
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
