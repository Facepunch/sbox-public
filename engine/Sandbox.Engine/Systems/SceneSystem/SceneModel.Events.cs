using Sandbox.Animation;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// A model scene object that supports animations and can be rendered within a <see cref="SceneWorld"/>.
/// </summary>
public sealed partial class SceneModel : SceneObject
{
	public record struct FootstepEvent( int FootId, Transform Transform, float Volume, string AttachmentName );

	public record struct GenericEvent( string Type, int Int, float Float, string String, Vector3 Vector );

	public record struct SoundEvent( string Name, Vector3 Position, string AttachmentName );

	public record struct AnimTagEvent( string Name, AnimTagStatus Status );

	/// <summary>
	/// Called when a footstep event happens
	/// </summary>
	public Action<FootstepEvent> OnFootstepEvent { get; set; }

	/// <summary>
	/// Called when a generic event happens
	/// </summary>
	public Action<GenericEvent> OnGenericEvent { get; set; }

	/// <summary>
	/// Called when a sound event happens
	/// </summary>
	public Action<SoundEvent> OnSoundEvent { get; set; }

	/// <summary>
	/// Called when a anim tag event happens
	/// </summary>
	public Action<AnimTagEvent> OnAnimTagEvent { get; set; }

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	internal delegate void AnimEventCallback( IntPtr typeName, float cycle, float time, IntPtr data );

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	internal delegate void AnimTagEventCallback( IntPtr pName, int status );

	/// <summary>
	/// Enumeration that describes how the AnimGraph tag state changed. Used in <see cref="AnimTagEvent"/>.
	/// </summary>
	public enum AnimTagStatus
	{
		/// <summary>
		/// Tag was activated and deactivated on the same frame
		/// </summary>
		Fired = 0,

		/// <summary>
		/// The tag has become active
		/// </summary>
		Start,

		/// <summary>
		/// The tag has become inactive
		/// </summary>
		End
	}

	DelegateFunctionPointer animEventCallback;
	DelegateFunctionPointer animTagEventCallback;

	public void RunPendingEvents()
	{
		if ( animNative.PendingAnimationEvents() == 0 )
			return;

		if ( animEventCallback == DelegateFunctionPointer.Null )
			animEventCallback = DelegateFunctionPointer.Get<AnimEventCallback>( OnAnimationEvent );

		// I wonder if this is slow and we should cache it?

		animNative.RunAnimationEvents( animEventCallback );
	}


	public void DispatchTagEvents()
	{
		if ( animTagEventCallback == DelegateFunctionPointer.Null )
			animTagEventCallback = DelegateFunctionPointer.Get<AnimTagEventCallback>( OnAnimTagEventCallback );

		animNative.DispatchTagEvents( animTagEventCallback );
	}

	void OnAnimTagEventCallback( IntPtr pName, int status )
	{
		string name = Interop.GetString( pName );

		try
		{
			OnAnimTagEvent?.Invoke( new( name, (AnimTagStatus)status ) );
		}
		catch ( Exception e )
		{
			Log.Warning( e );
		}
	}

	void OnAnimationEvent( IntPtr typeName, float cycle, float time, IntPtr data )
	{
		string name = Interop.GetString( typeName );
		KeyValues3 kv = data;

		if ( name == "AE_MANAGED_EVENT" )
		{
			var renderer = Component as SkinnedModelRenderer;
			if ( !renderer.IsValid() || !renderer.Active )
				return;

			if ( renderer.Components.Get<Component.IAnimationEvent>( FindMode.EnabledInSelf | FindMode.InAncestors ) is null )
				return;

			try
			{
				if ( kv.IsNull )
					throw new InvalidOperationException( "Animation event has no payload." );

				var payload = AnimationEvent.Deserialize<AnimationEvent>( kv.GetMemberString( "Type" ), kv.GetMemberString( "Data" ) );
				renderer.DispatchAnimationEvent( payload );
			}
			catch ( Exception e )
			{
				Log.Warning( e, "Couldn't dispatch animation event" );
			}
			return;
		}

		if ( name == "AE_FOOTSTEP" )
		{
			var foot = kv.GetMemberInt( "Foot", 0 );
			var attachment = kv.GetMemberString( "attachment" );
			var volume = kv.GetMemberFloat( "volume", 1.0f );
			var transform = GetAttachment( attachment ) ?? Transform;

			try
			{
				OnFootstepEvent?.Invoke( new( foot, transform, volume, attachment ) );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}

			return;
		}

		if ( name == "AE_CL_BODYGROUP_SET_VALUE" )
		{
			var bodygroup = kv.GetMemberString( "bodygroup" );
			var value = kv.GetMemberInt( "value", 0 );

			SetBodyGroup( bodygroup, value );
		}

		if ( name == "AE_GENERIC_EVENT" )
		{
			GenericEvent ev = default;

			ev.Type = kv.GetMemberString( "TypeName" );
			ev.Int = kv.GetMemberInt( "Int", 0 );
			ev.Float = kv.GetMemberFloat( "Float", 0.0f );
			ev.Vector = kv.GetMemberVector( "Vector", Vector3.Zero );
			ev.String = kv.GetMemberString( "StringData" );

			try
			{
				OnGenericEvent?.Invoke( ev );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}

			return;
		}

		if ( name == "AE_CL_PLAYSOUND" || name == "AE_CL_PLAYSOUND_ATTACHMENT" || name == "AE_CL_PLAYSOUND_POSITION" || name == "AE_SV_PLAYSOUND" )
		{
			SoundEvent ev = default;
			ev.Name = kv.GetMemberString( "name" );
			ev.Position = Transform.Position;
			ev.AttachmentName = kv.GetMemberString( "attachment" );

			if ( !string.IsNullOrWhiteSpace( ev.AttachmentName ) && GetAttachment( ev.AttachmentName ) is Transform tx )
			{
				ev.Position = tx.Position;
			}

			try
			{
				OnSoundEvent?.Invoke( ev );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}

			return;
		}

		// AE_CL_STOPSOUND
		// AE_CL_PLAYSOUND_LOOPING

		//Log.Info( $"EVENT {name} / {cycle} / {time}" );
		//for ( int i = 0; i < kv.GetMemberCount(); i++ )
		//{
		//	var membername = kv.GetMemberName( i );
		//	var member = kv.GetMember( i );
		//	var memberType = member.GetType_Native();

		//	Log.Info( $" - {i} / {membername} / {memberType}" );
		//}

	}

}
