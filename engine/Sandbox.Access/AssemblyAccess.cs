using Microsoft.CodeAnalysis;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;

namespace Sandbox;

[SkipHotload]
public class AccessControlResult
{
	public bool Success { get; internal set; }
	public ConcurrentBag<string> Errors { get; } = new();
	public ConcurrentBag<(string Name, AccessControl.CodeLocation[] Locations)> WhitelistErrors { get; } = new();
}

/// <summary>
/// An assembly instance to verify against access control rules.
/// </summary>
[SkipHotload]
internal partial class AssemblyAccess
{
	public AccessControl Global { get; }

	public ConcurrentDictionary<string, Access> Touched = new();
	public AssemblyDefinition Assembly { get; private set; }

	public AccessControlResult Result { get; private set; }

	private byte[] bytes;

	public AssemblyAccess( AccessControl global, byte[] _bytes )
	{
		Global = global;
		Result = new AccessControlResult();
		bytes = _bytes;

		LoadAssemblyDefinition();
	}

	private void LoadAssemblyDefinition()
	{
		var ms = new MemoryStream( bytes );

		var readerParams = new ReaderParameters
		{
			ReadingMode = ReadingMode.Immediate,
			InMemory = true,
			AssemblyResolver = Global,
			ReadSymbols = true,
		};

		try
		{
			Assembly = AssemblyDefinition.ReadAssembly( ms, readerParams );
		}
		catch ( Mono.Cecil.Cil.SymbolsNotFoundException )
		{
			ms.Seek( 0, SeekOrigin.Begin );

			// Fallback: may not have symbols
			readerParams.ReadSymbols = false;
			Assembly = AssemblyDefinition.ReadAssembly( ms, readerParams );
		}

		Global.ForgetOlderAssemblyDefinitions( Assembly.Name );

		// Keep this around, we might need it again if we load an assembly that depends on it
		Global.Assemblies[Assembly.Name] = Assembly;
	}

	internal void Verify( out TrustedBinaryStream outStream )
	{
		outStream = null;

		try
		{
			//
			// All assemblies are compiled with the name package.*
			// Shouldn't let them masquerade as anything because they could say they're Sandbox.Test and have access to all InternalsVisibleTo
			//
			if ( !Assembly.Name.Name.StartsWith( "package." ) )
			{
				Result.Errors.Add( "Assembly name is invalid" );
				Result.Success = false;
				return;
			}

			if ( Global.CheckSafeAssembly( Assembly.Name.Name, bytes ) )
			{
				outStream = TrustedBinaryStream.CreateInternal( bytes );
				Result.Success = true;
				return;
			}

			// do the heavy work of touching everything
			InitTouches( bytes );

			if ( !CheckPassesRules() )
			{
				Result.Success = false;
				return;
			}

			if ( Result.Errors.Count > 0 )
			{
				Result.Success = false;
				return;
			}

			outStream = TrustedBinaryStream.CreateInternal( bytes );
			Result.Success = true;
		}
		catch ( System.Exception e )
		{
			Result.Errors.Add( $"{e.Message} ({e.GetType()})\n{e.StackTrace}" );
			Result.Success = false;
		}
	}

	/// <summary>
	/// Clear the touches and fill them with everything this dll touches
	/// </summary>
	private void InitTouches( byte[] dll )
	{
		Touched.Clear();

		// Test assembly attributes (these are defined differently to module attributes)
		TestAttributes( Assembly.CustomAttributes );

		foreach ( var module in Assembly.Modules )
		{
			Location = new AccessControl.CodeLocation( module.FileName );

			TestModule( module );
		}

		RemoveLocalTouches();
	}

