namespace Editor.AssetBrowsing.Nodes;

partial class CloudCollectionsNode : TreeNode.Header
{
	Package.FindResult LastResult;

	public Action OnLoaded { get; set; }

	public CloudCollectionsNode() : base( "grading", "My Collections" )
	{
		_ = Update();
	}

	public override string GetTooltip() => "The collections you've favourited";

	async Task Update()
	{
		LastResult = await Package.FindAsync( "sort:favourite type:collection" );
		// Reset the dirty flag before calling Dirty() so the guard in TreeNode.Dirty()
		// never swallows the notification — this matters when Refresh() is called while
		// the node is already dirty from a previous (unconsumed) rebuild request.
		dirty = false;
		Dirty();
	}

	/// <summary>
	/// Optimistically add or remove <paramref name="package"/> from the in-memory result
	/// based on its current <see cref="Package.PackageInteraction.Favourite"/> state, then
	/// trigger a rebuild — no network round-trip required.
	/// </summary>
	public void ApplyFavouriteChange( Package package )
	{
		var existing = LastResult?.Packages ?? Array.Empty<Package>();
		bool inList = existing.Any( x => x.FullIdent == package.FullIdent );
		bool shouldBe = package.Interaction.Favourite;

		if ( inList == shouldBe ) return; // already in the right state

		LastResult ??= new Package.FindResult();

		LastResult.Packages = shouldBe
			? existing.Append( package ).ToArray()
			: existing.Where( x => x.FullIdent != package.FullIdent ).ToArray();

		dirty = false;
		Dirty();
	}

	/// <summary>
	/// Re-fetch favourited collections from the server and rebuild the tree.
	/// Used by the right-click context menu; event-driven updates use
	/// <see cref="ApplyFavouriteChange"/> instead to avoid search-index lag.
	/// </summary>
	public void Refresh() => _ = Update();

	protected override void BuildChildren()
	{
		Clear();

		if ( LastResult?.Packages == null )
			return;

		var filter = (TreeView as CloudLocations)?.CollectionFilter ?? "";

		foreach ( var x in LastResult.Packages )
		{
			if ( !string.IsNullOrEmpty( filter ) &&
				 !x.Title.Contains( filter, StringComparison.OrdinalIgnoreCase ) )
				continue;

			AddItem( new CloudPackageNode( x ) );
		}

		// Fire OnLoaded exactly once (the first unfiltered build) so SetDefaultView
		// runs at startup but not again on every Refresh() call.
		if ( string.IsNullOrEmpty( filter ) && OnLoaded != null )
		{
			var cb = OnLoaded;
			OnLoaded = null;
			cb();
		}
	}

	public override bool OnContextMenu()
	{
		var menu = new ContextMenu( null );
		menu.AddOption( "Refresh", "refresh", () => { _ = Update(); } );
		menu.OpenAtCursor();

		return true;
	}


}
