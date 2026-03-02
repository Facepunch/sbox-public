using Sandbox.Internal;

namespace Sandbox;

/// <summary>
/// Describes a field. We use this class to wrap and return <see cref="FieldInfo">FieldInfo</see>'s that are safe to interact with.
///
/// Returned by <see cref="Internal.TypeLibrary"/> and <see cref="Sandbox.TypeDescription"/>.
/// </summary>
public sealed class FieldDescription : MemberDescription
{
	public override bool IsField => true;

	/// <inheritdoc cref="System.Reflection.FieldInfo.IsInitOnly"/>
	public bool IsInitOnly { get; private set; }

	FieldInfo FieldInfo => MemberInfo as FieldInfo;

	internal static FieldDescription Create( FieldInfo i, TypeDescription td, MemberDescription previous )
	{
		FieldDescription o = previous as FieldDescription;

		o ??= new FieldDescription( td.library );
		o.TypeDescription = td;
		o.InitField( i );

		return o;
	}

	internal FieldDescription( TypeLibrary tl ) : base( tl )
	{

	}

	private void InitField( FieldInfo x )
	{
		base.Init( x );

		FieldType = x.FieldType;
		IsStatic = x.IsStatic;
		IsInitOnly = x.IsInitOnly;
		IsPublic = x.IsPublic;
		IsFamily = x.IsFamily;

		if ( x.GetEventInfo() is { } eventInfo )
		{
			CaptureAttributes( eventInfo );
		}
	}

	/// <summary>
	/// Property type.
	/// </summary>
	public Type FieldType { get; internal set; }

	/// <summary>
	/// Get the value of this property on given object.
	/// </summary>
	public object GetValue( object obj )
	{
		return FieldInfo.GetValue( IsStatic ? null : obj );
	}

	/// <summary>
	/// Set the value of this property on given object.
	/// </summary>
	public void SetValue( object obj, object value )
	{
		if ( FieldInfo.FieldType.IsEnum && value is not null && value.GetType() != FieldInfo.FieldType )
		{
			value = Enum.ToObject( FieldInfo.FieldType, value );
		}

		// correct type
		if ( value == null || value.GetType().IsAssignableTo( FieldInfo.FieldType ) )
		{
			FieldInfo.SetValue( obj, value );
			return;
		}

		if ( value is IConvertible )
		{
			var changedValue = Convert.ChangeType( value, FieldInfo.FieldType );
			if ( changedValue is not null )
			{
				FieldInfo.SetValue( obj, changedValue );
			}
		}
	}
}
