namespace Editor.AssetBrowsing.Nodes;

partial class CloudLocalNode : AssetFilterNode, ResourceLibrary.IEventListener
{
	public CloudLocalNode() : base( "attach_file", "Referenced", "@referenced" )
	{
		EditorEvent.Register( this );
	}

	~CloudLocalNode()
	{
		EditorEvent.Unregister( this );
	}

	void ResourceLibrary.IEventListener.OnSave( GameResource resource ) => InvalidateCount();
	void ResourceLibrary.IEventListener.OnExternalChanges( GameResource resource ) => InvalidateCount();

	/// <summary>
	/// Counting scans every project resource, so it only happens when the node is painted. Hidden or
	/// closed browsers never pay for it, and a burst of saves (scene + terrain + prefabs) costs one recount.
	/// </summary>
	bool _countStale = true;

	void InvalidateCount()
	{
		_countStale = true;
		TreeView?.Update();
	}

	public override void OnPaint( VirtualWidget item )
	{
		if ( _countStale )
		{
			_countStale = false;
			Count = CloudAsset.GetAssetReferences( true ).Count;
		}

		base.OnPaint( item );
	}
}
