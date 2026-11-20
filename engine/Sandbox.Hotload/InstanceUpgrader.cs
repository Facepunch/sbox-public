using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;
using Sandbox.Upgraders;

namespace Sandbox
{
	public partial class Hotload
	{
		/// <summary>
		/// Interface to implement a custom object instance upgrade process for types that match a condition.
		/// Instances of any derived types will be created and added to a <see cref="Sandbox.Hotload"/> instance that uses
		/// <see cref="AddUpgraders"/> on the declaring assembly of the derived type, unless a
		/// <see cref="DisableAutoCreationAttribute"/> has been specified.
		///
		/// You can configure which order <see cref="IInstanceUpgrader"/>s are queried by using <see cref="UpgraderGroupAttribute"/>,
		/// <see cref="AttemptBeforeAttribute"/> and / or <see cref="AttemptAfterAttribute"/>.
		/// </summary>
		public interface IInstanceUpgrader
		{
			bool IsInitialized { get; }
			void Initialize( Hotload hotload );

			void HotloadStart();
			void HotloadComplete();
			void ClearCache();

			bool ShouldProcessType( Type type );

			bool TryCreateNewInstance( object oldInstance, out object newInstance );
			bool TryUpgradeInstance( object oldInstance, object newInstance );
		}

		public interface IInstanceProcessor
		{
			int ProcessInstance( object oldInstance, object newInstance );
		}

#pragma warning disable CA1034 // Nested types should not be visible
		public abstract partial class InstanceUpgrader : IInstanceUpgrader, IInstanceProcessor
#pragma warning restore CA1034 // Nested types should not be visible
		{
			private Hotload Hotload;

			protected DefaultUpgrader DefaultUpgrader { get; private set; }
			protected CachedUpgrader CachedUpgrader { get; private set; }

			public bool IsInitialized => Hotload != null;

			public bool TracePaths => Hotload.TracePaths;

			public ReferencePath CurrentPath
			{
				get => Hotload.CurrentPath;
				set => Hotload.CurrentPath = value;
			}

			public FieldInfo CurrentSrcField
			{
				get => Hotload.CurrentSrcField;
				set => Hotload.CurrentSrcField = value;
			}

			public FieldInfo CurrentDstField
			{
				get => Hotload.CurrentDstField;
				set => Hotload.CurrentDstField = value;
			}

			void IInstanceUpgrader.Initialize( Hotload hotload )
			{
				Hotload = hotload;
				DefaultUpgrader = hotload.GetUpgrader<DefaultUpgrader>();
				CachedUpgrader = hotload.GetUpgrader<CachedUpgrader>();
				OnInitialize();
			}

			void IInstanceUpgrader.HotloadStart()
			{
				OnHotloadStart();
			}

			void IInstanceUpgrader.HotloadComplete()
			{
				OnHotloadComplete();
			}

			void IInstanceUpgrader.ClearCache()
			{
				OnClearCache();
			}

			/// <summary>
			/// A mapping of assembles to swap with new versions.
			/// </summary>
			protected IReadOnlyDictionary<Assembly, Assembly> Swaps => Hotload.Swaps;

			internal bool TryGetTypeReplacement( Assembly assembly, out Assembly replacement ) => Hotload.TryGetTypeReplacement( assembly, out replacement );

			protected TUpgrader GetUpgrader<TUpgrader>()
				where TUpgrader : IInstanceUpgrader
			{
				return Hotload.GetUpgrader<TUpgrader>();
			}

			protected TUpgrader GetUpgraderOrDefault<TUpgrader>()
				where TUpgrader : IInstanceUpgrader
			{
				return Hotload.TryGetUpgrader<TUpgrader>( out var upgrader ) ? upgrader : default;
			}

			protected bool TryGetUpgrader<TUpgrader>( out TUpgrader upgrader )
				where TUpgrader : IInstanceUpgrader
			{
				return Hotload.TryGetUpgrader( out upgrader );
			}

			protected bool IsAssemblyIgnored( Assembly asm ) => Hotload.IsAssemblyIgnored( asm );

			/// <summary>
			/// When hotswapping this will switch types from the old assembly into the type from the new assembly.
			/// </summary>
			/// <param name="oldType">The old type.</param>
			/// <returns>The new type, or null if no substitution exists. The old type will be returned if it's still valid (not from a swapped assembly).</returns>
			protected Type GetNewType( Type oldType ) => Hotload.GetNewType( oldType );

			/// <summary>
			/// Returns an upgraded version of the given object, replacing any types from a swapped-out
			/// assembly with their new up-to-date types. The result is cached, so if you pass the same
			/// object to this method multiple times it will always return the same instance. Fields inside
			/// the new instance may not be initialized until later in the hotload.
			/// </summary>
			/// <param name="oldInstance">Object to upgrade.</param>
			/// <returns>An upgraded version of the given object.</returns>
			protected object GetNewInstance( object oldInstance ) => Hotload.GetNewInstance( oldInstance );

			protected T GetNewInstance<T>( T oldInstance ) => (T)GetNewInstance( (object)oldInstance );

			protected void AddCachedInstance( object oldInstance, object newInstance )
			{
				CachedUpgrader.AddCachedInstance( oldInstance, newInstance );
			}

			protected void SuppressFinalize( object oldInstance, object newInstance )
			{
				if ( !ReferenceEquals( oldInstance, newInstance ) )
				{
					GC.SuppressFinalize( oldInstance );
				}
			}

			protected bool TryGetDefaultValue( FieldInfo field, out object value ) =>
				Hotload.TryGetDefaultValue( field, out value );

			protected bool IsSwappedType( Type type ) => Hotload.IsSwappedType( type );

			protected bool AreEquivalentTypes( Type oldType, Type newType ) =>
				Hotload.AreEquivalentTypes( oldType, newType );

			protected bool AreEqualTypes( Type a, Type b ) =>
				Hotload.AreEqualTypes( a, b );

			protected MethodBase FindScopeMethod( Type declaringType, int scopeMethodOrdinal ) =>
				Hotload.FindScopeMethod( declaringType, null, scopeMethodOrdinal );

			protected MethodBase FindScopeMethod( Type declaringType, string scopeMethodName, int scopeMethodOrdinal ) =>
				Hotload.FindScopeMethod( declaringType, scopeMethodName, scopeMethodOrdinal );

			protected int GetScopeMethodOrdinal( MethodBase scopeMethod ) =>
				Hotload.GetScopeMethodOrdinal( scopeMethod );

			/// <summary>
			/// Logs a message in the current hotload.
			/// </summary>
			protected void Log( HotloadEntryType type, FormattableString message, MemberInfo member = null ) =>
				Hotload.Log( type, message, member );

			/// <summary>
			/// Logs an exception in the current hotload.
			/// </summary>
			protected void Log( Exception exception, FormattableString message = null, MemberInfo member = null ) =>
				Hotload.Log( exception, message, member );

			/// <summary>
			/// Called when this upgrader has been added to a <see cref="Hotload"/> instance.
			/// </summary>
			protected virtual void OnInitialize() { }

			protected virtual void OnHotloadStart() { }

			protected virtual void OnHotloadComplete() { }

			/// <summary>
			/// Called between hotloads, should clear up any cached resources that won't be needed in future hotloads.
			/// </summary>
			protected virtual void OnClearCache() { }

			/// <summary>
			/// Check to see if this upgrader can possibly handle the given type.
			/// </summary>
			/// <param name="type">Type to upgrade an instance of.</param>
			/// <returns>True if this upgrader should attempt to upgrade an instance of the given type.</returns>
			public abstract bool ShouldProcessType( Type type );

			public bool TryCreateNewInstance( object oldInstance, out object newInstance )
			{
				if ( !OnTryCreateNewInstance( oldInstance, out newInstance ) )
				{
					return false;
				}

				if ( !OnTryUpgradeInstance( oldInstance, newInstance, false ) )
				{
					return true;
				}

				return true;
			}

			public bool TryUpgradeInstance( object oldInstance, object newInstance )
			{
				if ( !OnTryUpgradeInstance( oldInstance, newInstance, true ) )
				{
					return false;
				}

				return true;
			}

			protected void ScheduleProcessInstance( object oldInstance, object newInstance )
			{
				Hotload.ScheduleInstanceTask( this, oldInstance, newInstance );
			}

			protected void ScheduleLateProcessInstance( object oldInstance, object newInstance )
			{
				Hotload.ScheduleLateInstanceTask( this, oldInstance, newInstance );
			}

			protected AssemblyDefinition GetAssemblyDefinition( Assembly asm ) => Hotload.GetAssemblyDefinition( asm );
			protected TypeDefinition GetTypeDefinition( Type type ) => Hotload.GetTypeDefinition( type );
			protected MethodDefinition GetMethodDefinition( MethodBase method ) => Hotload.GetMethodDefinition( method );

			/// <summary>
			/// If this upgrader supports upgrading the given <paramref name="oldInstance"/>, returns <value>true</value> and
			/// assigns <paramref name="newInstance"/> to be the value that should replace <paramref name="oldInstance"/>. This
			/// method doesn't need to copy the inner state of the instance across, but just creates an empty instance to be
			/// populated later.
			/// </summary>
			/// <remarks>
			/// <para>
			/// It's safe to just directly assign <paramref name="newInstance"/> to <paramref name="oldInstance"/> if the type
			/// isn't declared in a replaced assembly.
			/// </para>
			/// <para>
			/// Returning true will cause <see cref="OnTryUpgradeInstance"/> to be called immediately after this method, which
			/// schedules copying the state of the old instance to the new one.
			/// </para>
			/// </remarks>
			/// <param name="oldInstance">Instance that should be replaced / upgraded.</param>
			/// <param name="newInstance">
			/// If this method returns true, this should contain the instance that replaces <paramref name="oldInstance"/>,
			/// or <paramref name="oldInstance"/> itself if no replacement is necessary.
			/// </param>
			/// <returns>True if this upgrader handles the replacement of the given <paramref name="oldInstance"/>.</returns>
			protected abstract bool OnTryCreateNewInstance( object oldInstance, out object newInstance );

			/// <summary>
			/// Called immediately after <see cref="OnTryCreateNewInstance"/> if it returned true, or on instances from fields
			/// that can't be re-assigned (see <see cref="FieldInfo.IsInitOnly"/>). This method determines what kind of extra
			/// processing is required for the given replacement.
			/// </summary>
			/// <remarks>
			/// <para>
			/// In this method we can call things like <see cref="ProcessInstance"/>, <see cref="ScheduleInstanceTask"/> or
			/// <see cref="ScheduleLateInstanceTask"/> to handle copying values from the old instance to the new one.
			/// </para>
			/// <para>
			/// If <paramref name="newInstance"/> should be cached as the canonical replacement for <paramref name="oldInstance"/>,
			/// call <see cref="AddCachedInstance"/> here.
			/// </para>
			/// <para>
			/// If finalization should be suppressed, call <see cref="SuppressFinalize"/>.
			/// </para>
			/// </remarks>
			/// <param name="oldInstance">Original instance that is being replaced / upgraded from.</param>
			/// <param name="newInstance">
			/// New instance that replaces <paramref name="oldInstance"/>, or <paramref name="oldInstance"/> itself if no replacement is necessary.
			/// </param>
			/// <param name="createdElsewhere">
			/// True if <paramref name="newInstance"/> was created outside of the hotloading system, for example when the
			/// containing field has <see cref="FieldInfo.IsInitOnly"/> set to true. Otherwise, when false, <see cref="OnTryCreateNewInstance"/>
			/// will have been called just before this method.
			/// </param>
			/// <returns></returns>
			protected abstract bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere );

			public int ProcessInstance( object oldInstance, object newInstance )
			{
				try
				{
					return OnProcessInstance( oldInstance, newInstance );
				}
#if !HOTLOAD_NOCATCH
				catch ( Exception e )
				{
					Log( e, member: oldInstance.GetType() );
					return 1;
				}
#endif
				finally
				{

				}
			}

			/// <summary>
			/// Perform extra field processing on a new instance that has previously been created by this upgrader in
			/// <see cref="OnTryCreateNewInstance"/>. This is a good place to discover any other instances that should be upgraded
			/// that are stored in <paramref name="oldInstance"/>, which can be upgraded by calling <see cref="GetNewInstance"/>.
			/// </summary>
			/// <param name="oldInstance">The original instance that was upgraded.</param>
			/// <param name="newInstance">Upgraded version of <paramref name="oldInstance"/>, or even the same object if no upgrade
			/// was required.</param>
			/// <returns>Roughly how many instances were processed by this method. Only used for performance stats.</returns>
			protected virtual int OnProcessInstance( object oldInstance, object newInstance )
			{
				return 1;
			}
		}
	}
}
