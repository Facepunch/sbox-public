namespace Sandbox;

public sealed partial class PlayerController : Component
{
	/// <summary>
	/// The object we're currently looking at
	/// </summary>
	public Component Hovered { get; set; }

	/// <summary>
	/// The object we're currently using by holding down USE
	/// </summary>
	public Component Pressed { get; set; }

	/// <summary>
	/// Called in Update when Using is enabled
	/// </summary>
	public void UpdateLookAt()
	{
		if ( !EnablePressing )
			return;

		if ( Pressed.IsValid() )
		{
			UpdatePressed();
			return;
		}

		UpdateHovered();
	}

	/// <summary>
	/// Called every frame to update our pressed object
	/// </summary>
	void UpdatePressed()
	{
		if ( string.IsNullOrWhiteSpace( UseButton ) )
			return;

		//DebugOverlay.Text( Pressed.WorldPosition, $"Using: {Pressed}" );

		// keep pressing while use is down
		bool keepPressing = Input.Down( UseButton );

		// unless the IPressable says it wants to stop
		if ( keepPressing && Pressed is IPressable p )
		{
			keepPressing = p.Pressing( new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this } );
		}

		if ( GetDistanceFromGameObject( Pressed.GameObject, EyePosition ) > ReachLength )
		{
			keepPressing = false;
		}

		if ( !keepPressing )
		{
			StopPressing();
		}
	}

	float GetDistanceFromGameObject( GameObject obj, Vector3 point )
	{
		float distance = Vector3.DistanceBetween( obj.WorldPosition, EyePosition );

		foreach ( var c in Pressed.GetComponentsInChildren<Collider>() )
		{
			var closestPoint = c.FindClosestPoint( EyePosition );
			var dist = Vector3.DistanceBetween( closestPoint, EyePosition );

			if ( dist < distance )
			{
				distance = dist;
			}
		}

		return distance;
	}

	/// <summary>
	/// Called every frame to update our hovered status, unless it's being pressed
	/// </summary>
	void UpdateHovered()
	{
		SwitchHovered( TryGetLookedAt() );

		//DebugOverlay.Text( Hovered.WorldPosition, $"Looking: {Hovered}" );

		// If it's pressable then send an update to the hovered component every frame
		// This is to allow things like cursors to update their position
		if ( Hovered is IPressable pressable )
		{
			pressable.Look( new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this } );
		}

		// We are pressing "use", so press our hovered component
		if ( Input.Pressed( UseButton ) )
		{
			StartPressing( Hovered );
		}
	}

	/// <summary>
	/// Stop pressing. Pressed will become null.
	/// </summary>
	public void StopPressing()
	{
		if ( !Pressed.IsValid() )
			return;

		IEvents.PostToGameObject( GameObject, x => x.StopPressing( Pressed ) );

		if ( Pressed is IPressable pressable )
		{
			pressable.Release( new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this } );
		}

		Pressed = default;
	}

	/// <summary>
	/// Start pressing a target component. This is called automatically when Use is pressed.
	/// </summary>
	public void StartPressing( Component obj )
	{
		StopPressing();

		if ( !obj.IsValid() )
		{
			IEvents.PostToGameObject( GameObject, x => x.FailPressing() );
			return;
		}

		var pressable = obj.GetComponent<IPressable>();
		if ( pressable is not null )
		{
			if ( !pressable.CanPress( new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this } ) )
			{
				IEvents.PostToGameObject( GameObject, x => x.FailPressing() );
				return;
			}

			pressable.Press( new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this } );
		}

		Pressed = obj;

		IEvents.PostToGameObject( GameObject, x => x.StartPressing( obj ) );
	}

	/// <summary>
	/// Called every frame with the component we're looking at - even if it's null
	/// </summary>
	void SwitchHovered( Component obj )
	{
		var ev = new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this };

		// Didn't change
		if ( Hovered == obj )
		{
			if ( Hovered is IPressable stillHovering )
			{
				stillHovering.Look( ev );
			}

			return;
		}

		// Stop hovering old one
		if ( Hovered is IPressable stoppedHovering )
		{
			stoppedHovering.Blur( ev );
			Hovered = default;
		}

		Hovered = obj;

		// Start hovering new one
		if ( Hovered is IPressable startedHovering )
		{
			startedHovering.Hover( ev );
			startedHovering.Look( ev );
		}
	}

	/// <summary>
	/// Get the best component we're looking at. We don't just return any old component, by default
	/// we only return components that implement IPressable. Components can implement GetUsableComponent
	/// to search and provide better alternatives.
	/// </summary>
	Component TryGetLookedAt()
	{
		// Search with a ray first, and if that doesn't hit anything
		// use a bigger sphere until it's 4inches wide. This means that
		// when looking through holes we'll be able to use stuff, but also
		// when trying to use smaller things it won't be so fiddly. This is
		// what we did in Rust and it worked great.
		for ( float f = 0.0f; f <= 4.0f; f += 2.0f )
		{
			var hits = Scene.Trace
							.Ray( EyePosition, EyePosition + EyeAngles.Forward * (ReachLength - f) )
							.IgnoreGameObjectHierarchy( GameObject )
							.Radius( f )
							.HitTriggers()
							.RunAll();

			foreach ( var hit in hits )
			{
				// Get the GameObject of the collider, not the physics body
				var hitObject = hit.Collider?.GameObject ?? hit.GameObject;

				if ( !hitObject.IsValid() )
					continue;

				// Allow our other components to provide something
				Component foundComponent = default;
				IEvents.PostToGameObject( GameObject, x => foundComponent = x.GetUsableComponent( hitObject ) ?? foundComponent );

				if ( foundComponent.IsValid() )
					return foundComponent;

				// Check for IPressable components
				foreach ( var c in hitObject.GetComponents<IPressable>() )
				{
					if ( !c.CanPress( new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this } ) )
						continue;

					return c as Component;
				}

				// If we hit a non-trigger and found nothing pressable, we should stop
				// This prevents looking through solid objects
				// WorldPhysics is not a standard Collider
				if ( hit.Collider is null || !hit.Collider.IsTrigger )
					break;
			}
		}

		return default;
	}
}
