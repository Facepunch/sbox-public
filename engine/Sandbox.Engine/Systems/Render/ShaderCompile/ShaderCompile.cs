using System.Runtime.InteropServices;
using System.IO;
using System.Threading;

namespace Sandbox.Engine.Shaders;

public static class ShaderCompile
{
	static IVfx native;

	/// <summary>
	/// The results of a shader compile
	/// </summary>
	public class Results
	{
		/// <summary>
		/// True if the shader was compiled successfully. False indicates an error
		/// occurred. You can dig deeper into why in Programs.
		/// </summary>
		public bool Success { get; set; }

		/// <summary>
		/// If true then this compile was skipped because nothing changed
		/// </summary>
		public bool Skipped { get; set; }

		/// <summary>
		/// If successful, this contains the actual resource-encoded bytes of the
		/// shader compile.
		/// </summary>
		public byte[] CompiledShader { get; set; }

		/// <summary>
		/// The results of an individual shader program compile (PS, VS etc)
		/// </summary>
		public class Program
		{
			/// <summary>
			/// The identifier for this program
			/// </summary>
			public string Name { get; set; }

			/// <summary>
			/// How many combos had to be compiled for this program. This is Static * Dynamic.
			/// </summary>
			public int ComboCount { get; set; }

			/// <summary>
			/// The full pre-processed source for this shader
			/// </summary>
			public string Source { get; set; }

			/// <summary>
			/// True if this was compiled successfully
			/// </summary>
			public bool Success { get; set; }

			/// <summary>
			/// Shader compile output, warnings and errors
			/// </summary>
			public List<string> Output { get; set; }

			internal void Log( string line )
			{
				Output ??= new List<string>();
				Output.Add( line );
			}
		}

		public List<Program> Programs { get; set; } = new List<Program>();
	}


	static ShaderCompile()
	{
		string vfxDllName = "vfx_vulkan";
		string filesystemDllName = "filesystem_stdio";
		string dllExtension = ".dll";

		if ( OperatingSystem.IsLinux() )
		{
			vfxDllName = "libvfx_vulkan";
			filesystemDllName = "libfilesystem_stdio";
			dllExtension = ".so";
		}

		string fullVfxDllName = $"{vfxDllName}{dllExtension}";
		bool useOpenEngine = Environment.GetEnvironmentVariable( "SBOX_OPENENGINE_VFX" ) == "1";
		if ( useOpenEngine )
		{
			fullVfxDllName = "libengine2.so";
			filesystemDllName = "libfilesystem_stdio";
			dllExtension = ".so";
		}

		// Ensure filesystem is initialized for shader includes when running tools/CLI
		if ( EngineFileSystem.Root is null || EngineFileSystem.Assets is null )
		{
			var baseDir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
			var gameRoot = Path.GetFullPath( Path.Combine( baseDir, "..", ".." ) );
			EngineFileSystem.Initialize( gameRoot );
			EngineFileSystem.InitializeAddonsFolder();
			EngineFileSystem.InitializeDataFolder();
			EngineFileSystem.InitializeDownloadsFolder();
		}

		if ( !native.IsNull )
			return;

		var vfxPath = fullVfxDllName;
		if ( OperatingSystem.IsLinux() )
		{
			var baseDir = AppContext.BaseDirectory ?? string.Empty;
			var linuxRt64 = Path.GetFullPath( Path.Combine( baseDir, "..", "linuxsteamrt64", fullVfxDllName ) );
			if ( File.Exists( linuxRt64 ) )
			{
				vfxPath = linuxRt64;
			}
			else
			{
				var baseDirPath = Path.Combine( baseDir, fullVfxDllName );
				if ( File.Exists( baseDirPath ) )
					vfxPath = baseDirPath;
			}
		}

		var createInterfacePtr = NativeEngine.CreateInterface.GetCreateInterface( vfxPath );
		if ( createInterfacePtr == IntPtr.Zero )
			throw new System.Exception( $"Failed to find CreateInterface in {vfxPath}" );

		var createInterface = Marshal.GetDelegateForFunctionPointer<NativeEngine.CreateInterface.CreateInterfaceFn>( createInterfacePtr );
		native = new IVfx( createInterface( "VFX_DLL_001", IntPtr.Zero ) );
		Log.Info( $"Loaded VFX native: type={native.GetType().FullName}, value={native}" );
		
		if ( native.IsNull )
			throw new System.Exception( $"Failed to load {fullVfxDllName}" );

		// the shader compiler only needs the filesystem interface so we just pass
		// in the createinterface for that, directly.
		var fsDll = $"{filesystemDllName}{dllExtension}";
		if ( OperatingSystem.IsLinux() )
		{
			var baseDir = AppContext.BaseDirectory ?? string.Empty;
			var linuxRt64Fs = Path.GetFullPath( Path.Combine( baseDir, "..", "linuxsteamrt64", fsDll ) );
			if ( File.Exists( linuxRt64Fs ) )
			{
				fsDll = linuxRt64Fs;
			}
			else
			{
				var baseDirFs = Path.Combine( baseDir, fsDll );
				if ( File.Exists( baseDirFs ) )
					fsDll = baseDirFs;
			}
		}

		var createinterface = NativeEngine.CreateInterface.GetCreateInterface( fsDll );

		native.Init( createinterface );
	}

