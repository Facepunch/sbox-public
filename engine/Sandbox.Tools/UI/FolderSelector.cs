using Sandbox.UI;
using System;

namespace Editor;

/// <summary>
/// A text entry with a browse button that opens the OS folder picker. Editor only - games
/// don't get to poke around the filesystem.
/// </summary>
public class FolderSelector : Panel
{
	/// <summary>
	/// The folder path has changed - typed or picked.
	/// </summary>
	public Action<string> ValueChanged { get; set; }

	readonly TextEntry _entry;

	public FolderSelector()
	{
		AddClass( "folderselector" );

		_entry = AddChild<TextEntry>();
		_entry.OnTextEdited = x => ValueChanged?.Invoke( x );

		AddChild( new Sandbox.UI.Button( null, "folder_open", "iconbutton", Browse ) );
	}

	/// <summary>
	/// The folder path in the box.
	/// </summary>
	public string Text
	{
		get => _entry.Text;
		set => _entry.Text = value;
	}

	async void Browse()
	{
		var window = PanelWindow.FromPanel( this );
		if ( window is null ) return;

		var picked = await window.PickFolder( Text );
		if ( picked is null ) return;

		Text = picked;
		ValueChanged?.Invoke( picked );
	}
}
