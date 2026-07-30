namespace Editor.CodeEditors;

/// <summary>
/// Cookie-backed settings for <see cref="CustomCodeEditor"/>, exposed as properties so they can be
/// added to a <see cref="ControlSheet"/>.
/// </summary>
internal static class CustomCodeEditorSettings
{
	/// <summary>
	/// Path to the executable to launch.
	/// </summary>
	[File( Extension = "exe" )]
	public static string ExecutablePath
	{
		get => EditorCookie.GetString( CustomCodeEditor.ExecutablePathKey, "" );
		set => EditorCookie.SetString( CustomCodeEditor.ExecutablePathKey, value );
	}

	/// <summary>
	/// Arguments used when opening a file. Placeholders: {file}, {line}, {column}, {solution}, {project_root}
	/// </summary>
	public static string OpenFileArgs
	{
		get => EditorCookie.GetString( CustomCodeEditor.OpenFileArgsKey, CustomCodeEditor.DefaultOpenFileArgs );
		set => EditorCookie.SetString( CustomCodeEditor.OpenFileArgsKey, value );
	}

	/// <summary>
	/// Arguments used when opening a solution. Placeholders: {solution}, {project_root}
	/// </summary>
	public static string OpenSolutionArgs
	{
		get => EditorCookie.GetString( CustomCodeEditor.OpenSolutionArgsKey, CustomCodeEditor.DefaultOpenSolutionArgs );
		set => EditorCookie.SetString( CustomCodeEditor.OpenSolutionArgsKey, value );
	}
}
