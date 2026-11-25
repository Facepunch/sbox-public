using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	[TestMethod]
	public void ScriptsCanSelectAndExtendNpcSchedules()
	{
		LoadSource( "npc.csx", """
			using Sandbox.Npcs;
			using Sandbox.Npcs.Schedules;
			using Sandbox.Npcs.Tasks;
			class ScriptNpc : BaseNpc
			{
				protected override BaseSchedule SelectSchedule() => GetSchedule<ScriptSchedule>();
				public BaseSchedule Choose() => SelectSchedule();
				public WanderSchedule Wander() => GetSchedule<WanderSchedule>();
			}
			class ScriptSchedule : BaseSchedule
			{
				public override int Priority => SchedulePriorities.Ambient;
				protected override void OnStart()
				{
					AddTask( new ScriptTask() );
					AddTask( new WaitTask( 1 ) );
				}
			}
			class ScriptTask : BaseTask
			{
				protected override TaskResult OnUpdate()
				{
					Npc.Health -= 1;
					return TaskResult.Success;
				}
			}
			""" );
		scene = new Scene();
		using var scope = scene.Push();
		var type = Game.TypeLibrary.GetType( "ScriptNpc" );
		var npc = (BaseNpc)scene.CreateObject().Components.Create( type );
		var choose = type.GetMethod( "Choose" );
		var schedule = choose.InvokeWithReturn<BaseSchedule>( npc );
		Assert.AreSame( schedule, choose.InvokeWithReturn<BaseSchedule>( npc ) );
		Assert.IsInstanceOfType<WanderSchedule>( type.GetMethod( "Wander" ).InvokeWithReturn<BaseSchedule>( npc ) );
		Assert.AreEqual( SchedulePriorities.Ambient, schedule.Priority );
		var health = npc.Health;
		schedule.InternalInit( npc );
		Assert.AreEqual( TaskResult.Running, schedule.InternalUpdate() );
		Assert.AreEqual( health - 1, npc.Health );
		schedule.InternalEnd( TaskResult.Failed );
	}

	[TestMethod]
	public void ScriptsCanAccessAnimationGraphParameters()
	{
		LoadSource( "graph-parameters.csx", """
			class ParameterProbe : Component
			{
				public bool Exercise()
				{
					var parameters = GetComponent<SkinnedModelRenderer>().Animation.Parameters;
					var target = new Transform( Vector3.Zero, Rotation.Identity );
					parameters.Set( "ik.foot_left", target, persist: true );
					bool stored = parameters.Contains( "ik.foot_left" );
					bool live = parameters.TrySet( "ik.foot_left", target );
					parameters.TrySet( "ik.foot_left.enabled", false );
					parameters.TryUnsetTarget( "ik.foot_left" );
					parameters.Clear();
					return stored && !live && !parameters.Contains( "ik.foot_left" );
				}
			}
			""" );
		scene = new Scene();
		using var scope = scene.Push();
		var type = Game.TypeLibrary.GetType( "ParameterProbe" );
		var renderer = scene.CreateObject().Components.Create( type );
		renderer.GameObject.Components.Create<SkinnedModelRenderer>();
		Assert.IsTrue( type.GetMethod( "Exercise" ).InvokeWithReturn<bool>( renderer ) );
	}
}
