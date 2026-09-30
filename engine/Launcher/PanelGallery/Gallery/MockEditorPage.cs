namespace Sandbox.PanelGallery;

/// <summary>
/// Introduces the panel-based editor prototype and opens it in its own window.
/// </summary>
public class MockEditorPage : GalleryPage
{
	public MockEditorPage() : base( "Mock Editor", "A prototype scene editor built entirely with our panel UI system." )
	{
		AddClass( "mock-editor-demo" );
		Add.Label( "Our goal is to prove that this UI system is suitable to one day replace the whole editor. This prototype brings menus, a scene hierarchy, an inspector, asset browsing and a console together so we can try out real editor workflows and test how the system feels and performs.", "page-blurb" );

		var actions = Add.Panel( "row" );
		actions.AddChild( new Sandbox.UI.Button( "Open Mock Editor", "open_in_new", "primarybutton", () => EditorWindow.Open() ) );
	}
}
