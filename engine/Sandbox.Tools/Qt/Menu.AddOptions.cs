#nullable enable

using System;
using System.Text.RegularExpressions;
using Sandbox.Internal;

namespace Editor;

partial class Menu
{
	public record struct PathElement( string Name, string? Icon = null, string? Description = null, int? Order = null, bool IsHeading = false ) : IComparable<PathElement>
	{
		public int CompareTo( PathElement other )
		{
			var orderComparison = Nullable.Compare( Order, other.Order );
			if ( orderComparison != 0 )
			{
				return orderComparison;
			}

			return string.Compare( Name, other.Name, StringComparison.Ordinal );
		}

		public static int Compare( IReadOnlyList<PathElement> aPath, IReadOnlyList<PathElement> bPath )
		{
			for ( var i = 0; ; ++i )
			{
				if ( aPath.Count == i && bPath.Count == i )
				{
					return 0;
				}

				if ( aPath.Count == i )
				{
					return 1;
				}

				if ( bPath.Count == i )
				{
					return -1;
				}

				var aHead = aPath[i];
				var bHead = bPath[i];

				var isHeadingCompare = aHead.IsHeading.CompareTo( bHead.IsHeading );
				if ( isHeadingCompare != 0 ) return isHeadingCompare;

				var orderCompare = (aHead.Order ?? 0).CompareTo( bHead.Order ?? 0 );
				if ( orderCompare != 0 ) return orderCompare;

				var nameCompare = string.Compare( aHead.Name, bHead.Name, StringComparison.Ordinal );
				if ( nameCompare != 0 ) return nameCompare;
			}
		}

		public bool Matches( PathElement other )
		{
			return Name == other.Name && IsHeading == other.IsHeading;
		}

		public PathElement Merge( PathElement other )
		{
			return this with { Icon = Icon ?? other.Icon, Description = Description ?? other.Description, Order = Order ?? other.Order };
		}

		internal static void CleanUp( PathElement[] path )
		{
			if ( path[^1].IsHeading )
			{
				path[^1] = path[^1] with { IsHeading = false };
			}
		}

		internal static PathElement[] Flatten( PathElement[] path )
		{
			if ( path[0].IsHeading )
			{
				return new[]
				{
					path[0],
					path[^1] with { Name = string.Join( " → ", path.Skip( 1 ).Where( x => !x.IsHeading ).Select( x => x.Name ) ) }
				};
			}

			return new[] { path[^1] with { Name = string.Join( " → ", path.Where( x => !x.IsHeading ).Select( x => x.Name ) ) } };
		}
	}

	private static Regex PathPartRegex { get; } = new( @"(?<heading>#)?(?<part>[^:/@]+)(?::(?<icon>[^/@]+))?(?:@(?<order>-?[0-9]+))?(?:/|$)" );

	/// <summary>
	/// Splits a path as a list of <c>/</c>-delimited elements, each with the form <c>"[#]name[:icon][@order]"</c>.
	/// </summary>
	/// <param name="path">Path to split.</param>
	public static PathElement[] GetSplitPath( string path )
	{
		var pathPartMatches = PathPartRegex.Matches( path );

		var splitPath = pathPartMatches.Count == 0
			? new[] { new PathElement( path ) }
			: pathPartMatches.Select( x => new PathElement( x.Groups["part"].Value,
					Icon: x.Groups["icon"] is { Success: true, Value: { } icon } ? icon : null,
					Order: x.Groups["order"] is { Success: true, Value: { } orderStr } && int.TryParse( orderStr, out var order ) ? order : null,
					IsHeading: x.Groups["heading"] is { Success: true } ) )
				.ToArray();

		return splitPath;
	}

	/// <summary>
	/// Combines the <see cref="ICategoryProvider.Value"/> (if exists) and <see cref="ITitleProvider.Value"/>, then splits it with <see cref="GetSplitPath(string)"/>.
	/// </summary>
	public static PathElement[] GetSplitPath( ITitleProvider item )
	{
		var title = item.Value;
		var group = (item as ICategoryProvider)?.Value;
		var icon = (item as IIconProvider)?.Value;
		var desc = (item as IDescriptionProvider)?.Value;
		var order = (item as IOrderProvider)?.Value;

		var path = string.IsNullOrEmpty( group ) ? title : $"{group}/{title}";
		var split = GetSplitPath( path );

		split[^1] = split[^1] with { Icon = icon, Description = desc, Order = order };

		return split;
	}

	private class MenuItem<T>
	{
		public PathElement PathElement;
		public List<MenuItem<T>>? SubItems;
		public T? Value;
		public bool IsReduced;

		public string Name => PathElement.Name;
		public string? Icon => PathElement.Icon;
		public string? Description => PathElement.Description;
		public bool IsHeading => PathElement.IsHeading;
	}

	private static void Add<T>( List<MenuItem<T>> list, ReadOnlySpan<PathElement> path, T value )
	{
		while ( true )
		{
			var head = path[0];

			if ( path.Length == 1 )
			{
				list.Add( new MenuItem<T> { PathElement = head, Value = value } );
				return;
			}

			if ( list.FirstOrDefault( x => x.SubItems != null && x.PathElement.Matches( head ) ) is { } match )
			{
				match.PathElement = match.PathElement.Merge( head );
			}
			else
			{
				match = new MenuItem<T> { PathElement = head, SubItems = new List<MenuItem<T>>() };
				list.Add( match );
			}

			list = match.SubItems!;
			path = path[1..];
		}
	}

	private static void Reduce<T>( List<MenuItem<T>> list )
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

	public delegate void CreateOptionDelegate<T>( Menu parent, PathElement display, T value );

