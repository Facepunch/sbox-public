namespace Editor.AnimGraph;

/// <summary>
/// Popup for picking a bone - the skeleton as a tree, with a search that flattens it to matches.
/// </summary>
internal sealed class BonePicker : PopupWidget
{
	private readonly IReadOnlyList<(string Name, int ParentIndex)> _bones;
	private readonly Action<string> _picked;
	private readonly TreeView _tree;
	private readonly LineEdit _search;
	private string _reveal;

	public BonePicker( Model model, string current, Action<string> picked ) : this( BoneMaskWeights.GetBones( model ), current, picked )
	{
	}

	public BonePicker( IReadOnlyList<(string Name, int ParentIndex)> bones, string current, Action<string> picked ) : base( null )
	{
		_bones = bones;
		_picked = picked;

		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		_search = new LineEdit( this ) { PlaceholderText = "⌕  Search bones..", ClearButtonEnabled = true, FixedHeight = Theme.RowHeight };
		_search.TextEdited += Filter;
		_search.ReturnPressed += () => Pick( _tree.Items.FirstOrDefault() );

		_tree = new TreeView( this ) { MinimumHeight = 280, IndentWidth = 12, ExpandWidth = 16 };
		_tree.ItemClicked = Pick;

		Layout.Add( _search );
		Layout.Add( _tree, 1 );

		Filter( null );
		_reveal = current;
	}

	protected override void OnVisibilityChanged( bool visible )
	{
		base.OnVisibilityChanged( visible );

		// Type straight into the search once the popup is up
		if ( visible )
			_search.Focus();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		// Scrolling reads the height the tree ended up with, which is only real once it's laid out
		Reveal();
	}

	private void Filter( string search )
	{
		if ( string.IsNullOrWhiteSpace( search ) )
		{
			_tree.SetItems( new[] { BoneItem.None }
				.Concat( Enumerable.Range( 0, _bones.Count ).Where( index => _bones[index].ParentIndex < 0 ).Select( index => new BoneItem( _bones, index, true ) ) ) );

			// The skeleton is the point of the tree, so it starts open
			foreach ( var item in _tree.Items.ToArray() )
				_tree.Open( item, recursive: true );

			return;
		}

		// A filtered tree is mostly empty branches, so matches are listed flat, closest first
		var matches = Enumerable.Range( 0, _bones.Count ).Where( index => Matches( _bones[index].Name ) ).Select( index => new BoneItem( _bones, index, false ) );

		_tree.SetItems( matches.OrderBy( item => ((string)item.Value).Length ).ThenBy( item => (string)item.Value ) );

		bool Matches( string name ) => name.Contains( search, StringComparison.OrdinalIgnoreCase );
	}

	private void Reveal()
	{
		if ( string.IsNullOrEmpty( _reveal ) || _tree.Height <= 0 )
			return;

		// Only once - after this the scroll position is the user's
		var bone = _reveal;
		_reveal = null;

		_tree.SelectItem( bone );

		if ( _tree.TryGetItemRect( bone, out var rect ) )
			_tree.ScrollTo( rect.Top, rect.Height );
	}

	private void Pick( object item )
	{
		// Clicks report the node, everything else the value it holds
		if ( item is TreeNode node )
			item = node.Value;

		if ( item is not string name )
			return;

		// The None row holds an empty name, and means clear the property
		_picked( name.Length == 0 ? null : name );
		Close();
	}

	/// <summary>
	/// A row in the tree - a bone, or the None row. Search results are bones listed flat, so they
	/// don't carry their children.
	/// </summary>
	private sealed class BoneItem : TreeNode
	{
		private readonly IReadOnlyList<(string Name, int ParentIndex)> _bones;
		private readonly int _index;
		private readonly bool _showChildren;
		private readonly string _label;

		/// <summary>Clears the property - an empty name, so it can still live in the tree.</summary>
		public static BoneItem None => new( "", "None" );

		private BoneItem( string value, string label )
		{
			Value = value;
			_label = label;
		}

		public BoneItem( IReadOnlyList<(string Name, int ParentIndex)> bones, int index, bool showChildren ) : this( bones[index].Name, bones[index].Name )
		{
			_bones = bones;
			_index = index;
			_showChildren = showChildren;
		}

		public override bool HasChildren => _showChildren && _bones.Any( bone => bone.ParentIndex == _index );

		public override void OnPaint( VirtualWidget item )
		{
			PaintSelection( item );

			var highlighted = Paint.HasSelected || Paint.HasMouseOver;
			var iconRect = item.Rect;
			iconRect.Width = iconRect.Height;

			if ( _bones is not null )
			{
				Paint.Antialiasing = true;
				Paint.Draw( iconRect.Shrink( 4 ), "modeldoc_editor/preview_outliner_bone.png" );
			}

			Paint.SetDefaultFont();
			Paint.SetPen( highlighted ? Color.White : Theme.TextControl.WithAlpha( _bones is null ? 0.5f : 1f ) );

			// Lines the label up with the bone names, whose icons sit at the left of the row
			Paint.DrawText( item.Rect.Shrink( 22, 0, 0, 0 ), _label, TextFlag.LeftCenter );
		}

		protected override void BuildChildren()
		{
			if ( !_showChildren )
				return;

			SetItems( Enumerable.Range( 0, _bones.Count ).Where( index => _bones[index].ParentIndex == _index ).Select( index => new BoneItem( _bones, index, true ) ) );
		}
	}
}
