

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
	[Shortcut( "tools.primitive-tool", "Shift+B", typeof( SceneViewWidget ) )]
	public void ActivatePrimitiveTool() => EditorToolManager.SetSubTool( nameof( PrimitiveTool ) );

	[Shortcut( "tools.vertex-tool", "1", typeof( SceneViewWidget ) )]
	public void ActivateVertexTool() => EditorToolManager.SetSubTool( nameof( VertexTool ) );

	[Shortcut( "tools.edge-tool", "2", typeof( SceneViewWidget ) )]
	public void ActivateEdgeTool() => EditorToolManager.SetSubTool( nameof( EdgeTool ) );

	[Shortcut( "tools.face-tool", "3", typeof( SceneViewWidget ) )]
	public void ActivateFaceTool() => EditorToolManager.SetSubTool( nameof( FaceTool ) );

	[Shortcut( "tools.texture-tool", "4", typeof( SceneViewWidget ) )]
	public void ActivateTextureTool() => EditorToolManager.SetSubTool( nameof( TextureTool ) );

	[Shortcut( "tools.mesh-selection", "5", typeof( SceneViewWidget ) )]
	public void ActivateMeshSelection() => EditorToolManager.SetSubTool( nameof( MeshSelection ) );

	[Shortcut( "tools.vertex-paint-tool", "6", typeof( SceneViewWidget ) )]
	public void ActivateVertexPaintTool() => EditorToolManager.SetSubTool( nameof( VertexPaintTool ) );
}
