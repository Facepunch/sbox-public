namespace Editor.AnimGraph;

[CustomEditor( typeof( List<ClipDocument.MorphWeight> ), NamedEditor = "animgraph.morph-weights" )]
internal sealed class MorphWeightsControlWidget : ListControlWidget
{
	public override bool IsWideMode => true;
	public override bool IncludeLabel => false;

	public MorphWeightsControlWidget( SerializedProperty property ) : base( property )
	{
		PaintBackground = false;
	}
}

[CustomEditor( typeof( ClipDocument.MorphWeight ) )]
internal sealed class MorphWeightControlWidget : ControlWidget
{
	private readonly ComboBox _channel;
	private readonly FloatControlWidget _weight;
	private readonly SerializedProperty _name;
	private readonly ClipDocument _document;
	private string _skeleton;
	private bool _updating;

	protected override int ValueHash => HashCode.Combine( base.ValueHash, _document?.SkeletonName );

	public MorphWeightControlWidget( SerializedProperty property ) : base( property )
	{
		PaintBackground = false;
		Layout = Layout.Row();
		Layout.Spacing = 4;
		property.TryGetAsObject( out var serialized );
		_name = serialized.GetProperty( nameof( ClipDocument.MorphWeight.Morph ) );
		for ( var owner = property.Parent; owner is not null; owner = owner.ParentProperty?.Parent )
		{
			_document = owner.Targets?.OfType<ClipDocument>().FirstOrDefault();
			if ( _document is not null )
				break;
		}

		_channel = Layout.Add( new ComboBox( this ) { Editable = true, Insertion = ComboBox.InsertMode.Skip, MinimumWidth = 0, MaxVisibleItems = 12 }, 1 );
		_channel.LineEdit.PlaceholderText = "Choose a channel...";
		_channel.ToolTip = "Choose a channel declared in the skeleton, or enter a custom channel name.";
		_channel.ItemChanged += CommitName;
		_channel.LineEdit.EditingFinished += CommitName;
		_channel.SetAutoComplete( ( menu, search ) =>
		{
			foreach ( var channel in (_document?.SourceFloatChannels ?? []).Where( name => name.Contains( search, StringComparison.OrdinalIgnoreCase ) ) )
				menu.AddOption( channel );
		} );
		_weight = Layout.Add( new FloatControlWidget( serialized.GetProperty( nameof( ClipDocument.MorphWeight.Weight ) ) )
		{
			Label = null,
			PaintBackground = false,
			MinimumWidth = 110,
			MaximumWidth = 150,
			ToolTip = "Morph weight"
		}, 1 );
		OnValueChanged();
	}

	private void CommitName()
	{
		if ( _updating || IsControlDisabled )
			return;

		var name = _channel.CurrentText.Trim();
		if ( name == _name.As.String )
			return;

		PropertyStartEdit();
		_name.As.String = name;
		PropertyFinishEdit();
	}

	protected override void OnValueChanged()
	{
		_updating = true;
		if ( _skeleton != _document?.SkeletonName )
		{
			_skeleton = _document?.SkeletonName;
			_channel.Clear();
			foreach ( var name in _document?.SourceFloatChannels ?? [] )
				_channel.AddItem( name );
		}
		_channel.CurrentText = _name.As.String;
		_channel.Enabled = !IsControlDisabled;
		_weight.ReadOnly = IsControlDisabled;
		_updating = false;
	}
}
