/**
 * Send a message to a Teams chat via Microsoft Graph /chats/{chatId}/messages.
 *
 * @param {import('@microsoft/microsoft-graph-client').Client} graphClient
 * @param {{ chatId: string, content: string }} options
 */
export async function sendTeamsMessage(graphClient, { chatId, content }) {
    const msg = await graphClient
        .api(`/chats/${chatId}/messages`)
        .post({
            body: {
                content,
                contentType: 'html',
            },
        });

    return {
        id: msg.id,
        chatId,
        createdDateTime: msg.createdDateTime,
        sent: true,
    };
}
