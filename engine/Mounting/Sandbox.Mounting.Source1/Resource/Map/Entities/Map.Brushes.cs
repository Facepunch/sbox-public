using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private abstract class BrushEntityLoader : EntityLoader
	{
		public override void Load( EntityBuilder builder, MapEntity entity ) => builder.BuildBrush( entity, this );
		public virtual bool IsEnabled( MapEntity entity ) => true;
		public virtual bool IsSolid( MapEntity entity ) => true;
		public virtual bool CollidesWhenHidden( MapEntity entity ) => false;
		public virtual bool HasUnsupportedBehavior => false;
		public virtual bool AddInteraction( EntityBuilder builder, MapEntity entity, GameObject instance, BspModel model ) => false;
	}

	private sealed class ToggleBrushEntityLoader : BrushEntityLoader
	{
		public override bool IsEnabled( MapEntity entity ) => entity.Integer( "StartDisabled" ) == 0;
		public override bool IsSolid( MapEntity entity ) => entity.Integer( "Solidity" ) != 1;
		public override bool CollidesWhenHidden( MapEntity entity ) => entity.Integer( "Solidity" ) == 2;
	}

	private class WallEntityLoader : BrushEntityLoader
	{
		public override Angles ReadAngles( MapEntity entity ) => Angles.Zero;
	}

	private sealed class ToggleWallEntityLoader : WallEntityLoader
	{
		public override bool IsEnabled( MapEntity entity ) => (entity.Integer( "spawnflags" ) & 1) == 0;
	}

	private sealed class IllusionaryEntityLoader : WallEntityLoader
	{
		public override bool IsSolid( MapEntity entity ) => false;
	}

	private sealed class LodBrushEntityLoader : BrushEntityLoader
	{
		public override bool IsSolid( MapEntity entity ) => entity.Integer( "Solid" ) == 0;
		public override bool HasUnsupportedBehavior => true;
	}

	private sealed class ConveyorEntityLoader : WallEntityLoader
	{
		public override bool HasUnsupportedBehavior => true;
	}

	private class UnsimulatedBrushEntityLoader : BrushEntityLoader
	{
		public override bool HasUnsupportedBehavior => true;
	}

	private sealed class BreakableEntityLoader : UnsimulatedBrushEntityLoader
	{
		public override Angles ReadAngles( MapEntity entity ) =>
			string.IsNullOrWhiteSpace( entity.Get( "parentname" ) ) && entity.Vector( "gibdir" ) == Vector3.Zero ? Angles.Zero : entity.Angles;
	}

	private sealed partial class EntityBuilder
	{
		private readonly Dictionary<int, Model> brushModels = [];
		private readonly Dictionary<int, List<Model>> shadowBrushModels = [];
		private GameObject brushParent;
		public void BuildBrush( MapEntity entity, BrushEntityLoader loader )
		{
			var path = entity.Get( "model" );
			if ( path is null || !path.StartsWith( '*' ) || !int.TryParse( path.AsSpan( 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var index ) || index <= 0 || index >= bsp.Models.Length )
				throw new InvalidDataException( $"{owner.FullPath}: brush entity '{entity.Get( "classname" )}' has invalid model '{path}'." );
			if ( !brushModels.TryGetValue( index, out var model ) )
			{
				model = owner.BuildWorldModel( bsp, useAggregates: false, modelIndex: index, skybox: skybox ).Surfaces;
				brushModels.Add( index, model );
			}

			brushParent ??= new GameObject( true, "brushes" )
			{
				Parent = World
			};
			var className = entity.Get( "classname" ).ToLowerInvariant();
			var instance = new GameObject( true, entity.Get( "targetname" ) ?? className )
			{
				Parent = brushParent,
				IsStatic = true,
				LocalPosition = entity.Vector( "origin" ),
				LocalRotation = Rotation.From( loader.ReadAngles( entity ) )
			};
			instance.Tags.Add( "world" );
			var prop = ReadProp( entity );
			var visible = prop.Visible && (entity.Integer( "effects" ) & 32) == 0 && loader.IsEnabled( entity );
			if ( model is not null )
			{
				var renderer = instance.AddComponent<ModelRenderer>();
				renderer.Model = model;
				renderer.Tint = prop.Tint;
				renderer.Enabled = visible;
			}
			var moving = !skybox && loader.AddInteraction( this, entity, instance, bsp.Models[index] );
			instance.IsStatic = !moving;
			if ( loader.IsSolid( entity ) )
			{
				owner.BuildWorldPhysics( bsp, instance, modelIndex: index, staticCollider: !moving );
				foreach ( var collider in instance.GetComponentsInChildren<PhysicsCollider>( true ) )
					collider.Enabled = visible || loader.CollidesWhenHidden( entity );
			}
			if ( !skybox && loader.HasUnsupportedBehavior )
				warnings.Add( $"Entity '{className}' has no equivalent native behavior; retaining its geometry and collision without Source simulation." );
			if ( !skybox && (entity.Integer( "effects" ) & 16) == 0 )
			{
				if ( !shadowBrushModels.TryGetValue( index, out var shadowModel ) )
				{
					shadowModel = owner.BuildShadowModel( bsp, index );
					shadowBrushModels.Add( index, shadowModel );
				}

				owner.AddShadowBlocker( shadowModel, instance, visible );
			}
		}
	}
}
