namespace Sandbox;

public abstract partial class SerializedProperty : IValid
{
	/// <summary>
	/// Return a version of this property that can be customized for editor UI. You'll be able to change
	/// things like display name and tooltip, and add extra attributes that control how editor controls interact with it.
	/// </summary>
	public CustomizableSerializedProperty GetCustomizable()
	{
		return new CustomizableSerializedProperty( this );
	}

	/// <summary>
	/// A proxy around a SerializedProperty that allows overriding any property for UI customization.
	/// Unset values fall through to the underlying property.
	/// </summary>
	public class CustomizableSerializedProperty : Proxy
	{
		SerializedProperty _target;
		protected override SerializedProperty ProxyTarget => _target;

		public CustomizableSerializedProperty( SerializedProperty property )
		{
			_target = property;
		}

		// String properties
		string _name;
		public override string Name => _name ?? base.Name;
		/// <summary> Override the property's internal name. </summary>
		public void SetName( string value ) => _name = value;

		string _displayName;
		public override string DisplayName => _displayName ?? base.DisplayName;
		/// <summary> Override the label shown in the inspector. </summary>
		public void SetDisplayName( string value ) => _displayName = value;

		string _description;
		public override string Description => _description ?? base.Description;
		/// <summary> Override the tooltip / description text. </summary>
		public void SetDescription( string value ) => _description = value;

		string _groupName;
		public override string GroupName => _groupName ?? base.GroupName;
		/// <summary> Override which inspector group this property appears in. </summary>
		public void SetGroupName( string value ) => _groupName = value;

		string _sourceFile;
		public override string SourceFile => _sourceFile ?? base.SourceFile;
		/// <summary> Override the reported source file path. </summary>
		public void SetSourceFile( string value ) => _sourceFile = value;

		// Nullable value types
		int? _order;
		public override int Order => _order ?? base.Order;
		/// <summary> Override the sort order within the inspector. </summary>
		public void SetOrder( int value ) => _order = value;

		int? _sourceLine;
		public override int SourceLine => _sourceLine ?? base.SourceLine;
		/// <summary> Override the reported source line number. </summary>
		public void SetSourceLine( int value ) => _sourceLine = value;

		bool? _isEditable;
		public override bool IsEditable => _isEditable ?? base.IsEditable;
		/// <summary> Force the property to be editable or read-only. </summary>
		public void SetIsEditable( bool value ) => _isEditable = value;

		bool? _isPublic;
		public override bool IsPublic => _isPublic ?? base.IsPublic;
		/// <summary> Override the public visibility flag. </summary>
		public void SetIsPublic( bool value ) => _isPublic = value;

		bool? _isProperty;
		public override bool IsProperty => _isProperty ?? base.IsProperty;
		/// <summary> Override whether this appears as a property. </summary>
		public void SetIsProperty( bool value ) => _isProperty = value;

		bool? _isField;
		public override bool IsField => _isField ?? base.IsField;
		/// <summary> Override whether this appears as a field. </summary>
		public void SetIsField( bool value ) => _isField = value;

		bool? _isMethod;
		public override bool IsMethod => _isMethod ?? base.IsMethod;
		/// <summary> Override whether this appears as a method. </summary>
		public void SetIsMethod( bool value ) => _isMethod = value;

		bool? _hasChanges;
		public override bool HasChanges => _hasChanges ?? base.HasChanges;
		/// <summary> Override the dirty/changed flag. </summary>
		public void SetHasChanges( bool value ) => _hasChanges = value;

		bool? _isValid;
		public override bool IsValid => _isValid ?? base.IsValid;
		/// <summary> Override the validity flag. </summary>
		public void SetIsValid( bool value ) => _isValid = value;

		// Reference types
		SerializedObject _parent;
		public override SerializedObject Parent => _parent ?? base.Parent;
		/// <summary> Override the parent SerializedObject. </summary>
		public void SetParent( SerializedObject value ) => _parent = value;

		Type _propertyType;
		public override Type PropertyType => _propertyType ?? base.PropertyType;
		/// <summary> Override the reported property type. </summary>
		public void SetPropertyType( Type value ) => _propertyType = value;

		// Attributes
		List<Attribute> _extraAttributes;

		/// <summary> Returns the underlying attributes merged with any added via <see cref="AddAttribute"/>. </summary>
		public override IEnumerable<Attribute> GetAttributes()
		{
			var attrs = base.GetAttributes();
			if ( _extraAttributes != null )
				attrs = attrs.Concat( _extraAttributes );
			return attrs;
		}

		/// <summary> Append an extra attribute visible to the editor and control widgets. </summary>
		public void AddAttribute( Attribute attribute )
		{
			_extraAttributes ??= new();
			_extraAttributes.Add( attribute );
		}
	}
}
