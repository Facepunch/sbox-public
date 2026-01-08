
namespace Sandbox;

internal class SerializedArray : SerializedCollection
{
	internal Array array;

	public override void SetTargetObject( object obj, SerializedProperty property )
	{
		base.SetTargetObject( obj, property );

		array = obj as Array;
		ArgumentNullException.ThrowIfNull( array );
	}

	protected override void PrepareEnumerator()
	{
		if ( PropertyList is not null && PropertyList.Count == array.Length )
			return;

		PropertyList = new List<SerializedProperty>();
		for ( int i = 0; i < array.Length; i++ )
			PropertyList.Add( new SerializedArrayProperty( this, i ) );
	}

	public override bool Remove( SerializedProperty property )
	{
		var index = PropertyList.IndexOf( property );
		if ( index == -1 )
			return false;

		return RemoveAt( index );
	}

	public override bool RemoveAt( object index )
	{
		var idx = Convert.ToInt32( index );
		if ( idx < 0 ) return false;
		if ( idx >= array.Length ) return false;

		var newArray = Array.CreateInstance( array.GetType().GetElementType(), array.Length - 1 );
		var j = 0;

		for ( var i = 0; i < array.Length; i++ )
		{
			if ( i != idx )
			{
				newArray.SetValue( array.GetValue( i ), j );
				j++;
			}
		}

		NotePreChange();

		ParentProperty.SetValue( newArray );
		array = newArray;

		PropertyList.RemoveAt( idx );

		for ( int i = 0; i < PropertyList.Count; i++ )
			((SerializedArrayProperty)PropertyList[i]).Index = i;

		OnEntryRemoved?.Invoke();
		NoteChanged();
		return true;
	}

	public override bool Add( object value )
	{
		return Insert( array.Length, value );
	}

	public override bool Add( object key, object value )
	{
		if ( key is not int index ) return false;
		return Insert( index, value );
	}

	private bool Insert( int index, object value )
	{
		if ( index < 0 || index > array.Length ) return false;

		if ( value == null && array.GetType().GetElementType().IsValueType )
			value = Activator.CreateInstance( array.GetType().GetElementType() );

		var length = array.Length;
		var newArray = Array.CreateInstance( array.GetType().GetElementType(), length + 1 );

		for ( var i = 0; i < index; i++ )
			newArray.SetValue( array.GetValue( i ), i );

		for ( var i = index; i < length; i++ )
			newArray.SetValue( array.GetValue( i ), i + 1 );

		newArray.SetValue( value, index );

		var prop = new SerializedArrayProperty( this, index );
		NotePreChange( prop );

		ParentProperty.SetValue( newArray );
		array = newArray;

		for ( var i = index; i < PropertyList.Count; ++i )
		{
			if ( PropertyList[i] is SerializedArrayProperty arrayProperty )
			{
				arrayProperty.Index = i + 1;
			}
		}

		PropertyList.Insert( index, prop );
		OnEntryAdded?.Invoke();
		NoteChanged( prop );
		return true;
	}
}

file class SerializedArrayProperty : SerializedProperty
{
	private readonly SerializedArray serializedArray;
	public int Index;

	public override string Name => $"{Index}";
	public override Type PropertyType => serializedArray.ValueType;
	public override SerializedObject Parent => serializedArray;
	public override bool IsValid => serializedArray.array.Length > Index && base.IsValid;

	public SerializedArrayProperty( SerializedArray serializedList, int i )
	{
		serializedArray = serializedList;
		Index = i;
	}

	public override IEnumerable<Attribute> GetAttributes()
	{
		return serializedArray?.ParentProperty?.GetAttributes() ?? Enumerable.Empty<Attribute>();
	}

	public Type ValueType { get; internal set; }


	public override T GetValue<T>( T defaultValue = default )
	{
		if ( Index < 0 || Index >= serializedArray.array.Length )
			return defaultValue;

		return ValueToType( serializedArray.array.GetValue( Index ), defaultValue );
	}

	public override void SetValue<T>( T value )
	{
		object val = value;

		if ( Translation.TryConvert( val, PropertyType, out var converted ) )
		{
			NotePreChange();
			serializedArray.array.SetValue( converted, Index );
			NoteChanged();
		}
	}

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		obj = serializedArray.PropertyToObject?.Invoke( this ) ?? null;
		return obj is not null;
	}
}
