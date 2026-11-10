using System.IO;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

internal sealed class ModelLoader( string fullPath ) : ResourceLoader<BaseMount>
{
	protected override object Load() => Host.ReadSourceModel( fullPath ).Build( Path );
}

internal sealed partial class SourceModelBuilder( BaseMount mount, string fullPath )
{
	private BaseMount Host { get; } = mount;
	private string FullPath { get; } = fullPath;
	private string ContentRoot { get; } = BaseMount.GetContentRoot( fullPath, "models" );
	internal Model BuildModel( MdlFile mdl, VtxFile vtx, PhyFile physics, string name = null, Transform[] referencePose = null,
		StaticPropLighting lighting = null )
	{
		var builder = Model.Builder.WithName( name );
		var viewBounds = GetViewBounds( mdl );
		var flexNames = mdl.FlexNames.ToArray();
		var bodyGroupNames = AddBodyGroups( builder, mdl );
		var materials = new MaterialSlots( this, mdl, vtx, lighting );
		var boneTransforms = GetBoneTransforms( mdl, referencePose );
		VertexTransform[] skinTransforms = null;
		if ( referencePose is not null )
		{
			skinTransforms = new VertexTransform[boneTransforms.Length];
			for ( var i = 0; i < skinTransforms.Length; i++ )
			{
				var matrix = mdl.Bones[i].PoseToBone;
				var bone = boneTransforms[i];
				skinTransforms[i] = new VertexTransform(
					bone.Rotation * new Vector3( matrix.M11, matrix.M21, matrix.M31 ),
					bone.Rotation * new Vector3( matrix.M12, matrix.M22, matrix.M32 ),
					bone.Rotation * new Vector3( matrix.M13, matrix.M23, matrix.M33 ),
					bone.PointToWorld( new Vector3( matrix.M14, matrix.M24, matrix.M34 ) ) );
			}
		}
		var boneBounds = AddGeometry( builder, mdl, vtx, materials, viewBounds, bodyGroupNames, flexNames, skinTransforms, lighting );
		materials.AddGroups( builder );

		var boneNames = AddSkeleton( builder, mdl, boneBounds, boneTransforms, parentLocal: vtx == null );
		PhysicsPlan physicsPlan = null;
		if ( physics != null )
		{
			try
			{
				physicsPlan = PreparePhysics( physics, mdl, boneTransforms, boneNames );
			}
			catch ( InvalidDataException e )
			{
				Log.Trace( $"{FullPath}: physics was not imported: {e.Message}" );
			}
			if ( physicsPlan != null )
				AddPhysics( builder, physics, physicsPlan );
		}

		AddMetadata( builder, mdl, boneNames );
		AddHitboxes( builder, mdl, boneNames );
		AddGameData( builder, mdl, physics );

		builder.WithViewBounds( viewBounds );
		var result = builder.Create();
		if ( physicsPlan != null )
			ValidatePhysics( result, physics, physicsPlan );
		return result;
	}
}