	private void TestModule( ModuleDefinition module )
	{
		if ( module.HasExportedTypes )
		{
			Touch( "System.Private.CoreLib/System.Runtime.CompilerServices.TypeForwardedToAttribute", "attribute" );
		}

		TestAttributes( module.CustomAttributes );

#if true
		{
			Parallel.ForEach( EnumerateTypes( module.Types ), new ParallelOptions { MaxDegreeOfParallelism = 16 },
				TestTypeInThread );
		}
#else
		{
			foreach ( var type in module.Types )
			{
				TestType( type );
			}
		}
#endif
	}

	IEnumerable<TypeDefinition> EnumerateTypes( Mono.Collections.Generic.Collection<TypeDefinition> types )
	{
		foreach ( var t in types )
		{
			yield return t;

			if ( t.HasNestedTypes )
			{
				foreach ( var nt in EnumerateTypes( t.NestedTypes ) )
				{
					yield return nt;
				}
			}
		}
	}

	// Explicit layouts can be exploited but have legitimate usages in generated code.
	// Can't just check for CompilerGeneratedAttribute, so only allow specific stuff.
	private bool TypeAllowedExplicitLayout( TypeDefinition type )
	{
		// Allow static array initializers
		if ( type.DeclaringType?.Name == "<PrivateImplementationDetails>" &&
			 type.Name.StartsWith( "__StaticArrayInitTypeSize=" ) )
			return true;

		return false;
	}

	void TestTypeInThread( TypeDefinition type )
	{
		try
		{
			TestType( type );
		}
		catch ( System.Exception e )
		{
			Result.Errors.Add( $"{e.Message}\n{e.StackTrace}" );
		}
	}

	private void TestType( TypeDefinition type )
	{
		Location = new AccessControl.CodeLocation( type.ToString() );
		//Console.WriteLine( $"Touching {$"{type.Module.Assembly.Name.Name}/{type.FullName}"}" );

		if ( (type.Attributes & Mono.Cecil.TypeAttributes.ExplicitLayout) != 0 && !TypeAllowedExplicitLayout( type ) )
		{
			Touch( $"System.Private.CoreLib/System.Runtime.InteropServices.StructLayout", "attribute" );
		}

		TestAttributes( type.CustomAttributes );

		if ( type.IsArray )
		{
			TestType( type.GetElementType().Resolve() );
		}

		//  log.Info( $"Type: {type}" );

		foreach ( var member in type.Fields )
		{
			TestField( member );
			TestAttributes( member.CustomAttributes );
		}

		foreach ( var member in type.Properties )
		{
			TestProperty( member );
			TestAttributes( member.CustomAttributes );
		}

		foreach ( var inter in type.Interfaces )
		{
			Touch( inter.InterfaceType );
		}

		if ( type.BaseType is not null )
		{
			TestBaseType( type, type.BaseType );
		}

		Parallel.ForEach( type.Methods,
			new ParallelOptions { MaxDegreeOfParallelism = 8, TaskScheduler = TaskScheduler.Default }, member =>
			{
				TestMethod( member );
				TestAttributes( member.CustomAttributes );
			} );
	}

	private void TestBaseType( TypeDefinition parent, TypeReference baseType )
	{
		var typeInformation = baseType.Resolve();
		var baseTypeName = $"{typeInformation.Module.Assembly.Name.Name}{typeInformation.FullName}";

		// this is a struct - we can trust it
		if ( baseTypeName == "System.Private.CoreLibSystem.ValueType" ) return;

		// this is an enum - we can trust it
		if ( baseTypeName == "System.Private.CoreLibSystem.Enum" ) return;

		// this is basically no baseclass, or a static class
		if ( baseTypeName == "System.Private.CoreLibSystem.Object" ) return;

		var constructors = typeInformation.GetConstructors().ToArray();
		var parentConstructors = parent.GetConstructors().ToArray();

		//
		// no parent constructors - then what are they doing? Have they modified the assembly?
		// in this case check that every child constructor is allowed
		//
		if ( parentConstructors.Length == 0 )
		{
			Console.WriteLine( $"Type {parent} had no constructors {baseTypeName}" );
			TestType( typeInformation );

			foreach ( var c in constructors )
			{
				Touch( c );
			}

			return;
		}

		//
		// The assumption here is that their class constructor is going to call the base
		// class constructor. If that's allowed then it's going to get picked up in the
		// normal method checks.
		//
		// Could it be a problem if the base class has no constructor? Is that legal?
		// Is it possible to edit the assembly so the constructor of this class never calls
		// the base class and triggers this stuff?
		// If the base class has a constructor, should we be making sure that at least one
		// of them is being called from our constructors?
		//
	}

