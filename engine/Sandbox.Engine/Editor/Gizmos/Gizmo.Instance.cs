using Sandbox.Hashing;
using Sandbox.Utility;
using System.Runtime.InteropServices;

namespace Sandbox;

public static partial class Gizmo
{
	internal static Instance Active { get; set; }

	/// <summary>
	/// Allocation-free pool key for a gizmo scene object. <see cref="Type"/> compares by reference.
	/// </summary>
	internal readonly record struct GizmoObjectKey( ulong PathHash, int Create, ulong KeyHash, Type Type, int Disambiguator = 0 );

	/// <summary>
	/// Deterministic, allocation-free hash of a char span. Not <see cref="string.GetHashCode()"/> (per-process random).
	/// </summary>
	internal static ulong HashString( ReadOnlySpan<char> span ) => XxHash3.HashToUInt64( MemoryMarshal.AsBytes( span ) );

	/// <summary>
	/// Holds the backend state for a Gizmo scope. This allows us to have multiple different gizmo
	/// states (for multiple views, multiple windows, game and editor) and push them as the current
	/// active state whenever needed.
	/// </summary>
	[Expose]
	public partial class Instance : IDisposable
	{
		/// <summary>
		/// If true, we'll draw some debug information
		/// </summary>
		public bool Debug { get; set; } = false;

		/// <summary>
		/// If true we'll enable hitbox debugging
		/// </summary>
		public bool DebugHitboxes { get; set; } = false;

		/// <summary>
		/// The SceneWorld this instance is writing to. This world exists only for this instance.
		/// You need to add this world to your camera for it to render (!)
		/// </summary>
		SceneWorld _world;

		/// <summary>
		/// The world the gizmos are drawn into. Created on first use - every scene owns
		/// a gizmo instance, but most never draw a gizmo, so don't pay for a native
		/// world up front.
		/// </summary>
		public SceneWorld World => _world ??= new SceneWorld();

		/// <summary>
		/// Input state. Should be setup before push.
		/// </summary>
		public Inputs Input;

		/// <summary>
		/// The previous input state
		/// </summary>
		public Inputs PreviousInput => previous.Input;

		/// <summary>
		/// Last frame's objects that are available for reuse
		/// </summary>
		internal Dictionary<GizmoObjectKey, object> Pool { get; set; }

		/// <summary>
		/// This frame's created (or re-used) objects
		/// </summary>
		internal Dictionary<GizmoObjectKey, object> Entries { get; set; }

		/// <summary>
		/// This frame's created (or re-used) objects
		/// </summary>
		public SelectionSystem Selection { get; set; } = new SelectionSystem();

		//
		// stored frame states
		//

		internal Frame current;
		internal Frame previous;
		internal Frame pressed;

		/// <summary>
		/// When a new scope is pushed, we store the old one and copy it to this.
		/// When it's disposed we restore this to the old one. This is the currently
		/// active scope.
		/// </summary>
		internal ScopeState scope;
		internal GizmoDraw _renderer;
		internal GizmoDraw Renderer => _renderer ??= new();

		/// <summary>
		/// Holds the current hitbox line scope status
		/// </summary>
		internal HitboxLineScope lineScope;

		/// <summary>
		/// The current control mode. This is generally implementation specific.
		/// We tend to use "mouse" and "firstperson".
		/// </summary>
		public string ControlMode { get; set; }

		/// <summary>
		/// Some global settings accessible to the gizmos. Your implementation
		/// generally lets your users set up  these things to their preference,
		/// and the gizmos should try to obey them.
		/// </summary>
		public Gizmo.SceneSettings Settings { get; set; } = new SceneSettings();

		Dictionary<string, object> storage { get; } = new Dictionary<string, object>( StringComparer.OrdinalIgnoreCase );

		/// <summary>
		/// Generic storage for whatever you want to do.
		/// You're responsible for not spamming into this and cleaning up after yourself.
		/// </summary>
		public T GetValue<T>( string name )
		{
			if ( storage.TryGetValue( name, out var stored ) && stored is T t )
				return t;

			return default;
		}

		/// <summary>
		/// Generic storage for whatever you want to do.
		/// You're responsible for not spamming into this and cleaning up after yourself.
		/// </summary>
		public void SetValue<T>( string name, T value )
		{
			storage[name] = value;
		}

		/// <summary>
		/// Called when the scene changes and we don't want to inherit a bunch of values.
		/// We might want to just target some specific values here instead of clearing the log.
		/// </summary>
		public void Clear()
		{
			storage.Clear();
			_paths.Clear();
			hitShapes.Clear();
			displayedHitShapes.Clear();
			pointerEvents.Clear();
			_pickRevision = -1;
			_pickVisibility.Clear();
			_pickResult = default;
			_pickView = default;
			ClearPress();
		}


		/// <summary>
		/// how long the previous loop took, in milliseconds
		/// </summary>
		internal float LoopMilliseconds { get; set; }
		internal int ObjectsCreated { get; set; }
		internal int ObjectsDestroyed { get; set; }

		FastTimer timer;

		public Instance()
		{
			Entries = new();
			Pool = new();
		}

