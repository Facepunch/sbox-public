using Mono.Cecil;
using MonoMod.Core;
using MonoMod.Utils;
using Sandbox.Upgraders;
using Sentry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MethodImplAttributes = System.Reflection.MethodImplAttributes;

namespace Sandbox;

/// <summary>
/// A fast path hotload that patches the existing assemblies IL when applicable
/// </summary>
public partial class ILHotload : IDisposable
{
	internal static bool IgnoreAttachedDebugger { get; set; }

	private Logger log;

	/// <summary>
	/// All active detours within an Assembly
	/// </summary>
	Dictionary<Assembly, Dictionary<MethodBase, ICoreDetour>> ActiveDetours = new();

	/// <summary>
	/// Make sure we have an initialized detour factory, as it takes a small while to load
	/// </summary>
	private IDetourFactory DetourFactory;

	/// <summary>
	/// True if <see cref="ILHotload"/> is supported on this platform.
	/// </summary>
	public bool IsSupported => DetourFactory != null;

	private Exception _notSupportedException;

	private record AssemblyChange
	{
		public required Assembly OriginalAssembly { get; init; }
		public required Assembly ReplacingAssembly { get; init; }
	}

	public ILHotload( string name )
	{
		log = new Logger( $"{nameof( ILHotload )}/{name}" );

		try
		{
			DetourFactory = MonoMod.Core.DetourFactory.Current;
		}
		catch ( Exception e )
		{
			// Let's log the error when we first actually try to hotload
			_notSupportedException = e;
		}
	}

	public void Dispose()
	{
		ActiveDetours.Clear();
		ActiveDetours = null;

		DetourFactory = null;
	}

	public bool Replace( Assembly baseAssembly, Assembly oldIlHotloadAssembly, Assembly newIlHotloadAssembly )
	{
		if ( !IsSupported )
		{
			if ( _notSupportedException is null )
			{
				return false;
			}

			log.Warning( $"ILHotload not supported on this platform.{Environment.NewLine}{_notSupportedException}" );
			_notSupportedException = null;

			return false;
		}

		using var scope = SentrySdk.PushScope();

		SentrySdk.ConfigureScope( x =>
		{
			x.SetTag( "group", "fast-hotload" );
		} );

		if ( !TryFindChangedMethods( baseAssembly, oldIlHotloadAssembly, newIlHotloadAssembly, out var changes, out var unexpected, out var hasAttribute ) )
		{
			if ( hasAttribute )
			{
				log.Warning( $"Fast hotload attempted, but members have changed.{Environment.NewLine}{string.Join( Environment.NewLine, unexpected.Select( x => $"  {x.ToSimpleString()}" ) )}" );
			}

			return false;
		}

		//
		// We shouldn't do IL replacement hotloading when the debugger is attached for two reasons:
		// 1. If a user is debugging a method, the PDB will mismatch the actual execution
		// 2. MonoMod is ridiculously slow with a debugger attached
		//
		// Obviously you can comment this out if you need to debug this stuff in particular
		//
		if ( Debugger.IsAttached && !IgnoreAttachedDebugger ) return false;

		var anyFailed = false;

		foreach ( var (oldMethod, newMethod) in changes )
		{
			using var innerScope = SentrySdk.PushScope();

			SentrySdk.ConfigureScope( x =>
			{
				x.Contexts["Source Method"] = new
				{
					MethodName = $"{newMethod.ToSimpleString()}"
				};
			} );

			if ( TryReplaceMethod( oldMethod, newMethod ) )
			{
				continue;
			}

			log.Warning( $"Failed to replace method {newMethod.ToSimpleString()}" );
			anyFailed = true;
		}

		return !anyFailed;
	}

	private const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic
		| BindingFlags.Instance | BindingFlags.Static
		| BindingFlags.DeclaredOnly;