	private void TestProperty( PropertyDefinition member )
	{
		if ( member.PropertyType.IsGenericParameter )
			return;

		Touch( member.PropertyType );
	}

	private void TestAttributes( Mono.Collections.Generic.Collection<CustomAttribute> attributes )
	{
		if ( attributes == null )
			return;

		foreach ( var attr in attributes )
		{
			TestAttribute( attr );
		}
	}

	private void TestField( FieldDefinition member )
	{
		if ( member.ContainsGenericParameter )
			return;

		if ( member.HasLayoutInfo )
		{
			Touch( $"System.Private.CoreLib/System.Runtime.InteropServices.FieldOffset", "attribute" );
		}

		if ( member.HasMarshalInfo )
		{
			Touch( $"System.Private.CoreLib/System.Runtime.InteropServices.MarshalAs", "attribute" );
		}

		Touch( member.FieldType );
	}

	private void TestMethod( MethodDefinition member )
	{
		if ( member.IsNative )
			Touch( $"System.Private.CoreLib/System.Runtime.InteropServices.DllImportAttribute", "attribute" );
		if ( member.IsPInvokeImpl )
			Touch( $"System.Private.CoreLib/System.Runtime.InteropServices.DllImportAttribute", "attribute" );
		if ( member.IsUnmanagedExport )
			Touch( $"System.Private.CoreLib/System.Runtime.InteropServices.DllImportAttribute", "attribute" );

		if ( member.DebugInformation.HasSequencePoints &&
			 member.DebugInformation.SequencePoints.FirstOrDefault( x => !x.IsHidden ) is { } sequencePoint )
			UpdateLocation( sequencePoint );
		else
			Location = new AccessControl.CodeLocation( member.FullName );

		Touch( member );

		if ( ShouldSkipExploration( member.Module.Assembly ) )
			return;

		Touch( member.MethodReturnType.ReturnType );

		if ( member.HasBody )
		{
			foreach ( var variable in member.Body.Variables )
			{
				if ( variable.IsPinned )
					Touch( $"System.Private.CoreLib/System.Security.UnverifiableCodeAttribute", "attribute" );
			}

			foreach ( var instruction in member.Body.Instructions )
			{
				var l = Location;
				UpdateLocation( member.DebugInformation.GetSequencePoint( instruction ) );
				TestInstruction( member, instruction );
				Location = l;
			}
		}

		foreach ( var parameter in member.Parameters )
		{
			foreach ( var attribute in parameter.CustomAttributes )
			{
				Touch( attribute.AttributeType );
			}
		}
	}

	private void TestAttribute( CustomAttribute attr )
	{
		Touch( attr.AttributeType );

		foreach ( var arg in attr.ConstructorArguments )
		{
			Touch( arg.Type );
		}
	}

	void TestOpCode( OpCode opcode )
	{
		// todo - whitelist instead of blacklist?

		if ( opcode == OpCodes.Cpobj )
		{
			Touch( $"System.Private.CoreLib/System.Runtime.Opcode.Cpobj", "class" );
		}

		//Touch( $"System.Private.CoreLib/System.Runtime.Opcode.{opcode}", "class" );
	}

