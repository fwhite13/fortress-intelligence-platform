/**
 * Send an email via Microsoft Graph /me/sendMail.
 *
 * @param {import('@microsoft/microsoft-graph-client').Client} graphClient
 * @param {{ to: string[], subject: string, body: string, cc?: string[] }} options
 */
export async function sendEmail(graphClient, { to, subject, body, cc = [] }) {
    const message = {
        subject,
        body: {
            contentType: 'HTML',
            content: body,
        },
        toRecipients: to.map(address => ({
            emailAddress: { address },
        })),
        ccRecipients: cc.map(address => ({
            emailAddress: { address },
        })),
    };

    await graphClient
        .api('/me/sendMail')
        .post({ message, saveToSentItems: true });

    return { sent: true, to, subject };
}
