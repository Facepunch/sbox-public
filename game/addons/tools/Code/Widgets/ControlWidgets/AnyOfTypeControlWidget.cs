namespace Editor;

/// <summary>
/// Editor control widget for <see cref="AnyOfType{T}"/>.
/// Shows a dropdown to select the concrete type, and an inline editor for the selected type's properties.
/// </summary>
[CustomEditor( typeof( AnyOfType<> ) )]
sealed class AnyOfTypeControlWidget : StickyPopupControlWidget
{
	public override bool SupportsMultiEdit => false;

	Type _baseType;
	TypeDescription _wrapperType;

	List<TypeDescription> SupportedTypes => EditorTypeLibrary.GetTypes( _baseType )
		.Where( x => !x.IsAbstract && x.TargetType.IsAssignableTo( _baseType ) )
		.OrderBy( x => x.Title )
		.ToList();

	public AnyOfTypeControlWidget( SerializedProperty property ) : base( property )
	{
		_baseType = property.PropertyType.GenericTypeArguments[0];
		_wrapperType = EditorTypeLibrary.GetType( typeof( AnyOfType<> ) );

		SerializedProperty.OnChanged = OnChanged;
		InitializeInlineEditor();
	}

	protected override void OnPaint()
	{
		if ( IsInlineEditor )
		{
			PaintInlineEditorHeader();
			return;
		}
		var inner = GetInnerValue();
		var text = inner is null
			? $"Select {DisplayInfo.ForType( _baseType ).Name}..."
			: DisplayInfo.ForType( inner.GetType() ).Name;

		Theme.DrawDropdown( LocalRect, text, "category", _popup.IsValid(), IsControlDisabled );
	}

	void OnChanged( SerializedProperty prop )
	{
		RebuildEditor();
	}

	void Clear()
	{
		PropertyStartEdit();
		SerializedProperty.SetValue( _wrapperType.CreateGeneric<object>( [_baseType] ) );
		RebuildEditor();
		SignalValuesChanged();
		PropertyFinishEdit();
	}

	void SetType( TypeDescription type )
	{
		var instance = type.Create<object>();
		if ( instance is null ) return;

		PropertyStartEdit();
		WriteWrapper( instance );
		RebuildEditor();
		SignalValuesChanged();
		PropertyFinishEdit();
	}

	void OpenTypePopup()
	{
		var menu = new ContextMenu( IsInlineEditor ? this : _popup );
		var currentType = GetInnerValue()?.GetType();

		foreach ( var type in SupportedTypes )
		{
			var item = menu.AddOption( type.Title, action: () => SetType( type ) );
			item.Icon = type.Icon ?? "category";
			item.Enabled = type.TargetType != currentType;
		}

		menu.OpenAt( _toolbar.ScreenRect.BottomLeft );
	}

	protected override void BuildEditor( Widget target, bool isPopup )
	{
		if ( !target.IsValid() ) return;
		var inner = GetInnerValue();

		PrepareEditor( target, isPopup );
		var typeName = inner is null ? "No type selected" : DisplayInfo.ForType( inner.GetType() ).Name;
		var typeIcon = inner is null ? "block" : DisplayInfo.ForType( inner.GetType() ).Icon ?? "category";
		_toolbar.AddOption( IsInlineEditor ? typeName : "Set Type", IsInlineEditor ? typeIcon : "type_specimen", action: OpenTypePopup ).Enabled = !target.ReadOnly && SupportedTypes.Any();
		AddClipboardOptions( target, RebuildEditor );
		_toolbar.AddSeparator();
		_toolbar.AddOption( "Clear", "delete", action: Clear ).Enabled = !target.ReadOnly;

		FinishEditor( target, isPopup, inner?.GetSerialized() );
	}

	object GetInnerValue()
	{
		var wrapper = SerializedProperty.GetValue<object>();
		return wrapper?.GetType().GetProperty( "Value" )?.GetValue( wrapper );
	}

	void WriteWrapper( object instance )
	{
		SerializedProperty.SetValue( _wrapperType.CreateGeneric<object>( [_baseType], [instance] ) );
	}
}