	private void TestInstruction( MethodDefinition method, Instruction instruction )
	{
		TestOpCode( instruction.OpCode );

		// Skipping these just so the log at the bottom of this function isn't spammy
		if ( instruction.Operand is string ) return;
		if ( instruction.Operand is float ) return;
		if ( instruction.Operand is double ) return;
		if ( instruction.Operand is int ) return;
		if ( instruction.Operand is sbyte ) return;

		if ( instruction.Operand is Instruction[] instructions )
		{
			foreach ( var i in instructions )
			{
				TestInstruction( method, i );
			}

			return;
		}

		if ( instruction.Operand is MethodReference methodref )
		{
			if ( methodref.DeclaringType.IsArray )
			{
				if ( methodref.DeclaringType.GetElementType() is TypeReference
					{
						IsGenericParameter: true
					} elementType )
				{
					// We can't Resolve() a generic this way
					// Pass the element type to Touch(TypeReference) which should handle these gracefully
					Touch( elementType );
				}
				else
				{
					Touch( methodref.DeclaringType.Resolve() );
				}
			}
			else
			{
				Touch( methodref );
			}


			Touch( methodref.ReturnType );

			if ( methodref.DeclaringType is GenericInstanceType git )
			{
				foreach ( var param in git.GenericArguments )
				{
					Touch( param );
				}
			}

			if ( instruction.Operand is GenericInstanceMethod gim )
			{
				foreach ( var m in gim.GenericArguments )
				{
					Touch( m );
				}
			}

			foreach ( var param in methodref.GenericParameters )
			{
				Touch( param );
			}

			foreach ( var param in methodref.Parameters )
			{
				Touch( param.ParameterType );
			}

			//
			// The compiler catches base.Finalize() or this.Finalize() and errors as CS0245
			// However you can get a reference and invoke that action just fine, which can be abused and should be invalid.
			// We check against ldftn/ldvirtftn only as call is valid runtime usage from the compiler
			//
			if ( methodref.Name == Microsoft.CodeAnalysis.WellKnownMemberNames.DestructorName &&
				 (instruction.OpCode.Code == Code.Ldftn || instruction.OpCode.Code == Code.Ldvirtftn) )
			{
				// Dummy invalid method
				Touch( $"System.Private.CoreLib/System.InvalidFinalizeMethodReference", "method" );
			}

			return;
		}

		if ( instruction.Operand is VariableDefinition vardef )
		{
			Touch( vardef.VariableType );
			return;
		}

		if ( instruction.Operand is FieldDefinition fielddef )
		{
			Touch( fielddef.FieldType );
			return;
		}

		if ( instruction.Operand is TypeReference typeRef )
		{
			Touch( typeRef );
			return;
		}

		if ( instruction.Operand is FieldReference fieldRef )
		{
			Touch( fieldRef.FieldType );
			return;
		}

		if ( instruction.Operand is ParameterDefinition paramDef )
		{
			Touch( paramDef.ParameterType );
			return;
		}

		if ( instruction.Operand is Instruction instrct && instrct != instruction )
		{
			TestInstruction( method, instrct );
			return;
		}

		if ( instruction.Operand != null )
		{
			//log.Trace( $"Unhandled Instruction: {instruction.Operand.GetType()}" );
		}
	}


	private bool ShouldSkipExploration( AssemblyDefinition candidate )
	{
		if ( Assembly == candidate ) return false;

		// Everything else is either a dependent addon (which we've already checked)
		// Or a system/game dll (which we don't need to explore the contents of)

		// If we let them use their own dlls we'll need to check those too

		return true;
	}

	/// <summary>
	/// Remove touches that are inside addon depends or this dll.
	/// </summary>
	void RemoveLocalTouches()
	{
		var whitelist = new List<string>();
		whitelist.AddRange( Global.SafeAssemblies.Select( x => $"{x.Key}/" ) );
		whitelist.Add( $"{Assembly.Name.Name}/" );

		Parallel.ForEach( Touched.Keys, ( key ) =>
		{
			//
			// Don't bother looking at them if they're in our list of approved
			//
			if ( whitelist.Any( x => key.StartsWith( x ) ) )
			{
				Touched.Remove( key, out var _ );
				return;
			}
		} );
	}
}
