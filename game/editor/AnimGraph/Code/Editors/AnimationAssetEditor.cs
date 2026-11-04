using Editor.Inspectors;

namespace Editor.AnimGraph;

[EditorForAssetType( "skel" ), EditorForAssetType( "anim" )]
public sealed class AnimationAssetEditor : AssetEditorWindow
{
	public AnimationAssetEditor()
	{
		MaximumWidth = 4096;
		Size = new Vector2( 900, 920 );
	}

	protected override bool OnClose() => Inspector?.ConfirmClose( Close ) ?? true;
}
