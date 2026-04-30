namespace Editor.CodeEditors;

/// <summary>
/// Opens source files with a user-picked executable. The path is persisted in
/// an editor cookie and is configured via the browse button on the code editor
/// control. Useful as a fallback when Visual Studio / Rider / VS Code aren't
/// installed, or for any editor not covered by a dedicated <see cref="ICodeEditor"/>.
/// </summary>
[Title( "Custom" )]
[Description( "Manually selected code editor" )]
public class CustomCodeEditor : ICodeEditor
{
	public static string CustomPath
	{
		get => EditorCookie.GetString( "CodeEditor.CustomPath", "" );
		set => EditorCookie.SetString( "CodeEditor.CustomPath", value );
	}

	/// <summary>
	/// Opens the file in the custom editor. <paramref name="line"/> and
	/// <paramref name="column"/> are ignored — the argument syntax to jump to a
	/// line varies per editor (<c>path:line:col</c>, <c>-n{line}</c>, <c>+line</c>)
	/// and can't be predicted for an arbitrary executable.
	/// </summary>
	public void OpenFile( string path, int? line = null, int? column = null )
		=> Launch( $"\"{path}\"" );

	public void OpenSolution()
		=> Launch( $"\"{CodeEditor.AddonSolutionPath()}\"" );

	public void OpenAddon( Project addon )
		=> OpenSolution();

	public bool IsInstalled()
	{
		var path = CustomPath;
		return !string.IsNullOrEmpty( path ) && System.IO.File.Exists( path );
	}

	private void Launch( string arguments )
	{
		var exe = CustomPath;
		if ( string.IsNullOrEmpty( exe ) ) return;

		System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo
		{
			FileName = exe,
			Arguments = arguments,
			CreateNoWindow = true,
		} );
	}
}
