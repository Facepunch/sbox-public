using System.Text.Json.Serialization;

namespace Sandbox.Volumes;

/// <summary>
/// A generic way to represent volumes in a scene. If we all end up using this instead of defining our own version
/// in everything, we can improve this and improve everything at the same time.
/// </summary>
[Expose]
public struct SceneVolume : IEquatable<SceneVolume>
{
	/// <summary>
	/// Creates a box volume with editable defaults for every shape.
	/// </summary>
	public SceneVolume()
	{
	}

	/// <summary>
	/// Shapes supported by scene volumes. Existing serialized values remain stable.
	/// </summary>
	public enum VolumeTypes
	{
		/// <summary>
		/// A round area defined by its center and radius.
		/// </summary>
		[Icon( "circle" )]
		Sphere,

		/// <summary>
		/// A rectangular area with an editable width, depth and height.
		/// </summary>
		[Icon( "check_box_outline_blank" )]
		Box,

		/// <summary>
		/// A rounded tube defined by two endpoints and a radius.
		/// </summary>
		[Icon( "rounded_corner" )]
		Capsule,

		/// <summary>
		/// An unlimited area, with no boundary or dimensions to edit.
		/// </summary>
		[Icon( "all_inclusive" )]
		Infinite = 1000,
	}

	/// <summary>
	/// Active primitive.
	/// </summary>
	[JsonInclude, Title( "Shape Type" ), EnumButtonGroup( IconOnly = true )]
	public VolumeTypes Type = VolumeTypes.Box;

	/// <summary>
	/// Sphere center and radius in local space.
	/// </summary>
	[JsonInclude]
	[ShowIf( "Type", VolumeTypes.Sphere )]
	public Sphere Sphere = new Sphere( 0, 10 );

	/// <summary>
	/// Bounds of a box in local space.
	/// </summary>
	[JsonInclude]
	[ShowIf( "Type", VolumeTypes.Box )]
	public BBox Box = BBox.FromPositionAndSize( 0, 100 );

	/// <summary>
	/// Capsule endpoints and radius in local space.
	/// </summary>
	[JsonInclude]
	[ShowIf( "Type", VolumeTypes.Capsule ), InlineEditor]
	public Capsule Capsule = Capsule.FromHeightAndRadius( 100, 10 );

	/// <summary>
	/// Center of the active primitive; the origin for infinite volumes.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly Vector3 Center => Type switch
	{
		VolumeTypes.Sphere => Sphere.Center,
		VolumeTypes.Capsule => (Capsule.CenterA + Capsule.CenterB) * 0.5f,
		VolumeTypes.Box => Box.Center,
		_ => Vector3.Zero
	};

	/// <summary>
	/// Draws the primitive with the current gizmo color and depth settings.
	/// </summary>
	private readonly void DrawWireframe()
	{
		switch ( Type )
		{
			case VolumeTypes.Sphere:
				Gizmo.Draw.LineSphere( Sphere );
				break;
			case VolumeTypes.Capsule:
				Gizmo.Draw.LineCapsule( Capsule );
				break;
			case VolumeTypes.Box:
				Gizmo.Draw.LineBBox( Box );
				break;
		}
	}

	/// <summary>
	/// Draws the volume and optionally its standard shape controls.
	/// </summary>
	public void DrawGizmos( bool withControls ) => DrawGizmos( withControls, out _ );

	/// <summary>
	/// Draws the volume and reports edits made through its shape controls.
	/// </summary>
	public void DrawGizmos( bool withControls, out bool changed )
	{
		changed = false;
		if ( withControls )
		{
			if ( Type == VolumeTypes.Sphere )
			{
				using var center = Gizmo.Scope( "SphereCenter", new Transform( Sphere.Center ) );
				changed |= Gizmo.Control.Sphere( "Volume", Sphere.Radius, out Sphere.Radius, Color.Yellow );
			}
			else if ( Type == VolumeTypes.Capsule )
			{
				changed |= Gizmo.Control.Capsule( "Volume", Capsule, out Capsule, Color.Yellow );
			}
			else if ( Type == VolumeTypes.Box )
			{
				changed |= Gizmo.Control.BoundingBox( "Volume", Box, out Box );
			}
		}

		Gizmo.Draw.IgnoreDepth = false;
		Gizmo.Draw.Color = Gizmo.Colors.Blue.WithAlpha( 0.8f );
		DrawWireframe();
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = Color.White.WithAlpha( 0.05f );
		DrawWireframe();
	}

