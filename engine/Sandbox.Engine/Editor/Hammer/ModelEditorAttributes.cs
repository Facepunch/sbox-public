using Sandbox.ModelEditor.Internal;
using System;
using System.Text;

namespace Sandbox.ModelEditor.Internal
{
	public class BaseModelDocAttribute : Editor.MetaDataAttribute
	{
		internal string HelperName = "";

		public BaseModelDocAttribute( string name )
		{
			HelperName = name;
		}

		public override void AddHelpers( List<Tuple<string, string[]>> helpers )
		{
			StringBuilder sb = new();
			sb.AppendLine( "{" );

			AddTransform( sb );

			Dictionary<string, object> KVs = new();
			AddKeys( KVs );
			foreach ( var pair in KVs )
			{
				// Skip empty strings
				if ( pair.Value == null || pair.Value is string && string.IsNullOrEmpty( (string)pair.Value ) ) continue;

				sb.AppendLine( $"\t{pair.Key.QuoteSafe()} = {(pair.Value is string @string ? @string.QuoteSafe() : pair.Value.ToString().ToLowerInvariant())}" );
			}
			sb.AppendLine( "}" );

			helpers.Add( Tuple.Create( HelperName, new string[] { sb.ToString() } ) );
		}

		/// <summary>
		/// Internal, used to add multi level key-values.
		/// </summary>
		protected virtual void AddTransform( StringBuilder sb )
		{
		}

		/// <summary>
		/// Add generic key-values to the helper.
		/// </summary>
		protected virtual void AddKeys( Dictionary<string, object> dict )
		{
		}
	}

	public class BaseTransformAttribute : BaseModelDocAttribute
	{
		/// <summary>
		/// Internal name of the key that dictates which bone to use as parent for position/angles.
		/// </summary>
		public string Bone { get; set; }

		/// <summary>
		/// Internal name of the key that dictates which attachment to use as parent for position/angles.
		/// </summary>
		public string Attachment { get; set; }

		/// <summary>
		/// Internal name of the key to store position in, if set, allows the helper to be moved.
		/// </summary>
		public string Origin { get; set; }

		/// <summary>
		/// Internal name of the key to store angles in, allows the helper to be rotated.
		/// </summary>
		public string Angles { get; set; }

		// These are present in the helper but do unknown things, unsed by default nodes
		//public bool transform_chain { get; set; }
		//public bool true_worldspace { get; set; }
		//public string translation_only_key { get; set; }

		public BaseTransformAttribute( string name ) : base( name )
		{
		}

		protected override void AddTransform( StringBuilder sb )
		{
			var transformKeys = "";
			if ( !string.IsNullOrEmpty( Origin ) ) transformKeys += $"\t\torigin_key = {Origin.QuoteSafe()}\r\n";
			if ( !string.IsNullOrEmpty( Angles ) ) transformKeys += $"\t\tangles_key = {Angles.QuoteSafe()}\r\n";
			if ( !string.IsNullOrEmpty( Attachment ) ) transformKeys += $"\t\tattachment_key = {Attachment.QuoteSafe()}\r\n";
			if ( !string.IsNullOrEmpty( Bone ) ) transformKeys += $"\t\tbone_key = {Bone.QuoteSafe()}\r\n";

			if ( !string.IsNullOrEmpty( transformKeys ) )
			{
				sb.AppendLine( $"\ttransform =\r\n\t{{\r\n{transformKeys}\t}}" );
			}
		}
	}
}

