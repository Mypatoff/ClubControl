namespace ClubControl.Shared;

/// <summary>What kind of billable thing a Pc row represents (SPEC extension: not every billable thing is a computer).</summary>
public enum ResourceKind
{
    Computer,
    Other
}

public enum PcState
{
    Free,
    Locked,
    PrepaySession,
    PostpaySession,
    LimitReached,
    ManualUnlocked
}

public enum SessionType
{
    Prepay,
    Postpay,
    Free
}

public enum SessionStatus
{
    Active,
    Ended
}

public enum EventType
{
    PcConnected,
    PcDisconnected,
    SessionStarted,
    SessionEnded,
    SessionExtended,
    SessionMoved,
    Warning,
    LimitReached,
    OfflineUnlock,
    PowerCommand,
    AuthRejected,
    ManualUnlock,
    ItemAdded,
    ShiftOpened,
    ShiftClosed
}

public enum LockScreenLanguage
{
    Uzbek,
    Russian,
    English
}

public enum AdminPanelLanguage
{
    Russian,
    Uzbek
}

public enum AfterSessionAction
{
    CloseProcesses,
    Restart,
    Shutdown
}

public enum CommandType
{
    Lock,
    Unlock,
    StartSession,
    UpdateSession,
    EndSession,
    Shutdown,
    Restart,
    ApplySettings,
    ProvisionToken,
    UpdateClient
}
