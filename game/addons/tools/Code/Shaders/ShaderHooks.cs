using System.Diagnostics;
using System.Threading;

namespace Editor;

internal static partial class ShaderHooks
{
	[Event( "content.changed" )]
	public static void OnShaderWasEdited( string filename )
	{
		// only respond to shaders that changed
		// todo if a hlsl changed we could look at what files include it and recompile those
		// that would be genuinely useful

		//
		// Garry: at some point in the future we could run just this, which enables dynamic
		//		  compiling, and then it'll only recompile the shader that have changed.
		//
		//		  Or we could query the game and ask what materials are loaded and find out
		//		  which combos are being used. Then somehow restrict the actual compile to just
		//		  those combos. This would make iteration a ton faster. That is probably our
		//		  best bet. That would kick ass. Lets do that. Fuck dynamic compiling.
		//
		// ConsoleSystem.Run( $"mat_reloadshaders {shaderName}" );

		//
		// Compile the shaders - in fast mode
		//
		CompileShader( filename.Trim( '/' ) );
	}

	private static ICodeEditor _editor;
	private static ICodeEditor Editor
	{
		get
		{
			if ( _editor != null )
				return _editor;

			var editor = new CodeEditors.VisualStudioCode();
			if ( editor.IsInstalled() )
			{
				_editor = editor;
				return editor;
			}

			return null;
		}
	}

	[Event( "open.shader" )]
	public static void OpenShader( string filename )
	{
		Editor?.OpenFile( filename );
	}

	static CancellationTokenSource cts;

	[Event( "compile.shader" )]
	public static void CompileShader( string shader )
	{
		if ( !FileSystem.Mounted.FileExists( shader ) ) return;
		if ( !shader.EndsWith( ".shader" ) ) return;

		cts?.Cancel();
		cts?.Dispose();
		cts = new CancellationTokenSource();

		_ = RunCompile( shader, cts.Token );
	}

	/// <summary>
	/// Nothing awaits the compile, so without this a failure would sit on the task until the
	/// finalizer rethrew it - long after the edit, and blamed on whatever was running then.
	/// </summary>
	static async Task RunCompile( string shader, CancellationToken token )
	{
		try
		{
			await CompileShader( shader, token );
		}
		catch ( System.OperationCanceledException )
		{
			// A newer edit took over.
		}
		catch ( System.Exception e )
		{
			Log.Error( e, $"Failed to compile {shader}" );
		}
	}

	static async Task CompileShader( string file, CancellationToken token )
	{
		Log.Info( $"Compiling: {file}" );
		var sw = Stopwatch.StartNew();

		var options = new Sandbox.Engine.Shaders.ShaderCompileOptions
		{
			ConsoleOutput = false,
			ForceRecompile = true
		};

		var t = await EditorUtility.CompileShader( file, options, token );
		int combos = 0;
		foreach ( var program in t.Programs )
		{
			combos += program.ComboCount;

			if ( program.Output is not null )
			{
				foreach ( var line in program.Output )
				{
					Log.Warning( line );
				}
			}
		}

		if ( !t.Success )
		{
			Log.Warning( $"Shader compile failed after {sw.Elapsed.TotalMilliseconds:0.00}ms" );
		}
		else
		{
			Log.Info( $"Done {combos} combos in {sw.Elapsed.TotalMilliseconds:0.00}ms" );
		}
	}
}
