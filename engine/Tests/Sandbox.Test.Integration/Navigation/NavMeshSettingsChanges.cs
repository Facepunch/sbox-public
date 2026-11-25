using Sandbox.Navigation;
using System;
using System.Collections.Generic;

namespace NavigationTests;

/// <summary>
/// Agent settings are fixed per library mesh. After a change the next full generation builds a second
/// mesh aside, and the one in use goes on serving queries and agents until the new one is complete.
/// </summary>
[TestClass]
public class NavMeshSettingsChanges
{
	// Updates until the mesh built aside has taken over and nothing is pending, counting how often
	// the mesh in use changed.
	static async Task<int> Rebuild( Scene scene, Action whileRebuilding = null )
	{
		int swaps = 0;
		var inUse = scene.NavMesh.mesh;
		for ( int i = 0; i < 5000; i++ )
		{
			scene.NavUpdate();
			if ( scene.NavMesh.mesh != inUse )
			{
				swaps++;
				inUse = scene.NavMesh.mesh;
			}
			if ( swaps == 0 ) whileRebuilding?.Invoke();
			else if ( inUse.PendingTiles == 0 ) return swaps;
			await Task.Delay( 1 );
		}
		Assert.Fail( "The rebuilt navmesh never took over" );
		return swaps;
	}

