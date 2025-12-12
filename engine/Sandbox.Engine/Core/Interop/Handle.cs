using Sandbox.Engine;
using System.Collections.Concurrent;
using System.Threading;

namespace Sandbox
{
	/// <summary>
	/// A base interface that all handles should use
	/// </summary>
	internal interface IHandle : IValid
	{
		internal bool HandleValid();
		internal void HandleInit( IntPtr ptr );
		internal void HandleDestroy();

		bool IValid.IsValid => HandleValid();

		internal static NextHandleScope MakeNextHandle( IHandle obj ) => new NextHandleScope( obj );

		internal ref struct NextHandleScope
		{
			IHandle self;
			IHandle last;

			public NextHandleScope( IHandle handle )
			{
				self = handle;
				last = HandleIndex.nextObject;
				HandleIndex.nextObject = self;
			}

			public void Dispose()
			{
				if ( HandleIndex.nextObject == self )
				{
					Log.Warning( "NextHandleScope: Didn't use nextObject!! WTF!!!" );
				}

				HandleIndex.nextObject = last;
			}
		}
	}

	/// <summary>
	/// This struct exists to differentiate the constructor of a handle object
	/// from the regular constructors. This way we can prevent clients creating
	/// the object manually, but still be able to create them at runtime.
	/// </summary>
	internal struct HandleCreationData
	{

	}

	/// <summary>
	/// An index that can convert from a handle (int) to a class. This is
	/// usually a static on your Handle object called HandleIndex.
	/// </summary>
	internal static class HandleIndex
	{
		static ConcurrentDictionary<int, IHandle> Index = new ConcurrentDictionary<int, IHandle>();
		static int lastIndex = 1;

		[ThreadStatic]
		internal static IHandle nextObject;

		public static int New<T>( IntPtr ptr, Func<HandleCreationData, T> createFunction ) where T : IHandle
		{
			T p;

			if ( nextObject != null )
			{
				p = (T)nextObject;
				nextObject = null;
			}
			else
			{
				p = createFunction( new HandleCreationData() );
			}

			p.HandleInit( ptr );

			var i = Interlocked.Increment( ref lastIndex );
			Index[i] = p;
			return i;
		}

		public static void Delete( int handle )
		{
			if ( !Index.TryRemove( handle, out var value ) )
				return;

			Assert.NotNull( value );
			value.HandleDestroy();
		}

		public static T Get<T>( int index ) where T : IHandle
		{
			if ( index < 0 )
				return default;

			if ( !Index.TryGetValue( index, out var handle ) )
				return default;

			if ( handle is T t )
			{
				return t;
			}

			Log.Warning( $"Couldn't find handle: {index} is {handle} - but wanted type {typeof( T )}" );
			return default;
		}

		internal static int RegisterHandle( IntPtr ptr, uint type )
		{
			// No auto create, should already be waiting in NextObject
			if ( type == Types.ManagedSceneObject ) return New<SceneObject>( ptr, null );

			if ( type == Types.SceneWorld ) return New<SceneWorld>( ptr, ( h ) => new SceneWorld( h ) );
			if ( type == Types.SceneObject ) return New<SceneObject>( ptr, ( h ) => new SceneObject( h ) );
			if ( type == Types.SceneLightObject ) return New<SceneLight>( ptr, ( h ) => new SceneLight( h ) );
			if ( type == Types.SceneSkyBoxObject ) return New<SceneSkyBox>( ptr, ( h ) => new SceneSkyBox( h ) );
			if ( type == Types.SceneAnimatableObject ) return New<SceneModel>( ptr, ( h ) => new SceneModel( h ) );
			if ( type == Types.EnvMapSceneObject ) return New<SceneCubemap>( ptr, ( h ) => new SceneCubemap( h ) );
			if ( type == Types.SceneLightProbeVolumeObject ) return New<SceneLightProbe>( ptr, ( h ) => new SceneLightProbe( h ) );
			if ( type == Types.DecalSceneObject ) return New<DecalSceneObject>( ptr, ( h ) => new DecalSceneObject( h ) );

			if ( type == Types.PhysicsWorld ) return New<PhysicsWorld>( ptr, ( h ) => new PhysicsWorld( h ) );
			if ( type == Types.PhysicsBody ) return New<PhysicsBody>( ptr, ( h ) => new PhysicsBody( h ) );
			if ( type == Types.PhysicsShape ) return New<PhysicsShape>( ptr, ( h ) => new PhysicsShape( h ) );
			if ( type == Types.PhysicsAggregate ) return New<PhysicsGroup>( ptr, ( h ) => new PhysicsGroup( h ) );

			if ( type == Types.PhysicsWeldJoint ) return New( ptr, ( h ) => new Sandbox.Physics.FixedJoint( h ) );
			if ( type == Types.PhysicsSpringJoint ) return New( ptr, ( h ) => new Sandbox.Physics.SpringJoint( h ) );
			if ( type == Types.PhysicsRevoluteJoint ) return New( ptr, ( h ) => new Sandbox.Physics.HingeJoint( h ) );
			if ( type == Types.PhysicsPrismaticJoint ) return New( ptr, ( h ) => new Sandbox.Physics.SliderJoint( h ) );
			if ( type == Types.PhysicsSphericalJoint ) return New( ptr, ( h ) => new Sandbox.Physics.BallSocketJoint( h ) );
			if ( type == Types.PhysicsPulleyJoint ) return New( ptr, ( h ) => new Sandbox.Physics.PulleyJoint( h ) );
			if ( type == Types.PhysicsMotorJoint ) return New( ptr, ( h ) => new Sandbox.Physics.ControlJoint( h ) );
			if ( type == Types.PhysicsWheelJoint ) return New( ptr, ( h ) => new Sandbox.Physics.WheelJoint( h ) );
			if ( type == Types.PhysicsFilterJoint ) return New( ptr, ( h ) => new Sandbox.Physics.PhysicsJoint( h ) );
			if ( type == Types.PhysicsJoint ) return New( ptr, ( h ) => new Sandbox.Physics.PhysicsJoint( h ) );

			if ( type == Types.PhysicsConicalJoint ) return New( ptr, ( h ) => new Sandbox.Physics.PhysicsJoint( h ) );
			if ( type == Types.PhysicsGenericJoint ) return New( ptr, ( h ) => new Sandbox.Physics.PhysicsJoint( h ) );
			if ( type == Types.PhysicsNullJoint ) return New( ptr, ( h ) => new Sandbox.Physics.PhysicsJoint( h ) );

			if ( type == Types.AudioStream ) return New( ptr, ( h ) => new SoundStream( h ) );

			var o = IToolsDll.Current?.RegisterHandle( ptr, type );
			if ( o != null && o.Value != -1 ) return o.Value;

			Log.Warning( $"RegisterHandle: Unhandled Handle Type {type} ({StringToken.GetValue( type )})" );
			return -1;
		}

