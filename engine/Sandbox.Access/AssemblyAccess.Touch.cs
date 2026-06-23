using Microsoft.CodeAnalysis;
using Mono.Cecil;
using System;

namespace Sandbox;

internal partial class AssemblyAccess
{
	public class Access
	{
		public string Name { get; set; }
		public string Type { get; set; }
		public int Count { get; set; }
		public List<AccessControl.CodeLocation> Locations { get; set; } = new( 5 );
	}

	bool Touch( string name, string type )
	{
		bool alreadyTouched = false;

		Touched.AddOrUpdate( name,
			add => new Access { Name = name, Type = type, Count = 1, Locations = new List<AccessControl.CodeLocation> { Location } },
			( name, update ) =>
			{
				update.Count++;

				if ( update.Locations.Count() < 5 )
					update.Locations.Add( Location );

				alreadyTouched = true;

				return update;
			} );

		return alreadyTouched;
	}

	bool Touch( TypeDefinition typedef )
	{
		return Touch( AccessSignature.Type( typedef ), "type" );
	}

	bool Touch( TypeReference typeRef )
	{
		if ( typeRef == null )
			return false;

		switch ( typeRef.MetadataType )
		{
			case Mono.Cecil.MetadataType.Void:
			case Mono.Cecil.MetadataType.Single:
			case Mono.Cecil.MetadataType.Int32:
			case Mono.Cecil.MetadataType.Int16:
			case Mono.Cecil.MetadataType.Int64:
			case Mono.Cecil.MetadataType.Boolean:
			case Mono.Cecil.MetadataType.String:
				return true;
		}

		if ( typeRef.IsGenericParameter )
			return false;

		if ( typeRef is IModifierType typeMod )
			return Touch( typeMod.ModifierType ) || Touch( typeMod.ElementType );

		if ( typeRef is GenericInstanceType git )
		{
			foreach ( var param in git.GenericArguments )
			{
				Touch( param );
			}
		}

		if ( typeRef.IsArray || typeRef.IsByReference )
		{
			return Touch( typeRef.GetElementType() );
		}

		var typeDef = typeRef.Resolve();
		if ( typeDef == null )
			throw new Exception( $"TypeDefinition was null - couldn't resolve {typeRef}" );

		//
		// If this type is in a dll we've already approved, or in this assembly we're scanning
		// then just let it slide because we're already gonna have whitelisted them
		//
		if ( typeDef.Module.Assembly == Assembly )
			return true;

		if ( Global.Assemblies.Any( x => x.Value == typeDef.Module.Assembly ) )
			return true;

		return Touch( typeDef );
	}

	/// <summary>
	/// These methods show up a lot and unexplitable, easy to skip
	/// </summary>
	static string[] skipMethods = new string[]
	{
		"System.Void System.Runtime.CompilerServices.RuntimeHelpers::EnsureSufficientExecutionStack()"
	};

	bool Touch( MethodReference source )
	{
		var typedef = source.Resolve();

		if ( typedef is null )
			throw new System.Exception( $"MethodDefinition was null - couldn't resolve {source}" );

		// No need to investigate this - we'll investigate in our crawl
		if ( typedef.DeclaringType.Module.Assembly == Assembly )
			return true;

		// This is called a LOT, lets skip it when we can
		if ( typedef.DeclaringType.Module.Assembly.Name.Name == "System.Private.CoreLib" &&
			 typedef.FullName == "System.Void System.Runtime.CompilerServices.RuntimeHelpers::EnsureSufficientExecutionStack()" )
		{
			return true;
		}

		return Touch( typedef );
	}

	bool Touch( MethodDefinition typedef )
	{
		var touchName = AccessSignature.Method( typedef );

		if ( Touch( touchName, "method" ) )
			return true;

		//
		// Note - this is the definition - we can't test the generic parameters here
		// but they should be checked on usage !
		//
		Touch( typedef.ReturnType );

		foreach ( var param in typedef.Parameters )
		{
			Touch( param.ParameterType );
		}

		return false;
	}

}
