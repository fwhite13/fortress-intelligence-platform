namespace FortressIntelligenceRM.Web.Models;

/// <summary>
/// UI-only status labels/colors for meeting badges. DB status values are unchanged.
/// WI #8277: subscribers sit in Waiting from creation, which read as "broken" for meetings
/// still in the future — show "Scheduled - Subscriber" until start time passes.
/// WI #8289: "Scheduled - Primary" only when the primary actually has subscribers (HasSubscribers);
/// solo primaries show plain "Scheduled".
/// </summary>
public static class MeetingStatusDisplay
{
    public static (string Color, string Label) GetStatusDisplay(this FirmMeeting meeting, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;

        if (meeting.IsPrimaryRecorder && meeting.HasSubscribers && meeting.Status == MeetingStatus.Scheduled)
            return ("#607d8b", "Scheduled - Primary");

        if (!meeting.IsPrimaryRecorder && meeting.Status == MeetingStatus.Waiting
            && meeting.StartDatetime.HasValue && meeting.StartDatetime.Value > now)
            return ("#607d8b", "Scheduled - Subscriber");

        return meeting.Status switch
        {
            MeetingStatus.Joining => ("#2196f3", "Joining"),
            MeetingStatus.Recording => ("#ff9800", "Recording"),
            MeetingStatus.Transcribing => ("#9c27b0", "Transcribing"),
            MeetingStatus.Summarizing => ("#00bcd4", "Summarizing"),
            MeetingStatus.Complete => ("#4caf50", "Complete"),
            MeetingStatus.Failed => ("#f44336", "Failed"),
            MeetingStatus.Scheduled => ("#607d8b", "Scheduled"),
            MeetingStatus.Waiting => ("#9e9e9e", "Waiting"),
            _ => ("#607d8b", meeting.Status.ToString())
        };
    }

    /// <summary>WI #8576: human-readable LastFailureReason for the Failed badge tooltip. Unknown codes
    /// fall back to the raw value; no code at all falls back to ErrorMessage (null if neither is set).</summary>
    public static string? GetFailureReasonText(this FirmMeeting meeting)
    {
        if (string.IsNullOrWhiteSpace(meeting.LastFailureReason))
            return string.IsNullOrWhiteSpace(meeting.ErrorMessage) ? null : meeting.ErrorMessage;

        return meeting.LastFailureReason switch
        {
            "lobby_timeout" => "Bot was not admitted to the meeting lobby",
            "no_primary_joined" => "No recording bot was able to join this meeting",
            "primary_failed_window_passed" => "The recording bot failed and the meeting's join window has passed",
            "bot_launch_failed" => "The recording bot stopped before it could join the meeting",
            "zoom_not_connected" => "Your Zoom account is not connected",
            "zoom_obf_unavailable" => "Zoom authorization was unavailable for this meeting",
            var raw => raw
        };
    }
}
