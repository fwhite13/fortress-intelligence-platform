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
    Failed,
    Waiting             // WI #7033: subscriber record in a multi-user dedup group, sitting idle
                         // while the primary recorder (IsPrimaryRecorder=true) owns the bot slot.
}
