namespace Editor;

public partial class ItemRow : Widget
{
	protected struct InfoItem
	{
		public string Icon;
		public string Text;

		// Implicit cast from tuple
		public static implicit operator InfoItem( (string Icon, string Text) tuple )
		{
			return new InfoItem()
			{
				Icon = tuple.Icon,
				Text = tuple.Text
			};
		}

		// Deconstruct
		public void Deconstruct( out string icon, out string text )
		{
			icon = Icon;
			text = Text;
		}
	}

	protected static float TargetHeight = 48.0f;

	public string Title { get; set; }
	public Action Click { get; set; }

	private List<InfoItem> Info { get; set; }

	private float ButtonLayoutWidth = 8.0f;

	protected Widget Buttons { get; set; }

	public ItemRow( Widget parent = null ) : base( parent )
	{
	}

	protected virtual void Init()
	{
		MinimumSize = new Vector2( 256, TargetHeight );
		FixedHeight = TargetHeight;
		FocusMode = FocusMode.Click;

		Layout = Layout.Column();
		Layout.Margin = 8.0f;
		Layout.AddStretchCell( 1 );

		{
			Buttons = new Widget( this );
			Buttons.Layout = Layout.AddRow();
			Buttons.Layout.Spacing = 4.0f;
			Buttons.Layout.AddStretchCell( 1 );
		}

		Layout.AddStretchCell( 1 );

		CreateUI();
		Info = GetInfo();
	}

	protected virtual void CreateUI()
	{

	}

	protected virtual List<InfoItem> GetInfo()
	{
		return new List<InfoItem>();
	}

	protected virtual void OnPaintIcon( Rect iconRect )
	{

	}

	public IconButton AddButton( string icon, string tooltip, Action onClick = null )
	{
		var btn = Buttons.Layout.Add( new IconButton( icon, parent: Buttons ) );
		btn.ToolTip = tooltip;

		if ( onClick != null )
			btn.OnClick = onClick;

		ButtonLayoutWidth += 28;
		return btn;
	}

	public virtual void OnClick()
	{

	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( !e.Accepted )
		{
			Click?.Invoke();
			OnClick();
		}
	}

	protected override Vector2 SizeHint()
	{
		return new Vector2( 1000, TargetHeight );
	}

	protected override void OnPaint()
	{
		var r = new Rect( 0, Size );

		var fg = Theme.Text.WithAlpha( 0.8f );
		var fg_faded = Theme.Text.WithAlpha( 0.3f );

		Paint.Antialiasing = true;
		Paint.ClearPen();

		if ( IsUnderMouse )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.TextControl.WithAlpha( 0.1f ) );
			Paint.DrawRect( r.Shrink( 1 ), 6.0f );

			fg = Theme.TextControl;
			fg_faded = Theme.TextControl.WithAlpha( 0.4f );
		}

		r = r.Shrink( 8.0f );

		var iconRect = r.Align( Height - 16.0f, TextFlag.LeftCenter );
		OnPaintIcon( iconRect );

		Paint.ClearBrush();
		Paint.SetDefaultFont();
		Paint.SetPen( fg );
		r = r.Shrink( Height - 8.0f, 0 );

		var x = Paint.DrawText( r, Title, TextFlag.LeftTop );

		r.Top += x.Height + 4.0f;

		// Middle bit
		{
			Paint.SetPen( fg_faded );

			for ( int i = 0; i < Info.Count; i++ )
			{
				var (Icon, Text) = Info[i];

				r.Right = LocalRect.Width - ButtonLayoutWidth;

				x = Paint.DrawIcon( r, Icon, 12.0f, TextFlag.LeftCenter );
				r.Left = x.Right + 4;

				x = Paint.DrawText( r, Text, TextFlag.LeftCenter );
				r.Left = x.Right + 4;

				if ( i != Info.Count - 1 )
				{
					// ascii bullet separator
					x = Paint.DrawText( r, "•", TextFlag.LeftCenter );
					r.Left = x.Right + 4;
				}
			}
		}
	}
}

