
using Sandbox.Physics;

namespace Editor;

[CustomEditor( typeof( ITagSet ) )]
public class TagSetControlWidget : ControlWidget
{
	Layout TagsArea;
	GridLayout TagsPopupGrid;

	public override bool SupportsMultiEdit => true;

	public TagSetControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Spacing = 3;
		Layout.Margin = new Sandbox.UI.Margin( 3, 0 );

		TagsArea = Layout.AddRow( 1 );
		TagsArea.Spacing = 2;
		TagsArea.Margin = new Sandbox.UI.Margin( 0, 3 );

		Layout.AddStretchCell();

		Layout.Add( new Button( null, "local_offer" ) { MouseLeftPress = OpenPopup, FixedWidth = Theme.RowHeight, FixedHeight = Theme.RowHeight, OnPaintOverride = PaintTagAdd, ToolTip = "Tags" } );
	}

	protected override int ValueHash
	{
		get
		{
			var tags = SerializedProperty.GetValue<ITagSet>();
			if ( tags is null )
				return 0;

			HashCode code = default;

			foreach ( var tag in tags.TryGetAll() )
			{
				code.Add( tag );
			}

			return code.ToHashCode();
		}
	}
	protected override void OnValueChanged()
	{
		TagsArea.Clear( true );

		Dictionary<string, int> tagCounts = new();
		List<string> ownTags = new();
		int maxCount = 1;
		if ( SerializedProperty.IsMultipleValues )
		{
			maxCount = SerializedProperty.MultipleProperties.Count();
			foreach ( var prop in SerializedProperty.MultipleProperties )
			{
				var tagset = prop.GetValue<ITagSet>();
				var isGameTags = tagset is GameTags;
				foreach ( var tag in tagset )
				{
					if ( tagCounts.ContainsKey( tag ) )
					{
						tagCounts[tag]++;
					}
					else
					{
						tagCounts[tag] = 1;
					}
					if ( !isGameTags && !ownTags.Contains( tag ) )
					{
						ownTags.Add( tag );
					}
				}

				if ( tagset is GameTags gameset )
				{
					foreach ( var tag in gameset.TryGetAll( false ) )
					{
						if ( !ownTags.Contains( tag ) )
						{
							ownTags.Add( tag );
						}
					}
				}
			}
		}
		else
		{
			var tags = SerializedProperty.GetValue<ITagSet>();
			if ( tags is null ) return;

			foreach ( var tag in tags )
			{
				tagCounts[tag] = 1;
			}

			if ( tags is GameTags gTags )
			{
				foreach ( var tag in gTags.TryGetAll( false ) )
				{
					if ( !ownTags.Contains( tag ) )
					{
						ownTags.Add( tag );
					}
				}
			}
		}

		var firstTags = SerializedProperty.GetValue<ITagSet>();
		if ( firstTags is null ) return;

		if ( firstTags is GameTags gameTags )
		{
			foreach ( var tag in tagCounts.Take( 32 ) )
			{
				var isSelf = ownTags.Contains( tag.Key );
				var isDifferent = tag.Value != maxCount;

				TagsArea.Add( new TagButton( this ) { TagText = tag.Key, MouseLeftPress = () => RemoveTag( tag.Key ), IsInherited = !isSelf, IsDifferent = isDifferent } );
			}

			return;
		}

		foreach ( var tag in tagCounts.Take( 32 ) )
		{
			TagsArea.Add( new TagButton( this ) { TagText = tag.Key, MouseLeftPress = () => RemoveTag( tag.Key ) } );
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		base.OnContextMenu( e );

		var menu = new Menu( this );
		menu.AddOption( "Clear Tags", "clear", ClearAllTags );
		menu.AddSeparator();

		menu.AddOption( "Copy All Tags", "content_copy", CopyAllTags );
		var pasteAndAdd = menu.AddOption( "Paste and Add Tags", "content_paste", PasteAndAddTags );
		var pasteAndReplace = menu.AddOption( "Paste and Replace Tags", "content_paste_go", PasteAndReplaceTags );

		var clipboard = EditorUtility.Clipboard.Paste();
		if ( !clipboard.Contains( ',' ) )
		{
			pasteAndAdd.Enabled = false;
			pasteAndReplace.Enabled = false;
		}

		menu.OpenAtCursor();

		e.Accepted = true;
	}

	private void ToggleTag( string tag )
	{
		PropertyStartEdit();

		try
		{
			if ( SerializedProperty.IsMultipleValues )
			{
				foreach ( var prop in SerializedProperty.MultipleProperties )
				{
					var propTags = prop.GetValue<ITagSet>();
					if ( propTags is null ) propTags = new TagSet();
					propTags.Toggle( tag );
					prop.Parent?.NoteChanged( prop );
				}
				SerializedProperty.Parent?.NoteChanged( SerializedProperty );
				return;
			}

			var tags = SerializedProperty.GetValue<ITagSet>();
			if ( tags is null )
				return;

			tags.Toggle( tag );
			SerializedProperty.Parent?.NoteChanged( SerializedProperty );
		}
		finally
		{
			PropertyFinishEdit();
		}
	}

	private void AddTag( string tag )
	{
		PropertyStartEdit();

		try
		{
			if ( SerializedProperty.IsMultipleValues )
			{
				foreach ( var prop in SerializedProperty.MultipleProperties )
				{
					var propTags = prop.GetValue<ITagSet>();
					if ( propTags is null ) propTags = new TagSet();

					propTags.Add( tag );
					prop.Parent?.NoteChanged( prop );
				}
				SerializedProperty.Parent?.NoteChanged( SerializedProperty );
				return;
			}

			var tags = SerializedProperty.GetValue<ITagSet>();
			if ( tags is null )
				return;

			tags.Add( tag );
			SerializedProperty.Parent?.NoteChanged( SerializedProperty );
		}
		finally
		{
			PropertyFinishEdit();
		}
	}

	internal void RemoveTag( string tag )
	{
		PropertyStartEdit();

		try
		{
			if ( SerializedProperty.IsMultipleValues )
			{
				foreach ( var prop in SerializedProperty.MultipleProperties )
				{
					var propTags = prop.GetValue<ITagSet>();
					if ( propTags is null ) continue;

					propTags.Remove( tag );
					prop.Parent?.NoteChanged( prop );
				}
				SerializedProperty.Parent?.NoteChanged( SerializedProperty );
				OnValueChanged();
				return;
			}

			var tags = SerializedProperty.GetValue<ITagSet>();
			if ( tags is null )
				return;

			tags.Remove( tag );
			SerializedProperty.Parent?.NoteChanged( SerializedProperty );
		}
		finally
		{
			PropertyFinishEdit();
		}
	}

	bool PaintTagAdd()
	{
		var alpha = Paint.HasMouseOver ? 1.0f : 0.7f;

		Paint.SetPen( Theme.Blue.WithAlpha( 0.5f * alpha ) );
		Paint.DrawIcon( new Rect( 0, Theme.RowHeight ), "local_offer", 16 );

		Paint.SetPen( Theme.Blue.WithAlpha( 0.8f * alpha ) );
		Paint.DrawIcon( new Rect( 0, Theme.RowHeight ), "add", 13, TextFlag.LeftBottom );
		return true;
	}

	void OpenPopup()
	{
		var tags = SerializedProperty.GetValue<ITagSet>();

		if ( tags is null )
		{
			try
			{
				tags = Activator.CreateInstance( SerializedProperty.PropertyType ) as ITagSet;
				SerializedProperty.SetValue( tags );
			}
			catch ( Exception e )
			{
				Log.Warning( $"ITagSet is null and we don't know how to create type: {SerializedProperty.PropertyType}\n\n{e.Message}" );
				return;
			}
		}

		var popup = new PopupWidget( this );
		popup.FixedWidth = 200;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 8;
		popup.Layout.Spacing = 4;

		var entry = popup.Layout.Add( new LineEdit( popup ) );
		TagsPopupGrid = popup.Layout.Add( Layout.Grid() ) as GridLayout;

		entry.PlaceholderText = "New tag..";
		entry.FixedHeight = Theme.RowHeight;
		entry.ReturnPressed += () =>
		{
			AddTag( entry.Value );
			entry.Clear();
			RebuildTagGrid();
		};

		RebuildTagGrid();

		popup.OpenAt( ScreenRect.BottomRight + new Vector2( -200, 0 ) );

		entry.Focus();
	}

	void RebuildTagGrid()
	{
		var tags = SerializedProperty.GetValue<ITagSet>();

		if ( tags == null ) return;
		if ( !TagsPopupGrid.IsValid() ) return;

		TagsPopupGrid.Clear( true );

		var suggestedTags = tags.GetSuggested();

		int i = 0;
		foreach ( var g in suggestedTags.GroupBy( x => x ).OrderByDescending( x => x.Count() ).Take( 32 ) )
		{
			var t = g.First();
			var c = g.Count();

			var button = new Button( "", this )
			{
				MouseLeftPress = () => ToggleTag( t ),
			};

			if ( tags is GameTags gameTags )
			{
				button.OnPaintOverride = () => PaintTagButton( t, c, button.LocalRect, gameTags.Has( t, false ) );
			}
			else
			{
				button.OnPaintOverride = () => PaintTagButton( t, c, button.LocalRect, tags.Has( t ) );
			}

			TagsPopupGrid.AddCell( i % 2, i / 2, button );
			i++;
		}
	}

	private bool PaintTagButton( string tagText, int count, Rect rect, bool has )
	{
		var alpha = Paint.HasMouseOver ? 1.0f : 0.7f;
		var tagColor = Theme.Blue;
		Color bg = Theme.TextControl.WithAlpha( 0.1f );
		Color color = Theme.TextControl.WithAlpha( 0.7f );

		if ( Paint.HasMouseOver )
		{
			bg = Theme.TextControl.WithAlpha( 0.2f );
			color = Theme.TextControl;
		}

		if ( has )
		{
			bg = tagColor.Darken( Paint.HasMouseOver ? 0.5f : 0.6f );
			color = Paint.HasMouseOver ? Theme.TextControl : tagColor;
		}

		Paint.SetDefaultFont( 8 );

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		//if ( Paint.HasMouseOver || has )
		{
			Paint.SetBrush( bg );
			Paint.ClearPen();
			Paint.DrawRect( rect.Shrink( 2 ), 3 );
		}

		Paint.SetPen( color.WithAlphaMultiplied( 0.9f * alpha ) );
		Paint.ClearBrush();
		Paint.DrawText( rect.Shrink( 10, 0 ), tagText.ToLower(), TextFlag.LeftCenter );

		Paint.SetDefaultFont( 7 );
		Paint.SetPen( color.WithAlphaMultiplied( 0.5f * alpha ) );
		Paint.DrawText( rect.Shrink( 10, 0 ), $"{count}", TextFlag.RightCenter );

		return true;
	}

	void ClearAllTags()
	{
		var tags = SerializedProperty.GetValue<ITagSet>();
		if ( tags is null ) return;
		tags.RemoveAll();
		SerializedProperty.Parent?.NoteChanged( SerializedProperty );
	}

	void CopyAllTags()
	{
		var tags = SerializedProperty.GetValue<ITagSet>();
		if ( tags is null ) return;
		var str = "";
		foreach ( var tag in tags.TryGetAll() )
		{
			str += tag + ",";
		}
		EditorUtility.Clipboard.Copy( str );
	}

	void PasteAndAddTags()
	{
		var tags = SerializedProperty.GetValue<ITagSet>();
		if ( tags is null ) return;
		var str = EditorUtility.Clipboard.Paste();
		foreach ( var tag in str.Split( ',' ) )
		{
			if ( !string.IsNullOrWhiteSpace( tag ) )
			{
				tags.Add( tag );
			}
		}
		SerializedProperty.Parent?.NoteChanged( SerializedProperty );
	}

	void PasteAndReplaceTags()
	{
		var tags = SerializedProperty.GetValue<ITagSet>();
		if ( tags is null ) return;
		var str = EditorUtility.Clipboard.Paste();
		tags.RemoveAll();
		foreach ( var tag in str.Split( ',' ) )
		{
			if ( !string.IsNullOrWhiteSpace( tag ) )
			{
				tags.Add( tag );
			}
		}
		SerializedProperty.Parent?.NoteChanged( SerializedProperty );
	}
}

