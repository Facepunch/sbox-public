using System;
using System.Collections.Generic;
using System.IO;

namespace ShaderGraphTests;

/// <summary>
/// Tests for ShaderGraph compilation
/// These tests verify that shader graphs from the tools addon can compile correctly
/// </summary>
[TestClass]
public class ShaderGraphTest
{
	private Sandbox.PackageLoader packageLoader;
	private Sandbox.PackageLoader.Enroller enroller;

	/// <summary>
	/// Initialize the package loader and load the tools addon
	/// </summary>
	private async Task InitializePackageLoader()
	{
		packageLoader = new Sandbox.PackageLoader( "shadergraph_test", GetType().Assembly );
		packageLoader.ToolsMode = true;
		enroller = packageLoader.CreateEnroller( "shadergraph_test" );

		var project = Project.AddFromFile( "addons/tools/.sbproj" );
		await Project.SyncWithPackageManager();
		await Project.CompileAsync();

		enroller.LoadPackage( project.Package.FullIdent, true );
		packageLoader.Tick();

		var loadedAssemblies = enroller.GetLoadedAssemblies();
		Assert.IsTrue( loadedAssemblies.Length > 0, "No assemblies were loaded from the tools addon" );
	}

	/// <summary>
	/// Find and return the required ShaderGraph types from loaded assemblies
	/// </summary>
	private (Type shaderGraphType, Type resultNodeType, Type graphCompilerType) FindShaderGraphTypes()
	{
		var loadedAssemblies = enroller.GetLoadedAssemblies();
		Type shaderGraphType = null;
		Type resultNodeType = null;
		Type graphCompilerType = null;

		foreach ( var loadedAssembly in loadedAssemblies )
		{
			var types = loadedAssembly.Assembly.GetTypes();
			foreach ( var type in types )
			{
				if ( type.Name == "ShaderGraph" && type.Namespace == "Editor.ShaderGraph" )
					shaderGraphType = type;
				else if ( type.Name == "Result" && type.Namespace == "Editor.ShaderGraph" )
					resultNodeType = type;
				else if ( type.Name == "GraphCompiler" && type.Namespace == "Editor.ShaderGraph" )
					graphCompilerType = type;
			}
		}

		Assert.IsNotNull( shaderGraphType, "ShaderGraph type not found" );
		Assert.IsNotNull( resultNodeType, "Result node type not found" );
		Assert.IsNotNull( graphCompilerType, "GraphCompiler type not found" );

		return (shaderGraphType, resultNodeType, graphCompilerType);
	}

	/// <summary>
	/// Create a minimal shader graph with a result node
	/// </summary>
	private object CreateMinimalShaderGraph( Type shaderGraphType, Type resultNodeType, string description )
	{
		var shaderGraph = Activator.CreateInstance( shaderGraphType );
		Assert.IsNotNull( shaderGraph, "Failed to create ShaderGraph instance" );

		// Set basic properties
		var descriptionProperty = shaderGraphType.GetProperty( "Description" );
		descriptionProperty?.SetValue( shaderGraph, description );

		// Create a Result node
		var resultNode = Activator.CreateInstance( resultNodeType );
		Assert.IsNotNull( resultNode, "Failed to create Result node instance" );

		// Add the result node to the graph
		var addNodeMethod = shaderGraphType.GetMethod( "AddNode" );
		Assert.IsNotNull( addNodeMethod, "AddNode method not found" );
		addNodeMethod.Invoke( shaderGraph, new[] { resultNode } );

		return shaderGraph;
	}

