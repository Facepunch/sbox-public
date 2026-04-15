using System.IO;
using System.Threading;

namespace Editor.AssetPickers;

public abstract class SimplePicker : AssetPicker
{
	protected LineEdit _search;
	protected ListView _assetList;
	protected SegmentedControl _tabSelect;

	bool _showCloudTab;
	bool _showCloud;

	public SimplePicker( Widget parent, AssetType assetType, PickerOptions options ) : base( parent, assetType, options )
	{
		Window.IsPopup = true;
		Window.StartCentered = false;
		Window.FixedSize = new Vector2( 450, 600 );

		_showCloud = Options.EnableCloud;
		_showCloudTab = Options.SeparateCloudTab;

		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 8;

		_tabSelect = Layout.Add( new SegmentedControl( this ) );
		_tabSelect.AddOption( "Local Assets", "folder" );
		_tabSelect.AddOption( "Cloud Assets", "cloud_download" );
		_tabSelect.OnSelectedChanged = ( i ) => Refresh();
		_tabSelect.Visible = _showCloudTab;

		var searchRow = Layout.AddRow();
		searchRow.Spacing = 8;

		_search = searchRow.Add( new LineEdit() );
		_search.Layout = Layout.Row();
		_search.Layout.AddStretchCell( 1 );
		_search.Focus();
		_search.PlaceholderText = $"⌕  Search {AssetType}s";
		_search.Height = 64;
		_search.TextEdited += ( s ) => Refresh();

		var clearButton = _search.Layout.Add( new ToolButton( string.Empty, "clear", this ) );
		clearButton.Bind( "Visible" ).ReadOnly().From( () => _search.Text.Length > 0, null );
		clearButton.MouseLeftPress = () =>
		{
			_search.Text = string.Empty;
			Refresh();
		};

		if ( _showCloud && !_showCloudTab )
		{
			var showCloud = searchRow.Add( new Checkbox( "", "cloud", this ) );
			showCloud.Bind( "Value" ).From( () => _showCloud, ( v ) => _showCloud = v );
			showCloud.StateChanged += ( s ) => Refresh();
		}

		_assetList = Layout.Add( new ListView( this ) );
		_assetList.ItemPaint = ( item ) => PaintLocalAsset( item.Rect, item.Object as IAssetListEntry );
		_assetList.ItemSpacing = 8;
		_assetList.MultiSelect = false;
		_assetList.ItemSize = new Vector2( 0, 64 );
		_assetList.ItemSelected = OnItemSelected;
		_assetList.ItemScrollEnter = OnItemScrollEnter;
		_assetList.ItemScrollExit = OnItemScrollExit;

		Log.Info( $"Searching for {_search.Value} type:{AssetType.FileExtension} showCloud:{_showCloud.ToString()}" );


		Refresh();
	}

	CancellationTokenSource _ct;
	public async void Refresh()
	{
		_ct?.Dispose();
		_ct = new CancellationTokenSource();

		_assetList.Clear();

		var items = new List<IAssetListEntry>();

		if ( _tabSelect.SelectedIndex == 0 || !_showCloudTab )
			items.AddRange( AssetSystem.All.Where( x => x.AssetType == AssetType && x.RelativePath.Contains( _search.Value ) && !ShouldFilterAsset( x ) ).Select( x => new AssetEntry( x ) ) );

		if ( _tabSelect.SelectedIndex == 1 || (!_showCloudTab && _showCloud && !string.IsNullOrEmpty( _search.Value )) )
		{
			var token = _ct.Token;

			var result = await Package.FindAsync( $"{_search.Value} type:{AssetType.FileExtension}", token: token );
			token.ThrowIfCancellationRequested();
			items.AddRange( result.Packages.Select( x => new PackageEntry( x ) ) );
		}

		_assetList.SetItems( items.OrderBy( x => x.Name ) );

	}

	public override void Show()
	{
		base.Show();
		Window.Position = Application.CursorPosition;
		Window.AdjustSize();
		Window.ConstrainToScreen();
	}
	private void OnItemScrollEnter( object item )
	{
		if ( item is not IAssetListEntry entry )
			return;

		entry.OnScrollEnter();
	}
	private void OnItemScrollExit( object item )
	{
		if ( item is not IAssetListEntry entry )
			return;

		entry.OnScrollExit();
	}

	protected virtual void PaintLocalAsset( Rect rect, IAssetListEntry entry )
	{
		Paint.SetPen( Theme.Text.WithAlpha( 0.7f ) );

		bool active = Paint.HasPressed;
		bool highlight = !active && (Paint.HasSelected || Paint.HasPressed);
		bool hover = !highlight && Paint.HasMouseOver;

		if ( active )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue.Darken( 0.5f ) );
			Paint.DrawRect( rect, 4 );
		}

		if ( highlight )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue.Darken( 0.4f ) );
			Paint.DrawRect( rect, 4 );
		}

		if ( hover )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.5f ) );
			Paint.DrawRect( rect, 4 );
		}

		var iconRect = new Rect( rect.Position, Vector2.One * 64 );
		Paint.BilinearFiltering = true;
		entry.DrawIcon( iconRect );
		Paint.BilinearFiltering = false;

		{
			if ( entry is PackageEntry pe )
			{
				Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
				Paint.DrawIcon( iconRect.Shrink( 4 ), "cloud", 16, TextFlag.LeftTop );
			}
		}

		var textRect = rect;
		textRect.Left = iconRect.Right + 8;
		textRect.Top += 12;

		Paint.SetPen( Theme.Text );
		var strText = Path.GetFileNameWithoutExtension( entry.Name );
		Paint.DrawText( textRect, strText, TextFlag.LeftTop );

		textRect.Top += 12;

		if ( entry is AssetEntry ae )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( textRect, ae.Asset.AssetType.FriendlyName, TextFlag.LeftTop );

			textRect.Top += 12;

			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( textRect, ae.Asset.RelativePath, TextFlag.LeftTop );
		}
		else if ( entry is PackageEntry pe )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( textRect, pe.AssetType?.FriendlyName, TextFlag.LeftTop );

			textRect.Top += 12;

			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( textRect, $"by {pe.Author}", TextFlag.LeftTop );
		}
	}

	void OnItemSelected( object item )
	{
		if ( item is AssetEntry ae )
		{
			Submit( ae.Asset );
		}
		else if ( item is PackageEntry pe )
		{
			Submit( pe.Package );
		}
	}

	public override void SetSelection( Asset asset )
	{
		if ( asset is null ) return;

		if ( asset.Package is not null )
		{
			SetSelection( asset.Package );
			return;
		}

		var entry = _assetList.Items.OfType<AssetEntry>().Where( x => x.Asset == asset ).FirstOrDefault();
		_assetList.SelectItem( entry, skipEvents: true );
		_assetList.ScrollTo( entry );

		_assetList.VerticalScrollbar.Value = (int)_assetList.SmoothScrollTarget;
	}

	public override void SetSelection( Package package )
	{
		_search.Value = package.Title;
		_search.Update();

		var entry = _assetList.Items.OfType<PackageEntry>().Where( x => x.Package == package ).FirstOrDefault();
		_assetList.SelectItem( entry, skipEvents: true );
		_assetList.ScrollTo( entry );

		_assetList.VerticalScrollbar.Value = (int)_assetList.SmoothScrollTarget;
	}
}

