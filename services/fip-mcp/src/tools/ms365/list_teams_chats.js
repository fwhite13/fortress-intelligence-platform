/**
 * List Teams chats the user is a member of via Microsoft Graph /me/chats.
 *
 * @param {import('@microsoft/microsoft-graph-client').Client} graphClient
 * @param {{ top?: number }} options
 */
export async function listTeamsChats(graphClient, { top = 10 }) {
    const response = await graphClient
        .api('/me/chats')
        .expand('members')
        .top(top)
        .select('id,chatType,topic,members,lastUpdatedDateTime')
        .get();

    return (response.value ?? []).map(chat => ({
        id: chat.id,
        chatType: chat.chatType,
        topic: chat.topic,
        members: (chat.members ?? []).map(m => ({
            id: m.id,
            displayName: m.displayName,
            email: m.email ?? m.userId,
        })),
        lastUpdatedDateTime: chat.lastUpdatedDateTime,
    }));
}
