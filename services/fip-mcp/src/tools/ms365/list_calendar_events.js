/**
 * List calendar events in a date range via Microsoft Graph /me/calendarView.
 *
 * @param {import('@microsoft/microsoft-graph-client').Client} graphClient
 * @param {{ startDateTime: string, endDateTime: string, top?: number }} options
 */
export async function listCalendarEvents(graphClient, { startDateTime, endDateTime, top = 10 }) {
    const response = await graphClient
        .api('/me/calendarView')
        .query({ startDateTime, endDateTime })
        .select('id,subject,start,end,location,organizer,attendees,isOnlineMeeting,onlineMeetingUrl')
        .top(top)
        .orderby('start/dateTime ASC')
        .get();

    return (response.value ?? []).map(evt => ({
        id: evt.id,
        subject: evt.subject,
        start: evt.start,
        end: evt.end,
        location: evt.location,
        organizer: evt.organizer,
        attendees: evt.attendees,
        isOnlineMeeting: evt.isOnlineMeeting,
        onlineMeetingUrl: evt.onlineMeetingUrl,
    }));
}
