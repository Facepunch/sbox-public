namespace Editor;

[CustomEditor( typeof( string ), NamedEditor = "organization" )]
public class OrganizationControlWidget : ControlWidget
{
	public string Value
	{
		get => SerializedProperty.As.String;
		set => SerializedProperty.As.String = value;
	}

	public OrganizationControlWidget( SerializedProperty property ) : base( property )
	{

		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		var iconRect = LocalRect.Shrink( 4 );
		iconRect.Width = iconRect.Height;

		var wordRect = LocalRect.Shrink( 1 );
		wordRect.Left = iconRect.Right + 8;

		Paint.SetPen( Theme.Text.WithAlpha( Paint.HasMouseOver ? 0.6f : 0.3f ) );
		Paint.DrawIcon( LocalRect.Shrink( 8, 8 ), "arrow_drop_down", 16, TextFlag.RightCenter );

		var orgName = SerializedProperty.As.String;

		if ( orgName == "local" )
		{
			Paint.SetPen( Theme.Blue );
			Paint.DrawIcon( iconRect, "computer", iconRect.Height );

			Paint.SetPen( Theme.Text );
			Paint.SetDefaultFont( 8, 500 );
			var r = Paint.DrawText( wordRect, "Local", TextFlag.LeftCenter );

			Paint.SetPen( Theme.TextLight );
			Paint.SetDefaultFont();
			Paint.DrawText( wordRect.Shrink( r.Width + 8, 0, 0, 0 ), "No organisation chosen", TextFlag.LeftCenter );
			return;
		}

		var org = EditorUtility.Account.Memberships.FirstOrDefault( x => x.Ident == orgName );
		if ( org != null )
		{
			Paint.Draw( iconRect, org.Thumb, borderRadius: 2 );

			Paint.SetPen( Theme.Text );
			Paint.SetDefaultFont();
			var r = Paint.DrawText( wordRect, org.Title, TextFlag.LeftCenter );

			Paint.SetPen( Theme.TextLight );
			Paint.SetDefaultFont();
			Paint.DrawText( wordRect.Shrink( r.Width + 8, 0, 0, 0 ), org.Ident, TextFlag.LeftCenter );
			return;
		}

		Paint.SetPen( Theme.Blue );
		Paint.DrawIcon( iconRect, "question", 28 );

		Paint.SetPen( Theme.Text );
		Paint.SetDefaultFont( 9 );
		Paint.DrawText( wordRect, Value, TextFlag.LeftTop );

		Paint.SetPen( Theme.TextLight );
		Paint.SetDefaultFont();
		Paint.DrawText( wordRect, "Membership not found", TextFlag.LeftBottom );
	}

	public async void OpenMenu( Rect buttonRect )
	{
		var menu = new ContextMenu( this );
		var local = menu.AddOption( "Make Local", null, () => { Value = "local"; SignalValuesChanged(); } );
		local.Checkable = true;
		local.Checked = Value == "local";

		menu.FixedWidth = buttonRect.Width;

		// no account information?
		await EditorUtility.Account.Assure();

		var orgs = EditorUtility.Account.Memberships.ToArray();

		if ( orgs.Length > 0 )
		{
			menu.AddSeparator();

			foreach ( var org in orgs )
			{
				var o = menu.AddOption( $"{org.Title} ({org.Ident})", null, () =>
				{
					Value = org.Ident;
					SignalValuesChanged();
				} );
				o.Checkable = true;
				o.Checked = Value == org.Ident;
			}
		}

		menu.AddSeparator();
		menu.AddOption( "Create New Organisation..", null, () => EditorUtility.OpenFolder( $"{Global.BackendUrl}/~create" ) );

		menu.OpenAt( buttonRect.BottomLeft - new Vector2( 0, -1 ), false );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.LeftMouseButton )
		{
			e.Accepted = true;
			OpenMenu( ScreenRect );
		}
	}
}
