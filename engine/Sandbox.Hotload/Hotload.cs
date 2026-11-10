
global using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Sandbox.Upgraders;

namespace Sandbox
{
	/// <summary>
	/// Provides methods for replacing loaded assemblies with new versions at runtime.
	/// </summary>
	public partial class Hotload
	{
		private readonly Logger _logger;

		/// <summary>
		/// A mapping of assembles to swap with new versions.
		/// </summary>
		private readonly Dictionary<Assembly, Assembly> Swaps = new Dictionary<Assembly, Assembly>();

		/// <summary>
		/// Assemblies that are being loaded in this hotload, either as a swap or replacing null.
		/// </summary>
		private readonly HashSet<Assembly> New = new HashSet<Assembly>();

		/// <summary>
		/// A list of assemblies containing members that should be skipped during a reference update.
		/// </summary>
		private readonly HashSet<Assembly> IgnoredAssemblies = new HashSet<Assembly>();
		private readonly HashSet<string> IgnoredAssemblyNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		private readonly Dictionary<Type, IInstanceUpgrader> AllUpgraders = new Dictionary<Type, IInstanceUpgrader>();
		private readonly RootUpgraderGroup RootUpgraderGroup = new RootUpgraderGroup();

		/// <summary>
		/// If true, the static field or watched object that instances are found under will be stored in <see cref="InstanceTimingEntry.Roots"/>.
		/// Defaults to false.
		/// </summary>
		public bool TraceRoots { get; set; }

		/// <summary>
		/// If true, keep track of the path to instances to include in logging. Defaults to false.
		/// </summary>
		public bool TracePaths { get; set; }

		/// <summary>
		/// If true, record per-type timing information.
		/// </summary>
		public bool IncludeTypeTimings { get; set; }

		/// <summary>
		/// If true, record instance processor timing information.
		/// </summary>
		public bool IncludeProcessorTimings { get; set; }

		/// <summary>
		/// Optional resolver required for features like setting default values of newly-added fields.
		/// </summary>
		public Mono.Cecil.IAssemblyResolver AssemblyResolver { get; set; }

		/// <summary>
		/// Optional formatter when pretty-printing assembly names in logs.
		/// </summary>
		public static Func<AssemblyName, string> AssemblyNameFormatter { get; set; }

		/// <summary>
		/// Default constructor that includes Sandbox.Hotload.dll and Mono.Cecil.dll to the
		/// ignored assembly list.
		/// </summary>
		public Hotload( bool addDefaultUpgraders = true, Logger logger = null )
		{
			_logger = logger;

			IgnoreAssembly( typeof( Hotload ).GetTypeInfo().Assembly );
			IgnoreAssembly( typeof( Mono.Cecil.AssemblyDefinition ).GetTypeInfo().Assembly );

			AllUpgraders.Add( typeof( RootUpgraderGroup ), RootUpgraderGroup );

			if ( addDefaultUpgraders )
			{
				AddUpgraders( typeof( Hotload ).GetTypeInfo().Assembly );
			}
		}

		/// <summary>
		/// Any fields declared on types defined in the given assembly will be skipped
		/// during future reference updates.
		/// </summary>
		/// <param name="toIgnore">Assembly to ignore the members of.</param>
		public void IgnoreAssembly( Assembly toIgnore )
		{
			if ( toIgnore == null )
				throw new System.ArgumentNullException( nameof( toIgnore ) );

			IgnoredAssemblies.Add( toIgnore );
		}


		/// <summary>
		/// Any fields declared on types defined in the given assembly will be skipped
		/// during future reference updates.
		/// </summary>
		public void IgnoreAssembly<T>() => IgnoreAssembly( typeof( T ).Assembly );

		/// <summary>
		/// Any fields declared on types defined in the named assembly will be skipped
		/// during future reference updates.
		/// </summary>
		public void IgnoreAssembly( string asmName )
		{
			IgnoredAssemblyNames.Add( asmName );
		}

		public bool IsAssemblyIgnored( Assembly asm ) => IgnoredAssemblies.Contains( asm )
			|| IgnoredAssemblyNames.Contains( asm.GetName().Name );

		/// <summary>
		/// To be called when one assembly is being replaced by another, is loaded for the first time,
		/// or unloaded for the last time.
		///
		/// This will add <paramref name="newAssembly"/> to be watched (if not null), and remove
		/// <paramref name="oldAssembly"/> from being watched (if not null). If both assemblies aren't
		/// null, they will be added to be swapped when <see cref="UpdateReferences"/> is next called, and
		/// true is returned.
		/// </summary>
		public bool ReplacingAssembly( [AllowNull] Assembly oldAssembly, [AllowNull] Assembly newAssembly )
		{
			if ( oldAssembly == newAssembly )
			{
				return false;
			}

			if ( newAssembly != null && !Swaps.ContainsKey( newAssembly ) )
			{
				New.Add( newAssembly );
			}

			if ( oldAssembly != null )
			{
				New.Remove( oldAssembly );

				if ( newAssembly == null )
				{
					UnwatchAssembly( oldAssembly );
					return false;
				}

				if ( !Swaps.TryGetValue( oldAssembly, out var prevSwap ) || prevSwap == newAssembly )
				{
					Swaps[oldAssembly] = newAssembly;
					return true;
				}
			}

			return false;
		}

