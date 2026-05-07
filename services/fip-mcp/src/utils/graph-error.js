/**
 * Handles Microsoft Graph API errors and normalizes them.
 * Graph SDK v3: err.body is a JSON string, err.code is set directly by SDK.
 */
export function handleGraphError(err) {
    console.error('[fip-mcp] Graph error:', err);
    // Graph SDK v3: err.code is set directly by SDK for well-known Graph errors
    if (err.code) {
        return { error: { code: err.code, message: err.message ?? 'Microsoft Graph error' } };
    }
    // Try parsing err.body (JSON string) for structured error
    if (err.body) {
        try {
            const parsed = JSON.parse(err.body);
            // SDK serializes inner error directly: { code, message } — no .error wrapper
            if (parsed?.code) {
                return { error: { code: parsed.code, message: parsed.message ?? err.message } };
            }
        } catch {
            // body wasn't valid JSON — fall through
        }
    }
    if (err.statusCode) {
        return { error: { code: `GRAPH_${err.statusCode}`, message: err.message ?? 'Microsoft Graph error' } };
    }
    return { error: { code: 'GRAPH_ERROR', message: err.message ?? 'Microsoft Graph error' } };
}
