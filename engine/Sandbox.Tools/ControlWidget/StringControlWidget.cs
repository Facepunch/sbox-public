namespace Editor;

[CustomEditor( typeof( string ) )]
public class StringControlWidget : ControlWidget
{
	protected LineEdit LineEdit;

	public override bool IsControlActive => LineEdit.IsFocused;
	public override bool SupportsMultiEdit => true;

	public override TextFlag CellAlignment => TextFlag.LeftCenter;

	/// <summary>
	/// Allow overriding the regex validator on <see cref="LineEdit"/>.
	/// </summary>
	public string RegexValidator
	{
		set => LineEdit.RegexValidator = value;
	}

	public override bool ReadOnly
	{
		get => base.ReadOnly;
		set
		{
			base.ReadOnly = value;
			LineEdit.ReadOnly = ReadOnly;
		}
	}

	public StringControlWidget( SerializedProperty property ) : base( property )
	{
		LineEdit = new LineEdit( this );
		LineEdit.TextEdited += OnEdited;
		LineEdit.MinimumSize = Theme.RowHeight;
		LineEdit.MaximumSize = new Vector2( 4096, Theme.RowHeight );
		LineEdit.EditingFinished += OnEditingFinished;
		LineEdit.EditingStarted += OnEditingStarted;
		LineEdit.Focused += OnLineEditFocused;
		LineEdit.MouseClick += OnLineEditClicked;
		LineEdit.SetStyles( "background-color: transparent;" );

		if ( property.TryGetAttribute<PlaceholderAttribute>( out var placeholder ) )
		{
			LineEdit.PlaceholderText = placeholder.Value;
		}

		LineEdit.Text = ValueToString();

		if ( !property.IsEditable )
			ReadOnly = true;
	}

	public override void StartEditing()
	{
		LineEdit.Focus();
		LineEdit.SelectAll();
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		LineEdit.Position = 0;
		LineEdit.Size = Size;
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		if ( LineEdit.IsFocused )
			return;

		LineEdit.Text = ValueToString();

		// we put the curor at the start of the line so that
		// it keeps the front of the string in focus, since that
		// is most likely the important part
		LineEdit.CursorPosition = 0;
	}

	void OnEditingStarted()
	{
		PropertyStartEdit();
	}

	void OnEdited( string text )
	{
		SerializedProperty.SetValue( StringToValue( text ) );
	}

	void OnEditingFinished()
	{
		LineEdit.Text = ValueToString();
		LineEdit.CursorPosition = 0;
		PropertyFinishEdit();
	}

	private bool _selectAllOnLineEditClick;

	void OnLineEditFocused( FocusChangeReason reason )
	{
		_selectAllOnLineEditClick = reason == FocusChangeReason.Mouse && !LineEdit.ReadOnly;
	}

	void OnLineEditClicked()
	{
		if ( !_selectAllOnLineEditClick )
			return;

		_selectAllOnLineEditClick = false;

		if ( !LineEdit.ReadOnly )
		{
			LineEdit.SelectAll();
		}
	}

	/// <summary>
	/// Change text to pink if we're editing multiple values, and they differ
	/// </summary>
	protected override void OnMultipleDifferentValues( bool state )
	{
		if ( state )
		{
			LineEdit.SetStyles( $"color: {Theme.MultipleValues.Hex}; background-color: transparent;" );
		}
		else
		{
			LineEdit.SetStyles( $"color: {Theme.TextControl.Hex}; background-color: transparent;" );
		}
	}

	protected virtual string ValueToString() => SerializedProperty.As.String;
	protected virtual object StringToValue( string text )
	{
		if ( Translation.TryConvert( text, SerializedProperty.PropertyType, out var convertedValue ) )
		{
			return convertedValue;
		}
		return text;
	}

	public override string ToClipboardString()
	{
		return ValueToString();
	}

	public override void FromClipboardString( string clipboard )
	{
		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( StringToValue( clipboard ) );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}

	public override void OnDestroyed()
	{
		if ( LineEdit != null )
		{
			LineEdit.EditingFinished -= OnEditingFinished;
			LineEdit.EditingStarted -= OnEditingStarted;
			LineEdit.TextEdited -= OnEdited;
			LineEdit.Focused -= OnLineEditFocused;
			LineEdit.MouseClick -= OnLineEditClicked;
		}

		base.OnDestroyed();
	}

}
