using Sandbox;

namespace Editor.AssetBrowsing.Nodes;

partial class CloudPackageNode : AssetFilterNode
{
	Package Package { get; set; }
	Package.FindResult LastResult;

	public CloudPackageNode( Package package ) : base( package.Thumb, package.Title, $"in:{package.FullIdent}" )
	{
		Package = package;
		_ = Update();
	}

	~CloudPackageNode()
	{
	}

	async Task Update()
	{
		LastResult = await Package.FindAsync( Filter );
		Count = LastResult.TotalCount;
		Dirty();
	}

	public override bool HasChildren => true;

	protected override void BuildChildren()
	{
		Clear();
		foreach ( var node in CloudTypeFilterNode.CreateForParent( Filter ) )
			AddItem( node );
	}

	protected override void BuildContextMenu( ContextMenu menu )
	{
		menu.AddOption( "Refresh", "refresh", () => { _ = Update(); } );
	}
}
