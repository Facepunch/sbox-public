using System.Diagnostics.CodeAnalysis;
using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Movie property that references a field or property contained in another <see cref="ITrackTarget"/>.
/// For example, a property in a <see cref="Component"/>.
/// </summary>
/// <typeparam name="T">Value type stored in the property.</typeparam>
file sealed record MemberProperty<T>( ITrackTarget Parent, MemberDescription Member ) : ITrackProperty<T>
{
	public string Name => IsValid ? Member.Name : "[removed]";

	/// <summary>
	/// True if <see cref="Member"/> still exists. Can become false after a hotload that removed the member.
	/// </summary>
	public bool IsValid => Member is { MemberInfo: not null };

	/// <summary>
	/// Default behaviour is to check if the parent is active. We need a special case for properties bound to
	/// <see cref="GameObject.Enabled"/> or <see cref="Component.Enabled"/>, otherwise we'd never be able to record them
	/// being false.
	/// </summary>
	public bool IsActive => IsValid && (Parent.IsActive || Name == nameof( GameObject.Enabled ) && Parent is ITrackReference { IsBound: true });
	public bool CanWrite => IsValid && Member switch
	{
		PropertyDescription propDesc => propDesc.CanWrite,
		FieldDescription fieldDesc => !fieldDesc.IsInitOnly,
		_ => false
	};

	public T Value
	{
		// TODO: we can avoid boxing / reflection here when we're in engine code using System.Linq.Expressions

		get => IsValid && Parent.Value is { } target ? Member switch
		{
			PropertyDescription propDesc => (T)propDesc.GetValue( target ),
			FieldDescription fieldDesc => (T)fieldDesc.GetValue( target ),
			_ => throw new NotImplementedException()
		} : default!;

		set
		{
			if ( Parent.Value is not { } target ) return;
			if ( !CanWrite ) return;

			SetInternal( target, value );

			if ( Parent is ITrackProperty { TargetType.IsValueType: true } parentMember )
			{
				parentMember.Value = target;
			}
		}
	}

	private void SetInternal( object target, object? value )
	{
		if ( IsBoneTransformProperty( out var boneObject ) )
		{
			boneObject.Flags |= GameObjectFlags.ProceduralBone;
		}

		switch ( Member )
		{
			case PropertyDescription propDesc:
				propDesc.SetValue( target, value );
				return;

			case FieldDescription fieldDesc:
				fieldDesc.SetValue( target, value );
				return;

			default:
				throw new NotImplementedException();
		}
	}

	private bool IsBoneTransformProperty( [NotNullWhen( true )] out GameObject? boneObject )
	{
		boneObject = null;

		if ( Name is not nameof( GameObject.LocalPosition ) and not nameof( GameObject.LocalRotation ) and not nameof( GameObject.LocalScale ) ) return false;
		if ( Parent is not ITrackReference<GameObject> { Value: { } go } ) return false;
		if ( (go.Flags & GameObjectFlags.Bone) == 0 ) return false;

		boneObject = go;
		return true;
	}
}

