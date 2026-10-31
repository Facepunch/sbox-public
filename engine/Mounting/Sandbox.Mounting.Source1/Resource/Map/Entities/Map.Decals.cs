using System;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private class DecalEntityLoader : EntityLoader
	{
		protected virtual bool Projected => false;
		public override void Load( EntityBuilder builder, MapEntity entity ) => builder.BuildDecal( entity, Projected );
	}

	private sealed class ProjectedDecalEntityLoader : DecalEntityLoader
	{
		protected override bool Projected => true;
	}

	private sealed partial class EntityBuilder
	{
		private readonly List<(MapEntity Entity, bool Projected)> authoredDecals = [];
		public void BuildDecal( MapEntity entity, bool projected ) => authoredDecals.Add( (entity, projected) );
		private void BuildAuthoredDecal( MapEntity entity, bool projected )
		{
			if ( !string.IsNullOrWhiteSpace( entity.Get( "targetname" ) ) )
			{
				warnings.Add( $"Triggered decal '{entity.Get( "targetname" )}' was omitted; Source entity inputs are not simulated." );
				return;
			}

			var path = entity.Get( "texture" );
			if ( string.IsNullOrWhiteSpace( path ) )
				throw new InvalidDataException( $"{owner.FullPath}: {entity.Get( "classname" )} has no decal texture." );
			var origin = entity.Vector( "origin" );
			var rotation = Rotation.From( entity.Angles );
			var distance = entity.Number( "Distance" );
			if ( projected && distance <= 0 )
				throw new InvalidDataException( $"{owner.FullPath}: projected decal '{path}' requires a positive Distance." );
			var start = projected ? origin : origin - new Vector3( 5 );
			var delta = projected ? rotation.Forward * distance : new Vector3( 10 );
			var hit = bsp.TraceDecal( start, delta, out var position, out var normal, skybox );
			var receiver = World;
			if ( !projected && !hit )
				hit = bsp.TraceDecal( start + delta, -delta, out position, out normal, skybox );
			if ( projected )
			{
				var transform = World.WorldTransform;
				var trace = World.Scene.Trace.Ray( transform.PointToWorld( start ), transform.PointToWorld( start + delta ) ).WithTag( "world" ).Run();
				if ( trace.Hit && !trace.StartedSolid && trace.Normal.LengthSquared > 1e-8f && trace.GameObject.IsValid() && trace.GameObject.IsAncestor( World ) && (!hit || trace.Fraction < (position - start).Length / delta.Length) )
				{
					receiver = trace.GameObject;
					position = transform.PointToLocal( trace.HitPosition );
					normal = transform.NormalToLocal( trace.Normal );
					hit = true;
				}
			}

			if ( !hit )
			{
				warnings.Add( $"Authored decal '{path}' at {origin} has no eligible world surface; omitted." );
				return;
			}

			try
			{
				var definition = owner.Host.GetDecal( path, owner.ContentRoot );
				var transform = World.WorldTransform;
				var child = new GameObject( true, "decal" )
				{
					Parent = receiver,
					IsStatic = true
				};
				child.WorldPosition = transform.PointToWorld( position );
				child.WorldRotation = transform.Rotation * DecalRotation( normal );
				child.Tags.Add( "world" );
				var decal = child.AddComponent<Decal>( false );
				decal.Decals = [definition];
				decal.Rotation = 0;
				decal.Transient = entity.Integer( "LowPriority" ) != 0;
				if ( decal.Transient )
					child.AddComponent<TemporaryEffect>();
				decal.Enabled = true;
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				warnings.Add( $"Authored decal '{path}' could not be imported: {e.Message}" );
			}
		}
	}

	internal static Rotation DecalRotation( Vector3 normal )
	{
		var s = Math.Abs( normal.z ) > 0.70710678f ? Vector3.Cross( normal, Vector3.Cross( Vector3.Forward, normal ) ).Normal : Vector3.Cross( normal, Vector3.Down ).Normal;
		var t = Vector3.Cross( s, normal ).Normal;
		return Rotation.LookAt( -normal, -t );
	}
}
