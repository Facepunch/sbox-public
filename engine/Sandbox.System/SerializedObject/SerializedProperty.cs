using Facepunch.ActionGraphs;
using Sandbox.Internal;
using static Sandbox.SerializedObject;

namespace Sandbox;

public abstract class SerializedProperty : IValid
{
	public virtual SerializedObject Parent { get; }

	public virtual bool IsProperty => false;
	public virtual bool IsField => false;
	public virtual bool IsMethod => false;
	public virtual string Name { get; }
	public virtual string DisplayName { get; }
	public virtual string Description { get; }
	public virtual string GroupName { get; }
	public virtual int Order { get; }
	public virtual bool IsEditable { get; } = true;
	public virtual bool IsPublic { get; } = true;
	public virtual Type PropertyType { get; }

	/// <inheritdoc cref="IValid.IsValid"/>
	public virtual bool IsValid => Parent?.IsValid ?? true;

	/// <summary>
	/// The source filename, if available
	/// </summary>
	public virtual string SourceFile { get; }

	/// <summary>
	/// The line in the source file, if available
	/// </summary>
	public virtual int SourceLine { get; }

	/// <summary>
	/// Returns true if the current set value differs from the actual value
	/// </summary>
	public virtual bool HasChanges { get; }

	/// <summary>
	/// Called when the property value is about to change.
	/// </summary>
	public PropertyPreChangeDelegate OnPreChange { get; set; }

	/// <summary>
	/// Called when the property value has changed.
	/// </summary>
	public PropertyChangedDelegate OnChanged { get; set; }

	/// <summary>
	/// Called when the property is about to be edited (eg. in a ControlWidget).
	/// </summary>
	public PropertyStartEditDelegate OnStartEdit { get; set; }

	/// <summary>
	/// Called when the property has finished being edited (eg. in a ControlWidget).
	/// </summary>
	public PropertyFinishEditDelegate OnFinishEdit { get; set; }

	public SerializedProperty()
	{
		_as.Property = this;
	}

	//
	// Accessors
	//
	public abstract void SetValue<T>( T value );
	public virtual void SetValue<T>( T value, SerializedProperty source ) => SetValue( value );
	public abstract T GetValue<T>( T defaultValue = default );

	/// <summary>
	/// Get the default value of a specific property type.
	/// </summary>
	/// <returns></returns>
	public object GetDefault()
	{
		// DefaultValue codegen
		if ( TryGetAttribute<DefaultValueAttribute>( out var defaultValue ) )
		{
			return defaultValue.Value;
		}

		var type = PropertyType;

		if ( IsNullable )
		{
			type = NullableType;
		}

		if ( type == typeof( Color ) ) return Color.White;
		if ( type == typeof( Color32 ) ) return Color32.White;
		if ( type == typeof( ColorHsv ) ) return new ColorHsv( 0, 0, 1 );

		if ( type.IsValueType )
		{
			return Activator.CreateInstance( type );
		}

		return null;
	}

	//
	// Property Access
	//

	/// <summary>
	/// Return true if the property has this attribute
	/// </summary>
	public bool HasAttribute<T>() where T : Attribute
	{
		return GetAttributes<T>().Any();
	}

	/// <summary>
	/// Return true if the property has this attribute
	/// </summary>
	public bool HasAttribute( Type t )
	{
		return GetAttributes( t ).Any();
	}

	/// <summary>
	/// Try to get this attribute from the property. Return false on fail.
	/// </summary>
	public bool TryGetAttribute<T>( out T attribute ) where T : Attribute
	{
		attribute = GetAttributes<T>().FirstOrDefault();
		return attribute != null;
	}

	/// <summary>
	/// Get all of these attributes from the property.
	/// </summary>
	public IEnumerable<T> GetAttributes<T>() where T : Attribute
	{
		return GetAttributes().OfType<T>();
	}

	/// <summary>
	/// Get all of these attributes from the property.
	/// </summary>
	public IEnumerable<Attribute> GetAttributes( Type t )
	{
		return GetAttributes().Where( t.IsInstanceOfType );
	}

