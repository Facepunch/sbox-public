namespace Editor.Inspectors;

[EditorForAssetType( "__fallback" )]
public partial class GameResourceEditor : BaseWindow, IAssetEditor
{
	public bool CanOpenMultipleAssets => false;

	public AssetInspector Inspector { get; private set; }
	public Asset Asset => Inspector?.Asset;

	public GameResourceEditor()
	{
		Size = new Vector2( 650, 920 );
		MaximumWidth = 800;
		MinimumWidth = 300;
		Layout = Layout.Column();

		// Makes the window always on top of the editor only, not other applications
		Parent = EditorWindow;
		WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint | WindowFlags.WindowTitle;
	}

	public void AssetOpen( Asset asset )
	{
		Show();
		WindowTitle = asset.Path;

		Inspector = new AssetInspector( asset.GetSerialized() );
		Layout.Add( Inspector );

		SetWindowIcon( asset.AssetType.Icon128 );
	}

	public void SelectMember( string memberName )
	{
		Inspector?.ResourceEditor?.SelectMember( memberName );
	}
}
