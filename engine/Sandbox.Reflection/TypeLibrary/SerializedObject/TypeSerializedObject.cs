namespace Sandbox.Internal;

/// <summary>
/// An implementation of SerializedObject which uses TypeLibrary to fill out properties
/// </summary>
class TypeSerializedObject : SerializedObject
{
	static Logger log = new Logger( "TypeSerializedObject" );

	public override string TypeIcon => TypeDescription?.Icon ?? base.TypeIcon;
	public override string TypeName => TypeDescription?.Name ?? base.TypeName;
	public override string TypeTitle => TypeDescription?.Title ?? base.TypeTitle;

	public override bool IsValid
	{
		// If we're targeting an IValid just ask that,
		// if target is null we're definitely invalid,
		// otherwise use base implementation that checks our parent.

		get => _targetObject switch
		{
			IValid target => target.IsValid,
			null => false,
			_ => base.IsValid
		};
	}

	/// <summary>
	/// If the object is a value type, we call a method to get the value each time before we change/update/read it.
	/// If it's a class, we fetch it once and set FetchTarget to null.
	/// </summary>
	internal Func<object> FetchTarget;
	internal TypeDescription TypeDescription;

	public TypeSerializedObject( object target, TypeDescription typeDescription, SerializedProperty parent = null )
	{
		ArgumentNullException.ThrowIfNull( target, nameof( target ) );
		ArgumentNullException.ThrowIfNull( typeDescription, nameof( typeDescription ) );

		_targetObject = target;
		TypeDescription = typeDescription;
		ParentProperty = parent;

		BuildProperties();
	}

	public TypeSerializedObject( Func<object> fetchTarget, TypeDescription typeDescription, SerializedProperty parent = null )
	{
		FetchTarget = fetchTarget;
		var target = FetchTarget();

		// only have to dynamically fetch value types 
		if ( !typeDescription.IsValueType ) FetchTarget = null;

		ArgumentNullException.ThrowIfNull( target, nameof( target ) );
		ArgumentNullException.ThrowIfNull( typeDescription, nameof( typeDescription ) );

		_targetObject = target;
		TypeDescription = typeDescription;
		ParentProperty = parent;

		BuildProperties();
	}

	internal object _targetObject;

	/// <summary>
	/// Get the target object. If the target object is a value type, we'll
	/// call FetchTarget() - which should fetch the latest copy of it from
	/// the parent. 
	/// Note that by design FetchTarget is null if it's not a value type.
	/// </summary>
	internal ref object GetTargetObject()
	{
		if ( FetchTarget is not null )
		{
			Assert.True( TypeDescription.IsValueType );
			_targetObject = FetchTarget?.Invoke();
		}

		return ref _targetObject;
	}

	string fullName;

	void BuildProperties()
	{
		fullName = TypeDescription.TargetType.AssemblyQualifiedName;

		PropertyList ??= new();
		PropertyList.Clear();

		foreach ( var prop in TypeDescription.Properties.Where( x => !x.IsStatic ) )
		{
			PropertyList.Add( new TypeSerializedProperty( this, prop ) );
		}

		foreach ( var field in TypeDescription.Fields.Where( x => !x.IsStatic ) )
		{
			PropertyList.Add( new TypeSerializedField( this, field ) );
		}

		foreach ( var method in TypeDescription.Methods.Where( x => !x.IsStatic ) )
		{
			PropertyList.Add( new TypeSerializedMethod( this, method ) );
		}
	}

	public override void NoteChanged( SerializedProperty childProperty )
	{
		//log.Trace( $"Note Changed: [{this}].[{childProperty.Name}]" );

		if ( ParentProperty is not null )
		{
			ParentProperty.SetValue( _targetObject, childProperty );
		}

		if ( childProperty is not null )
		{
			base.NoteChanged( childProperty );
		}
	}

	public override void NotePreChange( SerializedProperty childProperty )
	{
		if ( ParentProperty is not null )
		{
			ParentProperty.NotePreChange( childProperty );
		}

		if ( childProperty is not null )
		{
			base.NotePreChange( childProperty );
		}
	}

	protected override void PrepareEnumerator()
	{
		if ( fullName != TypeDescription.TargetType.AssemblyQualifiedName )
		{
			log.Trace( $"Rebuilding - detected assembly change [{fullName}] to [{TypeDescription.TargetType.AssemblyQualifiedName}]" );
			BuildProperties();
		}
	}

	public override IEnumerable<object> Targets
	{
		get
		{
			yield return GetTargetObject();
		}
	}
}