		internal static void FreeHandle( int handle )
		{
			if ( handle < -1 )
			{
				Log.Warning( $"FreeHandle is {handle}" );
				return;
			}

			if ( handle < 0 )
				return;

			Delete( handle );
		}

		static class Types
		{
			// Scene
			internal static uint SceneWorld { get; } = StringToken.FindOrCreate( "SceneWorld" );
			internal static uint SceneObject { get; } = StringToken.FindOrCreate( "SceneObject" );
			internal static uint SceneLightObject { get; } = StringToken.FindOrCreate( "SceneLightObject" );
			internal static uint SceneSkyBoxObject { get; } = StringToken.FindOrCreate( "SceneSkyBoxObject" );
			internal static uint SceneParticleObject { get; } = StringToken.FindOrCreate( "SceneParticleObject" );
			internal static uint SceneAnimatableObject { get; } = StringToken.FindOrCreate( "SceneAnimatableObject" );
			internal static uint ManagedSceneObject { get; } = StringToken.FindOrCreate( "ManagedSceneObject" );
			internal static uint EnvMapSceneObject { get; } = StringToken.FindOrCreate( "EnvMapSceneObject" );
			internal static uint SceneLightProbeVolumeObject { get; } = StringToken.FindOrCreate( "SceneLightProbeVolumeObject" );
			internal static uint DecalSceneObject { get; } = StringToken.FindOrCreate( "DecalSceneObject" );

			// Physics
			internal static uint PhysicsWorld { get; } = StringToken.FindOrCreate( "PhysicsWorld" );
			internal static uint PhysicsBody { get; } = StringToken.FindOrCreate( "PhysicsBody" );
			internal static uint PhysicsJoint { get; } = StringToken.FindOrCreate( "PhysicsJoint" );
			internal static uint PhysicsShape { get; } = StringToken.FindOrCreate( "PhysicsShape" );
			internal static uint PhysicsAggregate { get; } = StringToken.FindOrCreate( "PhysAggregate" );

			internal static uint PhysicsWeldJoint { get; } = StringToken.FindOrCreate( "PhysicsWeldJoint" );
			internal static uint PhysicsRevoluteJoint { get; } = StringToken.FindOrCreate( "PhysicsRevoluteJoint" );
			internal static uint PhysicsSpringJoint { get; } = StringToken.FindOrCreate( "PhysicsSpringJoint" );
			internal static uint PhysicsSphericalJoint { get; } = StringToken.FindOrCreate( "PhysicsSphericalJoint" );
			internal static uint PhysicsPrismaticJoint { get; } = StringToken.FindOrCreate( "PhysicsPrismaticJoint" );
			internal static uint PhysicsConicalJoint { get; } = StringToken.FindOrCreate( "PhysicsConicalJoint" );
			internal static uint PhysicsMotorJoint { get; } = StringToken.FindOrCreate( "PhysicsMotorJoint" );
			internal static uint PhysicsWheelJoint { get; } = StringToken.FindOrCreate( "PhysicsWheelJoint" );
			internal static uint PhysicsFilterJoint { get; } = StringToken.FindOrCreate( "PhysicsFilterJoint" );
			internal static uint PhysicsPulleyJoint { get; } = StringToken.FindOrCreate( "PhysicsPulleyJoint" );
			internal static uint PhysicsGenericJoint { get; } = StringToken.FindOrCreate( "PhysicsGenericJoint" );
			internal static uint PhysicsNullJoint { get; } = StringToken.FindOrCreate( "PhysicsNullJoint" );

			internal static uint AudioStream { get; } = StringToken.FindOrCreate( "AudioStream" );
		};
	}
}
