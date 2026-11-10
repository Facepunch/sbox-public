namespace Editor.AssetBrowsing.Nodes;

partial class CloudAssetNode : AssetFilterNode
{
	public CloudAssetNode( string icon, string title, string filter ) : base( icon, title, filter )
	{
	}

	public override bool HasChildren => true;

	protected override void BuildChildren()
	{
		Clear();
		foreach ( var node in CloudTypeFilterNode.CreateForParent( Filter ) )
			AddItem( node );
	}
}
