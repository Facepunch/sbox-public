namespace Editor;

/// <summary>
/// Information about project rows when opening a context menu in <see cref="Editor.ProjectRow"/>.
/// </summary>
public struct ProjectRowContextMenu
{
	/// <summary>
	/// The project in question.
	/// </summary>
	public Project Project;

	/// <summary>
	/// Position of the cursor on screen when the context menu was opened.
	/// </summary>
	public Vector2 ScreenPosition;

	/// <summary>
	/// The menu to add context menu options to.
	/// </summary>
	public Menu Menu;
}

public partial class ProjectRow
{
	protected ContextMenu menu;

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var menu = OpenContextMenu();

		menu.OpenAtCursor();
	}

	private ContextMenu OpenContextMenu()
	{
		menu = new ContextMenu( this );
		menu.AddSeparator();

		menu.AddOption( $"Open with Vulkan validation layers", "developer_mode", () => OpenProject( LaunchFlags.VulkanValidation ) );

		if ( Project.Config.Directory != null )
		{
			var o = menu.AddOption( $"Open {Project.Config.Directory.FullName}", "folder", () => EditorUtility.OpenFolder( Project.Config.Directory.FullName ) );
		}

		if ( Package?.IsRemote ?? false )
		{
			menu.AddSeparator();
			menu.AddOption( $"View on {Global.BackendTitle}..", "language", () => EditorUtility.OpenFolder( Project.ViewUrl ) );
			menu.AddOption( $"Copy Package Url", "content_paste", () => EditorUtility.Clipboard.Copy( Project.ViewUrl ) );
		}

		if ( !Project.IsBuiltIn )
		{
			menu.AddSeparator();

			menu.AddOption( "Remove Project", "delete", () =>
			{
				var message = $"Are you sure you wish to remove project \"{Project.Package.Title}\"?\n\nNo files will be deleted. Your project will remain at \"{Project.ConfigFilePath[..^6]}\".";

				EditorUtility.DisplayDialog( "Remove Project?", message, "Cancel", "Remove", () => { OnProjectRemove?.Invoke(); }, "🗑️" );

			} );
		}

		var cm = new ProjectRowContextMenu
		{
			Project = Project,
			ScreenPosition = Application.CursorPosition,
			Menu = menu
		};

		EditorEvent.Run( "projectrow.contextmenu", cm );

		return menu;
	}
}
