using System;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	static bool IsConstraint( string classname ) => classname is "phys_hinge" or "phys_lengthconstraint";

	static void ConnectConstraint( MapLoader.ObjectEntry data, GameObject owner, Func<string, GameObject> find, List<string> warnings )
	{
		var a = find( data.GetString( "attach1", "" ) );
		var b = find( data.GetString( "attach2", "" ) );
		bool HasBody( GameObject obj ) => obj.IsValid() && (obj.Components.Get<Prop>( FindMode.EverythingInSelf ) is not null || obj.Components.Get<Rigidbody>( FindMode.EverythingInSelf ) is not null || obj.Components.Get<Collider>( FindMode.EverythingInSelf ) is not null);
		bool swap = !HasBody( a ) || !HasBody( b );
		if ( swap )
		{
			// Hammer uses the world as the first body when only one attachment is physical.
			// Scene joints require a physical first body and use the world for the second.
			a = HasBody( a ) ? a : b;
			b = null;
		}
		if ( !HasBody( a ) )
		{
			warnings.Add( $"Constraint '{owner.Name}' has no physical attachment." );
			return;
		}

		// Anchor transforms must follow the bodies if this map becomes a prefab or instance.
		GameObject Anchor( GameObject parent, Vector3 at, Rotation frame )
		{
			var point = new GameObject( parent ?? owner, true, "Joint Anchor" );
			point.WorldTransform = new( at, frame );
			return point;
		}

		Joint joint;
		var start = owner.WorldPosition;
		var end = start;
		var rotation = Rotation.Identity;
		if ( data.TypeName == "phys_hinge" )
		{
			var axis = (data.GetValue<Vector3>( "hingeaxis" ) - start).Normal;
			if ( axis.LengthSquared > 0 ) rotation = Rotation.FromToRotation( Vector3.Up, swap ? -axis : axis );
			var hinge = owner.Components.Create<HingeJoint>();
			hinge.MinAngle = data.GetValue<float>( "min_rotation" );
			hinge.MaxAngle = data.GetValue<float>( "max_rotation" );
			hinge.Friction = data.GetValue<float>( "hingefriction" );
			joint = hinge;
		}
		else
		{
			end = data.GetValue<Vector3>( "attachpoint" );
			var spring = owner.Components.Create<SpringJoint>();
			spring.MaxLength = start.Distance( end ) + data.GetValue<float>( "addlength" );
			spring.MinLength = data.GetValue<bool>( "keeprigid" ) ? spring.MaxLength : data.GetValue<float>( "minlength" );
			spring.RestLength = start.Distance( end );
			spring.Frequency = 0;
			if ( swap ) (start, end) = (end, start);
			joint = spring;
		}
		joint.AnchorBody = Anchor( a, start, rotation );
		joint.Body = Anchor( b, end, rotation );
		joint.EnableCollision = data.GetValue<bool>( "enablecollision" );
		joint.BreakForce = data.GetValue<float>( "forcelimit" );
		joint.BreakTorque = data.GetValue<float>( "torquelimit" );
	}
}
