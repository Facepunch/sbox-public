using System;
using Door = Sandbox.Mapping.Door;
using Button = Sandbox.Mapping.Button;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private class DoorEntityLoader : BrushEntityLoader
	{
		protected virtual Door.DoorMode Mode => Door.DoorMode.Sliding;

		public override bool AddInteraction( EntityBuilder builder, MapEntity entity, GameObject instance, BspModel model )
		{
			var flags = entity.Integer( "spawnflags" );
			var door = instance.AddComponent<Door>( false );
			door.Mode = Mode;
			door.Speed = entity.Number( "speed", 100 );
			if ( door.Speed == 0 ) door.Speed = 100;
			door.StartOpen = entity.Integer( "spawnpos" ) == 1 || (flags & 1) != 0;
			door.IsLocked = entity.Integer( "locked" ) != 0 || (flags & 2048) != 0;
			door.OpenAwayFromPlayer = false;
			door.IsUsable = (flags & 256) != 0 && (flags & 32768) == 0;
			var wait = entity.Number( "wait", 4 );
			door.AutoClose = wait >= 0 && (flags & 32) == 0;
			door.AutoCloseDelay = Math.Max( 0, wait );
			door.SlideOffset = BrushMove( entity, model, 0 );
			door.TargetAngle = entity.Number( "distance", 90 ) * ((flags & 2) != 0 ? -1 : 1);
			if ( door.Mode == Door.DoorMode.Rotating && (flags & 192) != 0 )
			{
				var axis = BrushRotationAxis( entity );
				var reference = Math.Abs( axis.z ) < 0.99f ? Vector3.Up : Vector3.Forward;
				var pivot = new GameObject( true, "door pivot" )
				{
					Parent = instance.Parent,
					LocalPosition = instance.LocalPosition,
					LocalRotation = Rotation.LookAt( Vector3.Cross( axis, reference ), axis )
				};
				door.Pivot = pivot;
			}
			builder.RegisterEntityIO( entity, door );
			if ( (flags & 1024) != 0 )
				builder.ReportWarning( $"Door '{instance.Name}' requests touch activation, which has no equivalent native door event; only supported use and Doo inputs are imported." );
			door.Enabled = true;
			return true;
		}
	}

	private sealed class RotatingDoorEntityLoader : DoorEntityLoader
	{
		protected override Door.DoorMode Mode => Door.DoorMode.Rotating;
	}

	private sealed class ButtonEntityLoader : BrushEntityLoader
	{
		public override bool AddInteraction( EntityBuilder builder, MapEntity entity, GameObject instance, BspModel model )
		{
			var flags = entity.Integer( "spawnflags" );
			var button = instance.AddComponent<Button>( false );
			button.Mode = Button.ButtonMode.Toggle;
			button.MoveDelta = BrushMove( entity, model, entity.Number( "lip" ) == 0 ? 4 : entity.Number( "lip" ) );
			button.Move = (flags & 1) == 0 && button.MoveDelta.Length >= 1;
			var speed = entity.Number( "speed", 40 );
			if ( speed == 0 ) speed = 40;
			button.AnimationTime = button.Move ? button.MoveDelta.Length / Math.Max( 1, speed ) : 0;
			var wait = entity.Number( "wait", 1 );
			if ( wait == 0 ) wait = 1;
			button.AutoReset = wait >= 0 && (flags & 32) == 0;
			button.ResetTime = Math.Max( 0, wait );
			builder.RegisterEntityIO( entity, button );
			button.Enabled = true;
			return true;
		}
	}

	internal static Vector3 BrushRotationAxis( MapEntity entity )
	{
		var flags = entity.Integer( "spawnflags" );
		if ( (flags & 64) != 0 )
			return Rotation.From( entity.Angles ).Forward;
		if ( (flags & 128) != 0 )
			return Rotation.FromYaw( entity.Angles.yaw ).Left;
		return Vector3.Up;
	}

	internal static Vector3 BrushMove( MapEntity entity, BspModel model, float defaultLip )
	{
		var angles = entity.Vector( "movedir" );
		var direction = Rotation.From( new Angles( angles.x, angles.y, angles.z ) ).Forward;
		var size = Vector3.Max( model.Maxs - model.Mins, Vector3.Zero );
		var lip = entity.Number( "lip", defaultLip );
		if ( lip == 0 ) lip = defaultLip;
		var distance = Math.Abs( direction.x * size.x ) + Math.Abs( direction.y * size.y ) + Math.Abs( direction.z * size.z ) - lip;
		return direction * distance;
	}
}
