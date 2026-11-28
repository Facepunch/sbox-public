namespace Sandbox.SteamApi
{
	internal enum WorkshopVote
	{
		Unvoted = 0,
		Up = 1,
		Down = 2
	}

	//
	// EResult
	//
	internal enum Result : int
	{
		None = 0,
		OK = 1,
		Fail = 2,
		NoConnection = 3,
		InvalidPassword = 5,
		LoggedInElsewhere = 6,
		InvalidProtocolVer = 7,
		InvalidParam = 8,
		FileNotFound = 9,
		Busy = 10,
		InvalidState = 11,
		InvalidName = 12,
		InvalidEmail = 13,
		DuplicateName = 14,
		AccessDenied = 15,
		Timeout = 16,
		Banned = 17,
		AccountNotFound = 18,
		InvalidSteamID = 19,
		ServiceUnavailable = 20,
		NotLoggedOn = 21,
		Pending = 22,
		EncryptionFailure = 23,
		InsufficientPrivilege = 24,
		LimitExceeded = 25,
		Revoked = 26,
		Expired = 27,
		AlreadyRedeemed = 28,
		DuplicateRequest = 29,
		AlreadyOwned = 30,
		IPNotFound = 31,
		PersistFailed = 32,
		LockingFailed = 33,
		LogonSessionReplaced = 34,
		ConnectFailed = 35,
		HandshakeFailed = 36,
		IOFailure = 37,
		RemoteDisconnect = 38,
		ShoppingCartNotFound = 39,
		Blocked = 40,
		Ignored = 41,
		NoMatch = 42,
		AccountDisabled = 43,
		ServiceReadOnly = 44,
		AccountNotFeatured = 45,
		AdministratorOK = 46,
		ContentVersion = 47,
		TryAnotherCM = 48,
		PasswordRequiredToKickSession = 49,
		AlreadyLoggedInElsewhere = 50,
		Suspended = 51,
		Cancelled = 52,
		DataCorruption = 53,
		DiskFull = 54,
		RemoteCallFailed = 55,
		PasswordUnset = 56,
		ExternalAccountUnlinked = 57,
		PSNTicketInvalid = 58,
		ExternalAccountAlreadyLinked = 59,
		RemoteFileConflict = 60,
		IllegalPassword = 61,
		SameAsPreviousValue = 62,
		AccountLogonDenied = 63,
		CannotUseOldPassword = 64,
		InvalidLoginAuthCode = 65,
		AccountLogonDeniedNoMail = 66,
		HardwareNotCapableOfIPT = 67,
		IPTInitError = 68,
		ParentalControlRestricted = 69,
		FacebookQueryError = 70,
		ExpiredLoginAuthCode = 71,
		IPLoginRestrictionFailed = 72,
		AccountLockedDown = 73,
		AccountLogonDeniedVerifiedEmailRequired = 74,
		NoMatchingURL = 75,
		BadResponse = 76,
		RequirePasswordReEntry = 77,
		ValueOutOfRange = 78,
		UnexpectedError = 79,
		Disabled = 80,
		InvalidCEGSubmission = 81,
		RestrictedDevice = 82,
		RegionLocked = 83,
		RateLimitExceeded = 84,
		AccountLoginDeniedNeedTwoFactor = 85,
		ItemDeleted = 86,
		AccountLoginDeniedThrottle = 87,
		TwoFactorCodeMismatch = 88,
		TwoFactorActivationCodeMismatch = 89,
		AccountAssociatedToMultiplePartners = 90,
		NotModified = 91,
		NoMobileDevice = 92,
		TimeNotSynced = 93,
		SmsCodeFailed = 94,
		AccountLimitExceeded = 95,
		AccountActivityLimitExceeded = 96,
		PhoneActivityLimitExceeded = 97,
		RefundToWallet = 98,
		EmailSendFailure = 99,
		NotSettled = 100,
		NeedCaptcha = 101,
		GSLTDenied = 102,
		GSOwnerDenied = 103,
		InvalidItemType = 104,
		IPBanned = 105,
		GSLTExpired = 106,
		InsufficientFunds = 107,
		TooManyPending = 108,
		NoSiteLicensesFound = 109,
		WGNetworkSendExceeded = 110,
		AccountNotFriends = 111,
		LimitedUserAccount = 112,
		CantRemoveItem = 113,
		AccountDeleted = 114,
		ExistingUserCancelledLicense = 115,
		CommunityCooldown = 116,
		NoLauncherSpecified = 117,
		MustAgreeToSSA = 118,
		LauncherMigrated = 119,
		SteamRealmMismatch = 120,
		InvalidSignature = 121,
		ParseFailure = 122,
		NoVerifiedPhone = 123,
	}

	//
	// EBeginAuthSessionResult
	//
	internal enum BeginAuthResult : int
	{
		OK = 0,
		InvalidTicket = 1,
		DuplicateRequest = 2,
		InvalidVersion = 3,
		GameMismatch = 4,
		ExpiredTicket = 5,
	}

	//
	// EAuthSessionResponse
	//
	internal enum AuthResponse : int
	{
		OK = 0,
		UserNotConnectedToSteam = 1,
		NoLicenseOrExpired = 2,
		VACBanned = 3,
		LoggedInElseWhere = 4,
		VACCheckTimedOut = 5,
		AuthTicketCanceled = 6,
		AuthTicketInvalidAlreadyUsed = 7,
		AuthTicketInvalid = 8,
		PublisherIssuedBan = 9,
	}

	//
	// EChatRoomEnterResponse
	//
	internal enum RoomEnter : int
	{
		Success = 1,
		DoesntExist = 2,
		NotAllowed = 3,
		Full = 4,
		Error = 5,
		Banned = 6,
		Limited = 7,
		ClanDisabled = 8,
		CommunityBan = 9,
		MemberBlockedYou = 10,
		YouBlockedMember = 11,
		RatelimitExceeded = 15,
	}

	//
	// EFriendRelationship
	//
	internal enum Relationship : int
	{
		None = 0,
		Blocked = 1,
		RequestRecipient = 2,
		Friend = 3,
		RequestInitiator = 4,
		Ignored = 5,
		IgnoredFriend = 6,
		Suggested_DEPRECATED = 7,
		Max = 8,
	}

	//
	// EPersonaState
	//
	internal enum FriendState : int
	{
		Offline = 0,
		Online = 1,
		Busy = 2,
		Away = 3,
		Snooze = 4,
		LookingToTrade = 5,
		LookingToPlay = 6,
		Invisible = 7,
		Max = 8,
	}

	//
	// EPersonaChange
	//
	[Flags]
	internal enum PersonaChange : int
	{
		Name = 1,
		Status = 2,
		ComeOnline = 4,
		GoneOffline = 8,
		GamePlayed = 16,
		GameServer = 32,
		Avatar = 64,
		JoinedSource = 128,
		LeftSource = 256,
		RelationshipChanged = 512,
		NameFirstSet = 1024,
		Broadcast = 2048,
		Nickname = 4096,
		SteamLevel = 8192,
		RichPresence = 16384,
	}

	//
	// ETextFilteringContext
	//
	internal enum TextFilteringContext : int
	{
		Unknown = 0,
		GameContent = 1,
		Chat = 2,
		Name = 3,
	}

	//
	// ELobbyType
	//
	internal enum LobbyType : int
	{
		Private = 0,
		FriendsOnly = 1,
		Public = 2,
		Invisible = 3,
		PrivateUnique = 4,
	}

	//
	// EWorkshopFileType
	//
	internal enum WorkshopFileType : int
	{
		First = 0,
		Community = 0,
		Microtransaction = 1,
		Collection = 2,
		Art = 3,
		Video = 4,
		Screenshot = 5,
		Game = 6,
		Software = 7,
		Concept = 8,
		WebGuide = 9,
		IntegratedGuide = 10,
		Merch = 11,
		ControllerBinding = 12,
		SteamworksAccessInvite = 13,
		SteamVideo = 14,
		GameManagedItem = 15,
		Max = 16,
	}

	//
	// ESteamNetworkingAvailability
	//
	internal enum SteamNetworkingAvailability : int
	{
		CannotTry = -102,
		Failed = -101,
		Previously = -100,
		Retrying = -10,
		NeverTried = 1,
		Waiting = 2,
		Attempting = 3,
		Current = 100,
		Unknown = 0,
		Force32bit = 2147483647,
	}

	//
	// ESteamNetworkingConnectionState
	//
	internal enum ConnectionState : int
	{
		None = 0,
		Connecting = 1,
		FindingRoute = 2,
		Connected = 3,
		ClosedByPeer = 4,
		ProblemDetectedLocally = 5,
		FinWait = -1,
		Linger = -2,
		Dead = -3,
	}

	//
	// ESteamNetConnectionEnd
	//
	internal enum NetConnectionEnd : int
	{
		Invalid = 0,
		App_Min = 1000,
		App_Generic = 1000,
		App_Max = 1999,
		AppException_Min = 2000,
		AppException_Generic = 2000,
		AppException_Max = 2999,
		Local_Min = 3000,
		Local_OfflineMode = 3001,
		Local_ManyRelayConnectivity = 3002,
		Local_HostedServerPrimaryRelay = 3003,
		Local_NetworkConfig = 3004,
		Local_Rights = 3005,
		Local_P2P_ICE_NoPublicAddresses = 3006,
		Local_Max = 3999,
		Remote_Min = 4000,
		Remote_Timeout = 4001,
		Remote_BadCrypt = 4002,
		Remote_BadCert = 4003,
		Remote_BadProtocolVersion = 4006,
		Remote_P2P_ICE_NoPublicAddresses = 4007,
		Remote_Max = 4999,
		Misc_Min = 5000,
		Misc_Generic = 5001,
		Misc_InternalError = 5002,
		Misc_Timeout = 5003,
		Misc_SteamConnectivity = 5005,
		Misc_NoRelaySessionsToClient = 5006,
		Misc_P2P_Rendezvous = 5008,
		Misc_P2P_NAT_Firewall = 5009,
		Misc_PeerSentNoConnection = 5010,
		Misc_Max = 5999,
	}
}