		/// <summary>
		/// Destroy this instance, clean up any created resources/scene objects, destroy the world.
		/// </summary>
		public void Dispose()
		{
			Clear();
			_renderer?.Clear();
			_renderer = null;
			_world?.Delete();
			_world = null;
			Pool?.Clear();
			Entries.Clear();
		}

		/// <summary>
		/// Push this instance as the global Gizmo state. All Gizmo calls during this scope
		/// will use this instance.
		/// </summary>
		public IDisposable Push() => ScopeLease<DisposeAction<Instance, Instance>>.Rent( PushScope() );

		/// <summary>
		/// Internal instance scope used by engine call sites.
		/// </summary>
		internal DisposeAction<Instance, Instance> PushScope()
		{
			var oldInstance = Sandbox.Gizmo.Active;

			timer.Start();

			Start();
			Sandbox.Gizmo.BeginInstance( this );
			RefreshInput();

			unsafe
			{
				static void RestorePreviousInstance( Instance currentInstance, Instance oldInstance )
				{
					currentInstance.DrawDebug();
					currentInstance.End();
					Sandbox.Gizmo.EndInstance( oldInstance );
				}

				return new DisposeAction<Instance, Instance>( &RestorePreviousInstance, this, oldInstance );
			}
		}

		/// <summary>
		/// Called at the start of a 'frame'
		/// </summary>
		void Start()
		{
			var p = Pool;
			Pool = Entries;
			Entries = p;
			Entries.Clear();
			_pathFrame++;

			previous = current;
			current.Input = Input;
			current.Click = false;
			(hitShapes, displayedHitShapes) = (displayedHitShapes, hitShapes);
			hitShapes.Clear();
			eventsProcessed = false;
			MousePressed = false;
			MouseReleased = false;
			HasReleaseInput = false;

			lineScope = default;

			scope.HitDepthBias = 1.0f;

			Hitbox.Debug = DebugHitboxes;

		}

		void DrawDebug()
		{
			LoopMilliseconds = (float)timer.ElapsedMilliSeconds;

			if ( !Debug )
				return;

			// debug
			var txt = @$"Time Taken: {(LoopMilliseconds):n0}ms
Objects: {Entries.Count:n0}
Created: {ObjectsCreated:n0}
Destroy: {ObjectsDestroyed:n0}
Hovered: {current.HoveredPath}
Pressed: {current.PressedPath}
Selected: {(current.SelectedPath == null ? "" : string.Join( ", ", current.SelectedPath ))}";

			Sandbox.Gizmo.Draw.Color = Color.White;
			Sandbox.Gizmo.Draw.ScreenText( txt, new Vector2( 10, 10 ), flags: TextFlag.LeftTop );
		}

		/// <summary>
		/// Called at the end of a 'frame'
		/// </summary>
		void End()
		{
			_renderer?.EndFrame();
			UpdateHitRevision();
			if ( pressOwner == this && (MouseReleased || (captureHasShape && !HasCapturedShape())) )
				ClearPress();
			foreach ( var entry in Pool )
			{
				if ( entry.Value is SceneObject so )
				{
					so.Delete();
					ObjectsDestroyed++;
				}
			}

			Pool.Clear();
			PrunePaths();

			// don't force the world to exist just to flush deletes
			_world?.DeletePendingObjects();
		}

		bool HasCapturedShape()
		{
			foreach ( var shape in hitShapes )
				if ( shape.Path == current.PressedPath ) return true;
			return false;
		}

		/// <summary>
		/// Find a cached version of this sceneobject - if not found, create one
		/// </summary>
		internal T FindOrCreate<T>( string key, Func<T> value ) where T : SceneObject
			=> FindOrCreate( key, value, static ( _, factory ) => factory() );

		internal T FindOrCreate<T, TState>( string key, TState state, Func<SceneWorld, TState, T> factory, bool flushGeometry = true ) where T : SceneObject
		{
			if ( flushGeometry ) _renderer?.Flush();
			Active.scope.Create++;

			var objectKey = new GizmoObjectKey(
				scope.PathHash,
				Active.scope.Create,
				HashString( key ),
				typeof( T ) );

			// Disambiguate key collisions so the second object doesn't stomp (and leak) the first in Entries.
			while ( Entries.ContainsKey( objectKey ) ) objectKey = objectKey with { Disambiguator = objectKey.Disambiguator + 1 };

			//
			// Do we have this in our pool (created last frame)
			// If so, we can re-use it.
			//
			Pool.Remove( objectKey, out var obj );

			//
			// If not then create a new one
			//
			if ( obj == null )
			{
				obj = factory( World, state );
				ObjectsCreated++;
			}

			if ( obj == null )
				return default;

			Entries[objectKey] = obj;
			return obj as T;
		}

		/// <summary>
		/// Set all of the state's cursor positions to this value. This stomps previous values
		/// which will effectively clear any deltas. This should be used prior to starting a loop.
		/// </summary>
		public void StompCursorPosition( Vector2 position )
		{
			current.Input.CursorPosition = position;
			previous.Input.CursorPosition = position;
		}
	}


}