file class TagButton : Widget
{
	public bool IsInherited;
	public bool IsDifferent;

	public TagButton( Widget parent ) : base( parent )
	{
		SetSizeMode( SizeMode.CanShrink, SizeMode.Default );
	}

	public string TagText { get; set; }

	protected override Vector2 SizeHint()
	{
		Paint.SetDefaultFont( 7 );
		return Paint.MeasureText( TagText.ToLower() ) + new Vector2( 8, 0 );
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		base.OnContextMenu( e );

		var menu = new Menu( this );
		menu.AddOption( "Copy Tag", "content_copy", () => EditorUtility.Clipboard.Copy( TagText + "," ) );
		menu.AddOption( "Delete Tag", "delete", () => (Parent as TagSetControlWidget)?.RemoveTag( TagText ) );
		menu.OpenAtCursor();

		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		var alpha = Paint.HasMouseOver ? 1.0f : 0.7f;
		var color = Theme.Blue;

		if ( IsInherited ) color = Theme.TextLight;
		if ( IsDifferent ) color = Theme.MultipleValues;

		Paint.SetDefaultFont( 7 );

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;
		Paint.SetBrush( color.Darken( 0.3f ).WithAlpha( 0.6f * alpha ) );
		Paint.ClearPen();
		Paint.DrawRect( LocalRect, 3 );

		Paint.SetPen( color.WithAlpha( 0.9f * alpha ) );
		Paint.ClearBrush();
		Paint.DrawText( LocalRect.Shrink( 4, 0 ), TagText.ToLower() );
	}
}
