using System.Text.Json.Nodes;

namespace Sandbox;

public partial class GameObject
{
	/// <summary>
	/// Before GameObjectFlags.Static, static was authored per component. Flag objects with a
	/// static Collider or a MeshComponent (always level geometry). Rigidbodies are never static.
	/// </summary>
	[Expose, JsonUpgrader( typeof( GameObject ), 3 )]
	internal static void Upgrader_v3( JsonObject obj )
	{
		if ( obj[JsonKeys.Components] is not JsonArray components )
			return;

		if ( Game.TypeLibrary is null )
			return;

		var flags = (GameObjectFlags)obj.GetPropertyValue( JsonKeys.Flags, 0L );
		if ( flags.Contains( GameObjectFlags.Static ) )
			return;

		var isStatic = false;

		foreach ( var componentNode in components )
		{
			if ( componentNode is not JsonObject component )
				continue;

			var typeName = component.GetPropertyValue( Component.JsonKeys.Type, "" );
			if ( string.IsNullOrEmpty( typeName ) )
				continue;

			var type = Game.TypeLibrary.GetType<Component>( typeName, true );
			if ( type is null )
				continue;

			// Rigidbody means this is a physics object
			if ( type.TargetType.IsAssignableTo( typeof( Rigidbody ) ) )
				return;

			if ( type.TargetType.IsAssignableTo( typeof( MeshComponent ) ) )
			{
				isStatic = true;
			}
			else if ( type.TargetType.IsAssignableTo( typeof( Collider ) ) )
			{
				if ( component.GetPropertyValue( nameof( Collider.Static ), false ) )
					isStatic = true;
			}
		}

		if ( !isStatic )
			return;

		obj[JsonKeys.Flags] = (long)(flags | GameObjectFlags.Static);
	}
}
