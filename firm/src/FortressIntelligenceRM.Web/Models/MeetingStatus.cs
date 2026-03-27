namespace FortressIntelligenceRM.Web.Models;

public enum MeetingStatus
{
    Scheduled,          // Added to FIRM, bot not yet dispatched
    Pending,
    Joining,
    Recording,
    WaitingTranscript,   // Mode A — Graph subscription created, awaiting webhook notification
    Transcribing,
    Summarizing,
    Complete,
    Failed
}