	[TestMethod]
	public async Task AChangedRadiusIsBuiltAsideWhileTheOldMeshServes()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		var area = scene.Area( new Vector3( -250, -250, 0 ), new Vector3( 100, 100, 200 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		var target = new Vector3( 300, -300, 0 );
		var agent = scene.Agent( new Vector3( -300, 300, 0 ) );
		agent.MoveTo( target );
		scene.GameTick();
		Assert.IsTrue( agent.IsNavigating );
		var request = new CalculatePathRequest { Start = new Vector3( -300, 0, 0 ), Target = new Vector3( 300, 0, 0 ) };
		var oldPath = scene.NavMesh.CalculatePath( request );

		var old = scene.NavMesh.mesh;
		scene.NavMesh.AgentRadius = 48;
		scene.NavMesh.SetDirty();
		scene.NavUpdate();
		Assert.AreSame( old, scene.NavMesh.mesh, "The mesh in use stays until the new one is built" );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 330, 0, 0 ), 4 ), "Queries are still answered from it" );

		// Changed while the new mesh is being built: both end up in it
		area.WorldPosition = new Vector3( -120, -120, 0 );
		var link = scene.Link<NavMeshLink>( new Vector3( 200, 200, 0 ), new Vector3( 200, -200, 0 ) );

		float closest = agent.AgentPosition.Distance( target.WithZ( 4 ) );
		int swaps = await Rebuild( scene, () =>
		{
			Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( request ).Status );

			// Every update brings the agent closer, until it is there
			float distance = agent.AgentPosition.Distance( target.WithZ( 4 ) );
			Assert.IsTrue( distance < 6 || (agent.IsNavigating && distance < closest), $"The agent should walk on while the new mesh is built, was {distance} away after {closest}" );
			closest = distance;
		} );

		Assert.AreEqual( 1, swaps );
		Assert.AreEqual( 48f, scene.NavMesh.mesh.Settings.AgentRadius );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( 330, 0, 0 ), 4 ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 300, 0, 0 ), 6 ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( -250, -250, 0 ) ), "The blocker is no longer where it was" );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( -120, -120, 0 ) ), "The blocker cuts where it was moved to" );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( -200, -120, 0 ) ), "And keeps the new radius away" );
		Assert.IsNotNull( link.WorldStartPositionOnNavmesh, "The link added meanwhile is in the new mesh" );
		Assert.IsNotNull( link.WorldEndPositionOnNavmesh );

		// The agent came along with its target, and paths of the old mesh are not taken any more
		Assert.IsTrue( agent.TargetPosition == target || agent.AgentPosition.Distance( target.WithZ( 4 ) ) < 6 );
		for ( int i = 0; i < 200; i++ ) scene.GameTick();
		Assert.IsTrue( agent.AgentPosition.Distance( target.WithZ( 4 ) ) < 6, $"The agent was at {agent.AgentPosition}" );

		var other = scene.Agent( request.Start );
		other.SetPath( oldPath );
		Assert.IsFalse( other.IsNavigating );
		other.SetPath( scene.NavMesh.CalculatePath( request ) );
		Assert.IsTrue( other.IsNavigating );
	}

	[TestMethod]
	public async Task ASecondChangeDuringTheRebuildStartsOver()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		scene.NavMesh.AgentRadius = 48;
		scene.NavMesh.SetDirty();
		scene.NavUpdate();
		scene.NavMesh.AgentRadius = 32;
		scene.NavMesh.SetDirty();

		Assert.AreEqual( 1, await Rebuild( scene ), "The mesh for the radius in between must never be in use" );
		Assert.AreEqual( 32f, scene.NavMesh.mesh.Settings.AgentRadius );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 316, 0, 0 ), 4 ) );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( 330, 0, 0 ), 4 ) );
	}

	[TestMethod]
	public async Task ASettingDraggedEveryUpdateDoesNotStartAMeshEveryUpdate()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 8000 );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var old = scene.NavMesh.mesh;

		// Another radius in every update, faster than a batch of tiles is built
		var started = new HashSet<Nav.NavMesh>();
		const int updates = 60;
		for ( int i = 0; i < updates; i++ )
		{
			scene.NavMesh.AgentRadius = 20 + i * 0.5f;
			scene.NavMesh.SetDirty();
			scene.NavUpdate();
			if ( scene.NavMesh.meshAside is { } aside ) started.Add( aside );
			Assert.AreSame( old, scene.NavMesh.mesh, "A mesh for a radius on the way must never be in use" );
		}
		Assert.IsTrue( started.Count > 0 && started.Count <= updates / 3, $"{started.Count} meshes were started in {updates} updates" );

		// The radius the drag ended on is built once it stops
		Assert.AreEqual( 1, await Rebuild( scene ) );
		Assert.AreEqual( 49.5f, scene.NavMesh.mesh.Settings.AgentRadius );
		Assert.IsFalse( scene.NavMesh.IsDirty );
	}

	[TestMethod]
	public async Task AChangeBackDropsTheMeshBeingBuilt()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var old = scene.NavMesh.mesh;

		scene.NavMesh.AgentRadius = 48;
		scene.NavMesh.SetDirty();
		scene.NavUpdate();
		scene.NavMesh.AgentRadius = 16;
		scene.NavMesh.SetDirty();
		await scene.Settle();
		for ( int i = 0; i < 20; i++ ) scene.NavUpdate();

		Assert.AreSame( old, scene.NavMesh.mesh );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 330, 0, 0 ), 4 ) );
	}

	[TestMethod]
	public async Task SettingsTheLibraryCannotBuildWithAreBroughtIntoRange()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 2000 );
		scene.NavMesh.AgentRadius = 100000;
		scene.NavMesh.AgentHeight = float.PositiveInfinity;
		scene.NavMesh.AgentStepSize = float.NaN;
		scene.NavMesh.AgentMaxSlope = 400;

		// No exception: the radius is 127 cells, the height the tallest a tile can hold
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreEqual( 508f, scene.NavMesh.mesh.Settings.AgentRadius );
		Assert.AreEqual( 0f, scene.NavMesh.mesh.Settings.AgentClimb );
		Assert.AreEqual( 90f, scene.NavMesh.mesh.Settings.AgentMaxSlope );

		scene.NavMesh.AgentRadius = -5;
		scene.NavMesh.AgentHeight = -1;
		scene.NavMesh.AgentMaxSlope = -3;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreEqual( 0f, scene.NavMesh.mesh.Settings.AgentRadius );
		Assert.AreEqual( 0f, scene.NavMesh.mesh.Settings.AgentMaxSlope );

		// No slope at all leaves nothing to walk on
		scene.NavMesh.AgentMaxSlope = 40;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 990, 0, 0 ), 6 ) );
	}
}
