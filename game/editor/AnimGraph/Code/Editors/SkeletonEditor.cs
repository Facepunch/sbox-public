namespace Editor.AnimGraph;

/// <summary>
/// Compact skeleton settings and mask editing, hosted by the asset inspector.
/// </summary>
public sealed class SkeletonEditor : Widget
{
	private Asset _asset;
	private SkeletonDocument _document;
	private SkeletonDocument.BoneMask _selected;
	private BoneWeights _weights;
	private Widget _maskDetails;
	private ComboBox _masks;
	private string _rootBone;
	private string _boneFilter = "";
	private bool _authoredOnly;
	private FloatChannelWeights _channelWeights;
	private string _channelFilter = "";
	private FloatChannels _channels;
	private string _channelsFilter = "";

	public Action<string> Committed { get; set; }
	public Action<SkeletonDocument.BoneMask> MaskPreviewed { get; set; }

	public SkeletonEditor( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 4;
		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public void SetDocument( Asset asset, SkeletonDocument document, string selectedMask = null )
	{
		_asset = asset;
		_document = document;
		if ( selectedMask is not null )
			_selected = document?.Masks.FirstOrDefault( mask => mask.Name == selectedMask );
		Rebuild();
	}

	public void Refresh() => Rebuild();

	private void Rebuild()
	{
		RememberWeightFilters();
		if ( _channels.IsValid() )
			_channelsFilter = _channels.SearchText;
		using var suspend = SuspendUpdates.For( this );
		Layout.Clear( true );
		_weights = null;
		_channelWeights = null;
		_channels = null;

		if ( _document is null )
		{
			Layout.Add( Hint( _asset.HasSourceFile
				? "Couldn't read this skeleton. See the console for details."
				: "This skeleton has no source file. Its compiled bones are available in the preview." ) );
			return;
		}

		_rootBone = _document.RootBone;
		if ( _document.SourceSkeleton is null )
			Layout.Add( Hint( "Compile this skeleton to choose bones and edit mask weights." ) );

		var settings = AddSection( "Skeleton", "polyline" );
		settings.Enabled = !ReadOnly;
		settings.Layout.Add( MakeSheet() );

		var masks = AddSection( "Bone Masks", "layers" );
		masks.ToolTip = "Unweighted chains feather between authored weights. Terminal chains inherit their nearest authored ancestor.";
		var row = masks.Layout.AddRow();
		row.Spacing = 4;
		_masks = row.Add( new ComboBox( masks ) { MaxVisibleItems = 12, MinimumWidth = 0 }, 1 );
		row.Add( SectionHeader.CreateButton( masks, "add", Theme.Green, "Add mask", AddMask, !ReadOnly ) );
		row.Add( SectionHeader.CreateButton( masks, "more_horiz", Theme.TextControl, "Mask actions", ShowMaskMenu, !ReadOnly && _document.Masks.Count > 0 ) );

		if ( !_document.Masks.Contains( _selected ) )
			_selected = _document.Masks.FirstOrDefault();

		if ( _selected is null )
			_masks.AddItem( "No bone masks", enabled: false );

		foreach ( var mask in _document.Masks )
			_masks.AddItem( mask.Name ?? "Unnamed mask", selected: mask == _selected );

		_masks.ItemChanged += () =>
		{
			if ( _masks.CurrentIndex < 0 || _masks.CurrentIndex >= _document.Masks.Count )
				return;

			_selected = _document.Masks[_masks.CurrentIndex];
			RebuildMaskDetails();
		};

		_maskDetails = masks.Layout.Add( new Widget( masks ) { Layout = Layout.Column() } );
		_maskDetails.Layout.Spacing = 4;
		RebuildMaskDetails();

		var channels = AddSection( "Float Channels", "tune" );
		_channels = channels.Layout.Add( new FloatChannels( channels, _document )
		{
			Committed = name =>
			{
				Committed?.Invoke( name );
				_channelWeights?.Rebuild();
			}
		} );
		_channels.SearchText = _channelsFilter;
	}

	private void RebuildMaskDetails()
	{
		RememberWeightFilters();
		using var suspend = SuspendUpdates.For( _maskDetails );
		_maskDetails.Layout.Clear( true );
		_weights = null;
		_channelWeights = null;
		MaskPreviewed?.Invoke( _selected );

		if ( _selected is null )
		{
			_maskDetails.Layout.Add( Hint( "Add a mask to blend a pose over part of the skeleton." ) );
			return;
		}

		var mask = _selected;
		var row = _maskDetails.Layout.AddRow();
		row.Spacing = 4;
		row.Add( new Label( "Name", _maskDetails ) { FixedWidth = 48, Alignment = TextFlag.LeftCenter } );
		var name = row.Add( new LineEdit( mask.Name, _maskDetails ) { PlaceholderText = "Mask name", MinimumWidth = 0, ReadOnly = ReadOnly }, 1 );
		var problem = _maskDetails.Layout.Add( new Label( _maskDetails ) { Color = Theme.Red, WordWrap = true } );

		void ShowProblem()
		{
			problem.Text = string.IsNullOrWhiteSpace( name.Text ) ? "Enter a name for this mask."
				: _document.Masks.Any( other => other != mask && string.Equals( other.Name, name.Text, StringComparison.OrdinalIgnoreCase ) ) ? "A mask with this name already exists." : "";
			problem.Visible = !string.IsNullOrEmpty( problem.Text );
		}

		name.TextEdited += _ => ShowProblem();
		name.EditingFinished += () =>
		{
			ShowProblem();
			if ( !string.IsNullOrEmpty( problem.Text ) || mask.Name == name.Text )
				return;

			mask.Name = name.Text;
			Committed?.Invoke( "Rename Mask" );
			Rebuild();
		};
		ShowProblem();

		var tabs = _maskDetails.Layout.Add( new SegmentedControl( _maskDetails ) );
		tabs.AddOption( "Bone Weights", "account_tree" );
		tabs.AddOption( "Float Weights", "tune" );
		var bones = _maskDetails.Layout.Add( new Widget( _maskDetails ) { Layout = Layout.Column() } );
		if ( _document.SourceSkeleton is null )
		{
			bones.Layout.Add( Hint( "Compile this skeleton to edit bone weights." ) );
		}
		else
		{
			_weights = bones.Layout.Add( new BoneWeights( bones )
			{
				FixedHeight = 260,
				Changed = () => MaskPreviewed?.Invoke( _selected ),
				Committed = name =>
				{
					Committed?.Invoke( name );
					MaskPreviewed?.Invoke( _selected );
				}
			} );
			_weights.SetSource( _document );
			_weights.Mask = _selected;
			_weights.SearchText = _boneFilter;
			_weights.AuthoredOnly = _authoredOnly;
		}
		_channelWeights = _maskDetails.Layout.Add( new FloatChannelWeights( _maskDetails, _document, mask )
		{
			Committed = name => Committed?.Invoke( name )
		} );
		_channelWeights.SearchText = _channelFilter;
		tabs.OnSelectedChanged = _ =>
		{
			bones.Visible = tabs.SelectedIndex == 0;
			_channelWeights.Visible = tabs.SelectedIndex == 1;
			EditorCookie.Set( "SkeletonInspector.FloatWeights", tabs.SelectedIndex == 1 );
		};
		tabs.SelectedIndex = EditorCookie.Get( "SkeletonInspector.FloatWeights", false ) ? 1 : 0;
	}

	private void RememberWeightFilters()
	{
		if ( _channelWeights.IsValid() )
			_channelFilter = _channelWeights.SearchText;
		if ( !_weights.IsValid() )
			return;

		_boneFilter = _weights.SearchText;
		_authoredOnly = _weights.AuthoredOnly;
	}

	private void AddMask()
	{
		if ( ReadOnly )
			return;

		_selected = new SkeletonDocument.BoneMask { Name = UniqueName( "mask" ) };
		_document.Masks.Add( _selected );
		Committed?.Invoke( "Add Mask" );
		Rebuild();
	}

	private void ShowMaskMenu()
	{
		if ( _selected is null || ReadOnly )
			return;

		var menu = new Menu( this );
		menu.AddOption( "Duplicate Mask", "content_copy", () =>
		{
			var copy = new SkeletonDocument.BoneMask
			{
				Name = UniqueName( _selected.Name ),
				DefaultFloatChannelWeight = _selected.DefaultFloatChannelWeight,
				FloatChannelWeights = _selected.FloatChannelWeights.Select( weight => new SkeletonDocument.FloatChannelWeight { Channel = weight.Channel, Weight = weight.Weight } ).ToList(),
				Weights = _selected.Weights.Select( weight => new SkeletonDocument.BoneWeight { Bone = weight.Bone, Weight = weight.Weight } ).ToList()
			};
			_document.Masks.Insert( _document.Masks.IndexOf( _selected ) + 1, copy );
			_selected = copy;
			Committed?.Invoke( "Duplicate Mask" );
			Rebuild();
		} );
		menu.AddOption( "Delete Mask", "delete", () =>
		{
			_document.Masks.Remove( _selected );
			_selected = null;
			Committed?.Invoke( "Delete Mask" );
			Rebuild();
		} );
		menu.OpenAtCursor();
	}

	private string UniqueName( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			name = "mask";

		var index = 1;
		while ( _document.Masks.Any( mask => string.Equals( mask.Name, $"{name}_{index}", StringComparison.OrdinalIgnoreCase ) ) )
			index++;

		return $"{name}_{index}";
	}

	private Layout MakeSheet()
	{
		var so = _document.GetSerialized();
		var editing = false;
		so.OnPropertyStartEdit += _ => editing = true;
		so.OnPropertyFinishEdit += property =>
		{
			editing = false;
			CommitProperty( property );
		};
		so.OnPropertyChanged += property =>
		{
			if ( !editing )
				CommitProperty( property );
		};

		var sheet = new ControlSheet();
		foreach ( var property in so.Where( property => property.HasAttribute<PropertyAttribute>() && !property.HasAttribute<HideAttribute>() ) )
		{
			if ( sheet.AddRow( property ) is { } control )
				control.MinimumWidth = 0;
		}
		return sheet;
	}

	private void CommitProperty( SerializedProperty property )
	{
		while ( property is not null && int.TryParse( property.Name, out _ ) )
			property = property.Parent?.ParentProperty;

		Committed?.Invoke( property is null ? "Edit Skeleton" : $"Set {property.DisplayName}" );

		if ( _rootBone == _document.RootBone )
			return;

		_rootBone = _document.RootBone;
		_weights?.SetSource( _document );
		MaskPreviewed?.Invoke( _selected );
	}

	private Widget AddSection( string title, string icon )
	{
		var body = new Widget( this ) { Layout = Layout.Column() };
		body.Layout.Margin = 4;
		body.Layout.Spacing = 4;
		Layout.Add( new AnimationInspectorSection( title, icon, body, $"SkeletonInspector.{title}" ) );
		Layout.Add( body );
		return body;
	}

	private Label Hint( string text ) => new( text, this ) { Color = Theme.TextControl.WithAlpha( 0.5f ), WordWrap = true };

}
