using System;
using Door = Sandbox.Mapping.Door;
using Button = Sandbox.Mapping.Button;

partial class QuakeMap
{
	private static readonly Dictionary<int, (string Move, string Stop)> DoorSounds = new()
	{
		[1] = ("sound/doors/doormv1.wav", "sound/doors/drclos4.wav"),
		[2] = ("sound/doors/hydro1.wav", "sound/doors/hydro2.wav"),
		[3] = ("sound/doors/stndr1.wav", "sound/doors/stndr2.wav"),
		[4] = ("sound/doors/ddoor1.wav", "sound/doors/ddoor2.wav"),
	};

	private static readonly Dictionary<int, string> ButtonSounds = new()
	{
		[0] = "sound/buttons/airbut1.wav",
		[1] = "sound/buttons/switch21.wav",
		[2] = "sound/buttons/switch02.wav",
		[3] = "sound/buttons/switch04.wav",
	};

	private readonly record struct PendingDoor( Door Door, BBox Bounds, bool DontLink, string TargetName );

	private void SpawnEntities( Quake.BSP.File file )
	{
		foreach ( var entity in file.Entities )
			SpawnEntity( file, entity );

		LinkDoors( _doors );
		WireButtons();
	}

	private void SpawnEntity( Quake.BSP.File file, Quake.BSP.File.ObjectEntry entity )
	{
		switch ( entity.TypeName )
		{
			case "info_player_start" or "info_player_start2" or "info_player_deathmatch" or "info_player_coop":
				SpawnPlayerStart( entity );
				return;
		}

		if ( entity.TypeName is not null && AmbientSounds.TryGetValue( entity.TypeName, out var ambient ) )
		{
			SpawnAmbientSound( entity, ambient.Path, ambient.Volume );
			return;
		}

		SpawnBrushModel( file, entity );
	}

	private static void SpawnPlayerStart( Quake.BSP.File.ObjectEntry entity )
	{
		var go = new GameObject( true, entity.TypeName );
		go.WorldPosition = entity.Position;
		go.WorldRotation = Rotation.FromYaw( entity.Angle );
		go.AddComponent<SpawnPoint>();
	}

	private static void LinkDoors( List<PendingDoor> doors )
	{
		var linkable = doors.Where( d => !d.DontLink ).ToList();
		var visited = new bool[linkable.Count];
		var queue = new Queue<int>();
		var group = new List<Door>();

		for ( var i = 0; i < linkable.Count; i++ )
		{
			if ( visited[i] )
				continue;

			group.Clear();
			queue.Enqueue( i );
			visited[i] = true;

			while ( queue.Count > 0 )
			{
				var current = queue.Dequeue();
				group.Add( linkable[current].Door );

				for ( var j = 0; j < linkable.Count; j++ )
				{
					if ( visited[j] || !BoundsTouch( linkable[current].Bounds, linkable[j].Bounds ) )
						continue;

					visited[j] = true;
					queue.Enqueue( j );
				}
			}

			for ( var j = 0; group.Count > 1 && j < group.Count; j++ )
				group[j].LinkedDoor = group[(j + 1) % group.Count];
		}
	}

	private static bool BoundsTouch( BBox a, BBox b )
	{
		return a.Mins.x <= b.Maxs.x && a.Maxs.x >= b.Mins.x
			&& a.Mins.y <= b.Maxs.y && a.Maxs.y >= b.Mins.y
			&& a.Mins.z <= b.Maxs.z && a.Maxs.z >= b.Mins.z;
	}

	private void WireButtons()
	{
		if ( _buttons.Count == 0 )
			return;

		var doorsByTarget = _doors
			.Where( d => !string.IsNullOrEmpty( d.TargetName ) )
			.GroupBy( d => d.TargetName, StringComparer.OrdinalIgnoreCase )
			.ToDictionary( g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase );

		foreach ( var (button, target) in _buttons )
		{
			if ( !doorsByTarget.TryGetValue( target, out var doors ) )
				continue;

			button.OnTurnedOn = new Doo { Body = doors.Select( ToggleDoorBlock ).ToList() };
		}
	}

	private static Doo.Block ToggleDoorBlock( PendingDoor door ) => new Doo.InvokeBlock
	{
		InvokeType = Doo.InvokeType.Member,
		TargetComponent = new Doo.TargetComponent
		{
			Type = Doo.TargetComponent.TargetType.Direct,
			ComponentValue = door.Door
		},
		Member = "Sandbox.Mapping.Door.Toggle"
	};

