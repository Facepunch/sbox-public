using System.Text.Json.Serialization;

namespace Sandbox.Volumes;

/// <summary>
/// A generic way to represent volumes in a scene. If we all end up using this instead of defining our own version
/// in everything, we can improve this and improve everything at the same time.
/// </summary>
[Expose]
public struct SceneVolume
{
	public SceneVolume()
	{
	}

	public enum VolumeTypes
	{
		/// <summary>
		/// A sphere. It's like the earth. Or an eyeball.
		/// </summary>
		Sphere,

		/// <summary>
		/// A box, like a cube.
		/// </summary>
		Box,

		/// <summary>
		/// A capsule, like a pill or a hotdog.
		/// </summary>
		Capsule,

		/// <summary>
		/// A global, infinite, boundless volume.
		/// </summary>
		Infinite = 1000,
	}

	[JsonInclude]
	public VolumeTypes Type = VolumeTypes.Box;

	[JsonInclude]
	[ShowIf( "Type", VolumeTypes.Sphere )]
	public Sphere Sphere = new Sphere( 0, 10 );

	[JsonInclude]
	[ShowIf( "Type", VolumeTypes.Box )]
	public BBox Box = BBox.FromPositionAndSize( 0, 100 );

	[JsonInclude]
	[ShowIf( "Type", VolumeTypes.Capsule ), InlineEditor]
	public Capsule Capsule = Capsule.FromHeightAndRadius( 100, 10 );

	/// <summary>
	/// Draws an editable sphere/box gizmo, for adjusting the volume
	/// </summary>
	public void DrawGizmos( bool withControls )
	{
		if ( Type == VolumeTypes.Sphere )
		{
			if ( withControls )
			{
				Gizmo.Control.Sphere( "Volume", Sphere.Radius, out Sphere.Radius, Color.Yellow );
			}

			Gizmo.Draw.IgnoreDepth = false;
			Gizmo.Draw.Color = Gizmo.Colors.Blue.WithAlpha( 0.8f );
			Gizmo.Draw.LineSphere( Sphere );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.05f );
			Gizmo.Draw.LineSphere( Sphere );
		}

		if ( Type == VolumeTypes.Box )
		{
			Gizmo.Draw.IgnoreDepth = false;

			if ( withControls )
			{
				Gizmo.Control.BoundingBox( "Volume", Box, out Box );
			}

			Gizmo.Draw.Color = Gizmo.Colors.Blue.WithAlpha( 0.8f );
			Gizmo.Draw.LineBBox( Box );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.05f );
			Gizmo.Draw.LineBBox( Box );
		}

		if ( Type == VolumeTypes.Capsule )
		{
			if ( withControls )
			{
				Gizmo.Control.Capsule( "Volume", Capsule, out Capsule, Color.Yellow );
			}

			Gizmo.Draw.IgnoreDepth = false;
			Gizmo.Draw.Color = Gizmo.Colors.Blue.WithAlpha( 0.8f );
			Gizmo.Draw.LineCapsule( Capsule );
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.05f );
			Gizmo.Draw.LineCapsule( Capsule );
		}
	}

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
