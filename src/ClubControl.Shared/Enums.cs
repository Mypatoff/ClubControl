namespace ClubControl.Shared;

/// <summary>What kind of billable thing a Pc row represents (SPEC extension: not every billable thing is a computer).</summary>
public enum ResourceKind
{
    Computer,
    Billiard
}

public enum PcState
{
    Free,
    Locked,
    PrepaySession,
    PostpaySession,
    LimitReached,
    OfflineUnlocked
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
    AuthRejected
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
    Restart
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
    ProvisionToken
}
