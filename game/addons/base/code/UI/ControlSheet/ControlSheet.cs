using Sandbox.Internal;
namespace Sandbox.UI;

/// <summary>
/// A controlsheet is a panel that you can populate with a SerializedObject's properties.
/// </summary>
public class ControlSheet : Panel, IControlSheet
{
	/// <summary>
	/// The object we're trying to display properties for.
	/// </summary>
	[Parameter] public object Target { get; set; }

	/// <summary>
	/// body panel. Gets deleted and recreated on Rebuild()
	/// </summary>
	Panel _body;

	/// <summary>
	/// Filter any properties that are added to this
	/// </summary>
	[Parameter] public Func<SerializedProperty, bool> PropertyFilter { get; set; }

	/// <summary>
	/// Rebuild these controls. This will delete the current body panel and recreate it, then add all properties from the target object.
	/// </summary>
	public void Rebuild()
	{
		IControlSheet sheet = this;
		_body?.Delete();

		_body = AddChild<Panel>();
		_body.AddClass( "body" );

		if ( Target is null ) return;

		if ( Target is List<SerializedProperty> propertyList )
		{
			IControlSheet.FilterSortAndAdd( sheet, propertyList );
		}
		else
		{
			var so = Game.TypeLibrary.GetSerializedObject( Target );
			IControlSheet.FilterSortAndAdd( sheet, so.ToList() );
		}
	}

	int _hash;

	public override void Tick()
	{
		base.Tick();

		var hash = HashCode.Combine( Target );
		if ( hash != _hash )
		{
			_hash = hash;
			Rebuild();
		}
	}

	/// <summary>
	/// Called by IControlSheet logic to add a feature
	/// </summary>
	void IControlSheet.AddFeature( IControlSheet.Feature feature )
	{
		// Add feature tab
		Log.Warning( "TODO: TODO handle Feature Sheet" );

	}

	/// <summary>
	/// Called by IControlSheet logic to add a group
	/// </summary>
	void IControlSheet.AddGroup( IControlSheet.Group group )
	{
		var g = _body.AddChild<ControlSheetGroup>();
		g.Header.Title = group.Name;

		//
		// This is part of a toggle group - so add a checkbox to the header
		//
		var toggleGroup = group.Properties.FirstOrDefault( x => x.HasAttribute<ToggleGroupAttribute>() && x.Name == group.Name );
		if ( toggleGroup is not null )
		{
			toggleGroup.TryGetAttribute<ToggleGroupAttribute>( out var toggleAttr );
			if ( toggleGroup is not null )
			{
				group.Properties.Remove( toggleGroup );

				var enabler = BaseControl.CreateFor( toggleGroup );

				g.Header.Title = toggleAttr.Label ?? g.Header.Title;
				g.SetToggle( toggleGroup );
				g.SetVisibility( toggleGroup.GetAttributes<InspectorVisibilityAttribute>()?.ToArray() );

				if ( !toggleGroup.As.Bool ) g.Closed = true;
			}
		}

		foreach ( var prop in group.Properties )
		{
			var row = g.Body.AddChild<ControlSheetRow>();
			row.Initialize( prop );
		}
	}

	/// <summary>
	/// Called by IControlSheet logic to filter properties.
	/// </summary>
	bool IControlSheet.TestFilter( SerializedProperty prop )
	{
		return PropertyFilter?.Invoke( prop ) ?? true;
	}
}
