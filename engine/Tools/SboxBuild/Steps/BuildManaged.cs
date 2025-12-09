using System.Collections;
using System.Collections.Generic;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class BuildManaged( string name, bool clean = false ) : Step( name )
{
	private Dictionary<string, string> CopyEnvironmentVariables()
	{
		var envVars = new Dictionary<string, string>();
		foreach ( DictionaryEntry entry in Environment.GetEnvironmentVariables() )
		{
			if ( entry.Value is null || entry.Value is not string strValue )
				continue;

			envVars[(string)entry.Key] = strValue;
		}
		return envVars;
	}

	protected override ExitCode RunInternal()
	{
		string engineDir = Path.Combine( Directory.GetCurrentDirectory(), "engine" );
		string rootDir = Directory.GetCurrentDirectory();

		try
		{
			var envVars = CopyEnvironmentVariables(); // Capture env vars once
			envVars["DisableAndroidTFM"] = "true";
			const string disableAndroid = "-p:DisableAndroidTFM=true ";

			Log.Info( "Step 1: Dotnet Clean" );
			if ( clean )
			{
				if ( !Utility.RunDotnetCommand( engineDir, "clean", envVars ) )
					return ExitCode.Failure;
			}
			else
			{
				Log.Info( "Skipping dotnet clean as cleanBuild is false." );
			}

			Log.Info( "Step 2: Dotnet Restore" );
			if ( !Utility.RunDotnetCommand( engineDir, "restore", envVars ) )
				return ExitCode.Failure;

			Log.Info( "Step 3: Build CodeGen.exe" );
			if ( !Utility.RunDotnetCommand( engineDir, "build Tools/CodeGen/ -o Tools/CodeGen/bin", envVars ) )
				return ExitCode.Failure;

			Log.Info( "Step 3a: Build CreateGameCache.exe" );
			if ( !Utility.RunDotnetCommand( engineDir, "build Tools/CreateGameCache/ -o Tools/CreateGameCache/bin", envVars ) )
				return ExitCode.Failure;

			Log.Info( "Step 4: Clear managed folder" );
			string managedDir = Path.Combine( rootDir, "game", "bin", "managed" );
			if ( Directory.Exists( managedDir ) )
			{
				try
				{
					Directory.Delete( managedDir, true );
					Directory.CreateDirectory( managedDir ); // Recreate the empty directory
					Log.Info( $"Successfully cleared directory: {managedDir}" );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"Warning: Failed to clear directory: {managedDir}. Error: {ex.Message}" );
					// Continue execution since this is a warning in the original script
				}
			}
			else
			{
				Log.Info( $"Directory does not exist, creating: {managedDir}" );
				Directory.CreateDirectory( managedDir );
			}

			Log.Info( "Step 5: Build Managed" );

			// Build Sandbox.System
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.System/Sandbox.System.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Sandbox.Access
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.Access/Sandbox.Access.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Sandbox.CodeUpgrader
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.CodeUpgrader/Sandbox.CodeUpgrader.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Sandbox.Compiling
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.Compiling/Sandbox.Compiling.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Sandbox.Event
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.Event/Sandbox.Event.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Sandbox.Filesystem
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.Filesystem/Sandbox.Filesystem.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Sandbox.Hotload
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.Hotload/Sandbox.Hotload.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Sandbox.Reflection
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.Reflection/Sandbox.Reflection.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Sandbox.Services
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.Services/Sandbox.Services.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Sandbox.SolutionGenerator
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.SolutionGenerator/Sandbox.SolutionGenerator.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Sandbox.Generator
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.Generator/Sandbox.Generator.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build ThirdParty/Topten.RichTextKit
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release ThirdParty/Topten.RichTextKit/Topten.RichTextKit.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Microsoft.AspNetCore.Components
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Microsoft.AspNetCore.Components/Microsoft.AspNetCore.Components.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;
			// Build Mounting/Sandbox.Mounting
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Mounting/Sandbox.Mounting/Sandbox.Mounting.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;

			// Build Tools/ShaderCompiler
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Tools/ShaderCompiler/ShaderCompiler.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;

			// Copy ShaderCompiler.dll and ShaderCompiler.runtimeconfig.json to game/bin/managed
			string shaderCompilerBuildDir = Path.Combine( engineDir, "Tools", "ShaderCompiler", "bin", "Release" ); // This is the actual build output folder
			string shaderCompilerDllSource = Path.Combine( shaderCompilerBuildDir, "ShaderCompiler.dll" );
			string shaderCompilerRuntimeConfigSource = Path.Combine( shaderCompilerBuildDir, "ShaderCompiler.runtimeconfig.json" );
			
			string targetManagedDir = Path.Combine( rootDir, "game", "bin", "managed" );
			string targetShaderCompilerDllPath = Path.Combine( targetManagedDir, "ShaderCompiler.dll" );
			string targetShaderCompilerRuntimeConfigPath = Path.Combine( targetManagedDir, "ShaderCompiler.runtimeconfig.json" );

			try
			{
				Directory.CreateDirectory( targetManagedDir ); // Ensure target directory exists
				File.Copy( shaderCompilerDllSource, targetShaderCompilerDllPath, true );
				Log.Info( $"Copied {shaderCompilerDllSource} to {targetShaderCompilerDllPath}" );
				File.Copy( shaderCompilerRuntimeConfigSource, targetShaderCompilerRuntimeConfigPath, true );
				Log.Info( $"Copied {shaderCompilerRuntimeConfigSource} to {targetShaderCompilerRuntimeConfigPath}" );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Warning: Failed to copy ShaderCompiler files. Error: {ex.Message}" );
				return ExitCode.Failure; // Treat as failure for now
			}


			// Finally build Sandbox.Engine
			if ( !Utility.RunDotnetCommand( engineDir, $"build -c Release Sandbox.Engine/Sandbox.Engine.csproj {disableAndroid}-p:TreatWarningsAsErrors=true -p:BaseIntermediateOutputPath=obj/ -p:ExcludeProject=Sandbox.Server", envVars ) ) return ExitCode.Failure;




			Log.Info( "Build completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Build failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}
}
