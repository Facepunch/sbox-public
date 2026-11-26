using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using FlowControl = Mono.Cecil.Cil.FlowControl;
using StackBehaviour = Mono.Cecil.Cil.StackBehaviour;
#if NET_3_5 || NET_4_5
using Mono.Cecil.Rocks;
#endif

namespace Sandbox
{
	partial class Hotload
	{
		private delegate object DefaultDelegate();

		private readonly Dictionary<Type, Dictionary<FieldInfo, DefaultDelegate>> DefaultDelegates
			= new Dictionary<Type, Dictionary<FieldInfo, DefaultDelegate>>();

		private readonly Dictionary<Assembly, AssemblyDefinition> LoadedDefinitions = new();

		private void CloseAssemblyImages()
		{
			foreach ( var definition in LoadedDefinitions.Values )
			{
				definition.Dispose();
			}

			LoadedDefinitions.Clear();
		}

		private void ClearFieldDefaults()
		{
			DefaultDelegates.Clear();
		}

		private AssemblyDefinition GetAssemblyDefinition( Assembly asm )
		{
			if ( AssemblyResolver == null )
			{
				Log( HotloadEntryType.Warning, $"No assembly resolver provided" );
				return null;
			}

			if ( LoadedDefinitions.TryGetValue( asm, out var loaded ) ) return loaded;

			if ( AssemblyResolver is IAssemblyImageResolver images && images.OpenAssemblyImage( asm ) is { } exact )
			{
				LoadedDefinitions.Add( asm, exact );
				return exact;
			}

			var name = asm.GetName();
			var asmDef = AssemblyResolver.Resolve( new AssemblyNameReference( name.Name, name.Version ) );

			if ( asmDef == null )
			{
				Log( HotloadEntryType.Error, $"Unable to resolve assembly definition ({asm.FullName})" );
			}
			else if ( asmDef.Name.Version.CompareTo( name.Version ) < 0 )
			{
				Log( HotloadEntryType.Error, $"Resolved assembly definition older than requested (expected {asm.FullName}, resolved {asmDef.FullName})" );
			}

			return asmDef;
		}

		private TypeDefinition GetTypeDefinition( Type type )
		{
			if ( type.IsConstructedGenericType )
			{
				type = type.GetGenericTypeDefinition();
			}

			TypeDefinition typeDef;

			if ( type.IsNested )
			{
				var declTypeDef = GetTypeDefinition( type.DeclaringType );

				if ( declTypeDef == null )
				{
					return null;
				}

				typeDef = declTypeDef.NestedTypes.Single( x => x.Name == type.Name );
			}
			else
			{
				var asmDef = GetAssemblyDefinition( type.Assembly );

				if ( asmDef == null )
				{
					return null;
				}

				typeDef = asmDef.MainModule.GetType( type.FullName );
			}

			if ( typeDef == null )
			{
				Log( HotloadEntryType.Error, $"Unable to resolve type definition", type );
			}

			return typeDef;
		}

