using System.Threading.Tasks;

namespace Sandbox.UI;

/// <summary>
/// The OS file dialogs a panel window can open - pick a folder, open a file, save a file.
/// One dialog at a time; asking again while one is up joins it. The user's pick comes back
/// through <see cref="PanelWindowInput.OnFileDialogResult"/> from the native event pump.
/// </summary>
internal static class PanelWindowDialogs
{
	static TaskCompletionSource<string> _pending;

	static Task<string> Show( System.Action open )
	{
		if ( _pending is not null ) return _pending.Task;

		_pending = new();
		open();
		return _pending.Task;
	}

	/// <summary>
	/// Ask the OS to pick a folder, starting at <paramref name="defaultPath"/>. Null when the
	/// user cancels.
	/// </summary>
	internal static Task<string> PickFolder( IntPtr window, string defaultPath )
		=> Show( () => NativeEngine.PanelWindowNative.OpenFolderDialog( window, defaultPath ?? "" ) );

	/// <summary>
	/// Ask the OS for a file to open. The filter is name and extension list pairs, like
	/// "Scene files|scene;prefab|All files|*". Null when the user cancels.
	/// </summary>
	internal static Task<string> PickOpenFile( IntPtr window, string defaultPath, string filters )
		=> Show( () => NativeEngine.PanelWindowNative.OpenFileDialog( window, defaultPath ?? "", filters ?? "" ) );

	/// <summary>
	/// Ask the OS where to save a file. <paramref name="defaultPath"/> can end in a suggested
	/// file name. Null when the user cancels.
	/// </summary>
	internal static Task<string> PickSaveFile( IntPtr window, string defaultPath, string filters )
		=> Show( () => NativeEngine.PanelWindowNative.SaveFileDialog( window, defaultPath ?? "", filters ?? "" ) );

	/// <summary>
	/// What the user picked, from the native event pump. Null means cancelled.
	/// </summary>
	internal static void OnResult( string path )
	{
		var pending = _pending;
		_pending = null;
		pending?.TrySetResult( path );
	}
}
