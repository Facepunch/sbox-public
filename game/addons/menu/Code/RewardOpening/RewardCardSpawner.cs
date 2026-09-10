using Sandbox;

/// <summary>
/// Spawns reward cards from the crate when all flaps are open.
/// Creates WorldPanel-backed card GameObjects that fly out and position in front of the camera.
/// Listens to RewardUnboxCrate for the "fully open" event and manages the card lifecycle.
/// </summary>
public sealed class RewardCardSpawner : Component
{
	/// <summary>
	/// Reference to the RewardUnboxCrate component that triggers the card reveal.
	/// </summary>
	[Property] public RewardUnboxCrate Crate { get; set; }

	/// <summary>
	/// Distance from the camera to place the cards.
	/// </summary>
	[Property] public float CardDistanceFromCamera { get; set; } = 100f;

	/// <summary>
	/// Horizontal spacing between cards.
	/// </summary>
	[Property] public float CardSpacing { get; set; } = 35f;

	/// <summary>
	/// Vertical offset from camera center (positive = above center).
	/// </summary>
	[Property] public float CardVerticalOffset { get; set; } = 40f;

	/// <summary>
	/// Size of the world panel for each card.
	/// </summary>
	[Property] public Vector2 CardPanelSize { get; set; } = new Vector2( 300, 400 );

	/// <summary>
	/// The reward offer being displayed. Set by the UI system before the crate is opened.
	/// </summary>
	public RewardOffer Offer { get; set; }

	/// <summary>
	/// Fired when a card is selected/deselected. Passes the item def id and selection state.
	/// </summary>
	public Action<long, bool> OnSelectionChanged { get; set; }

	/// <summary>
	/// All spawned cards.
	/// </summary>
	public List<RewardCard> Cards { get; } = new();

	/// <summary>
	/// Currently selected item def ids.
	/// </summary>
	public List<long> SelectedItems { get; } = new();

	/// <summary>
	/// When true, selection can no longer change (e.g. after claiming).
	/// </summary>
	public bool SelectionLocked { get; set; }

	private bool _cardsSpawned;
	private bool _crateFullyOpen;

	protected override void OnUpdate()
	{
		if ( _cardsSpawned ) return;
		if ( Offer == null ) return;

		// Check if crate is fully open (all 4 flaps)
		if ( Crate != null && !_crateFullyOpen )
		{
			if ( Crate.IsFullyOpen )
			{
				_crateFullyOpen = true;
				SpawnCards();
			}
		}
	}

	/// <summary>
	/// Force-spawn cards immediately (for testing without the crate).
	/// </summary>
	public void ForceSpawnCards()
	{
		if ( _cardsSpawned ) return;
		if ( Offer == null ) return;

		SpawnCards();
	}

	private void SpawnCards()
	{
		_cardsSpawned = true;

		var camera = Scene.Camera;
		if ( camera == null ) return;

		int count = Offer.Items.Length;
		float totalWidth = (count - 1) * CardSpacing;
		float startX = -totalWidth * 0.5f;

		// Spawn position is from the crate
		var spawnPos = Crate != null ? Crate.WorldPosition : camera.WorldPosition + camera.WorldRotation.Forward * CardDistanceFromCamera;

		for ( int i = 0; i < count; i++ )
		{
			var item = Offer.Items[i];
			var card = SpawnCard( item, i, count, startX, spawnPos );
			Cards.Add( card );
		}

		// Notify any listening RewardUnboxUI that cards are now visible
		OnCardsRevealed?.Invoke();
	}

	/// <summary>
	/// Fired when all cards have been spawned and are flying into position.
	/// </summary>
	public Action OnCardsRevealed { get; set; }

	private RewardCard SpawnCard( RewardItem item, int index, int totalCount, float startX, Vector3 spawnPos )
	{
		var camera = Scene.Camera;

		// Calculate target position in front of camera
		var camForward = camera.WorldRotation.Forward;
		var camRight = camera.WorldRotation.Right;
		var camUp = camera.WorldRotation.Up;

		float xOffset = startX + index * CardSpacing;
		var targetPos = camera.WorldPosition
			+ camForward * CardDistanceFromCamera
			+ camRight * xOffset
			+ camUp * CardVerticalOffset;

		// Create the card GameObject
		var go = new GameObject( true, $"RewardCard_{index}" );
		go.WorldPosition = spawnPos;
		go.Parent = GameObject;

		// Add the WorldPanel component for rendering the card UI
		var worldPanel = go.AddComponent<Sandbox.WorldPanel>();
		worldPanel.PanelSize = CardPanelSize;
		worldPanel.LookAtCamera = false;
		worldPanel.RenderOptions.Game = false;
		worldPanel.RenderOptions.Overlay = true;

		// Add a RewardCardPanel to display the item
		var panelComponent = go.AddComponent<RewardCardPanel>();
		panelComponent.Item = item;

		// Add the RewardCard physics/interaction component
		var card = go.AddComponent<RewardCard>();
		card.Item = item;
		card.TargetPosition = targetPos;
		card.OnClicked = OnCardClicked;

		return card;
	}

	private void OnCardClicked( RewardCard card )
	{
		if ( Offer == null ) return;
		if ( SelectionLocked ) return;

		var itemId = card.Item.ItemDefId;

		if ( card.IsSelected )
		{
			// Deselect
			card.IsSelected = false;
			SelectedItems.Remove( itemId );
			card.SyncPanelSelection();
			OnSelectionChanged?.Invoke( itemId, false );
		}
		else
		{
			// If we're at max picks, replace (for single pick) or ignore
			if ( SelectedItems.Count >= Offer.PickCount )
			{
				if ( Offer.PickCount == 1 )
				{
					// Deselect the previous one
					foreach ( var c in Cards )
					{
						if ( c.IsSelected )
						{
							c.IsSelected = false;
							SelectedItems.Remove( c.Item.ItemDefId );
							c.SyncPanelSelection();
							OnSelectionChanged?.Invoke( c.Item.ItemDefId, false );
						}
					}
				}
				else
				{
					return;
				}
			}

			card.IsSelected = true;
			SelectedItems.Add( itemId );
			card.SyncPanelSelection();
			OnSelectionChanged?.Invoke( itemId, true );
		}
	}

	/// <summary>
	/// Clean up all spawned cards.
	/// </summary>
	public void DestroyCards()
	{
		foreach ( var card in Cards )
		{
			if ( card.IsValid() )
				card.GameObject.Destroy();
		}

		Cards.Clear();
		SelectedItems.Clear();
		_cardsSpawned = false;
		_crateFullyOpen = false;
	}
}
