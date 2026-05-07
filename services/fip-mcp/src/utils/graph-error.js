/**
 * Handles Microsoft Graph API errors and normalizes them.
 * Graph errors have a statusCode property and a body.error shape.
 */
export function handleGraphError(err) {
    if (err.statusCode) {
        // Graph client errors expose statusCode
        const msg = err.message ?? 'Microsoft Graph error';
        return { error: { code: `GRAPH_${err.statusCode}`, message: msg } };
    }
    console.error('[fip-mcp] Graph error:', err);
    return { error: { code: 'GRAPH_ERROR', message: err.message ?? 'Microsoft Graph error' } };
}
