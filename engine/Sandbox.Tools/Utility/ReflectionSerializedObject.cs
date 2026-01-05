using System;
using System.Reflection;
using System.Text.Json;

namespace Sandbox.Internal;

/// <summary>
/// An implementation of SerializedObject which uses reflection to fill out properties. This is only accessible from 
/// tools by default. We (probably) shouldn't trust the client with such things, as they can access 
/// </summary>
class ReflectionSerializedObject : SerializedObject
{
	internal object TargetObject;

	public override string TypeName => TargetObject.GetType().Name;
	public override string TypeTitle => TypeName;

	public override bool IsValid
	{
		// If we're targeting an IValid just ask that,
		// if target is null we're definitely invalid,
		// otherwise use base implementation that checks our parent.

		get => TargetObject switch
		{
			IValid target => target.IsValid,
			null => false,
			_ => base.IsValid
		};
	}

	public ReflectionSerializedObject( object target )
	{
		TargetObject = target;

		BuildProperties();
	}

	void BuildProperties()
	{
		PropertyList ??= new();

		var type = TargetObject.GetType();

		var properties = type.GetProperties( BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy )
							.Where( IsPropertyAcceptable );

		foreach ( var prop in properties )
		{
			PropertyList.Add( new TypeSerializedProperty( this, prop ) );
		}
	}

	private bool IsPropertyAcceptable( PropertyInfo x )
	{
		if ( !x.CanRead ) return false;
		if ( x.GetMethod.IsStatic ) return false;

		var info = DisplayInfo.ForMember( x );

		//if ( x.GetIndexParameters().Length > 0 ) return false;
		//if ( x.PropertyType.IsByRefLike ) return false;

		return info.Browsable;
	}
}


file class TypeSerializedProperty : SerializedProperty
{
	private ReflectionSerializedObject Owner;
	private PropertyInfo Prop;
	private DisplayInfo Info;

	public override SerializedObject Parent => Owner;
	public override string DisplayName => Info.Name;
	public override string Name => Prop.Name;
	public override string Description => Info.Description;
	public override System.Type PropertyType => Prop.PropertyType;
	public override string GroupName => Info.Group;

	public TypeSerializedProperty( ReflectionSerializedObject typeSerializedObject, PropertyInfo prop )
	{
		Owner = typeSerializedObject;
		Prop = prop;
		Info = DisplayInfo.ForMember( prop );
	}

	public override void SetValue<T>( T value )
	{
		try
		{
			// correct type
			if ( value == null || value.GetType().IsAssignableTo( Prop.PropertyType ) )
			{
				NotePreChange();
				Prop.SetValue( Owner.TargetObject, value );
			}
			else if ( Prop.PropertyType.IsEnum )
			{
				NotePreChange();
				var changedValue = Enum.ToObject( Prop.PropertyType, value );

				if ( changedValue is not null )
				{
					Prop.SetValue( Owner.TargetObject, changedValue );
				}
			}
			else if ( value is IConvertible )
			{
				NotePreChange();
				var changedValue = Convert.ChangeType( value, Prop.PropertyType );

				if ( changedValue is not null )
				{
					Prop.SetValue( Owner.TargetObject, changedValue );
				}
			}
			else
			{
				return;
			}

			NoteChanged();

		}
		catch ( System.Exception e )
		{
			var l = new Logger( "ReflectionSerializedProperty" );
			l.Warning( e, $"Error setting {PropertyType} to {value} ({value?.GetType()})" );
		}
	}

	public override T GetValue<T>( T defaultValue )
	{
		var value = Prop.GetValue( Owner.TargetObject );

		if ( Prop.PropertyType.IsAssignableTo( typeof( T ) ) )
			return (T)value;

		if ( typeof( T ) == typeof( string ) )
			return (T)(object)$"{value}";

		if ( Prop.PropertyType == typeof( string ) )
		{
			return JsonSerializer.Deserialize<T>( (string)value );
		}

		var jsonElement = JsonSerializer.SerializeToElement( value );
		return jsonElement.Deserialize<T>();
	}

	/// <inheritdoc />
	public override IEnumerable<Attribute> GetAttributes()
	{
		return Prop.GetCustomAttributes() ?? base.GetAttributes();
	}
}
