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
		if ( NativeEngine.EngineGlobal.AppIsDedicatedServer() )
			return;

		// Not outside of editor, we dont even resourcecompiler there
		if ( !Application.IsEditor )
			return;

		string dllName = "vfx_vulkan.dll";

		if ( !native.IsNull )
			return;

		native = NativeEngine.CreateInterface.LoadInterface( dllName, "VFX_DLL_001" );

		if ( native.IsNull )
			throw new System.Exception( $"Failed to load {dllName}" );

		// the shader compiler only needs the filesystem interface so we just pass
		// in the createinterface for that, directly.
		var createinterface = NativeEngine.CreateInterface.GetCreateInterface( "filesystem_stdio.dll" );

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

	/// <summary>
	/// Recompile a shader from an already-compiled <c>.shader_c</c>, using the per-program HLSL source embedded
	/// in it. Published packages ship only <c>.shader_c</c> (no <c>.shader</c> source), but addon shaders embed
	/// their masked + include-expanded source - so we can bring an outdated shader up to the current VCS version
	/// without the original source file. Returns <see cref="Results.Success"/> false (no <see cref="Results.CompiledShader"/>)
	/// if the shader has no embedded source or fails to load. The result's bytes are written by the caller.
	/// </summary>
	internal static async Task<Results> RecompileFromCompiled( string compiledFilePath, ShaderCompileOptions compileOptions, CancellationToken token )
	{
		var result = new Results();

		var embedded = ShaderSource.ReadEmbeddedSource( compiledFilePath );
		if ( embedded is null || embedded.Count == 0 )
		{
			// Core shaders don't embed source (and aren't published) - nothing we can do with just the .shader_c.
			Log.Warning( $"No embedded source in {compiledFilePath} - can't recompile" );
			return result;
		}

		var vfx = new Shader();
		if ( !vfx.LoadCompiledForRecompile( compiledFilePath ) )
		{
			Log.Warning( $"Failed to load compiled shader for recompile ({compiledFilePath})" );
			return result;
		}

		// Re-build the program list from the embedded source. Each entry was originally one compiled program
		// (the FEATURE pseudo-program isn't in here - Serialize writes it straight from the native vfx).
		var shaderSource = new ShaderSource { AbsolutePath = compiledFilePath };

		foreach ( var (name, source) in embedded )
		{
			if ( !Enum.TryParse<ShaderProgramType>( name, out var programType ) )
			{
				Log.Warning( $"Unknown shader program '{name}' in embedded source of {compiledFilePath}" );
				return result;
			}

			var program = new ProgramSource { ProgramType = programType };
			shaderSource.Programs.Add( program );

			var success = await program.RecompileFromMaskedSource( compileOptions, vfx, source, result, token );
			if ( !success )
				return result;
		}

		token.ThrowIfCancellationRequested();

		vfx.native.FinalizeCompile();
		vfx.native.InitializeWrite();

		// Re-embed the source so the output stays recompilable next time it goes out of date.
		var bytes = shaderSource.Serialize( vfx, result, serializeSource: true );

		// GenerateResourceBytes picks the resource compiler by file extension and only knows ".shader" (the source),
		// not ".shader_c" (the compiled output) - so hand it the source-equivalent name. The bytes are what matter;
		// the caller writes them to the .shader_c path.
		var sourceName = compiledFilePath.EndsWith( "_c", StringComparison.OrdinalIgnoreCase )
			? compiledFilePath[..^2]
			: compiledFilePath;

		result.CompiledShader = CompileResourceFile( sourceName, bytes );
		result.Success = true;

		return result;
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
