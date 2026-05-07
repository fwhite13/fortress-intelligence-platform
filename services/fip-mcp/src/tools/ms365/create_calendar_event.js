/**
 * Create a calendar event (optionally a Teams meeting) via Microsoft Graph /me/events.
 *
 * @param {import('@microsoft/microsoft-graph-client').Client} graphClient
 * @param {{ subject: string, start: string, end: string, attendees?: string[], body?: string, location?: string, isTeamsMeeting?: boolean }} options
 */
export async function createCalendarEvent(graphClient, {
    subject,
    start,
    end,
    attendees = [],
    body = '',
    location = '',
    isTeamsMeeting = false,
}) {
    const event = {
        subject,
        body: {
            contentType: 'HTML',
            content: body,
        },
        start: {
            dateTime: start,
            timeZone: 'UTC',
        },
        end: {
            dateTime: end,
            timeZone: 'UTC',
        },
        attendees: attendees.map(address => ({
            emailAddress: { address },
            type: 'required',
        })),
    };

    if (location) {
        event.location = { displayName: location };
    }

    if (isTeamsMeeting) {
        event.isOnlineMeeting = true;
        event.onlineMeetingProvider = 'teamsForBusiness';
    }

    const created = await graphClient
        .api('/me/events')
        .post(event);

    return {
        id: created.id,
        subject: created.subject,
        start: created.start,
        end: created.end,
        onlineMeeting: created.onlineMeeting ?? null,
        webLink: created.webLink,
    };
}