		internal static bool IsMatchingType( TypeReference typeRef, Type type )
		{
			if ( typeRef == null && type == null )
			{
				return true;
			}

			if ( typeRef == null || type == null )
			{
				return false;
			}

			if ( typeRef is RequiredModifierType reqModType )
			{
				typeRef = reqModType.ElementType;
			}

			if ( type.IsArray )
			{
				if ( typeRef is not ArrayType arrayType )
				{
					return false;
				}

				if ( type.GetArrayRank() != arrayType.Rank )
				{
					return false;
				}

				return IsMatchingType( arrayType.ElementType, type.GetElementType() );
			}

			if ( typeRef.IsArray )
			{
				return false;
			}

			if ( type.IsByRef )
			{
				if ( typeRef is not ByReferenceType byRefType )
				{
					return false;
				}

				return IsMatchingType( byRefType.ElementType, type.GetElementType() );
			}

			if ( typeRef.IsByReference )
			{
				return false;
			}

			if ( type.IsGenericParameter )
			{
				if ( typeRef is not GenericParameter genericParam )
				{
					return false;
				}

				if ( genericParam.Position != type.GenericParameterPosition )
				{
					return false;
				}

				// TODO: check constraints?

				if ( type.DeclaringMethod != null )
				{
					if ( type.DeclaringMethod.Name != genericParam.DeclaringMethod?.Name )
					{
						return false;
					}

					if ( genericParam.DeclaringMethod.DeclaringType.IsGenericInstance &&
						 (type.DeclaringMethod.DeclaringType?.IsGenericTypeDefinition ?? false) )
					{
						return IsMatchingType( genericParam.DeclaringMethod.DeclaringType.GetElementType(), type.DeclaringMethod.DeclaringType );
					}

					return IsMatchingType( genericParam.DeclaringMethod.DeclaringType, type.DeclaringMethod.DeclaringType );
				}

				if ( type.DeclaringType != null )
				{
					return IsMatchingType( genericParam.DeclaringType, type.DeclaringType );
				}

				return false;
			}

			if ( type.Name != typeRef.Name )
			{
				return false;
			}

			if ( type.DeclaringType != null )
			{
				if ( !IsMatchingType( typeRef.DeclaringType, type.DeclaringType ) )
				{
					return false;
				}
			}
			else if ( typeRef.DeclaringType != null )
			{
				return false;
			}
			else if ( (type.Namespace ?? string.Empty) != (typeRef.Namespace ?? string.Empty) )
			{
				return false;
			}

			if ( type.IsConstructedGenericType )
			{
				if ( typeRef is not GenericInstanceType genericInstanceType )
				{
					return false;
				}

				if ( type.GenericTypeArguments.Length != genericInstanceType.GenericArguments.Count )
				{
					return false;
				}

				for ( var i = 0; i < type.GenericTypeArguments.Length; ++i )
				{
					if ( !IsMatchingType( genericInstanceType.GenericArguments[i], type.GenericTypeArguments[i] ) )
					{
						return false;
					}
				}
			}
			else if ( typeRef.IsGenericInstance )
			{
				return false;
			}

			if ( type.IsGenericTypeDefinition )
			{
				if ( !typeRef.HasGenericParameters )
				{
					return false;
				}

				var typeParams = type.GetGenericArguments();

				if ( typeParams.Length != typeRef.GenericParameters.Count )
				{
					return false;
				}

				// TODO: Check constraints?

				return true;
			}

			return true;
		}

		internal static TypeReference ResolveGenericArguments( MethodReference methodRef, TypeReference typeRef )
		{
			if ( !typeRef.ContainsGenericParameter )
			{
				return typeRef;
			}

			if ( typeRef is GenericParameter genericParam )
			{
				if ( genericParam.DeclaringMethod != null && methodRef is GenericInstanceMethod genericMethodRef )
				{
					if ( genericMethodRef.GetElementMethod().FullName == genericParam.DeclaringMethod.FullName )
					{
						return genericMethodRef.GenericArguments[genericParam.Position];
					}
				}
				else if ( genericParam.DeclaringType != null )
				{
					var declTypeRef = methodRef.DeclaringType;

					while ( declTypeRef != null )
					{
						if ( declTypeRef is GenericInstanceType genericTypeRef )
						{
							if ( genericTypeRef.GetElementType().FullName == genericParam.DeclaringType.FullName )
							{
								return genericTypeRef.GenericArguments[genericParam.Position];
							}
						}

						declTypeRef = declTypeRef.DeclaringType;
					}
				}

				return typeRef;
			}

			if ( typeRef is ArrayType arrayType )
			{
				var elemType = ResolveGenericArguments( methodRef, arrayType.ElementType );
				return arrayType.Rank == 1
					? elemType.MakeArrayType()
					: elemType.MakeArrayType( arrayType.Rank );
			}

			if ( typeRef is ByReferenceType byRefType )
			{
				var elemType = ResolveGenericArguments( methodRef, byRefType.ElementType );
				return elemType.MakeByReferenceType();
			}

			if ( typeRef is GenericInstanceType genericInstType )
			{
				var elemType = ResolveGenericArguments( methodRef, genericInstType.ElementType );
				var typeArgs = genericInstType.GenericArguments
					.Select( x => ResolveGenericArguments( methodRef, x ) )
					.ToArray();

				return elemType.MakeGenericInstanceType( typeArgs );
			}

			return typeRef;
		}