	/// <summary>
	/// Get all attributes from the property.
	/// </summary>
	public virtual IEnumerable<Attribute> GetAttributes()
	{
		return Enumerable.Empty<Attribute>();
	}

	/// <summary>
	/// Try to convert this property into a serialized object for further editing and exploration
	/// </summary>
	/// <param name="obj"></param>
	/// <returns></returns>
	public virtual bool TryGetAsObject( out SerializedObject obj )
	{
		obj = default;
		return false;
	}


	AsAccessor _as;

	public virtual ref AsAccessor As => ref _as;

	public struct AsAccessor
	{
		internal SerializedProperty Property;

		public string String
		{
			get => Property.GetValue<string>();
			set => Property.SetValue<string>( value );
		}

		public Vector2 Vector2
		{
			get => Property.GetValue<Vector2>();
			set => Property.SetValue<Vector2>( value );
		}

		public Vector3 Vector3
		{
			get => Property.GetValue<Vector3>();
			set => Property.SetValue<Vector3>( value );
		}

		public Rotation Rotation
		{
			get => Property.GetValue<Rotation>();
			set => Property.SetValue<Rotation>( value );
		}

		public Angles Angles
		{
			get => Property.GetValue<Angles>();
			set => Property.SetValue<Angles>( value );
		}

		public float Float
		{
			get => Property.GetValue<float>();
			set => Property.SetValue<float>( value );
		}

		public double Double
		{
			get => Property.GetValue<double>();
			set => Property.SetValue<double>( value );
		}

		public int Int
		{
			get => Property.GetValue<int>();
			set => Property.SetValue<int>( value );
		}

		public long Long
		{
			get => Property.GetValue<long>();
			set => Property.SetValue<long>( value );
		}

		public bool Bool
		{
			get => Property.GetValue<bool>();
			set => Property.SetValue<bool>( value );
		}
	}

	/// <summary>
	/// True if this holds multiple values. That might all be the same.
	/// </summary>
	public virtual bool IsMultipleValues => false;

	/// <summary>
	/// True if this holds multiple values, and they're all different.
	/// </summary>
	public virtual bool IsMultipleDifferentValues => false;

	/// <summary>
	/// Get all properties if this holds multiple values
	/// </summary>
	public virtual IEnumerable<SerializedProperty> MultipleProperties
	{
		get
		{
			yield return this;
		}
	}

	/// <summary>
	/// Our value has changed, maybe our parent would like to know
	/// </summary>
	protected virtual void NoteChanged()
	{
		if ( OnChanged is not null )
		{
			OnChanged( this );
		}
		if ( Parent is not null )
		{
			Parent.NoteChanged( this );
		}
	}

	internal virtual void NoteChanged( SerializedProperty childProperty )
	{
		if ( OnChanged is not null )
		{
			OnChanged( childProperty );
		}
		if ( Parent is not null )
		{
			Parent.NoteChanged( childProperty );
		}
	}

	protected virtual void NotePreChange()
	{
		if ( OnPreChange is not null )
		{
			OnPreChange( this );
		}
		if ( Parent is not null )
		{
			Parent.NotePreChange( this );
		}
	}

	internal virtual void NotePreChange( SerializedProperty childProperty )
	{
		if ( OnPreChange is not null )
		{
			OnPreChange( childProperty );
		}
		if ( Parent is not null )
		{
			Parent.NotePreChange( childProperty );
		}
	}

	protected virtual void NoteStartEdit()
	{
		if ( OnStartEdit is not null )
		{
			OnStartEdit( this );
		}
		if ( Parent is not null )
		{
			Parent.NoteStartEdit( this );
		}
	}

	internal virtual void NoteStartEdit( SerializedProperty childProperty )
	{
		if ( OnStartEdit is not null )
		{
			OnStartEdit( childProperty );
		}
		if ( Parent is not null )
		{
			Parent.NoteStartEdit( childProperty );
		}
	}

