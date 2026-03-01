namespace Editor;

/// <summary>
/// A tree node for use with <see cref="AdvancedDropdownWidget"/>.
/// Items with children are categories; items without children are selectable leaves.
/// </summary>
public class AdvancedDropdownItem
{
	public string Title { get; set; }
	public string Icon { get; set; }
	public string Description { get; set; }
	public string Tooltip { get; set; }
	public object Value { get; set; }

	/// <summary>
	/// Optional custom icon painting. Receives the icon rect and current opacity.
	/// </summary>
	public Action<Rect, float> PaintIcon { get; set; }

	List<AdvancedDropdownItem> _children = new();

	public IReadOnlyList<AdvancedDropdownItem> Children => _children;
	public bool HasChildren => _children.Count > 0;

	public AdvancedDropdownItem() { }

	public AdvancedDropdownItem( string title, string icon = null, object value = null )
	{
		Title = title;
		Icon = icon;
		Value = value;
	}

	/// <summary>
	/// Add a child item and return it.
	/// </summary>
	public AdvancedDropdownItem Add( string title, string icon = null, object value = null )
	{
		var item = new AdvancedDropdownItem { Title = title, Icon = icon, Value = value };
		_children.Add( item );
		return item;
	}

	/// <summary>
	/// Add an existing item as a child.
	/// </summary>
	public void Add( AdvancedDropdownItem item )
	{
		_children.Add( item );
	}

	/// <summary>
	/// Remove all children.
	/// </summary>
	public void Clear()
	{
		_children.Clear();
	}

	/// <summary>
	/// Recursively collect all leaf items (items without children).
	/// </summary>
	internal IEnumerable<AdvancedDropdownItem> GetAllLeaves()
	{
		if ( !HasChildren )
		{
			yield return this;
			yield break;
		}

		foreach ( var child in _children )
		{
			foreach ( var leaf in child.GetAllLeaves() )
			{
				yield return leaf;
			}
		}
	}
}
