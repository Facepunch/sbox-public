namespace Sandbox;

public static partial class Gizmo
{
	internal enum HitShapeType { Sphere, Box, Circle, Model, Sprite, Line }

	internal readonly record struct HitShape(
		HitShapeType Type, Vector3 A = default, Vector3 B = default,
		float Size = 0, float InnerSize = 0, Model Model = null, bool WorldSpace = true )
	{
		internal Transform Transform { get; init; }
		internal string Path { get; init; }
		internal object Object { get; init; }
		internal Component Component { get; init; }
		internal bool IsSceneGizmo { get; init; }
		internal float DepthBias { get; init; }
		internal bool CanInteract { get; init; }

		internal void Trace()
		{
			switch ( Type )
			{
				case HitShapeType.Sphere: Hitbox.Sphere( new Sphere( A, Size ) ); break;
				case HitShapeType.Box: Hitbox.BBox( new BBox( A, B ) ); break;
				case HitShapeType.Circle: Hitbox.Circle( A, B, Size, InnerSize ); break;
				case HitShapeType.Model: Hitbox.Model( Model ); break;
				case HitShapeType.Sprite: Hitbox.Sprite( A, Size, WorldSpace ); break;
				case HitShapeType.Line: Hitbox.AddPotentialLine( A, B, Size ); break;
			}
		}
	}

	public sealed partial class GizmoHitbox
	{
		Ray cachedRay;
		Transform cachedTransform;
		Ray localRay;

		Ray LocalRay
		{
			get
			{
				if ( cachedRay != CurrentRay || cachedTransform != Transform )
				{
					cachedRay = CurrentRay;
					cachedTransform = Transform;
					localRay = CurrentRay.ToLocal( Transform );
				}
				return localRay;
			}
		}

		static bool Record( HitShape shape )
		{
			if ( Active.IsPicking ) return false;
			var path = Active.lineScope.Enabled ? Active.lineScope.LinePath : Path;
			if ( Active.scope.CanInteract || Active.current.PressedPath == path )
				Active.hitShapes.Add( shape with
				{
					Transform = Gizmo.Transform,
					Path = path,
					Object = Gizmo.Object,
					Component = Active.scope.Component,
					IsSceneGizmo = Active.scope.IsSceneGizmo,
					DepthBias = Active.scope.HitDepthBias,
					CanInteract = Active.scope.CanInteract
				} );
			return true;
		}
	}
}
