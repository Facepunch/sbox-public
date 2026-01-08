namespace Sandbox;

internal class SerializedList : SerializedCollection
{
	internal System.Collections.IList list;

	public override void SetTargetObject( object obj, SerializedProperty property )
	{
		base.SetTargetObject( obj, property );

		list = obj as System.Collections.IList;
		ArgumentNullException.ThrowIfNull( list );
	}

	protected override void PrepareEnumerator()
	{
		if ( PropertyList is not null && PropertyList.Count == list.Count )
			return;

		PropertyList = new List<SerializedProperty>();

		for ( int i = 0; i < list.Count; i++ )
		{
			PropertyList.Add( new SerializedListProperty( this, i ) );
		}
	}

	public override bool Remove( SerializedProperty property )
	{
		var index = PropertyList.IndexOf( property );

		if ( index == -1 )
		{
			return false;
		}

		return RemoveAt( index );
	}

	public override bool RemoveAt( object index )
	{
		int idx = Convert.ToInt32( index );

		if ( idx < 0 ) return false;
		if ( idx >= list.Count ) return false;

		NotePreChange();

		list.RemoveAt( idx );
		PropertyList.RemoveAt( idx );

		// re-index
		for ( int i = 0; i < PropertyList.Count; i++ )
		{
			((SerializedListProperty)PropertyList[i]).Index = i;
		}

		OnEntryRemoved?.Invoke();
		NoteChanged();
		return true;
	}

	public override bool Add( object value )
	{
		return Insert( list.Count, value );
	}

	public override bool Add( object key, object value )
	{
		if ( key is not int index ) return false;

		return Insert( index, value );
	}

	private bool Insert( int index, object value )
	{
		if ( index < 0 || index > list.Count ) return false;

		if ( value is null && ValueType.IsValueType )
			value = Activator.CreateInstance( ValueType );

		var prop = new SerializedListProperty( this, index );

		NotePreChange( prop );

		list.Insert( index, value );

		for ( var i = index; i < PropertyList.Count; ++i )
		{
			if ( PropertyList[i] is SerializedListProperty listProperty )
			{
				listProperty.Index = i + 1;
			}
		}

		PropertyList.Insert( index, prop );

		OnEntryAdded?.Invoke();
		NoteChanged( prop );
		return true;
	}
}

file class SerializedListProperty : SerializedProperty
{
	private SerializedList serializedList;
	public int Index;

	public override string Name => $"{Index}";
	public override Type PropertyType => serializedList.ValueType;
	public override SerializedObject Parent => serializedList;
	public override bool IsValid => serializedList.list.Count > Index && base.IsValid;

	public SerializedListProperty( SerializedList serializedList, int i )
	{
		this.serializedList = serializedList;
		this.Index = i;
	}

	public override IEnumerable<Attribute> GetAttributes()
	{
		return serializedList?.ParentProperty?.GetAttributes() ?? Enumerable.Empty<Attribute>();
	}

	public Type ValueType { get; internal set; }


	public override T GetValue<T>( T defaultValue = default )
	{
		if ( Index < 0 || Index >= serializedList.list.Count )
			return defaultValue;

		return ValueToType<T>( serializedList.list[Index], defaultValue );
	}

	public override void SetValue<T>( T value )
	{
		object val = value;

		if ( Translation.TryConvert( val, PropertyType, out var converted ) )
		{
			NotePreChange();
			serializedList.list[Index] = converted;
			NoteChanged();
		}
	}

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		obj = serializedList.PropertyToObject?.Invoke( this ) ?? null;
		return obj is not null;
	}
}