namespace Sandbox.ModelEditor
{
	/// <summary>
	/// Indicates that this class/struct should be available as GenericGameData node in ModelDoc
	/// </summary>
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct )]
	public class GameDataAttribute : LibraryAttribute
	{
		internal string ListName { get; private set; }

		/// <summary>
		/// Indicates that this type compiles as list, rather than a single entry in the model.
		/// This will also affect how you retrieve this data via Model.GetData().
		/// </summary>
		public bool AllowMultiple { get; set; } = false;

		public GameDataAttribute( string name ) : base( name )
		{
			// For the pre existing list names we gotta do this.
			if ( name == "particle" ) ListName = "particles_list";
			else if ( name == "break_list_piece" ) ListName = "break_list";
			else if ( name == "eye" ) ListName = "eye_data_list";
		}
	}

	/// <summary>
	/// Draws 3 line axis visualization, which can set up to be manipulated via gizmos. You can have multiple of these.
	/// </summary>
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true )]
	public class AxisAttribute : BaseTransformAttribute
	{
		/// <summary>
		/// Internal name of a boolean key that dictates whether this helper should draw or not. If unset, will draw always.
		/// </summary>
		public string Enabled { get; set; }

		/// <summary>
		/// If set to true, when the node is selected a line will be drawn from the helper to the parent attachment/bone.
		/// </summary>
		public bool ParentLine { get; set; }

		public AxisAttribute() : base( "locator_axis" )
		{
		}

		protected override void AddKeys( Dictionary<string, object> dict )
		{
			dict.Add( "enabled_key", Enabled );
			if ( ParentLine ) dict.Add( "draw_parent", true );
		}
	}

	/// <summary>
	/// Draws a box, which can be manipulated via gizmos. You can have multiple of these.
	/// </summary>
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true )]
	public class BoxAttribute : BaseTransformAttribute
	{
		internal string Dimensions { get; set; }
		internal string Mins { get; set; }
		internal string Maxs { get; set; }

		/// <summary>
		/// If set, the semi-transparent box "walls" will not be drawn.
		/// </summary>
		public bool HideSurface { get; set; }

		/// <summary>
		/// If set, gizmos will be shown in transform mode to quickly move/scale the box.
		/// For "dimensions" box Origin/Angles must be set.
		/// </summary>
		public bool ShowGizmos { get; set; }

		/// <summary>
		/// Store the box's dimensions in a single key, acting as (maxs-mins) which assumes the box's center is at the models origin.
		/// The box's center can be set up to be movable via "Origin" property and rotatable via "Angles" property.
		/// </summary>
		/// <param name="dimensionsKey">Internal name of a key on the node that will store the dimensions of the box.</param>
		public BoxAttribute( string dimensionsKey ) : base( "box" )
		{
			Dimensions = dimensionsKey;
		}

		/// <summary>
		/// Store the box's dimensions in 2 keys as Mins and Maxs. This type cannot be rotated.
		/// </summary>
		/// <param name="minsKey">Internal name of a key on the node that will store the mins of the box.</param>
		/// <param name="maxsKey">Internal name of a key on the node that will store the maxs of the box.</param>
		public BoxAttribute( string minsKey, string maxsKey ) : base( "box" )
		{
			Mins = minsKey;
			Maxs = maxsKey;
		}

		protected override void AddKeys( Dictionary<string, object> dict )
		{
			dict.Add( "dimensions_key", Dimensions );
			dict.Add( "min_key", Mins );
			dict.Add( "max_key", Maxs );
			//dict.Add( "draw_bone_name", Enabled ); // Draws the name of the Bone when selected and hovered, is that useful?
			if ( ShowGizmos ) dict.Add( "transform_gizmo", true );
			if ( HideSurface ) dict.Add( "draw_surface", false );
		}
	}

	/// <summary>
	/// Draws a sphere, which can be manipulated via gizmos. You can have multiple of these.
	/// </summary>
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true )]
	public class SphereAttribute : BaseTransformAttribute
	{
		internal string Radius { get; set; }
		internal string Center { get; set; }

		/// <summary>
		/// If set, the semi-transparent sphere "wall"/surface will not be drawn.
		/// </summary>
		public bool HideSurface { get; set; }

		public SphereAttribute( string radiusKey, string centerKey = "" ) : base( "sphere" )
		{
			Radius = radiusKey;
			Center = centerKey;
		}

		protected override void AddKeys( Dictionary<string, object> dict )
		{
			dict.Add( "radius_key", Radius );
			dict.Add( "center_key", Center );
			if ( HideSurface ) dict.Add( "draw_surface", false );
		}
	}

	/// <summary>
	/// Draws a capsule, which can be manipulated via gizmos. You can have multiple of these.
	/// </summary>
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true )]
	public class CapsuleAttribute : BaseTransformAttribute
	{
		internal string Point1 { get; set; }
		internal string Point2 { get; set; }
		internal string Radius1 { get; set; }
		internal string Radius2 { get; set; }

		/// <summary>
		/// This variation has 1 radius for both points.
		/// </summary>
		public CapsuleAttribute( string point1Key, string point2key, string radiusKey ) : base( "capsule" )
		{
			Point1 = point1Key;
			Point2 = point2key;
			Radius1 = radiusKey;
		}

		/// <summary>
		/// This variation has independent radius for each point.
		/// </summary>
		public CapsuleAttribute( string point1Key, string point2key, string radius1Key, string radius2Key ) : base( "capsule" )
		{
			Point1 = point1Key;
			Point2 = point2key;
			Radius1 = radius1Key;
			Radius2 = radius2Key;
		}

		protected override void AddKeys( Dictionary<string, object> dict )
		{
			dict.Add( "point0_key", Point1 );
			dict.Add( "point1_key", Point2 );

			if ( string.IsNullOrEmpty( Radius2 ) )
			{
				dict.Add( "radius_key", Radius1 );
				return;
			}

			dict.Add( "independent_radii", true );
			dict.Add( "radius0_key", Radius1 );
			dict.Add( "radius1_key", Radius2 );
		}
	}

	/// <summary>
	/// Draws a cylinder, which can be manipulated via gizmos. You can have multiple of these.
	/// </summary>
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true )]
	public class CylinderAttribute : CapsuleAttribute
	{
		/// <summary>
		/// This variation has 1 radius for both points.
		/// </summary>
		public CylinderAttribute( string point1Key, string point2key, string radiusKey ) : base( point1Key, point2key, radiusKey )
		{
			HelperName = "cylinder";
		}

		/// <summary>
		/// This variation has independent radius for each point.
		/// </summary>
		public CylinderAttribute( string point1Key, string point2key, string radius1Key, string radius2Key ) : base( point1Key, point2key, radius1Key, radius2Key )
		{
			HelperName = "cylinder";
		}
	}

	/// <summary>
	/// A helper that draws axis of rotation and angle limit of a hinge joint.
	/// </summary>
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true )]
	public class HingeJointAttribute : BaseTransformAttribute
	{
		/// <summary>
		/// Key name that dictates whether the hinge limit is enabled or not.
		/// </summary>
		public string EnableLimit { get; set; }

		/// <summary>
		/// Key name that stores the minimum angle value for the revolute joint.
		/// </summary>
		public string MinAngle { get; set; }

		/// <summary>
		/// Key name that stores the maximum angle value for the revolute joint.
		/// </summary>
		public string MaxAngle { get; set; }

		public HingeJointAttribute() : base( "physicsjoint_hinge" )
		{
		}

		protected override void AddKeys( Dictionary<string, object> dict )
		{
			dict.Add( "min_angle", MinAngle );
			dict.Add( "max_angle", MaxAngle );
			dict.Add( "enable_limit", EnableLimit );
		}
	}

	/// <summary>
	/// Adds a custom editor widget to the game data node.
	/// Currently only 1 option is available - "HandPosePairEditor"
	/// </summary>
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct )]
	public class EditorWidgetAttribute : Editor.MetaDataAttribute
	{
		internal string Editor;

		public EditorWidgetAttribute( string editor )
		{
			Editor = editor;
		}

		public override void AddHelpers( List<Tuple<string, string[]>> helpers )
		{
			helpers.Add( Tuple.Create( "custom_editor_widget", new string[] { Editor } ) );
		}
	}

	/// <summary>
	/// A helper used for VR hand purposes.
	/// </summary>
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true )]
	public class HandPoseAttribute : BaseModelDocAttribute
	{
		/// <summary>
		/// Internal name of the key to store position in.
		/// </summary>
		internal string Origin { get; set; }

		/// <summary>
		/// Internal name of the key to store angles in.
		/// </summary>
		internal string Angles { get; set; }

		/// <summary>
		/// Path to a model to use.
		/// </summary>
		internal string Model { get; set; }

		/// <summary>
		/// Whether this helper represents the right hand or not.
		/// This decides the names of the bones the helper will try to use.
		/// </summary>
		internal bool IsRightHand { get; set; }

		/// <summary>
		/// Text label this helper will have when hovered/selected.
		/// </summary>
		public string Label { get; set; }

		/// <summary>
		/// Internal name of the key that controls whether this helper is visible or not.
		/// </summary>
		public string Enabled { get; set; }

		/// <param name="originKey">Internal name of the key to store position in.</param>
		/// <param name="anglesKey">Internal name of the key to store angles in.</param>
		/// <param name="model">Path to a model to use.</param>
		/// <param name="isRightHand">Whether this helper represents the right hand or not. This decides the names of the bones the helper will try to use.</param>
		public HandPoseAttribute( string originKey, string anglesKey, string model, bool isRightHand ) : base( "hand_pose" )
		{
			Origin = originKey;
			Angles = anglesKey;
			Model = model;
			IsRightHand = isRightHand;
		}

		protected override void AddKeys( Dictionary<string, object> dict )
		{
			dict.Add( "enabled_key", Enabled );
			dict.Add( "origin_key", Origin );
			dict.Add( "angles_key", Angles );
			dict.Add( "label", Label );
			dict.Add( "model", Model );
			if ( IsRightHand ) dict.Add( "is_right_hand", true );
		}
	}

	public class LineAttribute : BaseModelDocAttribute
	{
		/// <summary>
		/// Internal name of the key that dictates which bone to use as parent for start position.
		/// </summary>
		public string BoneFrom { get; set; }

		/// <summary>
		/// Internal name of the key that dictates which attachment to use as parent for start position.
		/// </summary>
		public string AttachmentFrom { get; set; }

		/// <summary>
		/// Internal name of the key to read line start position from.
		/// </summary>
		public string OriginFrom { get; set; }

		/// <summary>
		/// Internal name of the key that dictates which bone to use as parent for end position.
		/// </summary>
		public string BoneTo { get; set; }

		/// <summary>
		/// Internal name of the key that dictates which attachment to use as parent for end position.
		/// </summary>
		public string AttachmentTo { get; set; }

		/// <summary>
		/// Internal name of the key to read line end position from.
		/// </summary>
		public string OriginTo { get; set; }

		/// <summary>
		/// Internal name of the key that controls whether this helper is visible or not.
		/// </summary>
		public string Enabled { get; set; }

		/// <summary>
		/// A string formatted color for this helper. Format is "255 255 255"
		/// </summary>
		public string Color { get; set; }

		/// <summary>
		/// The width of the line helper
		/// </summary>
		public float Width { get; set; }

		public LineAttribute() : base( "line" )
		{
		}

		protected override void AddTransform( StringBuilder sb )
		{
			var transformFromKeys = "";
			if ( !string.IsNullOrEmpty( OriginFrom ) ) transformFromKeys += $"\t\torigin_key = {OriginFrom.QuoteSafe()}\r\n";
			if ( !string.IsNullOrEmpty( AttachmentFrom ) ) transformFromKeys += $"\t\tattachment_key = {AttachmentFrom.QuoteSafe()}\r\n";
			if ( !string.IsNullOrEmpty( BoneFrom ) ) transformFromKeys += $"\t\tbone_key = {BoneFrom.QuoteSafe()}\r\n";

			if ( !string.IsNullOrEmpty( transformFromKeys ) )
			{
				sb.AppendLine( $"\tfrom =\r\n\t{{\r\n{transformFromKeys}\t}}" );
			}

			var transformToKeys = "";
			if ( !string.IsNullOrEmpty( OriginTo ) ) transformToKeys += $"\t\torigin_key = {OriginTo.QuoteSafe()}\r\n";
			if ( !string.IsNullOrEmpty( AttachmentTo ) ) transformToKeys += $"\t\tattachment_key = {AttachmentTo.QuoteSafe()}\r\n";
			if ( !string.IsNullOrEmpty( BoneTo ) ) transformToKeys += $"\t\tbone_key = {BoneTo.QuoteSafe()}\r\n";

			if ( !string.IsNullOrEmpty( transformToKeys ) )
			{
				sb.AppendLine( $"\tto =\r\n\t{{\r\n{transformToKeys}\t}}" );
			}
		}

		protected override void AddKeys( Dictionary<string, object> dict )
		{
			dict.Add( "enabled_key", Enabled );
			dict.Add( "color", Color );
			if ( Width > 0 ) dict.Add( "width", Width );
		}
	}

	/// <summary>
	/// Scales the vector with the "ScaleAndMirror" node, relative to associated bone.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property )]
	public class ScaleBoneRelativeAttribute : Editor.FieldMetaDataAttribute
	{
		public override void AddMetaData( Dictionary<string, string> meta_data )
		{
			meta_data["scale_bone_relative"] = "true";
		}
	}

	/// <summary>
	/// Scales the vector with the "ScaleAndMirror" node.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property )]
	public class ScaleWorldAttribute : Editor.FieldMetaDataAttribute
	{
		public override void AddMetaData( Dictionary<string, string> meta_data )
		{
			meta_data["scale_world"] = "true";
		}
	}
}
