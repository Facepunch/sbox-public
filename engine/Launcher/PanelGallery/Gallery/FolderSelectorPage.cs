using System.Threading.Tasks;

namespace Sandbox.PanelGallery;

/// <summary>
/// The OS file dialogs - the folder picker should be the modern dialog and open in the
/// folder the box shows.
/// </summary>
public class FolderSelectorPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;

	public FolderSelectorPage() : base( "Folder Select", "Editor.FolderSelector and the window's OS file dialogs. Everything async, null on cancel." )
	{
		var row = Case( "Folder selector" );
		var picker = new FolderSelector { Text = Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ) };
		picker.ValueChanged = x => _output.Text = x;
		row.AddChild( picker );

		row = Case( "File dialogs" );
		row.AddChild( new Sandbox.UI.Button( "Open File", "file_open", "flatbutton", () => Pick( w => w.PickOpenFile( null, "Images|png;jpg|All files|*" ) ) ) );
		row.AddChild( new Sandbox.UI.Button( "Save File", "save", "flatbutton", () => Pick( w => w.PickSaveFile( null, "Text files|txt|All files|*" ) ) ) );

		_output = Output();
	}

	async void Pick( Func<PanelWindow, Task<string>> open )
	{
		var window = PanelWindow.FromPanel( this );
		if ( window is null ) return;

		var picked = await open( window );
		_output.Text = picked ?? "cancelled";
	}
}
