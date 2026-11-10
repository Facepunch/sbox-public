using Sandbox.Audio;

namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// if we have a ::before element, this is it.
	/// </summary>
	Panel _beforeElement;

	/// <summary>
	/// if we have a ::after element, this is it.
	/// </summary>
	Panel _afterElement;

	/// <summary>
	/// Called during tick to create or destroy the ::before and ::after elements.
	/// </summary>
	void UpdateBeforeAfterElements()
	{
		// Don't do this if we ARE a ::before or ::after element.
		if ( PseudoClass.Contains( PseudoClass.Before ) ) return;
		if ( PseudoClass.Contains( PseudoClass.After ) ) return;

		BuildPseudoElement( Style.HasBeforeElement, PseudoClass.Before, ref _beforeElement );
		BuildPseudoElement( Style.HasAfterElement, PseudoClass.After, ref _afterElement );

		// Make sure it's always first
		if ( _beforeElement.IsValid() ) SetChildIndex( _beforeElement, 0 );

		// Make sure it's always last, before any scrollbars
		if ( _afterElement.IsValid() ) SetChildIndex( _afterElement, LastContentChildIndex );
	}

	/// <summary>
	/// Called to update the state of a before or after element. Either destroying it or creating it.
	/// </summary>
	private void BuildPseudoElement( bool shouldExist, PseudoClass additionalClass, ref Panel panel )
	{
		// destroy it if it exists
		if ( !shouldExist )
		{
			if ( panel is not null )
			{
				panel.Delete();
				panel = null;
			}

			return;
		}

		// create it if it doesn't exist
		if ( !panel.IsValid() )
		{
			panel = new Label();
			panel.ElementName = "element";
			panel.RemoveClass( "label" );
			panel.PseudoClass = additionalClass;
			AddChild( panel );
		}

	}
}
