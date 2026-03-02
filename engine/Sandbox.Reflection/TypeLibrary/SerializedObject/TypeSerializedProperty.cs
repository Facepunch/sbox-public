namespace Sandbox.Internal;

class TypeSerializedProperty : SerializedProperty
{
	private TypeSerializedObject typeSerializedObject;
	private PropertyDescription prop;

	public override bool IsProperty => true;

	public override SerializedObject Parent => typeSerializedObject;
	public override string DisplayName => prop.Title;
	public override string Name => prop.Name;
	public override string Description => prop.Description;
	public override Type PropertyType => prop.PropertyType;
	public override string SourceFile => prop.SourceFile;
	public override int SourceLine => prop.SourceLine;
	public override string GroupName => prop.Group;
	public override bool IsEditable => !prop.ReadOnly && prop.CanWrite;
	public override bool IsPublic => prop.IsPublic;
	public override int Order => prop.Order;

	public TypeSerializedProperty( TypeSerializedObject typeSerializedObject, PropertyDescription prop )
	{
		this.typeSerializedObject = typeSerializedObject;
		this.prop = prop;
	}

	public override void SetValue<T>( T value )
	{
		try
		{
			NotePreChange();
			prop.SetValue( typeSerializedObject.GetTargetObject(), value );
			NoteChanged();

		}
		catch ( System.Exception e )
		{
			var l = new Logger( "TypeSerializedProperty" );
			l.Warning( e, $"Error setting {PropertyType} to {value} ({value?.GetType()})" );
		}
	}

	/// <summary>
	/// When setting because a child property changed, we don't trigger NoteChanged
	/// because the expectation is that the NoteChanged from setting that property
	/// will instead propogate up, and will be more accurate.
	/// </summary>
	public override void SetValue<T>( T value, SerializedProperty source )
	{
		try
		{
			prop.SetValue( typeSerializedObject.GetTargetObject(), value );

		}
		catch ( System.Exception e )
		{
			var l = new Logger( "TypeSerializedProperty" );
			l.Warning( e, $"Error setting {PropertyType} to {value} ({value?.GetType()})" );
		}
	}

	public override T GetValue<T>( T defaultValue )
	{
		try
		{
			if ( prop is null )
				return default;

			var value = prop.GetValue( typeSerializedObject.GetTargetObject() );

			return ValueToType( value, defaultValue );
		}
		catch ( System.Exception )
		{
			return defaultValue;
		}
	}

	/// <inheritdoc />
	public override IEnumerable<Attribute> GetAttributes()
	{
		return prop?.Attributes ?? base.GetAttributes();
	}

	/// <inheritdoc/>
	public override bool TryGetAsObject( out SerializedObject obj )
	{
		obj = null;

		if ( Parent is not TypeSerializedObject szObj )
			return false;

		if ( TryGetAsContainer( out obj, szObj.TypeDescription.library ) )
			return true;

		var targetType = szObj.TypeDescription.library.GetType( PropertyType );

		if ( targetType is null )
			return false;

		var targetValue = prop.GetValue( typeSerializedObject.GetTargetObject() );

		if ( targetValue is null )
			return false;

		targetType = szObj.TypeDescription.library.GetType( targetValue.GetType() );

		if ( targetType is null )
			return false;

		obj = new TypeSerializedObject( () => prop.GetValue( typeSerializedObject.GetTargetObject() ), targetType, this );
		return obj is not null;
	}

	private bool TryGetAsContainer( out SerializedObject obj, TypeLibrary library )
	{
		obj = null;

		var so = SerializedCollection.Create( PropertyType );
		if ( so is not null )
		{
			so.ParentProperty = this;

			var targetObject = GetValue<object>( null );

			// This is very presumptuous but if we have a null list, create an empty list and modify the original object target
			// Otherwise we'll be editing nothing
			// Alternatively we could bitch and moan they haven't initialized their list..
			if ( targetObject is null )
			{
				targetObject = PropertyType.IsSZArray ?
					Array.CreateInstance( PropertyType.GetElementType(), 0 ) :
					Activator.CreateInstance( PropertyType );

				// Do not use SetValue, we don't want to propagate NoteChanged consumers
				prop.SetValue( typeSerializedObject.GetTargetObject(), targetObject );
				typeSerializedObject.ParentProperty?.SetValue( typeSerializedObject._targetObject );
			}

			so.SetTargetObject( targetObject, this );
			so.PropertyToObject = library.PropertyToObject;
			obj = so;
			return true;
		}

		return false;
	}
}