		private MethodDefinition GetMethodDefinition( MethodBase method )
		{
			if ( method is MethodInfo { IsConstructedGenericMethod: true } methodInfo )
			{
				method = methodInfo.GetGenericMethodDefinition();
			}

			var typeDef = GetTypeDefinition( method.DeclaringType );

			if ( typeDef == null )
			{
				return null;
			}

			// A method on a closed generic type has substituted parameter types. Its
			// metadata token still identifies the definition in the exact loaded image.
			if ( method.DeclaringType.IsConstructedGenericType && method.Module.ModuleVersionId == typeDef.Module.Mvid )
			{
				return typeDef.Methods.FirstOrDefault( definition => definition.MetadataToken.ToInt32() == method.MetadataToken );
			}

			var parameters = method.GetParameters();

			foreach ( var methodDef in typeDef.Methods )
			{
				if ( methodDef.Name != method.Name ) continue;
				if ( methodDef.Parameters.Count != parameters.Length ) continue;

				var isMatch = true;

				for ( var i = 0; i < parameters.Length; ++i )
				{
					if ( !IsMatchingType( ResolveGenericArguments( methodDef, methodDef.Parameters[i].ParameterType ), parameters[i].ParameterType ) )
					{
						isMatch = false;
						break;
					}
				}

				if ( isMatch )
				{
					return methodDef;
				}
			}

			Log( HotloadEntryType.Warning, $"Unable to resolve method definition", method );
			return null;
		}

		private static string GetDefaultMethodName( Type type, FieldInfo field )
		{
			return $"__default__{field.Name}";
		}

		/// <summary>
		/// Attempts to get the default value for a newly created field on an
		/// existing type. Returns true if successful.
		/// </summary>
		/// <remarks>
		/// This value should not be cached, but evaluated for each instance.
		/// Works by finding the CIL that initializes the given field and
		/// generating a dynamic method, which is then cached and invoked.
		/// </remarks>
		/// <param name="field">Field to retrieve a default value for.</param>
		/// <param name="value">If successful, contains the default value.</param>
		private bool TryGetDefaultValue( FieldInfo field, out object value )
		{
			value = null;

			var type = field.DeclaringType;

			// Look for previously generated dynamic method.

			Dictionary<FieldInfo, DefaultDelegate> fieldDict;
			if ( !DefaultDelegates.TryGetValue( type, out fieldDict ) )
			{
				fieldDict = new Dictionary<FieldInfo, DefaultDelegate>();
				DefaultDelegates.Add( type, fieldDict );
			}

			DefaultDelegate deleg;
			if ( fieldDict.TryGetValue( field, out deleg ) )
			{
				if ( deleg == null ) return false;

				value = deleg();
				return true;
			}

			// Cached delegate wasn't found, so try to generate one.

			deleg = GetDefaultDelegate( type, field );
			fieldDict.Add( field, deleg );

			if ( deleg == null ) return false;

			value = deleg();
			return true;
		}

		private class CtorAction
		{
			public bool NeedsParameters;
			public bool IsThisCtorCall;
			public bool IsFieldSet;
			public Instruction First;
			public Instruction Last;
			public FieldReference Field;
			public MethodDefinition Method;
			public int MaxStack;
		}

