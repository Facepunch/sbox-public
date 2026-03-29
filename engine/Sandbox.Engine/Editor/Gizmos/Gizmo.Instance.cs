using Sandbox.Utility;

namespace Sandbox;

public static partial class Gizmo
{
	internal static Instance Active { get; set; }

	/// <summary>
	/// Holds the backend state for a Gizmo scope. This allows us to have multiple different gizmo
	/// states (for multiple views, multiple windows, game and editor) and push them as the current
	/// active state whenever needed.
	/// </summary>
	[Expose]
	public class Instance : IDisposable
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
		public SceneWorld World { get; init; }

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
		internal Dictionary<string, object> Pool { get; set; }

		/// <summary>
		/// This frame's created (or re-used) objects
		/// </summary>
		internal Dictionary<string, object> Entries { get; set; }

		/// <summary>
		/// This frame's created (or re-used) objects
		/// </summary>
		public SelectionSystem Selection { get; set; } = new SelectionSystem();

		//
		// stored frame states
		//

		internal Frame current;
		internal Frame previous;
		internal Frame builder;
		internal Frame pressed;

		/// <summary>
		/// When a new scope is pushed, we store the old one and copy it to this.
		/// When it's disposed we restore this to the old one. This is the currently
		/// active scope.
		/// </summary>
		internal ScopeState scope;

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
		}


		/// <summary>
		/// how long the previous loop took, in milliseconds
		/// </summary>
		internal float LoopMilliseconds { get; set; }
		internal int ObjectsCreated { get; set; }
		internal int ObjectsDestroyed { get; set; }

		FastTimer timer;

		/// <summary>
		/// To handle multiple viewports, so we keep track of which viewport is being pressed.
		/// </summary>
		private static string activePressedPath;
		private bool ownsActivePress;

		public Instance()
		{
			World = new SceneWorld();

			Entries = new();
			Pool = new();
		}

		/// <summary>
		/// Destroy this instance, clean up any created resources/scene objects, destroy the world.
		/// </summary>
		public void Dispose()
		{
			World?.Delete();
			Pool?.Clear();
			Entries.Clear();
		}

		/// <summary>
		/// Push this instance as the global Gizmo state. All Gizmo calls during this scope
		/// will use this instance.
		/// </summary>
		public IDisposable Push()
		{
			var oldInstance = Sandbox.Gizmo.Active;

			timer.Start();

			Start();
			Sandbox.Gizmo.BeginInstance( this );

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

			previous = current;
			current = builder;

			lineScope = default;
			builder = default;
			builder.HitDistance = float.MaxValue;
			builder.Input = Input;

			scope.HitDepthBias = 1.0f;

			Hitbox.Debug = DebugHitboxes;

			MouseUpdate();
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
			foreach ( var entry in Pool )
			{
				if ( entry.Value is SceneObject so )
				{
					so.Delete();
					ObjectsDestroyed++;
				}
			}

			Pool.Clear();
			World.DeletePendingObjects();
		}

		void MouseUpdate()
		{
			if ( !current.Input.IsHovered )
			{
				if ( ownsActivePress )
				{
					// Keep path alive in builder so Pressed.Any stays true until we regain hover and detect the actual release.
					builder.PressedPath = current.PressedPath;
				}
				else if ( !string.IsNullOrEmpty( activePressedPath ) )
				{
					// Mirror the owning instance's path so Pressed.Any correctly returns true across all views.
					builder.PressedPath = activePressedPath;
				}

				return;
			}

			// If we release the mouse outside of the viewport we pressed on, clear the stale press state.
			if ( ownsActivePress && !current.Input.LeftMouse && !previous.Input.LeftMouse )
			{
				ownsActivePress = false;
				activePressedPath = default;
			}

			//
			// left mouse button just clicked
			//
			if ( previous.Input.LeftMouse == false && current.Input.LeftMouse == true )
			{
				current.PressedPath = current.HoveredPath;
				activePressedPath = current.HoveredPath;
				ownsActivePress = true;
				pressed = current;
			}

			//
			// left mouse button just released
			//
			if ( previous.Input.LeftMouse == true && current.Input.LeftMouse == false )
			{
				ownsActivePress = false;
				activePressedPath = default;

				if ( current.HoveredPath == current.PressedPath )
				{
					current.Click = true;
				}
			}

			if ( current.Input.LeftMouse )
			{
				current.HoveredPath = current.PressedPath;
				builder.PressedPath = current.PressedPath;
			}

			builder.SelectedPath = current.SelectedPath;
		}

		/// <summary>
		/// Find a cached version of this sceneobject - if not found, create one
		/// </summary>
		internal T FindOrCreate<T>( string key, Func<T> value ) where T : SceneObject
		{
			Active.scope.Create++;
			var path = $"{Sandbox.Gizmo.Path}#{Active.scope.Create}/{key}/{typeof( T )}";

			//
			// Do we have this in our pool (created last frame)
			// If so, we can re-use it.
			//
			if ( Pool.TryGetValue( path, out var obj ) )
			{
				Pool.Remove( path );
			}

			//
			// If not then create a new one
			//
			if ( obj == null )
			{
				obj = value();
				ObjectsCreated++;
			}

			if ( obj == null )
				return default;

			//
			// If this happens then something is going wrong. We're creating objects with the same path
			// If we ignore it Entries values will get stomped and we'll leak objects. So create a unique value.
			//
			if ( Entries.ContainsKey( path ) )
			{
				path = $"{path}[{Guid.NewGuid()}]";
			}

			Entries[path] = obj;
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
			builder.Input.CursorPosition = position;
		}
	}


}
