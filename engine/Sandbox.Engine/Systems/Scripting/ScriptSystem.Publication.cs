using Sandbox.Internal;
using System.Reflection;
using System.Reflection.Emit;

namespace Sandbox;

public sealed partial class ScriptSystem
{
	private readonly HashSet<TypeLibrary> _libraries = new();
	private Assembly _assembly;
	private bool _publishingAssembly;

	/// <summary>
	/// Emits source navigation and documentation into the same CLR metadata as authored attributes.
	/// </summary>
	private static IEnumerable<CustomAttributeBuilder> DeclarationAttributes( Breen.ScriptDeclarationMetadata declaration )
	{
		yield return new CustomAttributeBuilder( typeof( SourceLocationAttribute ).GetConstructor( [typeof( string ), typeof( int )] ),
			[declaration.FilePath, declaration.Line] );

		if ( declaration.Summary is { } summary )
		{
			yield return new CustomAttributeBuilder( typeof( DescriptionAttribute ).GetConstructor( [typeof( string )] ), [summary] );
		}
	}

	/// <summary>
	/// Checks names and unsupported code-generation contracts before replacing the current assembly.
	/// </summary>
	private void ValidateTypes( IReadOnlyList<Type> types )
	{
		_libraries.Add( _context.TypeLibrary );
		if ( _context == Engine.GlobalContext.Game && TypeLibrary.Editor is not null )
		{
			_libraries.Add( TypeLibrary.Editor );
		}

		foreach ( var type in types )
		{
			if ( _resolver.IncomingAssemblies.Any( assembly => assembly.GetType( type.FullName ) is not null ) )
			{
				throw new InvalidOperationException( $"A type named '{type.FullName}' is already declared in an incoming C# assembly." );
			}

			foreach ( var library in _libraries )
			{
				var existing = library.GetType( type.FullName, exactFullName: true );
				if ( existing is not null && existing.TargetType.Assembly != _assembly )
				{
					throw new InvalidOperationException( $"A type named '{type.FullName}' is already registered." );
				}
			}

			foreach ( var member in type.GetMembers( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly ).Prepend( type ) )
			{
				foreach ( var attribute in member.GetCustomAttributesData() )
				{
					if ( attribute.AttributeType.IsDefined( typeof( CodeGeneratorAttribute ), true ) )
					{
						throw new NotSupportedException( $"[{attribute.AttributeType.Name}] requires C# code generation and is not supported on script declarations. Use a native component base for this contract." );
					}
				}
			}
		}
	}

	/// <summary>
	/// Replaces the real CLR assembly through ordinary hotload before executing startup code.
	/// TypeLibrary discovers members and attributes through its existing assembly registration path.
	/// </summary>
	private void ReplaceAssembly( Assembly previous, Assembly incoming )
	{
		foreach ( var library in _libraries )
		{
			if ( previous is not null )
			{
				library.RemoveAssembly( previous );
			}
		}

		_assembly = incoming;
		_publishingAssembly = true;
		try
		{
			_context.ReplaceScriptAssembly( previous, incoming, () =>
			{
				foreach ( var library in _libraries )
				{
					library.AddAssembly( incoming, true );
				}
			} );
		}
		finally
		{
			_publishingAssembly = false;
		}
	}
}