	public static bool TryFindChangedMethods( Assembly baseAsm, Assembly oldAsm, Assembly newAsm,
		out (MethodBase Old, MethodBase New)[] changedMethods,
		out MemberInfo[] unexpectedChanges,
		out bool hasSupportedAttribute )
	{
		oldAsm ??= baseAsm;

		hasSupportedAttribute = false;

		changedMethods = Array.Empty<(MethodBase, MethodBase)>();
		unexpectedChanges = Array.Empty<MemberInfo>();

		if ( oldAsm == null || newAsm == null ) return false;
		if ( oldAsm == newAsm ) return false;

		// Check if IL hotload is supported for this pair

		var supportedAttrib = newAsm.GetCustomAttribute<SupportsILHotloadAttribute>();
		var oldAsmVersion = oldAsm.GetName().Version;

		if ( supportedAttrib == null || oldAsmVersion?.ToString() != supportedAttrib.PreviousAssemblyVersion )
		{
			return false;
		}

		hasSupportedAttribute = true;

		// Look for methods / properties tagged with change attributes
		// Also make sure that no type definitions have changed

		var newChangedMethods = new ConcurrentBag<MethodBase>();
		var baseAsmTypes = baseAsm.GetTypes().ToDictionary( x => x.FullName, x => x );
		var newAsmTypes = newAsm.GetTypes();

		if ( baseAsmTypes.Count != newAsmTypes.Length )
		{
			return false;
		}

		var typeChanges = false;
		var comparer = new MemberEqualityComparer();
		var changedMembers = new ConcurrentBag<MemberInfo>();

		Parallel.ForEach( newAsmTypes, type =>
		{
			if ( !baseAsmTypes.TryGetValue( type.FullName, out var baseType ) )
			{
				typeChanges = true;
				return;
			}

			if ( !comparer.AllMembersEqual( type, baseType ) )
			{
				changedMembers.Add( type );
				typeChanges = true;
				return;
			}

			var baseMembers = baseType.GetMembers( AllDeclared );
			var newMembers = type.GetMembers( AllDeclared );

			if ( baseMembers.Length != newMembers.Length )
			{
				changedMembers.Add( type );
				typeChanges = true;
				return;
			}

			for ( var i = 0; i < newMembers.Length; i++ )
			{
				var baseMember = baseMembers[i];
				var newMember = newMembers[i];

				// This should always be true because of the AllMembersEqual test earlier
				Assert.AreEqual( baseMember.Name, newMember.Name );

				if ( newMember is MethodInfo methodInfo && methodInfo.GetCustomAttribute<MethodBodyChangeAttribute>() != null )
				{
					newChangedMethods.Add( methodInfo );
					continue;
				}

				if ( newMember is not PropertyInfo propertyInfo )
				{
					continue;
				}

				var changedAttribs = propertyInfo.GetCustomAttributes<PropertyAccessorBodyChangeAttribute>();

				foreach ( var attrib in changedAttribs )
				{
					switch ( attrib.Accessor )
					{
						case PropertyAccessor.Get when propertyInfo.GetMethod != null:
							newChangedMethods.Add( propertyInfo.GetMethod );
							break;

						case PropertyAccessor.Set when propertyInfo.SetMethod != null:
							newChangedMethods.Add( propertyInfo.SetMethod );
							break;
					}
				}
			}
		} );

		if ( typeChanges )
		{
			unexpectedChanges = changedMembers.ToArray();
			return false;
		}

		if ( newChangedMethods.Count == 0 )
		{
			return true;
		}

		if ( newChangedMethods.Any( HasTypeParameters ) )
		{
			//
			// Not supported yet, MonoMod will throw when trying to create a DynamicMethodDefinition
			//

			// Set this to false so we don't get a warning

			hasSupportedAttribute = false;

			return false;
		}

		changedMethods = newChangedMethods
			.Distinct()
			.Select( x => (FindMatchingMethod( baseAsm, x ), x) )
			.ToArray();

		return true;
	}

	private static MethodBase FindMatchingMethod( Assembly asm, MethodBase otherAsmMethod )
	{
		if ( asm == null || otherAsmMethod.DeclaringType?.FullName == null )
		{
			return null;
		}

		var declType = asm.GetType( otherAsmMethod.DeclaringType.FullName );

		if ( declType == null )
		{
			return null;
		}

		var otherParams = otherAsmMethod.GetParameters();

		foreach ( var methodInfo in declType.GetMethods( AllDeclared ) )
		{
			if ( methodInfo.Name != otherAsmMethod.Name )
			{
				continue;
			}

			var thisParams = methodInfo.GetParameters();

			if ( thisParams.Length != otherParams.Length )
			{
				continue;
			}

			var matchingParams = true;

			for ( var i = 0; i < thisParams.Length; ++i )
			{
				if ( !AreEquivalentParameters( thisParams[i], otherParams[i] ) )
				{
					matchingParams = false;
					break;
				}
			}

			if ( matchingParams )
			{
				return methodInfo;
			}
		}

		return null;
	}

