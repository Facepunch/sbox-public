namespace Editor;

class AssetFilterNode : TreeNode
{
	public string Icon { get; }
	public string Label { get; }
	public string Filter { get; }

	public int Count { get; set; }

	public bool IsDefaultView
	{
		get
		{
			if ( string.IsNullOrWhiteSpace( Filter ) )
				return false;

			return CloudAssetBrowser.DefaultView == Filter;
		}

		private set
		{
			CloudAssetBrowser.DefaultView = value ? Filter : "";
			TreeView.Update();
		}
	}

	public AssetFilterNode( string icon, string title, string filter )
	{
		Icon = icon;
		Label = title;
		Filter = filter;
	}

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );

		var rect = item.Rect;

		Paint.SetPen( Theme.Text );
		Paint.SetDefaultFont();
		var nameRect = Paint.DrawText( rect.Shrink( 24, 0 ), Label, TextFlag.LeftCenter );

		if ( Icon.StartsWith( "https:" ) )
		{
			var iconSize = rect.Align( 18, TextFlag.LeftCenter );

			Paint.Draw( iconSize, Icon, borderRadius: 2 );
		}
		else
		{
			Paint.SetPen( Theme.Text );
			Paint.DrawIcon( rect, Icon, 16, TextFlag.LeftCenter );
		}

		if ( Count > 0 )
		{
			var countRect = item.Rect;
			countRect.Left = nameRect.Right += 10;
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.4f * 1 ) );
			Paint.SetDefaultFont( 7 );
			Paint.DrawTextBox( countRect, $"{Count:n0}", Theme.Text.WithAlpha( 0.6f * 1 ), new Sandbox.UI.Margin( 4, 1 ), 3.0f, TextFlag.LeftCenter );
		}

		if ( IsDefaultView )
		{
			Paint.DrawIcon( rect, "star", 16, TextFlag.RightCenter );
		}
	}

	public override bool OnContextMenu()
	{
		var menu = new ContextMenu( null );

		if ( !string.IsNullOrWhiteSpace( Filter ) )
		{
			if ( IsDefaultView )
			{
				menu.AddOption( "Clear Default View", "grade", () => IsDefaultView = false );
			}
			else
			{
				menu.AddOption( "Set Default View", "star", () => IsDefaultView = true );
			}
		}

		BuildContextMenu( menu );

		menu.OpenAtCursor();

		return true;
	}

	protected virtual void BuildContextMenu( ContextMenu menu )
	{
	}
}
