namespace Sandbox;

public sealed partial class Dresser
{
	private (Model Model, ulong BodyGroups, string MaterialGroup)? _originalBody;
	private Model _appliedBodyModel;
	private Clothing _activeBody;
	private bool _hasSkinAttributes;
	private bool _hasEyeAttributes;

	private Sandbox.Clothing.Slots AllowedSlots => _activeBody?.AllowedSlots
		?? (IsHumanBody( BodyTarget?.Model ) ? Sandbox.Clothing.Slots.All : Sandbox.Clothing.Slots.None);

	// Restricted custom bodies keep their authored appearance while accepting selected accessories.
	private bool SupportsHumanAppearance => AllowedSlots == Sandbox.Clothing.Slots.All
		|| (AllowedSlots != Sandbox.Clothing.Slots.None && IsHumanBody( BodyTarget?.Model ));

	private static bool IsHumanBody( Model model ) =>
		(model?.BaseModel ?? model)?.Name.StartsWith( "models/citizen_human/", StringComparison.OrdinalIgnoreCase ) == true;

	/// <summary>
	/// An externally assigned model becomes the new baseline; internal swaps and same-model reloads retain it.
	/// </summary>
	private void OnBodyModelChanged()
	{
		if ( _settingAppearance )
			return;

		if ( BodyTarget.Model != _appliedBodyModel )
		{
			_originalBody = null;
			_activeBody = null;
			_appliedBodyModel = BodyTarget.Model;
		}

		UpdateAppearance();
	}

	/// <summary>
	/// Applies a body replacement before choosing garments, or restores the renderer's original body when removed.
	/// </summary>
	private void ApplyBodyReplacement( ClothingContainer outfit, SkinnedModelRenderer body )
	{
		_originalBody ??= (body.Model, body.BodyGroups, body.MaterialGroup);
		_activeBody = outfit.Clothing?
			.Select( x => x?.Clothing )
			.LastOrDefault( IsValidBodyReplacement );

		// ModelChanged fires while changing bodies. Appearance waits until the baseline is installed.
		_settingAppearance = true;
		try
		{
			body.ClearMaterialOverrides();
			if ( _activeBody is not null )
			{
				body.Model = Model.Load( _activeBody.BodyModel );
				body.BodyGroups = _activeBody.BodyGroupMask ?? body.Model.Parts.DefaultMask;
				body.MaterialGroup = _activeBody.BodyMaterialGroup;
			}
			else
			{
				var original = _originalBody.Value;
				body.Model = original.Model;
				body.BodyGroups = original.BodyGroups;
				body.MaterialGroup = original.MaterialGroup;
			}
		}
		finally
		{
			_appliedBodyModel = body.Model;
			_settingAppearance = false;
		}
	}

	private static bool IsValidBodyReplacement( Clothing clothing ) =>
		clothing?.ReplacesBody == true && IsValidModel( clothing.BodyModel );

	/// <summary>
	/// Removes only the appearance attributes written by this Dresser when switching to a custom body.
	/// </summary>
	private void ClearAppearanceAttributes()
	{
		var attributes = BodyTarget.Attributes.Get();
		if ( _hasSkinAttributes )
		{
			attributes.DeleteFloatValue( "skin_age" );
			attributes.DeleteFloatValue( "skin_tint" );
			_hasSkinAttributes = false;

#pragma warning disable CS0618
			if ( BodyTarget.Animation.Graph is not null )
			{
				BodyTarget.Animation.Parameters.Set( "scale_height", 1.0f, persist: true );
			}
			else
			{
				BodyTarget.Set( "scale_height", 1.0f );
			}
#pragma warning restore CS0618
		}

		if ( _hasEyeAttributes )
		{
			attributes.DeleteVector4DValue( "eye_color" );
			attributes.DeleteVector2DValue( "eye_align" );
			_hasEyeAttributes = false;
		}
	}
}