	private void SpawnBrushModel( Quake.BSP.File file, Quake.BSP.File.ObjectEntry entity )
	{
		var modelRef = entity.GetString( "model" );
		if ( string.IsNullOrEmpty( modelRef ) || modelRef[0] != '*' )
			return;

		if ( !int.TryParse( modelRef.AsSpan( 1 ), out var modelIndex ) )
			return;

		if ( modelIndex <= 0 || modelIndex >= file.Models.Length )
			return;

		var bspModel = file.Models[modelIndex];
		var center = (bspModel.Mins + bspModel.Maxs) * 0.5f;

		if ( entity.TypeName is not null && entity.TypeName.StartsWith( "trigger", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( entity.TypeName is "trigger_hurt" )
				AddTriggerHurt( entity, bspModel, center );

			return;
		}

		var model = GetBrushModel( file, bspModel, modelIndex, center );
		if ( model is null )
			return;

		var go = new GameObject( true, entity.TypeName ?? $"*{modelIndex}" );
		go.WorldPosition = entity.Position + center;
		go.AddComponent<ModelRenderer>().Model = model;
		go.AddComponent<ModelCollider>().Model = model;

		switch ( entity.TypeName )
		{
			case "func_door":
				AddDoor( go, entity, bspModel );
				break;

			case "func_button":
				AddButton( go, entity, bspModel );
				break;
		}
	}

	private Model GetBrushModel( Quake.BSP.File file, Quake.BSP.Model bspModel, int modelIndex, Vector3 center )
	{
		if ( _brushModels.TryGetValue( modelIndex, out var model ) )
			return model;

		model = CreateModel( file, bspModel, BrushModelName( modelIndex ), center );
		_brushModels[modelIndex] = model;
		return model;
	}

	private void AddDoor( GameObject go, Quake.BSP.File.ObjectEntry entity, Quake.BSP.Model model )
	{
		var door = go.AddComponent<Door>();
		door.Mode = Door.DoorMode.Sliding;
		door.SlideOffset = MoveDistance( entity, model, 8f );
		door.Speed = entity.GetValue<float>( "speed", 100f );
		door.IsUsable = true;

		var wait = entity.GetValue<float>( "wait", 3f );
		door.AutoClose = wait >= 0f;
		if ( door.AutoClose )
			door.AutoCloseDelay = wait;

		var spawnflags = entity.GetValue<int>( "spawnflags", 0 );
		door.StartOpen = (spawnflags & 1) != 0;

		if ( DoorSounds.TryGetValue( entity.GetValue<int>( "sounds", 0 ), out var sounds ) )
		{
			var move = BuildSoundEvent( sounds.Move, 1f );
			var stop = BuildSoundEvent( sounds.Stop, 1f );

			door.OpenSound = move;
			door.CloseSound = move;
			door.OpenFinishedSound = stop;
			door.CloseFinishedSound = stop;
		}

		var bounds = new BBox( entity.Position + model.Mins, entity.Position + model.Maxs );
		_doors.Add( new PendingDoor( door, bounds, (spawnflags & 4) != 0, entity.TargetName ) );
	}

	private void AddButton( GameObject go, Quake.BSP.File.ObjectEntry entity, Quake.BSP.Model model )
	{
		var button = go.AddComponent<Button>();
		button.Mode = Button.ButtonMode.Toggle;
		button.Move = true;
		button.MoveDelta = MoveDistance( entity, model, 4f );

		var speed = entity.GetValue<float>( "speed", 40f );
		button.AnimationTime = MathF.Max( button.MoveDelta.Length / MathF.Max( speed, 1f ), 0.05f );

		var wait = entity.GetValue<float>( "wait", 1f );
		button.AutoReset = wait >= 0f;
		if ( button.AutoReset )
			button.ResetTime = wait;

		if ( ButtonSounds.TryGetValue( entity.GetValue<int>( "sounds", 0 ), out var wav ) )
			button.OnSound = BuildSoundEvent( wav, 1f );

		var target = entity.GetString( "target" );
		if ( !string.IsNullOrEmpty( target ) )
			_buttons.Add( (button, target) );
	}

	private static void AddTriggerHurt( Quake.BSP.File.ObjectEntry entity, Quake.BSP.Model model, Vector3 center )
	{
		var go = new GameObject( true, entity.TypeName );
		go.WorldPosition = entity.Position + center;

		var box = go.AddComponent<BoxCollider>();
		box.Scale = model.Maxs - model.Mins;
		box.IsTrigger = true;

		var dmg = entity.GetValue<float>( "dmg", 0f );

		var hurt = go.AddComponent<TriggerHurt>();
		hurt.Damage = dmg != 0f ? dmg : 5f;
		hurt.Rate = 1f;
	}

	private static Vector3 MoveDistance( Quake.BSP.File.ObjectEntry entity, Quake.BSP.Model model, float lipDefault )
	{
		var dir = MoveDirection( entity );
		var size = model.Maxs - model.Mins;
		var lip = entity.GetValue<float>( "lip", lipDefault );
		var distance = MathF.Max( MathF.Abs( Vector3.Dot( dir, size ) ) - lip, 0f );
		return dir * distance;
	}

	private static Vector3 MoveDirection( Quake.BSP.File.ObjectEntry entity ) => entity.Angle switch
	{
		-1f => Vector3.Up,
		-2f => Vector3.Down,
		_ => entity.Rotation.Forward
	};

	private string BrushModelName( int modelIndex ) => $"{WorldModelName}*{modelIndex}";
}