		public Assembly[] GetOutgoingAssemblies()
		{
			return Swaps.Select( x => x.Key ).Distinct().ToArray();
		}

		/// <summary>
		/// Returns the queue of assemblies that will be swapped when
		/// <see cref="UpdateReferences"/> is called. These are added using the
		/// <see cref="ReplacingAssembly"/> method.
		/// </summary>
		/// <returns>The mapping of assembly replacements.</returns>
		public IReadOnlyDictionary<Assembly, Assembly> GetQueuedAssemblyReplacements()
		{
			return Swaps.ToDictionary( kv => kv.Key, kv => kv.Value );
		}

		public void AddUpgrader( IInstanceUpgrader upgrader )
		{
			var upgraderType = upgrader.GetType();

			if ( AllUpgraders.ContainsKey( upgraderType ) )
				throw new Exception( $"There is already an upgrader of type {upgraderType.FullName} added to this instance." );

			AllUpgraders.Add( upgraderType, upgrader );
			RootUpgraderGroup.AddUpgrader( upgrader );
		}

		public void AddUpgrader<TUpgrader>()
			where TUpgrader : IInstanceUpgrader, new()
		{
			AddUpgrader( new TUpgrader() );
		}

		public IInstanceUpgrader GetUpgrader( Type upgraderType )
		{
			if ( AllUpgraders.TryGetValue( upgraderType, out var upgrader ) )
			{
				return upgrader;
			}

			throw new Exception( $"Upgrader of type {upgraderType} not yet added." );
		}

		public TUpgrader GetUpgrader<TUpgrader>()
			where TUpgrader : IInstanceUpgrader
		{
			return (TUpgrader)GetUpgrader( typeof( TUpgrader ) );
		}

		public bool TryGetUpgrader( Type upgraderType, out IInstanceUpgrader upgrader )
		{
			return AllUpgraders.TryGetValue( upgraderType, out upgrader );
		}

		public bool TryGetUpgrader<TUpgrader>( out TUpgrader upgrader )
			where TUpgrader : IInstanceUpgrader
		{
			if ( TryGetUpgrader( typeof( TUpgrader ), out var value ) )
			{
				upgrader = (TUpgrader)value;
				return true;
			}

			upgrader = default;
			return false;
		}

		private struct UpgraderInfo
		{
			public readonly IInstanceUpgrader Upgrader;
			public readonly Type GroupType;

			public UpgraderInfo( IInstanceUpgrader upgrader, Type type )
			{
				Upgrader = upgrader;
				GroupType = UpgraderGroup.GetUpgraderGroupType( type );
			}
		}

		public void AddUpgraders( Assembly asm )
		{
			List<Exception> exceptions = null;

			var toAdd = new List<UpgraderInfo>();
			var addedGroupTypes = new HashSet<Type>
			{
				typeof(RootUpgraderGroup)
			};

			foreach ( var type in asm.GetTypes() )
			{
				if ( type.IsAbstract || type.IsGenericTypeDefinition )
					continue;

				if ( !typeof( IInstanceUpgrader ).IsAssignableFrom( type ) )
					continue;

				if ( type.GetCustomAttribute<DisableAutoCreationAttribute>() != null )
					continue;

				var ctor = type.GetConstructor( Array.Empty<Type>() );

				if ( ctor == null )
				{
					(exceptions ??= new List<Exception>()).Add( new Exception( $"Type {type.FullName} implements " +
						$"{nameof( IInstanceUpgrader )} without a {nameof( DisableAutoCreationAttribute )}, but doesn't have " +
						$"a parameterless constructor." ) );

					continue;
				}

				try
				{
					var inst = (IInstanceUpgrader)ctor.Invoke( Array.Empty<object>() );
					toAdd.Add( new UpgraderInfo( inst, type ) );
				}
				catch ( Exception e )
				{
					(exceptions ??= new List<Exception>()).Add( e );
				}
			}

			while ( toAdd.Count > 0 )
			{
				// Try to find an item where its group is already added, and
				// if none exists just try to add the first item and record
				// any exception thrown.

				var nextIndex = Math.Max( toAdd.FindIndex( x => x.GroupType == null || addedGroupTypes.Contains( x.GroupType ) ), 0 );
				var next = toAdd[nextIndex];

				toAdd.RemoveAt( nextIndex );

				try
				{
					AddUpgrader( next.Upgrader );

					if ( next.Upgrader is UpgraderGroup )
						addedGroupTypes.Add( next.Upgrader.GetType() );
				}
				catch ( Exception e )
				{
					(exceptions ??= new List<Exception>()).Add( e );
				}
			}

			if ( exceptions == null )
				return;

			if ( exceptions.Count == 1 )
				throw exceptions[0];

			throw new AggregateException( $"Exceptions thrown while attempting to add {nameof( IInstanceUpgrader )}s " +
				$"from assembly {asm.FullName}.", exceptions );
		}

		internal static string FormatAssemblyName( AssemblyName name )
		{
			return AssemblyNameFormatter?.Invoke( name ) ?? name.ToString();
		}

		internal static string FormatAssemblyName( Assembly asm )
		{
			return asm is null ? string.Empty : FormatAssemblyName( asm.GetName() );
		}
	}
}
