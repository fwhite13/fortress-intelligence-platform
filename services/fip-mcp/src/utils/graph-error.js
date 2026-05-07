/**
 * Handles Microsoft Graph API errors and normalizes them.
 * Graph errors have a statusCode property and a body.error shape.
 */
export function handleGraphError(err) {
    // Try to extract Graph error details from response body
    const graphError = err.body?.error ?? err.response?.error;
    if (graphError) {
        return {
            error: {
                code: `GRAPH_${graphError.code ?? err.statusCode ?? 'ERROR'}`,
                message: graphError.message ?? err.message
            }
        };
    }
    if (err.statusCode) {
        return { error: { code: `GRAPH_${err.statusCode}`, message: err.message } };
    }
    console.error('[fip-mcp] Graph error:', err);
    return { error: { code: 'GRAPH_ERROR', message: err.message ?? 'Microsoft Graph error' } };
}
