namespace Sandbox.Internal;

/// <summary>
/// Interface for a control sheet that manages the display of serialized properties in a structured way.
/// </summary>
[Expose]
public interface IControlSheet
{
	/// <summary>
	/// Adds properties to the control sheet, filtering them based on the provided filter function.
	/// </summary>
	public static void FilterSortAndAdd( IControlSheet sheet, List<SerializedProperty> q, bool allowFeatures = true )
	{
		var props = q.Where( sheet.TestFilter )
						.OrderBy( x => x.Order )
						.ThenBy( x => x.SourceFile )
						.ThenBy( x => x.SourceLine )
						.ToList();

		AddProperties( sheet, props, allowFeatures );
	}

	/// <summary>
	/// Add properties to a controlsheet, with a minimal of filtering and no sorting.
	/// </summary>
	public static void AddProperties( IControlSheet sheet, List<SerializedProperty> properties, bool allowFeatures = true )
	{
		sheet.RemoveUnusedMethods( properties );

		if ( allowFeatures )
		{
			string defaultFeature = "";

			//
			// If we have features then we group up into feature tabs
			//
			var features = properties.GroupBy( x => x.GetAttributes<FeatureAttribute>().FirstOrDefault()?.Identifier ?? defaultFeature ).ToDictionary( x => x.Key, x => x.ToList() );
			if ( features.Count > 1 || (features.FirstOrDefault().Key ?? defaultFeature) != defaultFeature )
			{
				foreach ( var feature in features.OrderBy( x => x.Key != defaultFeature ).ThenBy( x => x.Key ) )
				{
					var csf = new Feature( feature.Value );
					sheet.AddFeature( csf );
				}

				return;
			}
		}

		//
		// No features - just flat, normal groups
		//
		sheet.AddPropertiesWithGrouping( properties );
	}

	/// <summary>
	/// Remove methods that we have no hope of displaying
	/// </summary>
	void RemoveUnusedMethods( List<SerializedProperty> properties )
	{
		properties.RemoveAll( x => x.IsMethod && !x.HasAttribute<ButtonAttribute>() );
	}

	void AddPropertiesWithGrouping( List<SerializedProperty> properties )
	{
		RemoveUnusedMethods( properties );

		var grouped = properties.GroupBy( x => x.GroupName ?? null ).ToList();

		foreach ( var group in grouped.OrderBy( x => x.Key != null ).ThenBy( x => x.Max( y => y.Order ) ).ThenBy( x => x.Key ) )
		{
			var csg = new Group( group.ToList() );
			AddGroup( csg );
		}
	}

	/// <summary>
	/// We're adding a feature. Normally would store these in a tab control
	/// </summary>
	void AddFeature( Feature feature );

	/// <summary>
	/// We're adding a group. Normally would have a Group Panel with the properties as children
	/// </summary>
	void AddGroup( Group group );

	/// <summary>
	/// Implement to filter properties that should be displayed in the control sheet.
	/// </summary>
	bool TestFilter( SerializedProperty prop );

	/// <summary>
	/// A feature is usually displayed as a tab, to break things up in the inspector. They can sometimes be turned on and off.
	/// </summary>
	public sealed class Feature
	{
		/// <summary>
		/// The name of the feature, usually displayed as a tab title in the inspector.
		/// </summary>
		public string Name { get; init; }

		/// <summary>
		/// The description of the feature
		/// </summary>
		public string Description { get; init; }

		/// <summary>
		/// The icon of the feature
		/// </summary>
		public string Icon { get; init; }

		/// <summary>
		/// Allows tinting this feature, for some reason
		/// </summary>
		public EditorTint Tint { get; init; }

		/// <summary>
		/// The properties that are part of this feature, usually displayed together in the inspector.
		/// </summary>
		public List<SerializedProperty> Properties { get; init; }

		/// <summary>
		/// If we have a FeatureEnabled property, this will be it. If not then we assume it should always be enabled.
		/// </summary>
		public SerializedProperty EnabledProperty { get; init; }

		public Feature( List<SerializedProperty> properties )
		{
			Properties = properties;

			Name = properties.Select( x => x.GetAttributes<FeatureAttribute>().FirstOrDefault() )
						.Where( x => x is not null )
						.Select( x => x.Title )
						.Where( x => !string.IsNullOrWhiteSpace( x ) )
						.FirstOrDefault() ?? "";

			Description = properties.Select( x => x.GetAttributes<FeatureAttribute>().FirstOrDefault() )
						.Where( x => x is not null )
						.Select( x => x.Description )
						.Where( x => !string.IsNullOrWhiteSpace( x ) )
						.FirstOrDefault() ?? "";

			Icon = properties.Select( x => x.GetAttributes<FeatureAttribute>().FirstOrDefault() )
						.Where( x => x is not null )
						.Select( x => x.Icon )
						.Where( x => !string.IsNullOrWhiteSpace( x ) )
						.FirstOrDefault() ?? "";

			Tint = properties.Select( x => x.GetAttributes<FeatureAttribute>().FirstOrDefault() )
						.Where( x => x is not null )
						.Select( x => x.Tint )
						.Where( x => x != EditorTint.White )
						.FirstOrDefault( EditorTint.White );

			EnabledProperty = Properties.Where( x => x.GetAttributes<FeatureEnabledAttribute>().Any() ).FirstOrDefault();
		}
	}

	/// <summary>
	/// A group is a collection of properties that are related to each other, and can be displayed together in the inspector, usually with a title.
	/// </summary>
	public sealed class Group
	{
		/// <summary>
		/// The name of the group, usually displayed as a title in the inspector.
		/// </summary>
		public string Name { get; init; }

		/// <summary>
		/// The properties that are part of this group, usually displayed together in the inspector.
		/// </summary>
		public List<SerializedProperty> Properties { get; init; }

		public Group( List<SerializedProperty> properties )
		{
			Properties = properties;
			Name = properties.FirstOrDefault()?.GroupName ?? "";
		}
	}
}
