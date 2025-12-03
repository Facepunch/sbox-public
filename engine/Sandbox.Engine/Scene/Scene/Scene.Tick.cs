using Sandbox.Utility;
using static Sandbox.Component;

namespace Sandbox;

public partial class Scene : GameObject
{
	FixedUpdate fixedUpdate = new FixedUpdate();
	public bool IsFixedUpdate { get; private set; }

	public float FixedDelta => fixedUpdate.Delta;

	[Obsolete( "Moved to Sandbox.ProjectSettings.PhysicsSettings" )] public float FixedUpdateFrequency { get; set; } = 50.0f;
	[Obsolete( "Moved to Sandbox.ProjectSettings.PhysicsSettings" )] public int MaxFixedUpdates { get; set; } = 5;
	[Obsolete( "Moved to Sandbox.ProjectSettings.PhysicsSettings" )] public int PhysicsSubSteps { get; set; }
	[Obsolete( "Unused. Animation is always threaded." )] public bool ThreadedAnimation { get; set; } = true;
	[Obsolete( "Moved to Sandbox.ProjectSettings.PhysicsSettings" )] public bool UseFixedUpdate { get; set; }

	[Property, Range( 0, 1 )] public float TimeScale { get; set; } = 1.0f;

	/// <summary>
	/// The update loop will turn certain settings on
	/// Here we turn them to their defaults.
	/// </summary>
	void PreTickReset()
	{
		// Forward our preference to the Scene's PhysicsWorld
		if ( PhysicsWorld.IsValid() )
		{
			PhysicsWorld.SubSteps = Sandbox.ProjectSettings.Physics.SubSteps;
		}

		SceneWorld.GradientFog.Enabled = false;
	}

	double estimatedServerTime;

	/// <summary>
	/// Update the current time from the host
	/// </summary>
	internal void UpdateTimeFromHost( double time )
	{
		estimatedServerTime = time;

		if ( TimeNow == 0f )
		{
			TimeNow = time;
		}
	}

	internal double TimeNow { get; private set; }
	internal double TimeDelta { get; private set; } = 0.1;

	public void EditorTick( float timeNow, float timeDelta )
	{
		// Only tick here if we're an editor scene
		// The game will tick a game scene!
		if ( !IsEditor || !IsValid )
			return;

		TimeNow = timeNow;
		TimeDelta = timeDelta;

		using var timeScope = Time.Scope( TimeNow, TimeDelta );
		using var gizmoScope = gizmoInstance.Push();

		SharedTick();

		using ( PerformanceStats.Timings.NavMesh.Scope() )
		{
			Nav_Update();
		}
	}

	public void EditorDraw()
	{
		DebugDraw();
		DrawGizmos();
	}

	/// <summary>
	/// Run OnStart on all components that haven't had OnStart called yet
	/// </summary>
	internal void RunPendingStarts()
	{
		foreach ( var c in pendingStartComponents.EnumerateLocked() )
		{
			if ( !c.IsValid() )
				continue;

			c.InternalOnStart();
		}
	}

	internal void InternalUpdate()
	{
		RunPendingStarts();

		Signal( GameObjectSystem.Stage.Interpolation );

		foreach ( var c in updateComponents.EnumerateLocked( true ) ) c.InternalUpdate();
	}

	internal void PreRender()
	{
		foreach ( var c in preRenderComponents.EnumerateLocked() ) c.OnPreRenderInternal();
	}

	static Superluminal _updateTimer = new Superluminal( "Scene.Update", Color.Cyan );
	static Superluminal _preRenderTimer = new Superluminal( "Scene.PreRender", Color.Cyan );
	static Superluminal _signalUpdateBones = new Superluminal( "Signal.UpdateBones", Color.Cyan );
	static Superluminal _signalStarthUpdate = new Superluminal( "Signal.StartUpdate", Color.Cyan );
	static Superluminal _signalFinishUpdate = new Superluminal( "Signal.FinishUpdate", Color.Cyan );

	private void FixedUpdate()
	{
		if ( !ProjectSettings.Physics.UseFixedUpdate )
		{
			InternalFixedUpdate();
		}
		else
		{
			fixedUpdate.Frequency = ProjectSettings.Physics.FixedUpdateFrequency;

			IsFixedUpdate = true;
			fixedUpdate.Run( InternalFixedUpdate, Time.Now, ProjectSettings.Physics.MaxFixedUpdates );
			IsFixedUpdate = false;
		}
	}

