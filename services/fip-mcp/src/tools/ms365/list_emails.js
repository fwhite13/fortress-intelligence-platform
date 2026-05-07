/**
 * List emails from the user's mailbox via Microsoft Graph /me/messages.
 *
 * @param {import('@microsoft/microsoft-graph-client').Client} graphClient
 * @param {{ top?: number, filter?: string|null, search?: string|null }} options
 */
export async function listEmails(graphClient, { top = 10, filter = null, search = null }) {
    if (filter && search) {
        throw Object.assign(
            new Error('Microsoft Graph does not support $filter and $search simultaneously. Use one or the other.'),
            { statusCode: 400, code: 'INVALID_PARAMS' }
        );
    }

    let req = graphClient
        .api('/me/messages')
        .select('id,subject,from,receivedDateTime,bodyPreview,isRead')
        .top(top);

    if (!search) {
        req = req.orderby('receivedDateTime DESC');
    }

    if (filter) {
        req = req.filter(filter);
    }
    if (search) {
        req = req.search(`"${search}"`);
    }

    const response = await req.get();
    return (response.value ?? []).map(msg => ({
        id: msg.id,
        subject: msg.subject,
        from: msg.from,
        receivedDateTime: msg.receivedDateTime,
        bodyPreview: msg.bodyPreview,
        isRead: msg.isRead,
    }));
}
