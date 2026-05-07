import 'isomorphic-fetch';
import { Client } from '@microsoft/microsoft-graph-client';

/**
 * Creates an authenticated Microsoft Graph client using the caller's Bearer token.
 * The Entra Bearer token passed to fip-mcp already has MS Graph delegated scopes,
 * so no OAuth exchange is needed — pass it directly.
 *
 * @param {string} accessToken - Raw Bearer token from the incoming Authorization header
 * @returns {Client} Authenticated Graph client
 */
export function createGraphClient(accessToken) {
    return Client.init({
        authProvider: (done) => done(null, accessToken),
    });
}
