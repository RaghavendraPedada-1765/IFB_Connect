namespace IFB.Shared.Enums;

public enum UserRole
{
    Employee = 0,
    Manager = 1,
    Admin = 2
}

public enum PresenceStatus
{
    Offline = 0,
    Online = 1,
    Away = 2,
    DoNotDisturb = 3,
    Busy = 4
}

public enum MessageType
{
    Text = 0,
    File = 1,
    Image = 2,
    SystemEvent = 3,
    Announcement = 4
}

public enum ConversationType
{
    DirectMessage = 0,
    Group = 1,
    Channel = 2
}

public enum ReactionEmoji
{
    ThumbsUp = 0,
    Heart = 1,
    Laugh = 2,
    Wow = 3,
    Sad = 4,
    Clap = 5,
    Check = 6,
    Alert = 7
}

public enum AnnouncementPriority
{
    Normal = 0,
    Important = 1,
    Urgent = 2
}
