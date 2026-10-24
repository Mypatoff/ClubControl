namespace ClubControl.Shared;

public enum PcClass
{
    Standard,
    Vip
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
    PowerCommand
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
    ApplySettings
}
