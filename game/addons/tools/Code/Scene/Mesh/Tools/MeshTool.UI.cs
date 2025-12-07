

namespace Editor.MeshEditor;

partial class MeshTool
{
	public override Widget CreateToolFooter()
	{
		var materialProperty = this.GetSerialized().GetProperty( nameof( ActiveMaterial ) );
		return new ActiveMaterialWidget( materialProperty );
	}

	public override Widget CreateShortcutsWidget() => new MeshToolShortcutsWidget();

	public void CreateMoveModeButtons( Layout row )
	{
		var toolbar = new MoveModeToolBar( null, this );
		row.Add( toolbar );
	}
}

file class MeshToolShortcutsWidget : Widget
{
	[Shortcut( "tools.block-tool", "Shift+B", typeof( SceneDock ) )]
	public void ActivateBlockTool() => EditorToolManager.SetSubTool( nameof( BlockTool ) );

	[Shortcut( "tools.vertex-tool", "1", typeof( SceneDock ) )]
	public void ActivateVertexTool() => EditorToolManager.SetSubTool( nameof( VertexTool ) );

	[Shortcut( "tools.edge-tool", "2", typeof( SceneDock ) )]
	public void ActivateEdgeTool() => EditorToolManager.SetSubTool( nameof( EdgeTool ) );

	[Shortcut( "tools.face-tool", "3", typeof( SceneDock ) )]
	public void ActivateFaceTool() => EditorToolManager.SetSubTool( nameof( FaceTool ) );

	[Shortcut( "tools.texture-tool", "4", typeof( SceneDock ) )]
	public void ActivateTextureTool() => EditorToolManager.SetSubTool( nameof( TextureTool ) );
}