	/// <summary>
	/// Generate shader code from a shader graph using the GraphCompiler
	/// </summary>
	private string GenerateShaderCode( object shaderGraph, Type shaderGraphType, Type graphCompilerType )
	{
		// Create a compiler and generate shader code
		var compilerConstructor = graphCompilerType.GetConstructor( new[] { shaderGraphType, typeof( bool ) } );
		Assert.IsNotNull( compilerConstructor, "GraphCompiler constructor not found" );

		var compiler = compilerConstructor.Invoke( new[] { shaderGraph, false } );
		Assert.IsNotNull( compiler, "Failed to create GraphCompiler instance" );

		// Generate the shader source code
		var generateMethod = graphCompilerType.GetMethod( "Generate" );
		Assert.IsNotNull( generateMethod, "Generate method not found" );

		var shaderCode = generateMethod.Invoke( compiler, null ) as string;
		Assert.IsNotNull( shaderCode, "Shader compilation failed - returned null" );
		Assert.IsTrue( shaderCode.Length > 0, "Shader compilation returned empty string" );

		return shaderCode;
	}

	/// <summary>
	/// Test that we can create a shader graph, generate its Slang surface, and compile it with every pass.
	/// This also covers generating the code on its own, so the tools addon is only compiled once.
	/// </summary>
	[TestMethod]
	public async Task ShaderCompilation()
	{
		await InitializePackageLoader();
		var (shaderGraphType, resultNodeType, graphCompilerType) = FindShaderGraphTypes();

		var shaderGraph = CreateMinimalShaderGraph( shaderGraphType, resultNodeType, "Test shader graph for unit testing" );
		var shaderCode = GenerateShaderCode( shaderGraph, shaderGraphType, graphCompilerType );

		// A graph is a Slang surface: a [Material] struct, its pixel stage Evaluate
		Assert.IsTrue( shaderCode.Contains( "import sbox.surface;" ), "Generated shader isn't a Slang surface" );
		Assert.IsTrue( shaderCode.Contains( "public Material Evaluate( SurfaceInput i )" ), "Generated surface doesn't contain Evaluate" );
		Assert.IsFalse( Editor.ShaderUpgrade.IsLegacySource( shaderCode ), "Generated shader is still VFX" );

		// Write it to the game's shaders, named as the compiler names its struct, and compile it
		var gameDir = Directory.GetCurrentDirectory();
		var shadersDir = Path.Combine( gameDir, "core", "shaders" );
		Directory.CreateDirectory( shadersDir );
		var shaderPath = Path.Combine( shadersDir, "ShaderGraph.slang" );
		File.Delete( shaderPath );

		// A graph whose name isn't an identifier saves under that name, its struct named as one
		var namedPath = Path.Combine( shadersDir, "Shader Graph-2.slang" );
		File.Delete( namedPath );

		try
		{
			File.WriteAllText( shaderPath, shaderCode );
			Console.WriteLine( $"Wrote shader to: {shaderPath}" );

			var errors = await Editor.ShaderUpgrade.CompileSurfaceAsync( shaderPath );
			Assert.IsNull( errors, $"The generated surface didn't compile: {errors}" );

			var surfaceName = (string)graphCompilerType.GetMethod( "SurfaceName" ).Invoke( null, new object[] { "Shader Graph-2" } );
			Assert.AreEqual( "Shader_Graph_2", surfaceName );
			Assert.AreEqual( "ShaderGraph", graphCompilerType.GetMethod( "SurfaceName" ).Invoke( null, new object[] { "ShaderGraph" } ), "a name that is an identifier stays" );
			var named = Activator.CreateInstance( graphCompilerType, shaderGraph, false );
			graphCompilerType.GetProperty( "Name" ).SetValue( named, surfaceName );
			var namedCode = (string)graphCompilerType.GetMethod( "Generate" ).Invoke( named, null );
			Assert.IsTrue( namedCode.Contains( "public struct Shader_Graph_2 " ), "The surface's struct isn't the name as an identifier" );
			File.WriteAllText( namedPath, namedCode );
			errors = await Editor.ShaderUpgrade.CompileSurfaceAsync( namedPath );
			Assert.IsNull( errors, $"The surface of a graph named with a space and a dash didn't compile: {errors}" );
		}
		finally
		{
			// Clean up even when an assert above fails, so we don't leak files into game/core/shaders/
			try
			{
				File.Delete( shaderPath );
				File.Delete( namedPath );
			}
			catch ( Exception ex )
			{
				Console.WriteLine( $"Failed to clean up temp files: {ex.Message}" );
			}
		}
	}
}