	/// <summary>
	/// Compares all serialized shape settings without boxing.
	/// </summary>
	public readonly bool Equals( SceneVolume other ) => Type == other.Type && Sphere.Equals( other.Sphere ) &&
		Box.Equals( other.Box ) && Capsule.Equals( other.Capsule );

	/// <summary>
	/// Compares another object with this volume.
	/// </summary>
	public override readonly bool Equals( object obj ) => obj is SceneVolume other && Equals( other );

	/// <summary>
	/// Hashes all serialized shape settings.
	/// </summary>
	public override readonly int GetHashCode() => HashCode.Combine( Type, Sphere, Box, Capsule );

	/// <summary>
	/// Is this point within the volume
	/// </summary>
	public bool Test( in Transform volumeTransform, in Vector3 position )
	{
		if ( Type == VolumeTypes.Infinite ) return true;

		return Test( volumeTransform.PointToLocal( position ) );
	}

	/// <summary>
	/// Is this point within the volume
	/// </summary>
	internal bool Test( in Transform volumeTransform, in BBox worldSphere )
	{
		// TODO!
		return false;
	}

	/// <summary>
	/// Is this point within the volume
	/// </summary>
	internal bool Test( in Transform volumeTransform, in Sphere worldSphere )
	{
		// TODO!
		return false;
	}

	/// <summary>
	/// Is this point within the (local space) volume
	/// </summary>
	public bool Test( in Vector3 position )
	{
		if ( Type == VolumeTypes.Infinite ) return true;

		if ( Type == VolumeTypes.Sphere )
		{
			return Sphere.Contains( position );
		}

		if ( Type == VolumeTypes.Box )
		{
			return Box.Contains( position );
		}

		if ( Type == VolumeTypes.Capsule )
		{
			return Capsule.Contains( position );
		}

		return false;
	}

	/// <summary>
	/// Get the actual amount of volume in this shape. This is useful if you want to make
	/// a system where you prioritize by volume size. Don't forget to multiply by scale!
	/// </summary>
	public float GetVolume()
	{
		if ( Type == VolumeTypes.Sphere )
		{
			return Sphere.Volume;
		}

		if ( Type == VolumeTypes.Box )
		{
			return Box.Volume;
		}

		if ( Type == VolumeTypes.Capsule )
		{
			return Capsule.Volume;
		}

		return 0.0f;
	}

	/// <summary>
	/// Calculates the shortest distance from the specified world position to the edge of this volume.
	/// </summary>
	/// <param name="worldTransform">The world transform of the volume.</param>
	/// <param name="worldPosition">The position in world space to measure from.</param>
	/// <returns>The distance, in world units, from the position to the volume edge.</returns>
	public float GetEdgeDistance( in Transform worldTransform, in Vector3 worldPosition )
	{
		// A huge number to represent "infinity" in this context
		if ( Type == VolumeTypes.Infinite ) return float.MaxValue;

		if ( Type == VolumeTypes.Sphere )
		{
			var localPos = worldTransform.PointToLocal( worldPosition );
			return Sphere.GetEdgeDistance( localPos );
		}

		if ( Type == VolumeTypes.Box )
		{
			var localPos = worldTransform.PointToLocal( worldPosition );
			return Box.GetEdgeDistance( localPos );
		}

		if ( Type == VolumeTypes.Capsule )
		{
			var localPos = worldTransform.PointToLocal( worldPosition );
			return Capsule.GetEdgeDistance( localPos );
		}

		return 0.0f;
	}

	/// <summary>
	/// Returns the axis-aligned bounding box that encloses the current volume.
	/// </summary>
	public BBox GetBounds()
	{
		if ( Type == VolumeTypes.Sphere )
		{
			return BBox.FromPositionAndSize( Sphere.Center, Vector3.One * Sphere.Radius * 2 );
		}
		if ( Type == VolumeTypes.Box )
		{
			return Box;
		}
		if ( Type == VolumeTypes.Capsule )
		{
			return Capsule.Bounds;
		}
		if ( Type == VolumeTypes.Infinite )
		{
			return new BBox( new Vector3( float.MinValue ), new Vector3( float.MaxValue ) );
		}

		return BBox.FromPositionAndSize( 0 );
	}
}