	private static bool AreEquivalentParameters( ParameterInfo a, ParameterInfo b )
	{
		if ( a.Name != b.Name )
		{
			return false;
		}

		if ( a.IsIn != b.IsIn || a.IsOut != b.IsOut || a.IsOptional != b.IsOptional || a.IsRetval != b.IsRetval )
		{
			return false;
		}

		return AreEquivalentParameterTypes( a.ParameterType, b.ParameterType );
	}

	private static bool AreEquivalentParameterTypes( Type a, Type b )
	{
		if ( a == b )
		{
			return true;
		}

		// TODO
		return a.ToSimpleString() == b.ToSimpleString();
	}

	private static bool HasTypeParameters( MemberInfo member )
	{
		if ( member is MethodBase { ContainsGenericParameters: true } )
		{
			return true;
		}

		if ( member is Type { ContainsGenericParameters: true } )
		{
			return true;
		}

		return member.DeclaringType != null && HasTypeParameters( member.DeclaringType );
	}

	public bool TryReplaceMethod( MethodBase source, MethodBase replace )
	{
		//
		// Strong doubts this would work on JITed stuff
		//
		if ( source.MethodImplementationFlags != MethodImplAttributes.IL )
		{
			return false;
		}

		var sourceAssembly = source.Module.Assembly;

		if ( !ActiveDetours.TryGetValue( sourceAssembly, out var asmDetours ) )
		{
			asmDetours = new Dictionary<MethodBase, ICoreDetour>();
			ActiveDetours[sourceAssembly] = asmDetours;
		}

		//
		// If we already detoured this method before, undo it so we can compare the method body properly
		//
		if ( asmDetours.TryGetValue( source, out var oldDetour ) )
		{
			if ( oldDetour == null )
			{
				//
				// We're currently working on this detour
				//

				return true;
			}

			if ( oldDetour.Target == replace )
			{
				//
				// Exactly this detour is already in place
				//

				return true;
			}

			oldDetour.Undo();
			asmDetours.Remove( source );
		}

		asmDetours[source] = null;

		//
		// MonoMod caches assemblies / member infos by name, which breaks what
		// we're trying to do if you swap different versions of the same assembly more than once.
		//

		ReplaceMonoModReflectionCache( source.Module.Assembly );

		//
		// Create a dynamic method to remove it from the new assembly
		//

		try
		{
			using var dynamicMethodDef = new DynamicMethodDefinition( source );
			using var replaceMethodDef = new DynamicMethodDefinition( replace );

			dynamicMethodDef.Definition.Body = replaceMethodDef.Definition.Body.Clone( dynamicMethodDef.Definition );

			//
			// Replace references to members / types in replace.Module with the original
			// members / types in source.Module
			//

			var referencedMethods = FixReferences( dynamicMethodDef, source.Module, replace.Module );

			//
			// Methods with lambdas / async will reference compiler generated methods that also need replacing
			//

			foreach ( var (refSource, refTarget) in referencedMethods )
			{
				log.Trace( $"Replacing compiler generated {refTarget}" );

				if ( !TryReplaceMethod( refSource, refTarget ) )
				{
					return false;
				}
			}

			var dynamicMethod = dynamicMethodDef.Generate( source );

			//
			// Trampoline and keep reference to the detour
			//
			var detour = DetourFactory.CreateDetour( source, dynamicMethod );

			asmDetours[source] = detour;
		}
		catch ( NotSupportedException )
		{
			asmDetours.Remove( source );
			return false;
		}
		catch ( Exception e )
		{
			log.Error( e );
			asmDetours.Remove( source );
			return false;
		}

		return true;
	}

