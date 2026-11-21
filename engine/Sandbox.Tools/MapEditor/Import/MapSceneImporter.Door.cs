using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Sandbox.Mapping;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	static void ConvertDoor( GameObject target, MapLoader.ObjectEntry data, SourceNode source, List<string> warnings )
	{
		int mode = data.GetValue<int>( "movedir_type" );
		float initial = data.GetValue<float>( "initial_position" );
		var model = data.GetResource<Model>( "model" );
		var renderer = target.Components.Get<ModelRenderer>( FindMode.EverythingInSelf );
		var collider = target.Components.Get<ModelCollider>( FindMode.EverythingInSelf );
		if ( model is null )
		{
			renderer?.Destroy();
			collider?.Destroy();
		}
		else
		{
			// A prefab override can switch the same door between a moving model and an animated one.
			if ( renderer is not null && (renderer is SkinnedModelRenderer) != (mode == 2) )
				renderer.Destroy();

			if ( mode == 2 )
			{
				var animated = target.Components.GetOrCreate<SkinnedModelRenderer>();
				animated.Model = model;
#pragma warning disable CS0618 // Hammer doors drive AnimGraph1 parameters.
				animated.Set( "initial_position", initial / 100 );
				animated.Set( "update_position", initial > 0 );
				animated.Set( "open", initial >= 100 );
#pragma warning restore CS0618
			}
			else
			{
				target.Components.GetOrCreate<ModelRenderer>().Model = model;
			}
			(collider ?? target.Components.Create<ModelCollider>()).Model = model;
		}
		var door = target.Components.Get<Door>( FindMode.EverythingInSelf );
		if ( mode != 1 && door?.Pivot is { } hinge )
		{
			door.Pivot = null;
			hinge.Destroy();
		}
		if ( mode is not (0 or 1) )
		{
			door?.Destroy();
			warnings.Add( mode == 2 ? $"Animated door '{target.Name}' needs its game animation controller."
				: $"Door '{target.Name}' has unknown movement type {mode}." );
			return;
		}
		door ??= target.Components.Create<Door>();
		door.Mode = mode == 0 ? Door.DoorMode.Sliding : Door.DoorMode.Rotating;
		int flags = data.GetValue( "spawnsettings", 1 );
		door.IsUsable = (flags & 1) != 0;
		door.IsLocked = (flags & 2) != 0;
		door.OpenAwayFromPlayer = data.GetValue<bool>( "open_away" );
		door.Speed = data.GetValue( "speed", 100f );
		door.AutoCloseDelay = data.GetValue( "close_delay", 4f );
		door.AutoClose = door.AutoCloseDelay >= 0;
		door.StartOpen = initial >= 100;
		if ( initial is > 0 and < 100 ) warnings.Add( $"Door '{target.Name}' starts closed. Its Hammer start position is {initial}% open." );

		SoundEvent Sound( string key ) => data.GetString( key ) is { Length: > 0 } name ? SoundEvent.Find( name ) : null;
		door.OpenSound = Sound( "open_sound" );
		door.OpenFinishedSound = Sound( "fully_open_sound" );
		door.CloseSound = Sound( "close_sound" );
		door.CloseFinishedSound = Sound( "fully_closed_sound" );
		door.LockedSound = Sound( "locked_sound" );
		if ( ReadDoorCurve( data.GetString( "open_ease" ) ) is { } curve ) door.AnimationCurve = curve;

		var direction = data.GetValue<Angles>( "movedir" ).ToRotation();
		bool local = data.GetValue( "movedir_islocal", true );
		float distance = data.GetValue<float>( "distance" );
		if ( mode == 0 )
		{
			BBox? bounds = model?.PhysicsBounds;
			// Hammer falls back to the render bounds when the physics bounds are empty on any axis.
			if ( bounds is { Size: var size } && (size.x <= 0 || size.y <= 0 || size.z <= 0) )
				bounds = model.RenderBounds;
			void IncludeBrushes( SourceNode parentNode )
			{
				foreach ( var child in parentNode.Children )
				{
					if ( child.Kind != NodeKind.GroupOrMesh ) continue;
					if ( child.Meshes is { Count: > 0 } )
					{
						var fromMesh = source.Transform.ToLocal( child.Transform );
						foreach ( var part in child.Meshes )
						{
							var box = part.Mesh.CalculateBounds( fromMesh );
							bounds = bounds?.AddBBox( box ) ?? box;
						}
					}
					if ( child.Children is { Length: > 0 } ) IncludeBrushes( child );
				}
			}
			IncludeBrushes( source );
			var forward = direction.Forward;
			float travel = MathF.Abs( (bounds?.Size ?? Vector3.Zero).Dot( forward ) ) - distance;
			// Door.SlideOffset is expressed in the parent's frame.
			var worldOffset = (local ? target.WorldRotation * forward : forward) * travel;
			var parent = target.Parent?.WorldTransform ?? Transform.Zero;
			door.SlideOffset = (parent.Rotation.Inverse * worldOffset) / parent.SafeScale;
		}
		else
		{
			door.TargetAngle = distance;
			var rotation = local ? target.WorldRotation * direction : direction;
			if ( local || !rotation.Up.AlmostEqual( Vector3.Up ) || door.Pivot.IsValid() )
			{
				door.Pivot ??= new GameObject( target, true, "Hinge" );
				door.Pivot.WorldPosition = target.WorldPosition;
				door.Pivot.WorldRotation = rotation;
			}
		}
	}

	static Curve? ReadDoorCurve( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return null;
		int start = text.IndexOf( "m_spline", StringComparison.Ordinal );
		if ( start < 0 ) return null;
		int end = text.IndexOf( ']', start );
		if ( end < 0 ) return null;
		var frames = new List<Curve.Frame>();
		foreach ( Match block in Regex.Matches( text[start..end], @"\{([^{}]*)\}" ) )
		{
			float x = 0, y = 0, incoming = 0, outgoing = 0;
			foreach ( Match field in Regex.Matches( block.Groups[1].Value, @"(x|y|m_flSlopeIncoming|m_flSlopeOutgoing)\s*=\s*([-+.\deE]+)" ) )
			{
				float value = float.Parse( field.Groups[2].Value, CultureInfo.InvariantCulture );
				switch ( field.Groups[1].Value )
				{
					case "x": x = value; break;
					case "y": y = value; break;
					case "m_flSlopeIncoming": incoming = value; break;
					case "m_flSlopeOutgoing": outgoing = value; break;
				}
			}
			frames.Add( new Curve.Frame( x, y, -incoming, outgoing ) );
		}
		return frames.Count > 0 ? new Curve( frames ) : null;
	}
}