	/// <summary>
	/// This is called in EditorTick and GameTick. It's only called in EditorTick if we're actually
	/// an editor scene. 
	/// </summary>
	void SharedTick()
	{
		Scene.RunEvent<ISceneStage>( x => x.Start() );

		if ( !IsEditor )
		{
			using ( PerformanceStats.Timings.Network.Scope() )
			{
				SceneNetworkUpdate();
			}
		}

		// no profile scope, profile inside instead
		{
			FixedUpdate();
		}

		using ( PerformanceStats.Timings.Scene.Scope() )
		{
			ProcessDeletes();

			using ( _signalStarthUpdate.Start() )
			{
				Signal( GameObjectSystem.Stage.StartUpdate );
			}

			using ( _updateTimer.Start() )
			{
				PreTickReset();
				InternalUpdate();
			}

			using ( _signalUpdateBones.Start() )
			{
				Signal( GameObjectSystem.Stage.UpdateBones );
			}

			if ( !Application.IsHeadless )
			{
				using ( _preRenderTimer.Start() )
				{
					PreRender();
				}
			}

			ProcessDeletes();

			using ( _signalFinishUpdate.Start() )
			{
				Signal( GameObjectSystem.Stage.FinishUpdate );
			}

			using ( PerformanceStats.Timings.Audio.Scope() )
			{
				SoundHandle.FlushCreatedSounds();
			}
		}

		Scene.RunEvent<ISceneStage>( x => x.End() );

		if ( !IsEditor )
		{
			using ( PerformanceStats.Timings.Async.Scope() )
			{
				SyncContext.FrameStage.PreRender.Trigger();
			}
		}

	}

	internal void SyncServerTime()
	{
		if ( Networking.IsHost ) return;

		// Estimate what the server time is now
		estimatedServerTime += TimeDelta;

		// How far off are we?
		var timeDifference = Math.Abs( TimeNow - estimatedServerTime );

		// If the time difference is large, snap to it
		if ( timeDifference > 0.25f )
		{
			TimeNow = estimatedServerTime;
			return;
		}

		// Smoothly lerp to the correct time
		// The larger the difference, the faster we lerp
		TimeNow = MathX.Lerp( TimeNow, estimatedServerTime, RealTime.Delta + timeDifference );
	}

	internal void UpdateTime( double delta )
	{
		if ( delta <= 0.0 ) return;

		TimeDelta = delta * TimeScale;
		TimeNow += TimeDelta;
	}

	public void GameTick( double timeDelta = 0.1 )
	{
		UpdateTime( timeDelta );

		if ( Camera is not null )
		{
			gizmoInstance.Input.Camera = Camera.SceneCamera;

			UpdateDefaultListener();
		}

		using var timeScope = Time.Scope( TimeNow, TimeDelta );
		using var gizmoScope = gizmoInstance.Push();

		using ( PerformanceStats.Timings.Async.Scope() )
		{
			SyncContext.FrameStage.Update.Trigger();
		}

		if ( IsLoading )
			return;

		if ( Game.IsPaused )
			return;

		SharedTick();

		// If we started loading, then try to run a full tick again - because we might
		// be able to immediately finish the load and be ready to render propertly on
		// the next render!
		if ( IsLoading )
		{
			GameTick();
		}

	}

	Input.Context FixedUpdateInputContext { get; set; } = Input.Context.Create( "Scene.FixedUpdate" );

	static Superluminal _fixedUpdateTimer = new Superluminal( "Scene.FixedUpdate", Color.Cyan );
	static Superluminal _processDeletesTimer = new Superluminal( "ProcessDeletes", Color.Orange );

	internal void InternalFixedUpdate()
	{
		FixedUpdateInputContext.Flip();
		using var _ = FixedUpdateInputContext.Push();

		using ( _fixedUpdateTimer.Start() )
		{
			Signal( GameObjectSystem.Stage.StartFixedUpdate );

			// All components that have not had OnStart() called yet
			// ?: Is there a chance this gets called before Update()? Should this even be here

			RunPendingStarts();

			{
				foreach ( var c in fixedUpdateComponents.EnumerateLocked() )
				{
					if ( !c.IsValid() )
						continue;

					c.InternalFixedUpdate();
				}
			}

			Signal( GameObjectSystem.Stage.PhysicsStep );

			if ( !IsEditor )
			{
				using ( PerformanceStats.Timings.NavMesh.Scope() )
				{
					Nav_Update();
				}
			}

			using ( _processDeletesTimer.Start() )
			{
				ProcessDeletes();
			}

			using ( PerformanceStats.Timings.Async.Scope() )
			{
				SyncContext.FrameStage.FixedUpdate.Trigger();
			}

			Signal( GameObjectSystem.Stage.FinishFixedUpdate );
		}

		Connection.ClearFixedUpdateContextInput();
	}
}
