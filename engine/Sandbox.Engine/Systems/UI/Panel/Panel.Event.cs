using System.Reflection;

namespace Sandbox.UI;

public partial class Panel
{
	internal struct EventCallback
	{
		public string EventName;
		public Action BaseAction;
		public Action<PanelEvent> Action;
		public bool Automatic;

		public Action<EventCallback, PanelEvent> Event;
		public Panel Panel;
		public Panel Context;
	}

	internal List<EventCallback> EventListeners { get; set; }

	/// <summary>
	/// Called on creation and hotload to delete and re-initialize event listeners.
	/// </summary>
	protected virtual void InitializeEvents()
	{
		EventListeners?.RemoveAll( x => x.Automatic );

		if ( Game.TypeLibrary == null || !Game.TypeLibrary.TryGetType( GetType(), out var typeDescription ) )
			return;


		foreach ( var methodDesc in typeDescription.Members.OfType<MethodDescription>() )
		{
			if ( methodDesc.IsStatic ) continue;

			var pea = methodDesc.Attributes.OfType<PanelEventAttribute>().FirstOrDefault();
			if ( pea == null ) continue;

			var method = methodDesc.MemberInfo as MethodInfo;
			var event_name = methodDesc.Name.ToLower();

			var args = method.GetParameters(); // todo cache
			var ret = method.ReturnParameter;

			if ( event_name.EndsWith( "event" ) ) event_name = event_name[..^5];
			if ( pea != null && pea.Name != null ) event_name = pea.Name.ToLower();

			if ( args.Length == 1 )
			{
				var argType = args[0].ParameterType;
				var argcache = new object[1];

				if ( argType == typeof( PanelEvent ) )
				{
					AddAutomaticEventListener( event_name, ( x ) => { argcache[0] = x; method.Invoke( this, argcache ); } );
				}
				else
				{
					AddAutomaticEventListener( event_name, ( x ) =>
					{
						argcache[0] = Convert.ChangeType( x.Value, argType );
						var response = method.Invoke( this, argcache );

						// If the response is a bool, and false, stop propogation
						if ( response != null && response is bool bReturnedValue )
						{
							x.Propagate = x.Propagate && bReturnedValue;
						}

					} );

				}
			}
			else if ( args.Length == 0 )
			{
				AddAutomaticEventListener( event_name, ( x ) =>
				{
					var response = method.Invoke( this, null );

					// If the response is a bool, and false, stop propogation
					if ( response != null && response is bool bReturnedValue )
					{
						x.Propagate = x.Propagate && bReturnedValue;
					}

				} );
			}
			else
			{
				Log.Warning( $"PanelEvent {method} - couldn't set up (too many arguments)" );
			}
		}
	}

	internal void AddAutomaticEventListener( string name, Action<PanelEvent> e )
	{
		EventListeners ??= new List<EventCallback>();

		var ev = new EventCallback
		{
			EventName = name,
			Action = e,
			Automatic = true
		};

		EventListeners.Add( ev );
	}

	internal void RemoveEventListener( string name )
	{
		EventListeners?.RemoveAll( x => x.EventName == name );
	}

	/// <summary>
	/// Runs given callback when the given event is triggered.
	/// </summary>
	public void AddEventListener( string eventName, Action<PanelEvent> e )
	{
		AddEventListener( new EventCallback
		{
			EventName = eventName,
			Action = e
		} );
	}

	/// <summary>
	/// Runs given callback when the given event is triggered, without access to the <see cref="PanelEvent"/>.
	/// </summary>
	public void AddEventListener( string eventName, Action action )
	{
		AddEventListener( new EventCallback
		{
			EventName = eventName,
			BaseAction = action
		} );
	}

	internal void AddEventListener( EventCallback eventCallback )
	{
		EventListeners ??= new List<EventCallback>();
		EventListeners.Add( eventCallback );
	}

	List<PanelEvent> PendingEvents;

	internal void RunPendingEvents()
	{
		if ( PendingEvents is null || PendingEvents.Count == 0 ) return;

		for ( int i = 0; i < PendingEvents.Count; i++ )
		{
			var e = PendingEvents[i];
			if ( e.Time > TimeNow ) continue;

			PendingEvents.RemoveAt( i );
			i--;

			try
			{
				OnEvent( e );
			}
			catch ( System.Exception ex )
			{
				Log.Error( ex, $"\"{ex.Message}\" when running panel event \"{e.Name}\" from \"{e.Target}\"" );
			}
		}
	}

	/// <summary>
	/// Create a new event and pass it to the panels event queue.
	/// </summary>
	/// <param name="name">Event name.</param>
	/// <param name="value">Event value.</param>
	/// <param name="debounce">Time, in seconds, to wait before firing the event.<br/>
	/// All subsequent calls to <see cref="CreateEvent(string, object, float?)"/> with the same event
	/// name will update the original event instead of creating a new event, until it finally triggers.</param>
	public virtual void CreateEvent( string name, object value = null, float? debounce = null )
	{
		var e = PendingEvents?.FirstOrDefault( x => x.Name == name );
		if ( e == null )
		{
			e = new PanelEvent( name, this );
			CreateEvent( e );
		}

		e.Value = value;

		if ( debounce.HasValue )
		{
			e.Time = (float)(TimeNow + debounce.Value);
		}
	}


