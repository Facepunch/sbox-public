using Editor.AssetBrowsing.Nodes;
using Facepunch.ActionGraphs;
using System.IO;

namespace Editor;

public class CloudLocations : TreeView, IFilterHost
{
	/// <summary>
	/// Multi-select facet filter state. Nodes read and write this directly;
	/// the browser composes it into the query alongside BaseQuery.
	/// </summary>
	public ActiveFilterSet ActiveFilters { get; } = new();

	/// <summary>
	/// Called when a context navigation node (collection, type, browse) is selected.
	/// </summary>
	internal Action<AssetFilterNode> OnFilterSelected;

	/// <summary>
	/// Current name filter for collections/organisations, typed into the sidebar search.
	/// Read by <see cref="CloudCollectionsNode"/> and <see cref="CloudAccountNode"/> in BuildChildren.
	/// </summary>
	public string CollectionFilter { get; private set; } = "";

	/// <summary>
	/// Refilter the visible collections and organisations by display name.
	/// Pass empty/null to show all entries.
	/// </summary>
	public void FilterCollections( string text )
	{
		CollectionFilter = text ?? "";

		foreach ( var item in Items )
		{
			if ( item is CloudCollectionsNode ccn ) ccn.Dirty();
			else if ( item is CloudAccountNode can ) can.Dirty();
		}
	}

	public CloudLocations( CloudAssetBrowser parent ) : base( parent )
	{
		EditorEvent.Register( this );
		MinimumSize = 200;
		ItemSelected = OnItemClicked;

		if ( Global.IsApiConnected )
		{
			//
			// Cloud
			//
			{
				var browse = new AssetFilterNode( "search", "Browse", "type:model,material,map,sound,collection,prefab,sprite,decal" );
				AddItem( browse );

				if ( parent.FilterAssetTypes is null )
				{
					// Collections sit alongside Browse at the top
					AddItem( new CloudTypeFilterNode( "collection", "Collections", "grading", "" ) );

					AddItem( new TreeNode.Spacer( 6 ) );

					foreach ( var node in CloudTypeFilterNode.CreateForParent( "" ) )
						AddItem( node );
				}
			}

			AddItem( new TreeNode.Spacer( 10 ) );
		}

		{
			//
			// Project
			//
			AddItem( new CloudLocalNode() );

			AddItem( new TreeNode.Spacer( 10 ) );
		}

		if ( Global.IsApiConnected )
		{
			//
			// Collections
			//
			{
				var collections = new CloudCollectionsNode();

				AddItem( collections );
				Open( collections );

				AddItem( new TreeNode.Spacer( 10 ) );

				collections.OnLoaded = SetDefaultView;
			}

			//
			// Organisations
			//
			{
				var orgs = new CloudAccountNode();

				AddItem( orgs );
				Open( orgs );

				AddItem( new TreeNode.Spacer( 10 ) );
			}
		}
	}

	private void SetDefaultView()
	{
		bool foundDefaultView = false;

		void FindDefaultView( object item )
		{
			if ( item is AssetFilterNode { IsDefaultView: true } node )
			{
				SetSelected( node, true, false );
				foundDefaultView = true;
				return;
			}

			if ( item is TreeNode parent )
			{
				foreach ( var child in parent.Children )
				{
					if ( !foundDefaultView )
						FindDefaultView( child );
				}
			}
		}

		foreach ( var item in Items )
		{
			if ( !foundDefaultView )
				FindDefaultView( item );
		}

		if ( !foundDefaultView )
		{
			SetSelected( Items.First(), true, true );
		}
	}

	~CloudLocations()
	{
		EditorEvent.Unregister( this );
	}

	/// <summary>
	/// Called by the editor event system whenever a package's favourite state changes.
	/// Immediately applies the change to the in-memory collection list without a
	/// server round-trip — avoids search-index replication lag.
	/// </summary>
	[Event( "package.changed.favourite" )]
	void OnPackageFavouriteChanged( Package package )
	{
		if ( package.TypeName != "collection" )
			return;

		foreach ( var item in Items.OfType<CloudCollectionsNode>() )
			item.ApplyFavouriteChange( package );
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );

		base.OnPaint();
	}

	protected void OnItemClicked( object value )
	{
		// Facet value nodes toggle multi-select filters — they don't navigate
		if ( value is FacetValueNode facetNode )
		{
			facetNode.ToggleInclude();
			return;
		}

		if ( value is not AssetFilterNode filterNode )
			return;

		OnFilterSelected?.Invoke( filterNode );
	}
}
