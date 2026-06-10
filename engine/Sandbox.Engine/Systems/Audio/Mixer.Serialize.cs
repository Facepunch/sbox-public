using Sandbox.Internal;
using System.Text.Json.Nodes;

namespace Sandbox.Audio;

public partial class Mixer
{
	public JsonObject Serialize()
	{
		lock ( Lock )
		{
			var js = new JsonObject();

			js["Guid"] = Id;
			js["Name"] = Name;
			js["Volume"] = Volume;
			js["Mute"] = Mute;
			js["Solo"] = Solo;

			js["Spatializing"] = Spatializing;
			js["MaxVoices"] = MaxVoices;
			js["DistanceAttenuation"] = DistanceAttenuation;
			js["Occlusion"] = Occlusion;
			js["Reverb"] = Reverb;
			js["AirAbsorption"] = AirAbsorption;

#pragma warning disable CS0618
			js["OverrideOcclusion"] = OverrideOcclusion;
			js["OcclusionTags"] = (OcclusionTags?.IsEmpty ?? true) ? null : Json.ToNode( OcclusionTags );
#pragma warning restore CS0618

			js["BlockingTags"] = (BlockingTags?.IsEmpty ?? true) ? null : Json.ToNode( BlockingTags );
			js["IgnoredTags"] = (IgnoredTags?.IsEmpty ?? true) ? null : Json.ToNode( IgnoredTags );


			if ( Mixer.Default == this )
			{
				js["IsDefault"] = true;
			}

			var array = new JsonArray();

			foreach ( var processor in GetProcessors() )
			{
				var p = processor.Serialize();
				if ( p is null ) continue;

				array.Add( p );
			}

			js["Processors"] = array;

			var children = new JsonArray();
			foreach ( var child in Children )
			{
				children.Add( child.Serialize() );
			}

			js["Children"] = children;

			return js;
		}
	}

	protected void SetMasterOcclusionDefaults()
	{
#pragma warning disable CS0618
		OverrideOcclusion = true;
		OcclusionTags.RemoveAll();
		OcclusionTags.Add( "world" );
#pragma warning restore CS0618
	}

	protected void SetMasterSimulationTagDefaults()
	{
		BlockingTags.RemoveAll();
		IgnoredTags.RemoveAll();
		IgnoredTags.Add( "passaudio" );
		IgnoredTags.Add( "passbullets" );
		IgnoredTags.Add( "sky" );
		IgnoredTags.Add( "playerclip" );
		IgnoredTags.Add( "trigger" );
		IgnoredTags.Add( "player" );
	}

	public void Deserialize( JsonObject js, TypeLibrary typeLibrary )
	{
		lock ( Lock )
		{
			Id = (Guid)(js["Guid"] ?? Id);
			Name = (string)(js["Name"] ?? "Unnammed Mixer");
			Volume = (float)(js["Volume"] ?? 1.0f);
			Mute = (bool)(js["Mute"] ?? false);
			Solo = (bool)(js["Solo"] ?? false);

			// for these tracks, turn all this fun spatial stuff off by default
			bool is2d = Name == "Music" || Name == "UI";

			Spatializing = (float)(js["Spatializing"] ?? js["Spacializing"] ?? (is2d ? 0.0f : 1.0f));
			DistanceAttenuation = (float)(js["DistanceAttenuation"] ?? (is2d ? 0.0f : 1.0f));
			Occlusion = (float)(js["Occlusion"] ?? (is2d ? 0.0f : 1.0f));
			Reverb = (float)(js["Reverb"] ?? 1.0f);
			AirAbsorption = (float)(js["AirAbsorption"] ?? (is2d ? 0.0f : 1.0f));
			// Legacy migration: drop deprecated Mixer.OcclusionEnabled/ReverbEnabled bools; if either was
			// explicitly false in older resources, honour that by forcing the corresponding float to 0.
			if ( !js.GetPropertyValue( "OcclusionEnabled", true ) ) Occlusion = 0f;
			if ( !js.GetPropertyValue( "ReverbEnabled", true ) ) Reverb = 0f;
			MaxVoices = js.GetPropertyValue( "MaxVoices", 64 );

#pragma warning disable CS0618
			OcclusionTags = js.GetPropertyValue<TagSet>( "OcclusionTags", null ) ?? new();
			OverrideOcclusion = js.GetPropertyValue( "OverrideOcclusion", false );

			// If we're the master and we don't have any occlusion tags
			// lets add world on there as the default.
			if ( Parent == null && !js.ContainsKey( "OcclusionTags" ) && !js.ContainsKey( "OverrideOcclusion" ) )
			{
				SetMasterOcclusionDefaults();
			}
#pragma warning restore CS0618

			BlockingTags = js.GetPropertyValue<TagSet>( "BlockingTags", null )
				?? js.GetPropertyValue<TagSet>( "BlockingSimulationTags", null )
				?? new();
			IgnoredTags = js.GetPropertyValue<TagSet>( "IgnoredTags", null )
				?? js.GetPropertyValue<TagSet>( "IgnoredSimulationTags", null )
				?? new();

			// Seed the master mixer's default simulation tag set when loading older resources.
			if ( Parent == null && !js.ContainsKey( "BlockingTags" ) && !js.ContainsKey( "IgnoredTags" )
				&& !js.ContainsKey( "BlockingSimulationTags" ) && !js.ContainsKey( "IgnoredSimulationTags" ) )
			{
				SetMasterSimulationTagDefaults();
			}

			Clear();

			if ( (bool)(js["IsDefault"] ?? false) )
			{
				Mixer.Default = this;
			}

			if ( js["Children"] is JsonArray children )
			{
				foreach ( var child in children )
				{
					var mixer = AddChild();
					mixer.Deserialize( (JsonObject)child, typeLibrary );
				}
			}

			if ( js["Processors"] is JsonArray processors )
			{
				foreach ( var processor in processors )
				{
					var type = processor["__type"]?.ToString();
					if ( type is null ) continue;

					var p = typeLibrary.Create<AudioProcessor>( type );
					if ( p is null )
					{
						Log.Warning( $"Unknown processor type '{type}' when loading mixer '{Name}'" );
						continue;
					}

					p.Deserialize( processor as JsonObject );
					AddProcessor( p );
				}
			}
		}
	}

	void Clear()
	{
		ClearAllProcessors();

		foreach ( var child in GetChildren() )
		{
			child.Destroy();
		}
	}

	void ClearAllProcessors()
	{
		ClearProcessors();

		foreach ( var child in GetChildren() )
		{
			child.ClearAllProcessors();
		}
	}
}