		/// <summary>
		/// Stack size delta for each stack behaviour.
		/// </summary>
		private static readonly int[] StackBehaviourValues =
		{
			0, // Pop0,
			1, // Pop1,
			2, // Pop1_pop1,
			1, // Popi,
			2, // Popi_pop1,
			2, // Popi_popi,
			2, // Popi_popi8,
			3, // Popi_popi_popi,
			2, // Popi_popr4,
			2, // Popi_popr8,
			1, // Popref,
			2, // Popref_pop1,
			2, // Popref_popi,
			3, // Popref_popi_popi,
			3, // Popref_popi_popi8,
			3, // Popref_popi_popr4,
			3, // Popref_popi_popr8,
			3, // Popref_popi_popref,
			0, // PopAll,
			0, // Push0,
			1, // Push1,
			2, // Push1_push1,
			1, // Pushi,
			1, // Pushi8,
			1, // Pushr4,
			1, // Pushr8,
			1, // Pushref,
			0, // Varpop,
			0, // Varpush
		};

		/// <summary>
		/// Find the number of arguments that invoking the given method will pop.
		/// </summary>
		private static int GetArgCount( Mono.Cecil.Cil.OpCode opCode, MethodReference methodRef )
		{
			var count = 0;

			if ( methodRef.HasParameters ) count += methodRef.Parameters.Count;
			if ( methodRef.HasThis && opCode.Code != Code.Newobj ) ++count;

			return count;
		}

		/// <summary>
		/// Find the number of arguments that invoking the given method will pop.
		/// </summary>
		private static int GetArgCount( Mono.Cecil.Cil.OpCode opCode, MethodBase methodBase )
		{
			var count = 0;
			var parameters = methodBase.GetParameters();

			count += parameters.Length;
			if ( !methodBase.IsStatic && opCode.Code != Code.Newobj ) ++count;

			return count;
		}

		private static int GetStackDelta( MethodDefinition method, Instruction inst )
		{
			var delta = 0;

			if ( inst.OpCode.StackBehaviourPop != StackBehaviour.Varpop )
			{
				delta -= StackBehaviourValues[(int)inst.OpCode.StackBehaviourPop];
			}
			else if ( inst.OpCode.FlowControl == FlowControl.Return )
			{
				return method.ReturnType.FullName == "System.Void" ? 0 : 1;
			}
			else
			{
				delta -= inst.Operand switch
				{
					MethodReference methodRef => GetArgCount( inst.OpCode, methodRef ),
					MethodBase methodBase => GetArgCount( inst.OpCode, methodBase ),
					_ => throw new NotImplementedException( $"Pop ({inst.OpCode.Name}): {inst.Operand?.GetType()}" )
				};
			}

			if ( inst.OpCode.StackBehaviourPush != StackBehaviour.Varpush )
			{
				delta += StackBehaviourValues[(int)inst.OpCode.StackBehaviourPush];
			}
			else
			{
				delta += inst.Operand switch
				{
					MethodReference methodRef => methodRef.ReturnType.FullName == "System.Void" ? 0 : 1,
					MethodBase methodBase => methodBase is ConstructorInfo || methodBase is MethodInfo methodInfo && methodInfo.ReturnType != typeof( void ) ? 1 : 0,
					_ => throw new NotImplementedException( $"Push ({inst.OpCode.Name}): {inst.Operand?.GetType()}" )
				};
			}

			return delta;
		}

		private static bool IsLoadArg( Instruction inst )
		{
			switch ( inst.OpCode.Code )
			{
				case Code.Ldarg:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
				case Code.Ldarg_S:
				case Code.Ldarga:
				case Code.Ldarga_S:
					return true;
				default:
					return false;
			}
		}

		private static bool ReadNextInstruction( MethodDefinition method, ref Instruction inst, ref int stack, CtorAction action )
		{
			if ( inst == null ) return false;

			// Look for instructions that involve parameters passed to the constructor, since
			// default field values can't use them.

			if ( IsLoadArg( inst ) ) action.NeedsParameters = true;

			stack += GetStackDelta( method, inst );
			action.MaxStack = Math.Max( stack, action.MaxStack );

			// Stop when the stack frame size drops back to 0.

			if ( stack < 0 ) return false;

			inst = inst.Next;
			return true;
		}

