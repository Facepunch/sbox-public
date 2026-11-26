using Sandbox.Internal;

namespace Sandbox;

/// <summary>
/// Describes a property. We use this class to wrap and return <see cref="PropertyInfo">PropertyInfo</see>'s that are safe to interact with.
///
/// Returned by <see cref="Internal.TypeLibrary"/> and <see cref="Sandbox.TypeDescription"/>.
/// </summary>
public sealed class PropertyDescription : MemberDescription
{
	public override bool IsProperty => true;

	internal PropertyInfo PropertyInfo => MemberInfo as PropertyInfo;

	internal static PropertyDescription Create( PropertyInfo i, TypeDescription td, MemberDescription previous, PropertyDescription inherited )
	{
		PropertyDescription o = previous as PropertyDescription;

		o ??= new PropertyDescription( td.library );
		o.TypeDescription = td;

		if ( inherited is not null ) o.InitFrom( inherited );
		else o.InitProperty( i );

		return o;
	}

	internal PropertyDescription( TypeLibrary tl ) : base( tl )
	{

	}

	private void InitProperty( PropertyInfo x )
	{
		base.Init( x );

		CanWrite = x.CanWrite;
		CanRead = x.CanRead;
		PropertyType = x.PropertyType;

		IsGetMethodPublic = x.GetMethod?.IsPublic ?? false;
		IsSetMethodPublic = x.SetMethod?.IsPublic ?? false;

		IsSetMethodInitOnly = x.SetMethod?.ReturnParameter?.GetRequiredCustomModifiers()?.Contains( typeof( System.Runtime.CompilerServices.IsExternalInit ) ) == true;

		IsStatic = (x.GetMethod ?? x.SetMethod)?.IsStatic ?? false;
		IsPublic = IsGetMethodPublic || IsSetMethodPublic;
		IsFamily = x.GetMethod?.IsFamily ?? x.SetMethod?.IsFamily ?? false;

		IsIndexer = x.GetIndexParameters().Length > 0;
	}

	private void InitFrom( PropertyDescription inherited )
	{
		base.InitFrom( inherited );

		CanWrite = inherited.CanWrite;
		CanRead = inherited.CanRead;
		PropertyType = inherited.PropertyType;
		IsGetMethodPublic = inherited.IsGetMethodPublic;
		IsSetMethodPublic = inherited.IsSetMethodPublic;
		IsSetMethodInitOnly = inherited.IsSetMethodInitOnly;
		IsStatic = inherited.IsStatic;
		IsPublic = inherited.IsPublic;
		IsFamily = inherited.IsFamily;
		IsIndexer = inherited.IsIndexer;
	}

	// A private accessor of a base property is hidden when reflected through the derived type, so only share when both see the same accessors
	internal override bool CanBeSharedWith( MemberInfo member )
	{
		return member is PropertyInfo derived && base.CanBeSharedWith( member )
			&& (derived.GetMethod is null) == (PropertyInfo.GetMethod is null)
			&& (derived.SetMethod is null) == (PropertyInfo.SetMethod is null);
	}

	/// <summary>
	/// Whether this property can be written to.
	/// </summary>
	public bool CanWrite { get; internal set; }

	/// <summary>
	/// Whether this property can be read.
	/// </summary>
	public bool CanRead { get; internal set; }

	/// <summary>
	/// Whether the getter of this property is public.
	/// </summary>
	public bool IsGetMethodPublic { get; private set; }

	/// <summary>
	/// Whether the setter of this property is public.
	/// </summary>
	public bool IsSetMethodPublic { get; private set; }

	/// <summary>
	/// Whether the setter of this property is init only.
	/// </summary>
	internal bool IsSetMethodInitOnly { get; set; }

	/// <summary>
	/// Property type.
	/// </summary>
	public Type PropertyType { get; internal set; }

	/// <summary>
	/// True if this property has index parameters
	/// </summary>
	public bool IsIndexer { get; private set; }

	/// <summary>
	/// Get the value of this property on given object.
	/// </summary>
	public object GetValue( object obj )
	{
		return PropertyInfo.GetValue( IsStatic ? null : obj );
	}

	/// <summary>
	/// Set the value of this property on given object.
	/// </summary>
	public void SetValue( object obj, object value )
	{
		if ( PropertyInfo.SetMethod is null )
			return;

		// If we're an engine type, you can not use a non public setter
		if ( !IsDeclaredInDynamicAssembly && (!IsSetMethodPublic || IsSetMethodInitOnly) )
			return;

		if ( Translation.TryConvert( ref value, PropertyInfo.PropertyType ) )
		{
			PropertyInfo.SetValue( obj, value );
		}
	}

	/// <inheritdoc cref="SandboxSystemExtensions.CheckValidationAttributes"/>
	public bool CheckValidationAttributes( object obj, out string[] errors, string name = null )
	{
		return PropertyInfo.CheckValidationAttributes( obj, out errors, name );
	}
}
