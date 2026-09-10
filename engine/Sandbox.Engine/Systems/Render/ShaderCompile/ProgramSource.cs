using Humanizer;
using System.Threading;

namespace Sandbox.Engine.Shaders;

class ProgramSource
{
	internal ShaderProgramType ProgramType;

	public bool IsOutOfDate { get; set; }

	~ProgramSource()
	{
		if ( builder.IsValid )
		{
			builder.Delete();
			builder = default;
		}
	}

	CVfxByteCodeManager builder;

	/// <summary>
	/// Compile a single program on this shader
	/// </summary>
	internal async Task<bool> Compile( ShaderCompileOptions options, Shader vfx, string source, ShaderCompile.Results result, CancellationToken token, string absolutePath, string relativePath )
	{
		using var context = ShaderCompile.GetSharedContext( ProgramType );
		context.MaskedSource = ShaderTools.MaskShaderSource( source, ProgramType, false );

		ShaderPreprocessor preprocessor = new( new ShaderPreprocessorOptions() { ExpandIncludes = true, IgnoreCoreIncludes = true } );
		context.MaskedSource = preprocessor.Preprocess( context.MaskedSource, absolutePath, relativePath );

		return await CompileCore( options, vfx, result, context, token );
	}

	/// <summary>
	/// Recompile this program from source that has already been masked and include-expanded - the form embedded
	/// in a compiled <c>.shader_c</c>. Skips the mask + preprocess steps <see cref="Compile"/> runs on raw
	/// <c>.shader</c> source, because that source isn't available when recompiling from a published package.
	/// </summary>
	internal async Task<bool> RecompileFromMaskedSource( ShaderCompileOptions options, Shader vfx, string maskedSource, ShaderCompile.Results result, CancellationToken token )
	{
		using var context = ShaderCompile.GetSharedContext( ProgramType );
		context.MaskedSource = maskedSource;

		return await CompileCore( options, vfx, result, context, token );
	}

	/// <summary>
	/// Compiles every combo for this program using the source already set on <paramref name="context"/>.
	/// </summary>
	async Task<bool> CompileCore( ShaderCompileOptions options, Shader vfx, ShaderCompile.Results result, ShaderCompileContext context, CancellationToken token )
	{
		bool nonInteractiveConsole = Console.IsOutputRedirected || Console.IsInputRedirected || Console.IsErrorRedirected;

		FastTimer fastTimer = FastTimer.StartNew();

		var stepResult = new ShaderCompile.Results.Program();
		result.Programs.Add( stepResult );

		List<CompiledCombo> allCompiles = new();

		var p = vfx.GetProgram( ProgramType );

		var combos = p.EnumerateCombos( ProgramType )
								.ToArray()
								.OrderBy( x => Guid.NewGuid() )
								.ToArray();

		var totalCombos = combos.Length;

		stepResult.Name = $"{ProgramType}";
		stepResult.ComboCount = totalCombos;
		stepResult.Source = context.MaskedSource;

		if ( options.ConsoleOutput )
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine( $"	{ProgramType} - {totalCombos:n0} {"combo".Pluralize()}" );
			Console.ForegroundColor = ConsoleColor.White;
		}

		var originalPos = nonInteractiveConsole ? default : Console.GetCursorPosition();
		var updateUI = () =>
		{
			if ( nonInteractiveConsole )
			{
				Console.Write( $"	{Math.Floor( ((double)allCompiles.Count / (double)totalCombos) * 100.0 )}%" );
			}
			else
			{
				Console.SetCursorPosition( Console.WindowLeft, originalPos.Top );
				Console.Write( new string( ' ', 8 ) );
				Console.SetCursorPosition( Console.WindowLeft, originalPos.Top );
				Console.Write( $"	{Math.Floor( ((double)allCompiles.Count / (double)totalCombos) * 100.0 )}%" );
				Console.SetCursorPosition( Console.WindowLeft, originalPos.Top );
			}
		};

		var timeBetweenUpdates = nonInteractiveConsole ? 2000 : 32;
		int errors = 0;

		var compileCombo = ( ShaderProgram.Combo d ) =>
		{
			try
			{
				if ( errors > 0 )
					return;

				if ( token.IsCancellationRequested )
					return;

				var result = ShaderCompile.CompileSingleCombo( vfx, this, d.Static, d.Dynamic, context, !options.ForceRecompile );

				lock ( this )
				{
					if ( !result.IsSuccess )
					{
						Interlocked.Add( ref errors, 1 );
					}

					allCompiles.Add( result );

					if ( options.ConsoleOutput && fastTimer.ElapsedMilliSeconds > timeBetweenUpdates )
					{
						fastTimer = FastTimer.StartNew();
						updateUI();
					}
				}
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
		};

		if ( options.SingleThreaded )
		{
			foreach ( var d in combos )
			{
				await Task.Run( () => compileCombo( d ), token );
			}
		}
		else
		{
			await Task.Run( () =>
			{
				Parallel.ForEach( combos, compileCombo );
			}, token );
		}

		token.ThrowIfCancellationRequested();

		//
		// deduplicate the compiler outputs, add them to our log
		//
		{
			var outputs = string.Join( '\n', allCompiles.Select( x => x.CompilerOutput ).Distinct() ).Trim();

			foreach ( var line in outputs.Split( '\n', StringSplitOptions.RemoveEmptyEntries ) )
			{
				if ( options.ConsoleOutput )
				{
					Console.WriteLine( line );
				}

				stepResult.Log( line );
			}
		}

		// One of our compiles failed, so just bail
		if ( allCompiles.Any( x => !x.IsSuccess ) )
		{
			return false;
		}

		if ( builder.IsValid )
		{
			builder.Delete();
			builder = default;
		}

		builder = CVfxByteCodeManager.Create();

		foreach ( var staticGroup in allCompiles.GroupBy( x => x.StaticCombo ).OrderBy( x => x.Key ) )
		{
			builder.OnStaticCombo( staticGroup.Key );

			foreach ( var entry in staticGroup.OrderBy( x => x.DynamicCombo ) )
			{
				builder.OnDynamicCombo( entry.GetResult() );

				vfx.native.WriteCombo( ProgramType, entry.StaticCombo, entry.DynamicCombo, entry.GetResult() );
			}
		}

		stepResult.Success = true;
		return true;
	}

	public byte[] BuildCompiledShader( Shader vfx )
	{
		// Step 1. Copy all our compiled shaders into a CVfxByteCodeManager
		using var buffer = CUtlBuffer.Create();
		vfx.native.WriteProgramToBuffer( ProgramType, builder, buffer );

		return buffer.ToArray();
	}
}