	private static void BuildMenu<T>( Menu menu, List<MenuItem<T>> list, CreateOptionDelegate<T> createOption, string defaultMenuIcon )
	{
		foreach ( var item in list )
		{
			if ( item.SubItems is null )
			{
				createOption( menu, item.PathElement, item.Value! );
				continue;
			}

			var subMenu = menu;

			if ( !string.IsNullOrEmpty( item.Name ) )
			{
				if ( item.IsHeading )
				{
					menu.AddHeading( item.Name.ToTitleCase() );
				}
				else
				{
					subMenu = menu.AddMenu( item.Name, item.Icon ?? defaultMenuIcon );

					if ( !string.IsNullOrEmpty( item.Description ) )
					{
						menu.ToolTipsVisible = true;
						subMenu.ToolTip = item.Description;
					}
				}
			}

			BuildMenu( subMenu, item.SubItems!, createOption, defaultMenuIcon );
		}
	}

	/// <summary>
	/// Adds a bunch of options, creating sub-menus based on their paths.
	/// </summary>
	/// <param name="items">Items to create options for.</param>
	/// <param name="getPath">Gets the path of an item as a list of <c>/</c>-delimited elements, each with the form <c>"[#]name[:icon][@order]"</c>.</param>
	/// <param name="action">Action to call on a clicked element.</param>
	/// <param name="flat">If true, flatten each path after sorting.</param>
	/// <param name="reduce">If true, collapse sub-menus with single items.</param>
	/// <param name="defaultSubMenuIcon">Use this icon for any sub-menus without an icon specified.</param>
	public void AddOptions<T>( IEnumerable<T> items, Func<T, string> getPath,
		Action<T>? action = null,
		bool flat = false,
		bool reduce = true,
		string defaultSubMenuIcon = "folder" )
	{
		AddOptions( items, x => GetSplitPath( getPath( x ) ),
			action: action,
			flat: flat,
			reduce: reduce,
			defaultSubMenuIcon: defaultSubMenuIcon );
	}

	/// <summary>
	/// Adds a bunch of options, creating sub-menus based on their paths.
	/// </summary>
	/// <param name="items">Items to create options for.</param>
	/// <param name="action">Action to call on a clicked element.</param>
	/// <param name="flat">If true, flatten each path after sorting.</param>
	/// <param name="reduce">If true, collapse sub-menus with single items.</param>
	/// <param name="defaultSubMenuIcon">Use this icon for any sub-menus without an icon specified.</param>
	public void AddOptions<T>( IEnumerable<T> items,
		Action<T>? action = null,
		bool flat = false,
		bool reduce = true,
		string defaultSubMenuIcon = "folder" )
		where T : ITitleProvider
	{
		AddOptions( items, x => GetSplitPath( x ),
			action: action,
			flat: flat,
			reduce: reduce,
			defaultSubMenuIcon: defaultSubMenuIcon );
	}

	/// <summary>
	/// Adds a bunch of options, creating sub-menus based on their paths.
	/// </summary>
	/// <param name="items">Items to create options for.</param>
	/// <param name="getPath">Gets the path of an item.</param>
	/// <param name="action">Action to call on a clicked element.</param>
	/// <param name="flat">If true, flatten each path after sorting.</param>
	/// <param name="reduce">If true, collapse sub-menus with single items.</param>
	/// <param name="defaultSubMenuIcon">Use this icon for any sub-menus without an icon specified.</param>
	public void AddOptions<T>( IEnumerable<T> items, Func<T, PathElement[]> getPath,
		Action<T>? action = null,
		bool flat = false,
		bool reduce = true,
		string defaultSubMenuIcon = "folder" )
	{
		AddOptions( items, getPath,
			createOption: ( menu, display, value ) => DefaultCreateOption( menu, display, value, action ),
			flat: flat,
			reduce: reduce,
			defaultSubMenuIcon: defaultSubMenuIcon );
	}

	/// <summary>
	/// Adds a bunch of options, creating sub-menus based on their paths.
	/// </summary>
	/// <param name="items">Items to create options for.</param>
	/// <param name="getPath">Gets the path of an item.</param>
	/// <param name="createOption">Called to create an option for each item in <paramref name="items"/>.</param>
	/// <param name="flat">If true, flatten each path after sorting.</param>
	/// <param name="reduce">If true, collapse sub-menus with single items.</param>
	/// <param name="defaultSubMenuIcon">Use this icon for any sub-menus without an icon specified.</param>
	public void AddOptions<T>( IEnumerable<T> items, Func<T, PathElement[]> getPath,
		CreateOptionDelegate<T> createOption,
		bool flat = false,
		bool reduce = true,
		string defaultSubMenuIcon = "folder" )
	{
		var itemPaths = items.Select( x =>
		{
			var path = getPath( x );
			PathElement.CleanUp( path );
			return (Item: x, Path: path);
		} );

		if ( flat )
		{
			itemPaths = itemPaths.Select( x => (x.Item, PathElement.Flatten( x.Path )) );
		}

		itemPaths = itemPaths.OrderBy( x => x.Path,
			Comparer<PathElement[]>.Create( PathElement.Compare ) );

		var list = new List<MenuItem<T>>();

		foreach ( var (item, path) in itemPaths )
		{
			Add( list, path, item );
		}

		if ( reduce )
		{
			Reduce( list );
		}

		BuildMenu( this, list, createOption, defaultSubMenuIcon );
	}

	private static void DefaultCreateOption<T>( Menu menu, PathElement display, T value, Action<T>? action )
	{
		var option = menu.AddOption( display.Name, display.Icon );

		if ( !string.IsNullOrEmpty( display.Description ) )
		{
			menu.ToolTipsVisible = true;
			option.ToolTip = display.Description;
		}

		if ( action is not null )
		{
			option.Triggered += () => action( value );
		}
		else
		{
			option.Enabled = false;
		}
	}
}
