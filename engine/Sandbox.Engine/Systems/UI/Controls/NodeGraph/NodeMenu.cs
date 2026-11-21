namespace Sandbox.UI.NodeGraph;

/// <summary>
/// Fills a <see cref="Menu"/> with node types, a submenu per path level, like the editor's
/// <c>Menu.AddOptions</c>.
/// </summary>
internal static class NodeMenu
{
	/// <summary>
	/// Add an option per type in <paramref name="types"/>. With a <paramref name="query"/> the list
	/// is flat and the best matches come first; without one, paths become submenus, and a submenu
	/// holding one entry collapses into it.
	/// </summary>
	public static void AddOptions( Menu menu, IEnumerable<INodeType> types, NodeQuery? query, Action<INodeType> selected )
	{
		var items = new List<(INodeType Type, NodePathElement[] Path)>();

		foreach ( var type in types )
		{
			if ( type?.Path is not { Length: > 0 } path ) continue;

			// A copy: cleaning up mustn't change the type's own path
			path = query is { } q ? WithScore( path, q ) : (NodePathElement[])path.Clone();
			CleanUp( path );

			items.Add( (type, query is null ? path : Flatten( path )) );
		}

		var list = new List<MenuItem>();

		foreach ( var (type, path) in items.OrderBy( x => x.Path, Comparer<NodePathElement[]>.Create( NodePathElement.Compare ) ) )
		{
			Add( list, path, type );
		}

		Reduce( list );
		BuildMenu( menu, list, selected );
	}

	/// <summary>
	/// The last level orders by score, best first.
	/// </summary>
	static NodePathElement[] WithScore( NodePathElement[] path, NodeQuery query )
	{
		var copy = (NodePathElement[])path.Clone();
		copy[^1] = copy[^1] with { Order = -query.GetScore( path ) };
		return copy;
	}

	/// <summary>
	/// The entry itself is never a heading.
	/// </summary>
	static void CleanUp( NodePathElement[] path )
	{
		if ( path[^1].IsHeading )
		{
			path[^1] = path[^1] with { IsHeading = false };
		}
	}

	/// <summary>
	/// One level: the entry named by its whole path, under its heading if it has one.
	/// </summary>
	static NodePathElement[] Flatten( NodePathElement[] path )
	{
		if ( path[0].IsHeading )
		{
			return
			[
				path[0],
				path[^1] with { Name = string.Join( " → ", path.Skip( 1 ).Where( x => !x.IsHeading ).Select( x => x.Name ) ) }
			];
		}

		return [path[^1] with { Name = string.Join( " → ", path.Where( x => !x.IsHeading ).Select( x => x.Name ) ) }];
	}

	sealed class MenuItem
	{
		public NodePathElement PathElement;
		public List<MenuItem> SubItems;
		public INodeType Value;
		public bool IsReduced;

		public string Name => PathElement.Name;
		public string Icon => PathElement.Icon;
		public string Description => PathElement.Description;
		public bool IsHeading => PathElement.IsHeading;
	}

	static void Add( List<MenuItem> list, ReadOnlySpan<NodePathElement> path, INodeType value )
	{
		while ( true )
		{
			var head = path[0];

			if ( path.Length == 1 )
			{
				list.Add( new MenuItem { PathElement = head, Value = value } );
				return;
			}

			if ( list.FirstOrDefault( x => x.SubItems != null && x.PathElement.Matches( head ) ) is { } match )
			{
				match.PathElement = match.PathElement.Merge( head );
			}
			else
			{
				match = new MenuItem { PathElement = head, SubItems = new List<MenuItem>() };
				list.Add( match );
			}

			list = match.SubItems;
			path = path[1..];
		}
	}

	/// <summary>
	/// Fold submenus holding a single entry into it, as "Submenu → Entry".
	/// </summary>
	static void Reduce( List<MenuItem> list )
	{
		foreach ( var item in list )
		{
			while ( item.SubItems?.Count == 1 && (!item.IsHeading || item.IsReduced) )
			{
				var head = item.SubItems[0];

				if ( !head.IsHeading )
				{
					item.PathElement = head.PathElement with
					{
						IsHeading = false,
						Name = $"{item.Name} → {head.Name}",
						Icon = head.Icon,
						Description = head.Description
					};
				}

				item.Value = head.Value;
				item.SubItems = head.SubItems;
				item.IsReduced = true;
			}

			if ( item.SubItems != null )
			{
				Reduce( item.SubItems );
			}
		}
	}

	static void BuildMenu( Menu menu, List<MenuItem> list, Action<INodeType> selected )
	{
		foreach ( var item in list )
		{
			if ( item.SubItems is null )
			{
				var type = item.Value;
				var option = menu.AddOption( item.Name, item.Icon, () => selected( type ) );

				if ( !string.IsNullOrEmpty( item.Description ) ) option.Tooltip = item.Description;
				continue;
			}

			var subMenu = menu;

			if ( !string.IsNullOrEmpty( item.Name ) )
			{
				if ( item.IsHeading )
				{
					// No headings in a panel menu: a row that can't be picked stands in
					menu.AddOption( item.Name.ToTitleCase() ).Enabled = false;
				}
				else
				{
					subMenu = menu.AddMenu( item.Name, item.Icon ?? "folder" );

					if ( !string.IsNullOrEmpty( item.Description ) ) subMenu.Tooltip = item.Description;
				}
			}

			BuildMenu( subMenu, item.SubItems, selected );
		}
	}
}