	/// <summary>
	/// Compile a shader from a filename ("/folder/file.shader")
	/// </summary>
	internal static async Task<Results> Compile( string absoluteFilePath, string relativeFilePath, ShaderCompileOptions compileOptions, CancellationToken token )
	{
		var shader = new ShaderSource();
		shader.AbsolutePath = absoluteFilePath;
		shader.RelativePath = relativeFilePath;

		shader.Read();

		if ( !compileOptions.ForceRecompile && !shader.IsOutOfDate )
			return new Results { Success = true, Skipped = true };

		var results = await CompileShader( shader, compileOptions, token );
		if ( results.CompiledShader is null || results.Success == false )
			return results;

		token.ThrowIfCancellationRequested();

		await System.IO.File.WriteAllBytesAsync( shader.AbsolutePath + "_c", results.CompiledShader );

		return results;
	}

	// Mounted null in ShaderCompiler.exe, Assets doesn't contain all projects in editor
	internal static BaseFileSystem FileSystem => EngineFileSystem.Mounted ?? EngineFileSystem.Assets;

	static async Task<Results> CompileShader( ShaderSource s, ShaderCompileOptions compileOptions, CancellationToken token )
	{
		var result = new Results();
		var vfx = new Shader();
		if ( !vfx.LoadFromSource( s.AbsolutePath ) )
		{
			Log.Warning( $"Failed to load shader file ({s.AbsolutePath}) - is it in an assets folder?" );
			return result;
		}

		// Open the source file
		var source = await System.IO.File.ReadAllTextAsync( s.AbsolutePath );

		foreach ( var program in s.Programs )
		{
			var success = await program.Compile( compileOptions, vfx, source, result, token, s.AbsolutePath, s.RelativePath );

			if ( !success )
				return result;
		}

		vfx.native.FinalizeCompile();
		vfx.native.InitializeWrite();

		// core shader
		string relativePath = FileSystem.GetRelativePath( s.AbsolutePath );
		bool coreAsset = !string.IsNullOrEmpty( relativePath ) && EngineFileSystem.CoreContent.FileExists( relativePath );

		// serialize it
		var bytes = s.Serialize( vfx, result, serializeSource: !coreAsset );

		// convert to a resource file
		result.CompiledShader = CompileResourceFile( s.AbsolutePath, bytes );
		result.Success = true;

		// done
		return result;
	}

	/// <summary>
	/// Convert a shader to a resource file
	/// </summary>
	static unsafe byte[] CompileResourceFile( string filename, byte[] data )
	{
		fixed ( byte* dataPtr = data )
		{
			using CUtlBuffer buffer = IResourceCompilerSystem.GenerateResourceBytes( filename, (IntPtr)dataPtr, data.Length );
			return buffer.ToArray();
		}
	}

	internal static CompiledCombo CompileSingleCombo( Shader vfx, ProgramSource program, ulong staticCombo, ulong dynamicCombo, ShaderCompileContext context, bool useShaderCache )
	{
		var result = native.CompileShader( context.GetNative(), staticCombo, dynamicCombo, vfx.native, NativeEngine.VfxCompileTarget_t.SM_6_0_VULKAN, program.ProgramType, useShaderCache, 0 );
		return new CompiledCombo( result, program, staticCombo, dynamicCombo );
	}

	internal static ShaderCompileContext GetSharedContext( ShaderProgramType programType )
	{
		return new ShaderCompileContext( native.CreateSharedContext() );
	}
}
