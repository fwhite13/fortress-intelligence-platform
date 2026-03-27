namespace FortressIntelligenceRM.Web.Models;

public enum MeetingStatus
{
    Scheduled,
    Joining,
    Recording,
    WaitingTranscript,   // Mode A — Graph subscription created, awaiting webhook notification
    Transcribing,
    Summarizing,
    Complete,
    Failed
}
