using Sandbox.Internal;
using System.Linq.Expressions;
using System.Reflection;

namespace Editor;

public class ControlSheet : GridLayout, IControlSheet
{
	int rows = 0;

	public bool IncludePropertyNames = false;

	Func<SerializedProperty, bool> _filter;

	public ControlSheet() : base()
	{
		Margin = 0;
		HorizontalSpacing = 10;
		VerticalSpacing = 0;
		SetColumnStretch( 1, 2 );
		SetMinimumColumnWidth( 0, 120 );
	}

	public void AddObject( SerializedObject obj, Func<SerializedProperty, bool> filter = null )
	{
		Margin = new Sandbox.UI.Margin( 0, 0, 0, 0 );

		if ( obj is null )
			return;

		_filter = filter;
		IControlSheet.FilterSortAndAdd( this, obj.ToList() );
		_filter = default;
	}

	public static Widget CreateRow( SerializedProperty property, bool includeExtraInfo = false )
	{
		return ControlSheetRow.Create( property, includeExtraInfo );
	}

	Guid FindGuid( SerializedProperty sp )
	{
		return FindGuid( sp?.Parent );
	}

	Guid FindGuid( SerializedObject so )
	{
		if ( so is null ) return Guid.Empty;

		foreach ( var target in so.Targets )
		{
			if ( target is GameObject go ) return go.Id;
			if ( target is Component cmp ) return cmp.Id;
			if ( target is Resource rc ) return new Guid( rc.ResourceId, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 );
		}

		return FindGuid( so.ParentProperty?.Parent );
	}

	public void AddGroup( string groupName, SerializedProperty[] props )
	{
		try
		{
			var widget = new ControlSheetGroup( groupName, props, IncludePropertyNames );
			AddCell( 0, rows++, widget, xSpan: 2 );

			widget.UpdateVisibility();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"ControlGroupWidget: {e.Message}" );
		}
	}

	/// <summary>
	/// Add a serialized property row. This will create an editor for the row and a label.
	/// </summary>
	public ControlWidget AddRow( SerializedProperty property, float labelIndent = 0.0f )
	{
		var row = ControlSheetRow.Create( property, IncludePropertyNames );
		if ( !row.IsValid() ) return null;

		SetColumnStretch( 0 );
		AddCell( 0, rows++, row );
		return row.ControlWidget;
	}

	/// <summary>
	/// Add a property but force it to use a specific type of ControlWidhet
	/// </summary>
	public T AddControl<T>( SerializedProperty property, float labelIndent = 0.0f ) where T : ControlWidget
	{
		var editor = EditorTypeLibrary.Create<T>( typeof( T ), [property] );

		var row = ControlSheetRow.Create( property, editor, IncludePropertyNames );
		if ( !row.IsValid() ) return null;

		SetColumnStretch( 0 );
		AddCell( 0, rows++, row );
		return (T)row.ControlWidget;
	}

	/// <summary>
	/// Add a layout to a double wide cell
	/// </summary>
	public void AddLayout( Layout layout )
	{
		AddCell( 0, rows++, layout, 2, 1, TextFlag.LeftTop );
	}

	public static ControlSheet Create( SerializedObject so, Func<SerializedProperty, bool> filter = null )
	{
		var cs = new ControlSheet();
		cs.AddObject( so, filter );
		return cs;
	}

	public void AddProperty<T, U>( T target, Expression<Func<T, U>> property )
	{
		if ( property.Body is MemberExpression member )
		{
			var name = member.Member.Name;
			AddProperty( target, name );
		}
	}

	public void AddProperty<T>( Expression<Func<T>> property )
	{
		if ( property.Body is not MemberExpression member || member.Member is not PropertyInfo propertyInfo )
			throw new ArgumentException( "Expression must target a property.", nameof( property ) );

		if ( propertyInfo.GetMethod is null || propertyInfo.SetMethod is null )
			throw new InvalidOperationException( $"Property '{propertyInfo.Name}' must have a getter and a setter." );

		var getter = (Func<T>)Delegate.CreateDelegate( typeof( Func<T> ), propertyInfo.GetMethod );
		var setter = (Action<T>)Delegate.CreateDelegate( typeof( Action<T> ), propertyInfo.SetMethod );

		var row = ControlSheetRow.Create( new ReflectionSerializedProperty<T>( propertyInfo, getter, setter ), IncludePropertyNames );
		if ( !row.IsValid() )
			return;

		SetColumnStretch( 0 );
		AddCell( 0, rows++, row );
	}

	private void AddProperty<T>( T target, string name )
	{
		var so = target.GetSerialized();
		AddRow( so.GetProperty( name ) );
	}

	public static Widget CreateLabel( SerializedProperty property )
	{
		return new ControlSheetLabel( property );
	}

	FeatureTabWidget featureTabs;

	void IControlSheet.AddFeature( IControlSheet.Feature feature )
	{
		if ( featureTabs is null )
		{
			featureTabs = new FeatureTabWidget( null );

			var guid = FindGuid( feature.Properties.FirstOrDefault() );
			if ( guid != Guid.Empty )
			{
				featureTabs.StateCookie = guid.ToString();
			}

			AddCell( 0, rows++, featureTabs, xSpan: 2 );
		}

		featureTabs.AddFeature( feature );
	}

	void IControlSheet.AddGroup( IControlSheet.Group group )
	{
		try
		{
			var widget = new ControlSheetGroup( group.Name, group.Properties.ToArray(), IncludePropertyNames );
			AddCell( 0, rows++, widget, xSpan: 2 );

			widget.UpdateVisibility();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"ControlGroupWidget: {e.Message}" );
		}
	}

	bool IControlSheet.TestFilter( SerializedProperty prop )
	{
		if ( _filter is null ) return true;
		return _filter.Invoke( prop );
	}
}

file class ReflectionSerializedProperty<T> : SerializedProperty
{
	private readonly PropertyInfo _prop;
	private readonly DisplayInfo _info;
	private readonly Func<T> _get;
	private readonly Action<T> _set;

	public ReflectionSerializedProperty( PropertyInfo prop, Func<T> get, Action<T> set )
	{
		_prop = prop;
		_info = DisplayInfo.ForMember( _prop );

		_get = get;
		_set = set;
	}

	public override SerializedObject Parent => null;
	public override bool IsMethod => false;
	public override string Name => _info.Name;
	public override string DisplayName => _info.Name;
	public override string Description => _info.Description;
	public override string GroupName => _info.Group;
	public override bool IsEditable => true;
	public override int Order => _info.Order;
	public override Type PropertyType => typeof( T );
	public override string SourceFile => default;
	public override int SourceLine => default;
	public override bool HasChanges => false;

	public override ref AsAccessor As => ref base.As;

	public override bool TryGetAsObject( out SerializedObject obj ) => base.TryGetAsObject( out obj );
	public override U GetValue<U>( U defaultValue = default ) => ValueToType<U>( _get() );
	public override void SetValue<U>( U value ) => _set( ValueToType<T>( value ) );

	public override IEnumerable<Attribute> GetAttributes()
	{
		return _prop?.GetCustomAttributes() ?? base.GetAttributes();
	}
}