	protected virtual void NoteFinishEdit()
	{
		if ( OnFinishEdit is not null )
		{
			OnFinishEdit( this );
		}
		if ( Parent is not null )
		{
			Parent.NoteFinishEdit( this );
		}
	}

	internal virtual void NoteFinishEdit( SerializedProperty childProperty )
	{
		if ( OnFinishEdit is not null )
		{
			OnFinishEdit( childProperty );
		}
		if ( Parent is not null )
		{
			Parent.NoteFinishEdit( childProperty );
		}
	}

	/// <summary>
	/// Convert an object value to a T type 
	/// </summary>
	protected T ValueToType<T>( object value, T defaultValue = default )
	{
		try
		{
			if ( value is null )
				return defaultValue;

			if ( value.GetType().IsAssignableTo( typeof( T ) ) )
				return (T)value;

			if ( typeof( T ) == typeof( string ) )
				return (T)(object)$"{value}";

			if ( value.GetType() == typeof( string ) )
			{
				return JsonSerializer.Deserialize<T>( (string)value );
			}

			// Convert.ChangeType doesn't support long to enum
			if ( typeof( T ).IsEnum && value is IConvertible )
			{
				try
				{
					return (T)Enum.ToObject( typeof( T ), Convert.ToInt64( value ) );
				}
				catch
				{
					return defaultValue;
				}
			}

			var converted = Convert.ChangeType( value, typeof( T ) );
			if ( converted is not null )
				return (T)converted;

			var jsonElement = JsonSerializer.SerializeToElement( value );
			return jsonElement.Deserialize<T>();
		}
		catch ( System.Exception )
		{
			return defaultValue;
		}
	}

	/// <summary>
	/// If this entry is a dictionary, we can get the key for it here
	/// </summary>
	public virtual SerializedProperty GetKey() => null;

	//	Func<SerializedObject, bool> _shouldShowCache;

	/// <summary>
	/// Returns true if this property should be shown in the inspector
	/// </summary>
	public bool ShouldShow()
	{
		if ( HasAttribute<HideAttribute>() ) return false;
		if ( Parent is null ) return true;

		var conditionals = GetAttributes<InspectorVisibilityAttribute>();
		if ( !conditionals.Any() ) return true;

		return !conditionals.All( x => x.TestCondition( Parent ) );
	}

	/// <summary>
	/// Return true if this is a nullable value type
	/// </summary>
	public bool IsNullable
	{
		get
		{
			return Nullable.GetUnderlyingType( PropertyType ) is not null;
		}
	}

	/// <summary>
	/// If this is a nullable type, this will return the nullable target type
	/// </summary>
	public Type NullableType
	{
		get
		{
			return Nullable.GetUnderlyingType( PropertyType );
		}
	}

	/// <summary>
	/// True if the value is null
	/// </summary>
	public bool IsNull
	{
		get
		{
			return GetValue<object>() is null;
		}
	}

	/// <summary>
	/// If this is a nullable type, you can use this to toggle between it being null or the default value type
	/// </summary>
	public void SetNullState( bool isnull )
	{
		if ( !IsNullable )
			return;

		if ( IsNull == isnull )
			return;

		if ( isnull )
		{
			SetValue<object>( null );
		}
		else
		{
			SetValue( GetDefault() );
		}
	}

	/// <summary>
	/// If is method
	/// </summary>
	public virtual void Invoke()
	{
		// nothing
	}

	/// <summary>
	/// Allows easily creating SerializedProperty classes that wrap other properties.
	/// </summary>
	public abstract class Proxy : SerializedProperty
	{
		protected abstract SerializedProperty ProxyTarget { get; }

