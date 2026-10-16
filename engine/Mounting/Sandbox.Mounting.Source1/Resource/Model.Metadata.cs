using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private void AddMetadata( ModelBuilder builder, MdlFile metadata, string[] boneNames )
	{
		builder.WithHullBounds( GetHullBounds( metadata ) );
		builder.WithEyePosition( metadata.EyePosition );
		var attachmentIndex = 0;
		foreach ( var attachment in metadata.Attachments )
		{
			var name = metadata.GetAttachmentName( attachmentIndex++ );
			if ( attachment.LocalBone < 0 || attachment.LocalBone >= boneNames.Length )
				throw new InvalidDataException( $"MDL attachment '{name}' references an invalid bone." );
			var transform = GetAttachmentTransform( attachment.Local );
			builder.AddAttachment( name, transform.Position, transform.Rotation, boneNames[attachment.LocalBone] );
			if ( (attachment.Flags & 1) != 0 )
				Log.Trace( $"{FullPath}: world-aligned attachment '{name}' is imported with its authored local orientation." );
		}
	}

	private static Transform GetAttachmentTransform( MdlFile.Matrix3x4 matrix )
	{
		var forward = new Vector3( matrix.M11, matrix.M21, matrix.M31 );
		var up = new Vector3( matrix.M13, matrix.M23, matrix.M33 );
		var cross = Vector3.Cross( forward, up );
		if ( !float.IsFinite( cross.LengthSquared ) || cross.LengthSquared < 1e-12f )
			throw new InvalidDataException( "Invalid MDL attachment orientation." );
		var transform = new Transform( new Vector3( matrix.M14, matrix.M24, matrix.M34 ), Rotation.LookAt( forward, up ) );
		if ( !transform.IsValid )
			throw new InvalidDataException( "Invalid MDL attachment transform." );
		return transform;
	}

	private static string[] AddBodyGroups( ModelBuilder builder, MdlFile mdl )
	{
		long choices = 0;
		for ( var part = 0; part < mdl.BodyPartCount; part++ )
			choices += mdl.GetBodyPart( part ).NumModels;
		if ( choices > 64 )
			throw new NotSupportedException( $"MDL has {choices} bodygroup choices; the engine supports 64." );

		var names = new string[mdl.BodyPartCount];
		var usedNames = new HashSet<string>( StringComparer.Ordinal );
		for ( var part = 0; part < names.Length; part++ )
		{
			var sourceName = mdl.GetBodyPartName( part );
			if ( string.IsNullOrWhiteSpace( sourceName ) )
				sourceName = $"bodypart_{part}";
			var name = sourceName;
			for ( var suffix = 1; !usedNames.Add( name ); suffix++ )
				name = $"{sourceName}_{suffix}";
			names[part] = name;

			// Register blank alternatives too, so choice zero remains the authored default.
			for ( var choice = 0; choice < mdl.GetBodyPart( part ).NumModels; choice++ )
				builder.AddMesh( null, name, choice );
		}
		return names;
	}

	private BBox GetViewBounds( MdlFile mdl )
	{
		ValidateBounds( mdl.ViewMin, mdl.ViewMax );
		if ( mdl.ViewMin != Vector3.Zero || mdl.ViewMax != Vector3.Zero )
			return new BBox( mdl.ViewMin, mdl.ViewMax );

		return GetHullBounds( mdl );
	}

	private BBox GetHullBounds( MdlFile mdl )
	{
		if ( mdl.TotalVertices == 0 && mdl.HullMin == new Vector3( 9999 ) && mdl.HullMax == new Vector3( -9999 ) )
		{
			Log.Trace( $"{FullPath}: translating studiomdl's empty bounds sentinel to a native point bound for a geometry-free model." );
			return new BBox( Vector3.Zero, Vector3.Zero );
		}
		ValidateBounds( mdl.HullMin, mdl.HullMax );
		return new BBox( mdl.HullMin, mdl.HullMax );
	}

	private static void ValidateBounds( Vector3 mins, Vector3 maxs )
	{
		if ( !IsFinite( mins ) || !IsFinite( maxs )
			|| mins.x > maxs.x || mins.y > maxs.y || mins.z > maxs.z )
			throw new InvalidDataException( "Model bounds are non-finite or inverted." );
	}

	private static bool IsFinite( Vector3 value ) => float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z );
}
