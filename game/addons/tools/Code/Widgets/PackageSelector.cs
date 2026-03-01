using System.Threading;

namespace Editor;

public class PackageSelector : Dialog
{
	string Query;
	Action<Package[]> ConfirmSelection;
	Button OkayButton;
	LineEdit SearchFilter;
	ListView AddonList;
	ListView SelectedAddonsList;
	Package[] SelectedPackages { get; set; }

	/// <summary>
	/// Whether to allow selecting multiple packages.
	/// </summary>
	public bool MultiSelect
	{
		get => AddonList.MultiSelect;
		set
		{
			AddonList.MultiSelect = value;
			AddonList.ToggleSelect = value;
		}
	}

	public PackageSelector( Widget parent, string query = "", Action<Package[]> confirm = null, Package[] defaultSelection = null ) : base( parent )
	{
		Query = query;

		if ( confirm != null )
			ConfirmSelection = confirm;

		Window.Size = new Vector2( 500, 630 );
		Window.IsDraggable = true;
		Window.Title = $"Select packages from {Global.BackendTitle}";
		Window.SetWindowIcon( "cloud_download" );
		Window.SetModal( true, true );

		Layout = Layout.Column();
		SetSizeMode( SizeMode.CanGrow, SizeMode.CanGrow );
		Layout.Margin = 0;
		Layout.Spacing = 0;

		// Add splitter
		var splitter = new Splitter( this );
		splitter.IsVertical = true;
		Layout.Add( splitter, 0 );

		// Main content
		var mainWidget = new Widget( this );
		mainWidget.Layout = Layout.Column();
		mainWidget.Layout.Margin = 8;

		// Filtering
		{
			SearchFilter = new LineEdit( "" ) { PlaceholderText = "Filter By Name.." };
			SearchFilter.TextEdited += ( x ) => _ = UpdateQueryAsync();
			SearchFilter.Focus();
			mainWidget.Layout.Add( SearchFilter, 0 );
		}

		// Body
		{
			AddonList = new ListView();
			AddonList.ItemPaint = PaintAddonItem;
			AddonList.ItemSize = new Vector2( 0, 38 );
			AddonList.ItemSelected = x => UpdateLineEdit();
			AddonList.ItemDeselected = x => UpdateLineEdit();
			AddonList.OnSelectionChanged = x => UpdateLineEdit();
			mainWidget.Layout.Add( AddonList, 1 );
		}

		splitter.AddWidget( mainWidget );

		// Footer
		{
			var footerWidget = new Widget( this );
			footerWidget.Layout = Layout.Column();

			splitter.AddWidget( footerWidget );

			SelectedAddonsList = new ListView();
			SelectedAddonsList.ItemPaint = PaintSmallAddonItem;
			SelectedAddonsList.ItemSize = new Vector2( 40, 40 );
			SelectedAddonsList.ItemSelected = x =>
			{
				SelectedAddonsList.RemoveItem( x );
				AddonList.UnselectItem( x );
				UpdateLineEdit( false );
			};
			footerWidget.Layout.Add( SelectedAddonsList, 0 );

			// Set stretch factors
			splitter.SetStretch( 0, 9 );
			splitter.SetStretch( 1, 1 );

			footerWidget.Visible = MultiSelect;
		}

		Layout.AddSeparator();

		// Action Buttons
		{
			var lo = Layout.AddRow();
			lo.Margin = 20;
			lo.Spacing = 4;

			lo.AddStretchCell( -1 );
			OkayButton = lo.Add( new Button.Primary( "OK" ) { Clicked = TryOkay } );
			lo.Add( new Button( "Cancel" ) { Clicked = Window.Close } );
		}

		// If we have some packages to auto-select, do this now
		if ( defaultSelection != null )
		{
			SelectedPackages = defaultSelection;
			UpdateFromSelected();
		}

		UpdateOkay();
		_ = UpdateQueryAsync();
	}

	void UpdateFromSelected()
	{
		SelectedAddonsList.SetItems( SelectedPackages );
		UpdateOkay();
	}

	void UpdateLineEdit( bool updateSelected = true )
	{
		SelectedPackages = AddonList.SelectedItems.Select( x => x as Package ).ToArray();
		AddonList.Update();

		if ( updateSelected )
			UpdateFromSelected();
	}

	void TryOkay()
	{
		ConfirmSelection?.Invoke( SelectedPackages );
		Window.Destroy();
	}

	private void PaintSmallAddonItem( VirtualWidget v )
	{
		if ( v.Object is not Package package )
			return;

		var rect = v.Rect;

		Paint.Antialiasing = true;

		if ( Paint.HasSelected )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.9f ) );
			Paint.DrawRect( rect, 2 );

			Paint.SetPen( Theme.Text );
		}
		else if ( Paint.HasMouseOver )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Red.WithAlpha( 0.9f ) );
			Paint.DrawRect( rect, 2 );
		}

		var iconRect = rect.Shrink( 4, 4 );
		iconRect.Width = iconRect.Height;
		Paint.Draw( iconRect, package.Thumb, borderRadius: 2 );
	}

	private void PaintAddonItem( VirtualWidget v )
	{
		if ( v.Object is not Package package )
			return;

		var rect = v.Rect.Shrink( 8, 0 );

		Paint.Antialiasing = true;

		Color fg = Color.White.Darken( 0.2f );

		if ( Paint.HasSelected )
		{
			fg = Color.White;
			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.9f ) );
			Paint.DrawRect( rect, 4 );

			Paint.SetPen( Theme.Text );
		}
		else
		{
			Paint.SetDefaultFont();
			Paint.SetPen( Theme.TextLight );
		}

		if ( Paint.HasMouseOver )
		{
			Paint.ClearPen();
			Paint.SetBrush( Color.White.WithAlpha( 0.1f ) );
			Paint.DrawRect( rect, 4 );
		}

		var iconRect = rect.Shrink( 8, 4 );
		iconRect.Width = iconRect.Height;
		Paint.Draw( iconRect, package.Thumb, borderRadius: 2 );

		var textRect = rect.Shrink( 4 );
		textRect.Left = iconRect.Right + 8;

		Paint.SetHeadingFont( 10, 450 );
		Paint.SetPen( fg );
		Paint.DrawText( textRect, package.Title, TextFlag.LeftTop );

		Paint.SetDefaultFont();
		Paint.SetPen( fg.WithAlpha( 0.6f ) );
		Paint.DrawText( textRect, package.Org.Title, TextFlag.LeftBottom );
	}

	void UpdateOkay()
	{
		bool enabled = true;

		if ( !IsValidPackage() ) enabled = false;

		OkayButton.Enabled = enabled;
	}

	bool IsValidPackage()
	{
		if ( SelectedPackages == null ) return false;
		if ( SelectedPackages.Length < 1 ) return false;

		return true;
	}

	CancellationTokenSource taskSource;
	async Task UpdateQueryAsync()
	{
		taskSource?.Dispose();
		taskSource = new CancellationTokenSource();
		var token = taskSource.Token;
		var queryString = $"{Query} sort:trending {SearchFilter.Text}";

		var result = await Package.FindAsync( queryString, 200, 0, token );
		token.ThrowIfCancellationRequested();

		AddonList.SetItems( result.Packages );

		if ( SelectedPackages != null && AddonList.SelectedItems.Count() != SelectedPackages.Length )
		{
			foreach ( var package in SelectedPackages )
			{
				// Ugly hack
				var value = AddonList.Items.OfType<Package>().FirstOrDefault( x => x?.FullIdent == package?.FullIdent );
				if ( value != null ) AddonList.SelectItem( value, true, true );
			}
		}
	}
}