		public override SerializedObject Parent => ProxyTarget.Parent;
		public override bool IsProperty => ProxyTarget.IsProperty;
		public override bool IsField => ProxyTarget.IsField;
		public override bool IsMethod => ProxyTarget.IsMethod;
		public override string Name => ProxyTarget.Name;
		public override string DisplayName => ProxyTarget.DisplayName;
		public override string Description => ProxyTarget.Description;
		public override string GroupName => ProxyTarget.GroupName;
		public override int Order => ProxyTarget.Order;
		public override bool IsEditable => ProxyTarget.IsEditable;
		public override bool IsPublic => ProxyTarget.IsPublic;
		public override Type PropertyType => ProxyTarget.PropertyType;
		public override string SourceFile => ProxyTarget.SourceFile;
		public override int SourceLine => ProxyTarget.SourceLine;
		public override bool HasChanges => ProxyTarget.HasChanges;

		public override bool IsValid => ProxyTarget.IsValid();

		public override ref AsAccessor As => ref base.As;

		public override bool TryGetAsObject( out SerializedObject obj ) => ProxyTarget.TryGetAsObject( out obj );
		public override T GetValue<T>( T defaultValue = default ) => ProxyTarget.GetValue( defaultValue );
		public override void SetValue<T>( T value ) => ProxyTarget.SetValue( value );
		public override IEnumerable<Attribute> GetAttributes() => ProxyTarget.GetAttributes();
	}

	/// <summary>
	/// Create a serialized property that uses a getter and setter
	/// </summary>
	[Obsolete( "Best use TypeLibrary.CreateProperty" )]
	public static SerializedProperty Create<T>( string title, Func<T> get, Action<T> set, Attribute[] attributes = null )
	{
		return new ActionBasedSerializedProperty<T>( title, title, "", get, set, attributes, null );
	}
}

/// <summary>
/// Hide a property if a condition matches.
/// </summary>
public abstract class InspectorVisibilityAttribute : System.Attribute
{
	public abstract bool TestCondition( SerializedObject so );
}

internal class ActionBasedSerializedProperty<T> : SerializedProperty
{
	public Func<SerializedProperty, SerializedObject> PropertyToObject;

	string _name;
	string _title;
	string _description;
	string _groupName;
	string _sourceFile;
	int _sourceLine;
	Func<T> _get;
	Action<T> _set;
	List<Attribute> _attributes;
	SerializedObject _parent;

	public ActionBasedSerializedProperty( string name, string title, string description, Func<T> get, Action<T> set, Attribute[] attributes, SerializedObject parent )
	{
		_name = name;
		_title = title;
		_description = description;
		_get = get;
		_set = set;
		_attributes = new List<Attribute>( attributes ?? Array.Empty<Attribute>() );
		_parent = parent;

		_groupName = _attributes.OfType<IGroupAttribute>().FirstOrDefault()?.Value ?? _groupName;
		_groupName = _attributes.OfType<ICategoryProvider>().FirstOrDefault()?.Value ?? _groupName;
		_description = _attributes.OfType<IDescriptionAttribute>().FirstOrDefault()?.Value ?? _description;
		_title = _attributes.OfType<ITitleProvider>().FirstOrDefault()?.Value ?? _title;
		_sourceFile = _attributes.OfType<ISourcePathProvider>().FirstOrDefault()?.Path ?? _sourceFile;
		_sourceLine = _attributes.OfType<ISourceLineProvider>().FirstOrDefault()?.Line ?? _sourceLine;
		// todo - class location, readonly, order?
	}

	public override SerializedObject Parent => _parent;
	public override bool IsMethod => false;
	public override string Name => _name;
	public override string DisplayName => _title;
	public override string Description => _description;
	public override string GroupName => _groupName;
	public override bool IsEditable => true;
	public override int Order => 0;
	public override Type PropertyType => typeof( T );
	public override string SourceFile => _sourceFile;
	public override int SourceLine => _sourceLine;
	public override bool HasChanges => false;

	public override ref AsAccessor As => ref base.As;

	public override U GetValue<U>( U defaultValue = default ) => ValueToType<U>( _get() );
	public override void SetValue<U>( U value ) => _set( ValueToType<T>( value ) );
	public override IEnumerable<Attribute> GetAttributes() => _attributes;

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		obj = PropertyToObject?.Invoke( this ) ?? null;
		return obj is not null;
	}
}
