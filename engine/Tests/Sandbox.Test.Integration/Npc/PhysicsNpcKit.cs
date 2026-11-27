using NavigationTests;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NpcTests;

/// <summary>
/// NPCs walked by physics, with a body shaped like the shipped NPC prefabs, and the scenes and ticking their tests share.
/// </summary>
static class PhysicsNpcKit
{
	public const float Tick = 0.02f;

	public static NpcTestKit.TestNpc PhysicsNpc( Scene scene, Vector3 position, bool solid = true, Model model = null )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;
		go.Components.Create<NavMeshAgent>();
		var animation = go.Components.Create<AnimationLayer>();
		go.Components.Create<NavigationLayer>();
		var body = go.Components.Create<Rigidbody>();
		body.Locking = new PhysicsLock { Pitch = true, Roll = true };
		body.MassOverride = 100;
		var collider = go.Components.Create<BoxCollider>();
		collider.Center = Vector3.Up * 36;
		collider.Scale = new Vector3( 25, 25, 72 );
		collider.IsTrigger = !solid;
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;

		if ( model is not null )
		{
			var renderer = scene.CreateObject();
			renderer.Parent = go;
			renderer.LocalPosition = Vector3.Zero;
			npc.Renderer = renderer.Components.Create<SkinnedModelRenderer>();
			npc.Renderer.Model = model;
		}

		return npc;
	}

	public static NpcTestKit.TestNpc Standing( Scene scene, Vector3 position, Model model = null )
	{
		var npc = PhysicsNpc( scene, position, model: model );
		Run( scene, () => npc.IsOnGround, "landed" );
		return npc;
	}

	public static NavigationLayer Navigation( BaseNpc npc ) => npc.GetComponent<NavigationLayer>();
	public static Rigidbody Body( Component component ) => component.GetComponent<Rigidbody>();
	public static bool Arrived( BaseNpc npc ) => Navigation( npc ).Status == MovementLayer.MovementStatus.Arrived;

	// One tick, then the tile rebuilds it started, which finish on worker threads, so they land on the same tick every run
	public static void Step( Scene scene )
	{
		scene.GameTick( Tick );
		for ( int i = 0; i < 5000 && scene.NavMesh.mesh?.PendingTiles > 0; i++ )
		{
			System.Threading.Thread.Sleep( 1 );
			scene.NavMesh.UpdateCache( scene.PhysicsWorld );
		}
	}

	// Ticks until done, calling each before every tick, and fails if it never is
	public static void Run( Scene scene, Func<bool> done, string what, int ticks = 600, Action each = null )
	{
		for ( int i = 0; i < ticks; i++ )
		{
			each?.Invoke();
			Step( scene );
			if ( done() ) return;
		}

		Assert.Fail( $"Never {what}" );
	}

	// Ticks this many times, checking after every tick
	public static void Ticks( Scene scene, int ticks, Action check = null )
	{
		for ( int i = 0; i < ticks; i++ )
		{
			Step( scene );
			check?.Invoke();
		}
	}

	public static async Task<Scene> Ground( float size = 2400 )
	{
		var scene = new Scene();
		scene.Floor( Vector3.Zero, size );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		return scene;
	}
}
