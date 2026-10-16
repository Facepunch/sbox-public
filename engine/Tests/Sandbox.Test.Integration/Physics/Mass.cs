
using System;

namespace PhysicsTests;

[TestClass]
public class MassTest
{
	[TestMethod]
	public void InertiaImplementationIsInternal()
	{
		Assert.IsNull( typeof( PhysicsBody ).GetMethod( "ScaleInertia" ) );
		Assert.IsNull( typeof( PhysicsGroupDescription.BodyPart ).GetProperty( "InertiaScale" ) );
		Assert.IsNotNull( typeof( Rigidbody ).GetProperty( "InertiaScale" ) );
		Assert.IsNotNull( typeof( PhysicsBodyBuilder ).GetProperty( "InertiaScale" ) );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void InertiaScalePersistsWithoutCompounding( bool twoDimensional )
	{
		var world = twoDimensional ? new PhysicsWorld( new PhysicsWorld2d() ) : new PhysicsWorld();
		try
		{
			var body = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Dynamic, Mass = 1 };
			body.AddSphereShape( Vector3.Zero, 1 );
			var initial = body.Inertia;
			body.ScaleInertia( 10 );
			Assert.IsTrue( body.Inertia.Distance( initial * 10 ) < 0.001f );
			body.ScaleInertia( 10 );
			Assert.IsTrue( body.Inertia.Distance( initial * 10 ) < 0.001f );
			body.Mass = 2;
			Assert.IsTrue( body.Inertia.Distance( initial * 20 ) < 0.001f );
			body.RebuildMass();
			Assert.IsTrue( body.Inertia.Distance( initial * 20 ) < 0.001f );
			body.BodyType = PhysicsBodyType.Static;
			body.BodyType = PhysicsBodyType.Dynamic;
			Assert.IsTrue( body.Inertia.Distance( initial * 20 ) < 0.001f );
			body.SetInertiaTensor( initial * 3, Rotation.Identity );
			body.ScaleInertia( 0.5f );
			Assert.IsTrue( body.Inertia.Distance( initial * 3 ) < 0.001f );
			body.ResetInertiaTensor();
			Assert.IsTrue( body.Inertia.Distance( initial ) < 0.001f );
			body.ScaleInertia( 1 );
			Assert.IsTrue( body.Inertia.Distance( initial * 2 ) < 0.001f );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => body.ScaleInertia( 0 ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => body.ScaleInertia( float.NaN ) );
		}
		finally
		{
			world.Delete();
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AuthoredDampingReducesVelocity( bool twoDimensional )
	{
		var world = twoDimensional ? new PhysicsWorld( new PhysicsWorld2d() ) : new PhysicsWorld();
		try
		{
			world.Gravity = Vector3.Zero;
			world.SleepingEnabled = false;
			var body = new PhysicsBody( world )
			{
				BodyType = PhysicsBodyType.Dynamic,
				Mass = 1,
				LinearDamping = 0.01f,
				AngularDamping = 1.5f
			};
			body.AddSphereShape( Vector3.Zero, 1 );
			body.Velocity = Vector3.Forward * 100;
			body.AngularVelocity = Vector3.Up;
			for ( var i = 0; i < 120; i++ )
				world.Step( 1.0f / 120.0f );
			Assert.AreEqual( 100 * MathF.Exp( -0.01f ), body.Velocity.Length, 0.02f );
			Assert.AreEqual( MathF.Exp( -1.5f ), body.AngularVelocity.Length, 0.005f );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// Tests to ensure mass override doesn't get reset
	/// </summary>
	[TestMethod]
	public void MassOverride()
	{
		var world = new PhysicsWorld();

		var body = new PhysicsBody( world );

		var massOverride = 10.0f;
		body.Mass = massOverride;
		body.BodyType = PhysicsBodyType.Dynamic;
		body.AddSphereShape( 0, 100 );

		Assert.AreEqual( massOverride, body.Mass );

		massOverride = 1234.0f;
		body.Mass = 0;
		var massComputed = body.Mass;
		body.Mass = massOverride;

		Assert.AreEqual( massOverride, body.Mass );

		body.Mass = 0;

		Assert.AreEqual( massComputed, body.Mass );

		body.Mass = massOverride;
		body.LocalMassCenter = Vector3.Up * 100;

		Assert.AreEqual( massOverride, body.Mass );
	}
}
