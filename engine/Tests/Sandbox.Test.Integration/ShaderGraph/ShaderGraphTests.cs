using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

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
	/// Test that we can create a minimal shader graph and compile it to
	/// </summary>
	[TestMethod]
	public async Task GraphToShader()
	{
		await InitializePackageLoader();
		var (shaderGraphType, resultNodeType, graphCompilerType) = FindShaderGraphTypes();

		var shaderGraph = CreateMinimalShaderGraph( shaderGraphType, resultNodeType, "Test shader graph" );
		var shaderCode = GenerateShaderCode( shaderGraph, shaderGraphType, graphCompilerType );

		// Basic validation that it looks like shader code
		Assert.IsTrue( shaderCode.Contains( "HEADER" ), "Generated shader doesn't contain expected HEADER section" );
		Assert.IsTrue( shaderCode.Contains( "MainPs" ), "Generated shader doesn't contain expected MainPs function" );

		Console.WriteLine( $"Generated shader code length: {shaderCode.Length} characters" );
		Console.WriteLine( "First 500 characters of generated shader:" );
		Console.WriteLine( shaderCode.Substring( 0, Math.Min( 500, shaderCode.Length ) ) );
	}

	/// <summary>
	/// Test that we can create a shader graph, generate shader code, and compile it using shadercompiler.exe
	/// </summary>
	[TestMethod]
	public async Task ShaderCompilation()
	{
		await InitializePackageLoader();
		var (shaderGraphType, resultNodeType, graphCompilerType) = FindShaderGraphTypes();

		var shaderGraph = CreateMinimalShaderGraph( shaderGraphType, resultNodeType, "Test shader graph for unit testing" );
		var shaderCode = GenerateShaderCode( shaderGraph, shaderGraphType, graphCompilerType );

		// Write the shader code to a file in the game directory
		var gameDir = Directory.GetCurrentDirectory();
		var shadersDir = Path.Combine( gameDir, "core", "shaders" );
		Directory.CreateDirectory( shadersDir );

		var shaderFileName = "test_shadergraph_unit_test.shader";
		var shaderPath = Path.Combine( shadersDir, shaderFileName );
		var shaderCPath = shaderPath + "_c";

		// Delete stale artifacts from a previous run - a leftover shader_c
		// would satisfy the File.Exists assert below without compiling anything
		File.Delete( shaderPath );
		File.Delete( shaderCPath );

		try
		{
			File.WriteAllText( shaderPath, shaderCode );
			Console.WriteLine( $"Wrote shader to: {shaderPath}" );

			// Find shadercompiler.exe
			var shaderCompilerPath = Path.Combine( gameDir, "bin", "managed", "shadercompiler.exe" );
			Assert.IsTrue( File.Exists( shaderCompilerPath ), $"Shader compiler not found at: {shaderCompilerPath}" );

			// Run shadercompiler.exe on this specific shadershader
			var success = await RunShaderCompiler( shaderCompilerPath, gameDir, shaderPath );
			Assert.IsTrue( success, "Shader compilation with shadercompiler.exe failed" );

			// Check if shader_c file was created
			Assert.IsTrue( File.Exists( shaderCPath ), $"Compiled shader file not found at: {shaderCPath}" );

			var shaderCSize = new FileInfo( shaderCPath ).Length;
			Assert.IsTrue( shaderCSize > 0, "Compiled shader file is empty" );

			Console.WriteLine( $"Successfully compiled shader to {shaderCPath} ({shaderCSize} bytes)" );
		}
		finally
		{
			// Clean up even when an assert above fails, so we don't leak files into game/core/shaders/
			try
			{
				File.Delete( shaderPath );
				File.Delete( shaderCPath );
			}
			catch ( Exception ex )
			{
				Console.WriteLine( $"Failed to clean up temp files: {ex.Message}" );
			}
		}
	}

	private async Task<bool> RunShaderCompiler( string shaderCompilerPath, string workingDirectory, string shaderFile )
	{
		using ( var process = new Process() )
		{
			process.StartInfo.FileName = shaderCompilerPath;
			process.StartInfo.Arguments = shaderFile; // Just compile this specific shader
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;
			process.StartInfo.CreateNoWindow = true;
			process.StartInfo.WorkingDirectory = workingDirectory;

			var outputMessages = new List<string>();
			var errorMessages = new List<string>();

			process.OutputDataReceived += ( sender, e ) =>
			{
				if ( e.Data != null )
				{
					outputMessages.Add( e.Data );
					Console.WriteLine( $"ShaderCompiler: {e.Data}" );
				}
			};

			process.ErrorDataReceived += ( sender, e ) =>
			{
				if ( e.Data != null )
				{
					errorMessages.Add( e.Data );
					Console.WriteLine( $"ShaderCompiler ERROR: {e.Data}" );
				}
			};

			Console.WriteLine( $"Running: {shaderCompilerPath} {process.StartInfo.Arguments}" );
			process.Start();

			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			await process.WaitForExitAsync();

			if ( process.ExitCode != 0 )
			{
				Console.WriteLine( $"Shader compiler failed with exit code: {process.ExitCode}" );
				foreach ( var error in errorMessages )
				{
					Console.WriteLine( $"Error: {error}" );
				}
				return false;
			}

			return true;
		}
	}
}
