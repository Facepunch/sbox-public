using Facepunch.ActionGraphs;

namespace Sandbox;

[AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = false )]
public class CustomEmbeddedEditorAttribute : Attribute
{
	public Type TargetType { get; }
	public CustomEmbeddedEditorAttribute( Type targetType = null )
	{
		TargetType = targetType;
	}
}

[AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = false )]
public class CustomEditorAttribute : Attribute
{
	public Type TargetType { get; }
	public Type[] WithAllAttributes { get; set; }
	public bool ForMethod { get; set; }
	public string NamedEditor { get; set; }
	public bool ForInterface { get; set; }

	public CustomEditorAttribute( Type targetType = null )
	{
		TargetType = targetType;
	}

	public float GetEditorScore( SerializedProperty property )
	{
		// This is a method, and it doesn't apply to it
		if ( property.IsMethod != ForMethod )
			return 0;

		float score = 0;

		if ( ForMethod )
			score += 100;

		if ( property.PropertyType.IsInterface && ForInterface )
			score += 100;

		var t = property.PropertyType;
		if ( t is null ) return score;

		if ( Either.IsEitherType( t ) )
		{
			t = typeof( Either );
		}

		if ( Nullable.GetUnderlyingType( t ) is { } elemType )
		{
			t = elemType;
		}

		//
		// This attribute has a type set, look at that
		//
		if ( TargetType is not null && !ForMethod )
		{
			//
			// Order by derived classes so we get the most relevant editor
			//
			Type baseType = TargetType;
			while ( baseType.BaseType != null )
			{
				score += 10.0f;
				baseType = baseType.BaseType;
			}

			if ( TargetType == t ) score += 1000.0f; // directly targets this class
			else if ( t.IsGenericType && TargetType == t.GetGenericTypeDefinition() ) score += 500.0f; // generic type target
			else if ( t.IsAssignableTo( TargetType ) ) score += 100.0f;  // directly targets a base type
			else return -100; // definitely not this!
		}

		//
		// Properties can have [Editor( "NamedEditor" )]
		//
		if ( NamedEditor is not null )
		{
			if ( property.TryGetAttribute<EditorAttribute>( out var editorAttribute ) && string.Equals( editorAttribute.Value, NamedEditor ) )
			{
				score += 1000;
			}
			else
			{
				score -= 1000;
			}
		}

		//
		// Needs all of these attributes
		//
		if ( WithAllAttributes is not null )
		{
			foreach ( var a in WithAllAttributes )
			{
				if ( !property.HasAttribute( a ) )
					return -100;

				score += 1000;
			}


		}

		return score;
	}
}