	private static void ReplaceMonoModReflectionCache( Assembly asm )
	{
		const BindingFlags bFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		var assemblyCache = (Dictionary<string, WeakReference>)typeof( ReflectionHelper ).GetField( "AssemblyCache", bFlags )!.GetValue( null );
		var assembliesCache = (Dictionary<string, WeakReference[]>)typeof( ReflectionHelper ).GetField( "AssembliesCache", bFlags )!.GetValue( null );
		var resolveReflectionCache = (Dictionary<string, WeakReference>)typeof( ReflectionHelper ).GetField( "ResolveReflectionCache", bFlags )!.GetValue( null );

		assemblyCache!.Clear();
		assembliesCache!.Clear();
		resolveReflectionCache!.Clear();

		assemblyCache[asm.GetRuntimeHashedFullName()] = new WeakReference( asm );
	}

	private static MemberInfo ResolveMember( Module module, MemberReference memberRef )
	{
		switch ( memberRef )
		{
			case TypeReference typeRef:
				return module.ResolveType( typeRef );

			case FieldReference fieldRef:
				return module.ResolveField( fieldRef );

			case MethodReference methodRef:
				return module.ResolveMethod( methodRef );

			default:
				throw new NotImplementedException();
		}
	}

	private static void AddCompilerGeneratedMethods( HashSet<MethodBase> compilerGeneratedSourceMethods, HashSet<MemberInfo> seenMembers, MemberInfo memberInfo )
	{
		if ( !seenMembers.Add( memberInfo ) )
		{
			return;
		}

		if ( memberInfo is ConstructorInfo ctorInfo && DelegateUpgrader.IsCompilerGenerated( ctorInfo.DeclaringType ) )
		{
			memberInfo = ctorInfo.DeclaringType;
		}

		if ( DelegateUpgrader.IsCompilerGenerated( memberInfo.Name ) )
		{
			switch ( memberInfo )
			{
				case MethodBase sourceMethod:
					compilerGeneratedSourceMethods.Add( sourceMethod );
					break;
				case Type sourceType:
					foreach ( var innerSourceMethod in sourceType.GetMethods( AllDeclared ) )
					{
						compilerGeneratedSourceMethods.Add( innerSourceMethod );
					}

					break;
			}
		}
	}

	/// <summary>
	/// Replace any references to types or members in <paramref name="replaceModule"/> with references in <paramref name="sourceModule"/>.
	/// Returns an array of referenced compiler-generated methods that should also be detoured.
	/// </summary>
	private static (MethodBase Source, MethodBase Target)[] FixReferences( DynamicMethodDefinition dynamicMethodDef, Module sourceModule, Module replaceModule )
	{

		var compilerGeneratedSourceMethods = new HashSet<MethodBase>();
		var seenMembers = new HashSet<MemberInfo>();

		foreach ( var variable in dynamicMethodDef.Definition.Body.Variables )
		{
			var sourceInfo = sourceModule.ResolveType( variable.VariableType );

			variable.VariableType = dynamicMethodDef.Module.ImportReference( sourceInfo );

			AddCompilerGeneratedMethods( compilerGeneratedSourceMethods, seenMembers, sourceInfo );
		}

		foreach ( var inst in dynamicMethodDef.Definition.Body.Instructions )
		{
			switch ( inst.Operand )
			{
				case MemberReference memberRef:
					var sourceInfo = ResolveMember( sourceModule, memberRef );

					// Don't need to import a reference apparently!

					inst.Operand = sourceInfo;

					AddCompilerGeneratedMethods( compilerGeneratedSourceMethods, seenMembers, sourceInfo );
					break;
			}
		}

		if ( compilerGeneratedSourceMethods.Count == 0 )
		{
			return Array.Empty<(MethodBase, MethodBase)>();
		}

		var compilerGeneratedMethods = new (MethodBase, MethodBase)[compilerGeneratedSourceMethods.Count];
		var index = 0;

		foreach ( var source in compilerGeneratedSourceMethods )
		{
			var sourceRef = dynamicMethodDef.Module.ImportReference( source );
			var target = ResolveMember( replaceModule, sourceRef ) as MethodBase;

			Assert.NotNull( source );
			Assert.NotNull( target );

			compilerGeneratedMethods[index++] = (source, target);
		}

		return compilerGeneratedMethods;
	}
}