		private static CtorAction ReadAction( MethodDefinition method, ref Instruction inst )
		{
			// Actions should always start with loading 'this'.

			if ( inst?.OpCode.Code != Code.Ldarg_0 ) return null;

			var action = new CtorAction
			{
				Method = method,
				First = inst
			};

			inst = inst.Next;

			// Read instructions until the stack frame size drops back to 0.

			var stack = 0;
			while ( ReadNextInstruction( method, ref inst, ref stack, action ) ) ;
			if ( inst == null ) return null;

			action.Last = inst;
			action.IsFieldSet = inst.OpCode.Code == Code.Stfld;

			if ( action.IsFieldSet )
			{
				action.Field = inst.Operand as FieldReference;
			}
			else if ( inst.OpCode.Code == Code.Call )
			{
				// The first action that isn't setting the default value for a field should
				// be another constructor call for either a base type or on this type.

				var operand = inst.Operand as MethodReference;
				if ( operand == null ) return action;
				if ( operand.Name != ".ctor" ) return action;

				if ( operand.DeclaringType == method.DeclaringType ) action.IsThisCtorCall = true;
			}

			inst = inst.Next;

			return action;
		}

		private static IEnumerable<CtorAction> GetCtorActions( MethodDefinition method )
		{
			if ( !method.HasBody || method.Body.Instructions.Count == 0 ) yield break;

			var inst = method.Body.Instructions[0];

			CtorAction action;
			while ( (action = ReadAction( method, ref inst )) != null )
			{
				if ( !action.IsFieldSet ) yield break;
				yield return action;
			}
		}

		private static bool IsMatchingField( FieldReference a, FieldReference b )
		{
			return a == null && b == null || a != null && b != null && a.FullName == b.FullName;
		}

		private DefaultDelegate GetDefaultDelegate( Type type, FieldInfo field )
		{
			// First attempt to find the type definition for the given type.

			var typeDef = GetTypeDefinition( type );
			if ( typeDef == null ) return null;

			// Default values will be assigned before any base constructors are called,
			// but are not assigned in constructors that call other constructors on the
			// same type.

			var ctorDefs = typeDef.GetConstructors();
			var fieldRef = (FieldReference)typeDef.Fields.First( x => x.Name == field.Name );

			// For each constructor, get a list of all actions performed before a base
			// constructor is called. An action is a sequence of instructions that start and
			// end with the stack frame for the constructor being empty.

			var allActions = ctorDefs
				.Select( GetCtorActions )
				.Select( x => x.ToArray() );

			// Look for an action that ends by assigning the desired field.

			var matchingAction = allActions
				.Where( x => x.Length > 0 )
				.Where( x => x.All( y => !y.IsThisCtorCall && !y.NeedsParameters ) )
				.Select( x => x.FirstOrDefault( y => IsMatchingField( fieldRef, y.Field ) ) )
				.Where( x => x != null )
				.MinBy( x => x.Method.Parameters.Count );

			if ( matchingAction == null ) return null;

			// Generate a dynamic method using the instructions of the action we found.

			var dynamic = new DynamicMethod( GetDefaultMethodName( type, field ), typeof( object ), new Type[0], type );
			var il = dynamic.GetILGenerator();

			il.Emit( type.GetTypeInfo().Module, matchingAction.First.Next, matchingAction.Last );

			if ( field.FieldType.GetTypeInfo().IsValueType )
			{
				// Need to box value types since we return an object.
				il.Emit( System.Reflection.Emit.OpCodes.Box, field.FieldType );
			}

			il.Emit( System.Reflection.Emit.OpCodes.Ret );

			return (DefaultDelegate)dynamic.CreateDelegate( typeof( DefaultDelegate ) );
		}
	}
}
