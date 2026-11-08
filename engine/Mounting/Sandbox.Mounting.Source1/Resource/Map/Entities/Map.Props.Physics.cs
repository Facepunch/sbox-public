using System;
using System.Globalization;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	internal static Transform[] ReadRagdollPose( PhysicsGroupDescription physics, Transform entityTransform, string angleOverride )
	{
		var poses = physics.Parts.Select( part => entityTransform.ToWorld( part.Transform ) ).ToArray();
		if ( string.IsNullOrWhiteSpace( angleOverride ) )
			return poses;
		var parents = Enumerable.Repeat( -1, poses.Length ).ToArray();
		foreach ( var joint in physics.Joints )
			parents[joint.Body1] = joint.Body2;
		var tokens = angleOverride.Split( ',' );
		if ( tokens.Length % 2 != 0 )
			throw new InvalidDataException( "Ragdoll angleOverride requires physics-body index/angle pairs." );
		for ( var i = 0; i < tokens.Length; i += 2 )
		{
			if ( !int.TryParse( tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index )
				|| index < 0 || index >= poses.Length )
				throw new InvalidDataException( $"Ragdoll angleOverride has invalid physics-body index '{tokens[i]}'." );
			if ( !Vector3.TryParse( tokens[i + 1], CultureInfo.InvariantCulture, out var angles )
				|| !float.IsFinite( angles.x ) || !float.IsFinite( angles.y ) || !float.IsFinite( angles.z ) )
				throw new InvalidDataException( $"Ragdoll angleOverride has invalid angles for physics body {index}." );
			var parent = parents[index];
			var position = parent < 0 ? entityTransform.Position
				: poses[parent].PointToWorld( physics.Parts[parent].Transform.PointToLocal( physics.Parts[index].Transform.Position ) );
			poses[index] = new Transform( position, Rotation.From( new Angles( angles.x, angles.y, angles.z ) ), entityTransform.Scale );
		}
		return poses;
	}

	internal static void ApplyRagdollPose( GameObject instance, Model model, string angleOverride, Transform mapTransform )
	{
		var poses = ReadRagdollPose( model.Physics, mapTransform.ToLocal( instance.WorldTransform ), angleOverride );
		var bones = model.CreateBoneObjects( instance );
		foreach ( var (bone, boneObject) in bones )
			boneObject.LocalTransform = bone.Parent is { } parent
				? parent.LocalTransform.ToLocal( bone.LocalTransform ) : bone.LocalTransform;
		foreach ( var part in model.Physics.Parts )
			bones[model.Bones.GetBone( part.BoneName )].Flags |= GameObjectFlags.Absolute | GameObjectFlags.PhysicsBone;
		for ( var i = 0; i < poses.Length; i++ )
			bones[model.Bones.GetBone( model.Physics.Parts[i].BoneName )].WorldTransform = mapTransform.ToWorld( poses[i] );
	}

	private sealed partial class EntityBuilder
	{
		private void ReportPhysicsSettings( MapProp prop, Model model, string path )
		{
			if ( prop.Solid == 0 )
				return;
			if ( model.Physics is not { Parts.Count: > 0 } )
			{
				warnings.Add( $"Solid prop model '{path}' has no imported physics; no substitute collision was created." );
				return;
			}

			if ( prop.Solid != 6 )
				warnings.Add( $"Prop model '{path}' uses solid mode {prop.Solid}; importing its authored model collision rather than Source-specific solid behavior." );
			if ( (prop.Gameplay || prop.Physics == PropPhysics.Rigid) && prop.MassScale > 0 && prop.MassScale != 1 )
				warnings.Add( $"Prop model '{path}' uses entity mass scaling; retaining authored mass because ModelCollider overrides Rigidbody mass when creating its shapes." );
		}

		private void AddPhysics( MapProp prop, ModelRenderer renderer, SkinnedModelRenderer ragdollRenderer, string path )
		{
			if ( prop.Solid == 0 )
				return;
			var model = renderer.Model;
			if ( model.Physics is not { Parts.Count: > 0 } )
				return;
			if ( prop.Physics == PropPhysics.Ragdoll )
			{
				AddRagdoll( prop, ragdollRenderer, path );
				return;
			}

			var collider = renderer.GameObject.AddComponent<ModelCollider>( false );
			collider.Static = prop.Physics == PropPhysics.Static;
			collider.Model = model;
			if ( prop.Physics == PropPhysics.Rigid )
				AddRigidbody( prop, renderer );
			collider.Enabled = true;
		}

		private void AddRigidbody( MapProp prop, ModelRenderer renderer )
		{
			double totalMass = 0;
			foreach ( var part in renderer.Model.Physics.Parts )
				totalMass += part.Mass;
			var mass = (float)totalMass;
			if ( !float.IsFinite( mass ) || mass <= 0 )
				throw new InvalidDataException( $"{owner.FullPath}: prop '{prop.Name}' has invalid physics mass." );
			var body = renderer.GameObject.AddComponent<Rigidbody>( false );
			body.MassOverride = mass;
			body.StartAsleep = prop.StartAsleep;
			body.MotionEnabled = prop.MotionEnabled;
			body.Enabled = true;
		}

		private void AddRagdoll( MapProp prop, SkinnedModelRenderer renderer, string path )
		{
			var model = renderer.Model;
			foreach ( var part in model.Physics.Parts )
			{
				if ( model.Bones.GetBone( part.BoneName ) is not null )
					continue;
				warnings.Add( $"Ragdoll model '{path}' has physics bodies without matching bones; no substitute collision was created." );
				return;
			}

			var physics = renderer.GameObject.AddComponent<ModelPhysics>( false );
			physics.Model = model;
			physics.Renderer = renderer;
			renderer.UseAnimGraph = false;
			if ( !string.IsNullOrWhiteSpace( prop.RagdollPose ) )
				ApplyRagdollPose( renderer.GameObject, model, prop.RagdollPose, World.WorldTransform );
			physics.StartAsleep = prop.StartAsleep;
			if ( physics.StartAsleep )
				warnings.Add( $"Ragdoll model '{path}' starts asleep; the engine can wake serialized ragdolls when restoring their physics." );
			physics.Enabled = true;
		}
	}
}
