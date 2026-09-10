using Sandbox.Audio;

namespace Editor;

/// <summary>
/// Dropdown selection for DspPresetHandle
/// </summary>
[CustomEditor( typeof( MixerHandle ) )]
sealed class MixerHandleControlWidget : DropdownControlWidget<MixerHandle>
{
	public override bool SupportsMultiEdit => true;

	public MixerHandleControlWidget( SerializedProperty property ) : base( property )
	{
	}

	protected override IEnumerable<object> GetDropdownValues()
	{
		var parentMixer = GetParentMixer();
		var root = parentMixer ?? Mixer.Master;

		foreach ( var mixer in IterateMixerTree( root ) )
		{

			var e = new Entry
			{
				Value = (MixerHandle)mixer,
				Label = mixer.Name,
			};

			yield return e;
		}
	}

	IEnumerable<Mixer> IterateMixerTree( Mixer parent )
	{
		yield return parent;

		foreach ( var child in parent.GetChildren() )
		{
			foreach ( var childEntry in IterateMixerTree( child ) )
			{
				yield return childEntry;
			}
		}
	}

	Mixer GetParentMixer()
	{
		if ( !SerializedProperty.TryGetAttribute<Voice.ParentMixerAttribute>( out var voice ) )
		{
			return null;
		}

		return Mixer.FindMixerByName( voice.MixerName );
	}
}
