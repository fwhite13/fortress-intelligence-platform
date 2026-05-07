/**
 * Get a single email by ID, including full body content.
 *
 * @param {import('@microsoft/microsoft-graph-client').Client} graphClient
 * @param {{ messageId: string }} options
 */
export async function getEmail(graphClient, { messageId }) {
    const msg = await graphClient
        .api(`/me/messages/${messageId}`)
        .select('id,subject,from,toRecipients,ccRecipients,receivedDateTime,body,isRead,attachments')
        .get();

    return {
        id: msg.id,
        subject: msg.subject,
        from: msg.from,
        toRecipients: msg.toRecipients,
        ccRecipients: msg.ccRecipients,
        receivedDateTime: msg.receivedDateTime,
        body: msg.body,
        isRead: msg.isRead,
    };
}