[Expose]
file sealed class MemberPropertyFactory : ITrackPropertyFactory
{
	int ITrackPropertyFactory.Order => 0x4000_0000;

	IEnumerable<string> ITrackPropertyFactory.GetPropertyNames( ITrackTarget parent )
	{
		if ( TypeLibrary.GetType( parent.TargetType ) is not { } typeDesc ) return Enumerable.Empty<string>();
		if ( !CanMakeTrackFromProperties( typeDesc.TargetType ) ) return Enumerable.Empty<string>();

		return typeDesc.Members
			.Where( x => x is { IsPublic: true } and (FieldDescription or PropertyDescription) )
			.Select( x => x.Name );
	}

	private MemberDescription? GetMember( ITrackTarget parent, string name )
	{
		if ( TypeLibrary.GetType( parent.TargetType ) is not { } typeDesc ) return null;
		if ( !CanMakeTrackFromProperties( typeDesc.TargetType ) ) return null;

		return typeDesc.Members
			.Where( m => m.Name == name )
			.FirstOrDefault( CanMakeTrackFromMember );
	}

	public string GetCategoryName( ITrackTarget parent, string name ) => "Members";

	public Type? GetTargetType( ITrackTarget parent, string name )
	{
		return GetMember( parent, name ) switch
		{
			PropertyDescription propDesc => propDesc.PropertyType,
			FieldDescription fieldDesc => fieldDesc.FieldType,
			_ => null
		};
	}

	public ITrackProperty<T> CreateProperty<T>( ITrackTarget parent, string name )
	{
		return new MemberProperty<T>( parent, GetMember( parent, name )! );
	}

	// TODO: Because Type.IsPrimitive isn't allowed
	private static HashSet<Type> PrimitiveTypes { get; } = new()
	{
		typeof(bool),
		typeof(byte),
		typeof(sbyte),
		typeof(char),
		typeof(decimal),
		typeof(double),
		typeof(float),
		typeof(int),
		typeof(uint),
		typeof(long),
		typeof(ulong),
		typeof(short),
		typeof(ushort)
	};

	private static HashSet<Type> MathPrimitiveTypes { get; } = new()
	{
		typeof(Color),
		typeof(Color32),
		typeof(ColorHsv),

		typeof(Vector2),
		typeof(Vector3),
		typeof(Vector4),

		typeof(Vector2Int),
		typeof(Vector3Int),

		typeof(Angles),
		typeof(Rotation),

		typeof(Transform),
		typeof(TextRendering.Scope)
	};

	private static HashSet<Type> AccessorTypes { get; } = new()
	{
		typeof(SkinnedModelRenderer.MorphAccessor),
		typeof(SkinnedModelRenderer.ParameterAccessor),
		typeof(SkinnedModelRenderer.SequenceAccessor)
	};

	private static bool CanMakeTrackFromProperties( Type type )
	{
		if ( type.IsAssignableTo( typeof( GameObject ) ) ) return true;
		if ( type.IsAssignableTo( typeof( Component ) ) ) return true;

		if ( PrimitiveTypes.Contains( type ) ) return false;
		if ( MathPrimitiveTypes.Contains( type ) ) return type != typeof( Rotation );

		// TODO: not hard-code these

		if ( AccessorTypes.Contains( type ) ) return true;

		return false;
	}

	private static bool CanMakeTrackFromMember( MemberDescription member )
	{
		Type valueType;

		var canWrite = false;

		switch ( member )
		{
			case FieldDescription { IsPublic: true } field:
				valueType = field.FieldType;
				canWrite = !field.IsInitOnly;
				break;
			case PropertyDescription { CanRead: true, IsGetMethodPublic: true, IsIndexer: false } property:
				valueType = property.PropertyType;
				canWrite = property is { CanWrite: true, IsSetMethodPublic: true };
				break;
			default:
				return false;
		}

		if ( member.TypeDescription.TargetType.IsAssignableTo( typeof( Component ) ) )
		{
			// if ( !member.HasAttribute( typeof(PropertyAttribute) ) ) return false;
		}

		if ( !canWrite )
		{
			// Allow readonly members only if they're a reference type,
			// because we can modify its properties

			if ( valueType.IsValueType ) return false;

			// Filtering out scene object stuff to avoid the list getting cluttered

			// TODO: should we support this kind of indirection?

			if ( valueType == typeof( GameObject ) ) return false;
			if ( valueType.IsAssignableTo( typeof( Component ) ) ) return false;

			return true;
		}

		return IsValidPropertyType( valueType );
	}

	private static bool IsValidPropertyType( Type type )
	{
		if ( PrimitiveTypes.Contains( type ) ) return true;
		if ( MathPrimitiveTypes.Contains( type ) ) return true;

		if ( type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof( List<> ) )
		{
			return IsValidPropertyType( type.GetGenericArguments()[0] );
		}

		if ( TypeLibrary.GetType( type ) is null ) return false;
		if ( type.IsAssignableTo( typeof( Component ) ) ) return true;
		if ( type.IsAssignableTo( typeof( Resource ) ) ) return true;
		if ( type == typeof( GameObject ) ) return true;
		if ( type == typeof( string ) ) return true;

		// For any other type not covered above,
		// only support it if it has sub-properties we can control

		return CanMakeTrackFromProperties( type );
	}
}
