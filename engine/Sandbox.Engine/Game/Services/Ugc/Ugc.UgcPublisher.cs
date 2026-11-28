using System.Text.Json;
using Sandbox.SteamApi;

namespace Sandbox.Services;

internal readonly record struct WorkshopProgress( ulong BytesProcessed, ulong BytesTotal, float Percent, Ugc.UgcPublisher.UpdateStatus Status );

internal static partial class Ugc
{
	/// <summary>
	/// A class that can be used to update a workshop file.
	/// This is inaccessible in game. Instead, use the publisher modal.
	/// </summary>
	public class UgcPublisher
	{
		readonly SteamNative.WorkshopUpdate native;
		string title;
		string description;
		string metadata;
		string contentFolder;
		string previewImage;
		int? visibility;
		readonly List<string> tags = [];
		readonly List<string> additionalPreviews = [];
		readonly List<KeyValuePair<string, string>> keyValues = [];

		WorkshopProgress progress;
		ulong progressFrame;

		internal UgcPublisher( SteamNative.WorkshopUpdate native )
		{
			this.native = native;
		}

		~UgcPublisher()
		{
			MainThread.QueueDispose( native );
		}

		/// <summary>
		/// The published file this update belongs to.
		/// </summary>
		public ulong ItemId => native.GetPublishedFileId();

		/// <summary>
		/// Whether Steam needs the publisher to accept the Workshop legal agreement.
		/// </summary>
		public bool NeedsLegalAgreement => native.NeedsLegalAgreement();

		/// <summary>
		/// Whether Steam accepted this update for submission.
		/// </summary>
		public bool IsPublishing { get; private set; }

		/// <summary>
		/// Whether the submission has finished.
		/// </summary>
		public bool IsFinished { get; private set; }

		WorkshopProgress Progress
		{
			get
			{
				if ( IsFinished ) return default;

				if ( progressFrame != Application.FrameCount )
				{
					progress = native.GetProgress();
					progressFrame = Application.FrameCount;
				}

				return progress;
			}
		}

		/// <summary>
		/// Upload progress from zero to one hundred.
		/// </summary>
		public float PercentComplete => IsFinished ? 100.0f : Progress.Percent;

		/// <summary>
		/// Bytes Steam has processed in the current upload stage.
		/// </summary>
		public ulong BytesProcessed => Progress.BytesProcessed;

		/// <summary>
		/// Total bytes Steam expects to process in the current upload stage.
		/// </summary>
		public ulong BytesTotal => Progress.BytesTotal;

		/// <summary>
		/// Steam's EItemUpdateStatus - what the upload's doing right now
		/// </summary>
		public enum UpdateStatus
		{
			Invalid,
			PreparingConfig,
			PreparingContent,
			UploadingContent,
			UploadingPreviewFile,
			CommittingChanges
		}

		/// <summary>
		/// The current upload stage reported by Steam.
		/// </summary>
		public UpdateStatus Status => Progress.Status;

		/// <summary>
		/// Set the item's display title. Steam requires a nonempty title.
		/// </summary>
		public void SetTitle( string value )
		{
			if ( !string.IsNullOrEmpty( value ) ) title = value;
		}

		/// <summary>
		/// Set or clear the Workshop description.
		/// </summary>
		public void SetDescription( string value ) => description = value ?? description;

		/// <summary>
		/// Set the item's main preview image from a local file.
		/// </summary>
		public void SetPreviewImage( string value )
		{
			if ( !string.IsNullOrEmpty( value ) ) previewImage = value;
		}

		/// <summary>
		/// Append a local image after the main preview.
		/// </summary>
		public void AddPreviewImage( string value )
		{
			if ( !string.IsNullOrEmpty( value ) ) additionalPreviews.Add( value );
		}

		/// <summary>
		/// Add a value to a Workshop key. A key can have multiple values.
		/// </summary>
		public void SetKeyValue( string key, string value )
		{
			if ( !string.IsNullOrEmpty( key ) ) keyValues.Add( new( key, value ?? "" ) );
		}

		/// <summary>
		/// Remove existing values for this key. Steam receives removals and additions in call order.
		/// </summary>
		public void RemoveKeyValues( string key )
		{
			if ( !string.IsNullOrEmpty( key ) ) keyValues.Add( new( key, null ) );
		}

		/// <summary>
		/// Append a Workshop tag, up to the publisher's 32-tag limit.
		/// </summary>
		public void SetTag( string tag )
		{
			if ( !string.IsNullOrEmpty( tag ) && tags.Count < 32 ) tags.Add( tag );
		}

		/// <summary>
		/// Set the local folder containing the files to publish.
		/// </summary>
		public void SetContentFile( string fullPath )
		{
			if ( !string.IsNullOrEmpty( fullPath ) ) contentFolder = fullPath;
		}

		/// <summary>
		/// Set or clear the item's metadata.
		/// </summary>
		public void SetMetaData( string value ) => metadata = value ?? metadata;

		/// <summary>
		/// Set who can see the item after submission.
		/// </summary>
		public void SetVisibility( Storage.Visibility value ) => visibility = (int)value;

		/// <summary>
		/// Add or remove a required Workshop item, throwing if Steam rejects the change.
		/// </summary>
		internal async Task ChangeDependency( ulong dependency, bool add )
		{
			var call = SteamClient.IsValid ? native.ChangeDependency( dependency, add ) : 0;
			if ( call == 0 )
				throw new InvalidOperationException( "Couldn't start the Workshop dependency update." );

			var result = await SteamCalls.GetResultAsync( call, handle => SteamNative.WorkshopUpdate.GetDependencyResult( handle, add ), fallback: Result.IOFailure );
			if ( result != Result.OK )
				throw new InvalidOperationException( $"Couldn't {(add ? "add" : "remove")} Workshop dependency {dependency} on item {ItemId}: {result}." );
		}

		/// <summary>
		/// Submit the item update and wait for Steam's result.
		/// </summary>
		public async Task<bool> Submit( string changeNotes = "" )
		{
			if ( !SteamClient.IsValid || IsPublishing ) return false;

			var update = JsonSerializer.Serialize( new { title, description, metadata, contentFolder, previewImage, visibility, tags, additionalPreviews, keyValues } );
			var call = native.Submit( update, changeNotes );
			if ( call == 0 ) return false;

			IsPublishing = true;
			var result = await SteamCalls.GetResultAsync( call, native.GetSubmitResult, fallback: Result.IOFailure );
			IsFinished = true;
			return result == Result.OK;
		}
	}

}
