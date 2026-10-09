using System.IO;

namespace Editor.CodeEditors;

[Title( "Visual Studio Code" )]
public class VisualStudioCode : ICodeEditor
{
	public void OpenFile( string path, int? line, int? column )
	{
		var sln = CodeEditor.FindSolutionFromPath( Path.GetDirectoryName( path ) );
		var rootPath = Path.GetDirectoryName( sln );

		Launch( $"-g \"{path}:{line}:{column}\" \"{rootPath}\"" );
	}

	public void OpenSolution()
	{
		Launch( $"\"{Project.Current.GetRootPath()}\"" );
	}

	public void OpenAddon( Project addon )
	{
		var projectPath = (addon != null) ? addon.GetRootPath() : "";

		Launch( $"\"{projectPath}\"" );
	}

	public bool IsInstalled() => GetLocation() is not null;

	private static void Launch( string arguments ) => CodeEditorLocator.Launch( GetLocation(), arguments );

	static string Location;

	private static string GetLocation() => Location ??= CodeEditorLocator.Find(
		"code",
		// Windows
		"%LOCALAPPDATA%/Programs/Microsoft VS Code/Code.exe",
		"%ProgramFiles%/Microsoft VS Code/Code.exe",
		// macOS
		"/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code",
		"~/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code",
		// Linux
		"/usr/share/code/bin/code",
		"/snap/bin/code" );
}
