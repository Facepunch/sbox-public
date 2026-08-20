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

	static readonly List<string> queue = new();
	static string compiling;
	static CancellationTokenSource cts;
	static bool draining;

	[Event( "compile.shader" )]
	public static void CompileShader( string shader )
	{
		if ( !FileSystem.Mounted.FileExists( shader ) ) return;
		if ( !shader.EndsWith( ".shader" ) ) return;

		if ( queue.Contains( shader, StringComparer.OrdinalIgnoreCase ) )
			return;

		queue.Add( shader );

		if ( string.Equals( compiling, shader, StringComparison.OrdinalIgnoreCase ) )
			cts?.Cancel();

		if ( !draining )
			_ = Drain();
	}

	/// <summary>
	/// Compile queued shaders one by one
	/// </summary>
	static async Task Drain()
	{
		draining = true;

		try
		{
			await Task.Yield();

			var completed = 0;

			if ( queue.Count > 1 )
				Log.Info( $"Compiling {queue.Count} shaders.." );

			while ( queue.Count > 0 )
			{
				var shader = queue[0];
				queue.RemoveAt( 0 );

				var total = completed + 1 + queue.Count;
				completed++;

				compiling = shader;
				cts = new CancellationTokenSource();

				try
				{
					await CompileShader( shader, completed, total, cts.Token );
				}
				catch ( System.OperationCanceledException )
				{
					// catch cases when shader file was updated while being in queue, 
					// otherwise it is considered as a failed batch..
					Log.Info( $"Shader file was updated while in queue: {shader}" );
				}
				catch ( System.Exception e )
				{
					Log.Error( e, $"Failed to compile {shader}" );
				}
				finally
				{
					compiling = null;
					cts.Dispose();
					cts = null;
				}
			}
		}
		catch ( System.Exception e )
		{
			Log.Error( e, "Shader compile queue failed" );
		}
		finally
		{
			compiling = null;
			cts = null;
			draining = false;
		}
	}

	static async Task CompileShader( string file, int index, int total, CancellationToken token )
	{
		Log.Info( total > 1 ? $"Compiling: {file} ({index}/{total})" : $"Compiling: {file}" );
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