	/// <summary>
	/// Pass given event to the event queue.
	/// </summary>
	public virtual void CreateEvent( PanelEvent evnt )
	{
		PendingEvents ??= new List<PanelEvent>();
		PendingEvents.Add( evnt );
	}

	/// <summary>
	/// Called when various <see cref="PanelEvent"/>s happen. Handles event listeners and many standard events by default.
	/// </summary>
	protected virtual void OnEvent( PanelEvent e )
	{
		e.This = this;

		if ( e is CopyEvent || e is CutEvent )
		{
			var text = GetClipboardValue( e is CutEvent );
			if ( text != null )
			{
				NativeEngine.EngineGlobal.Plat_SetClipboardText( text );
			}
		}

		if ( e is PasteEvent paste )
		{
			OnPaste( paste.ClipboardValue );
		}

		if ( e is MousePanelEvent mpe )
		{
			if ( e.Is( "onclick" ) ) OnClick( mpe );
			if ( e.Is( "onmiddleclick" ) ) OnMiddleClick( mpe );
			if ( e.Is( "onrightclick" ) ) OnRightClick( mpe );
			if ( e.Is( "onmousedown" ) ) OnMouseDown( mpe );
			if ( e.Is( "onmouseup" ) ) OnMouseUp( mpe );
			if ( e.Is( "ondoubleclick" ) ) OnDoubleClick( mpe );
			if ( e.Is( "onmousemove" ) ) OnMouseMove( mpe );
			if ( e.Is( "onmouseover" ) ) OnMouseOver( mpe );
			if ( e.Is( "onmouseout" ) ) OnMouseOut( mpe );

			if ( !e.Is( "onmousemove" ) )
			{
				razorTreeDirty = true;
			}
		}

		if ( e is DragEvent de )
		{
			InternalDragEvent( de );
		}

		if ( e.Is( "onfocus" ) ) OnFocus( e );
		if ( e.Is( "onblur" ) ) OnBlur( e );
		if ( e.Is( "onback" ) ) OnBack( e );
		if ( e.Is( "onforward" ) ) OnForward( e );
		if ( e.Is( "onescape" ) ) OnEscape( e );

		if ( e is SelectionEvent se )
		{
			if ( e.Is( "ondragselect" ) ) OnDragSelect( se );
		}

		if ( !e.Propagate )
			return;

		if ( EventListeners != null )
		{
			foreach ( var listener in EventListeners )
			{
				if ( !e.Is( listener.EventName ) ) continue;

				listener.Event?.Invoke( listener, e );
				listener.Action?.Invoke( e );
				listener.BaseAction?.Invoke();
			}
		}

		if ( !e.Propagate ) return;

		Parent?.OnEvent( e );
	}

	/// <summary>
	/// Called when the player releases their left mouse button (Mouse 1) while hovering this panel.
	/// </summary>
	protected virtual void OnClick( MousePanelEvent e ) { }

	/// <summary>
	/// Called when the player releases their middle mouse button (Mouse 3) while hovering this panel.
	/// </summary>
	protected virtual void OnMiddleClick( MousePanelEvent e ) { }

	/// <summary>
	/// Called when the player releases their right mouse button (Mouse 2) while hovering this panel.
	/// </summary>
	protected virtual void OnRightClick( MousePanelEvent e ) { }

	/// <summary>
	/// Called when the player presses down the left or right mouse buttons while hovering this panel.
	/// </summary>
	protected virtual void OnMouseDown( MousePanelEvent e ) { }

	/// <summary>
	/// Called when the player releases left or right mouse button.
	/// </summary>
	protected virtual void OnMouseUp( MousePanelEvent e ) { }

	/// <summary>
	/// Called when the player double clicks the panel with the left mouse button.
	/// </summary>
	protected virtual void OnDoubleClick( MousePanelEvent e ) { }

	/// <summary>
	/// Called when the cursor moves while hovering this panel.
	/// </summary>
	protected virtual void OnMouseMove( MousePanelEvent e ) { }

	/// <summary>
	/// Called when the cursor enters this panel.
	/// </summary>
	protected virtual void OnMouseOver( MousePanelEvent e ) { }

	/// <summary>
	/// Called when the cursor leaves this panel.
	/// </summary>
	protected virtual void OnMouseOut( MousePanelEvent e ) { }

	/// <summary>
	/// Called when the player presses the "Back" button while hovering this panel, which is typically "mouse 5", aka one of the mouse buttons on its side.
	/// </summary>
	protected virtual void OnBack( PanelEvent e ) { }


	/// <summary>
	/// Called when the player presses the "Forward" button while hovering this panel, which is typically "mouse 4", aka one of the mouse buttons on its side.
	/// </summary>
	protected virtual void OnForward( PanelEvent e ) { }

	/// <summary>
	/// Called when the escape key is pressed
	/// </summary>
	protected virtual void OnEscape( PanelEvent e )
	{
		if ( HasFocus )
			Blur();
	}

	/// <summary>
	/// Called when this panel receives input focus.
	/// </summary>
	protected virtual void OnFocus( PanelEvent e ) { }

	/// <summary>
	/// Called when this panel loses input focus.
	/// </summary>
	protected virtual void OnBlur( PanelEvent e ) { }
}
