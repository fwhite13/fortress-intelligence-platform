const FAIT_BASE_URL = process.env.FAIT_BASE_URL ?? 'https://fait.fortressam.ai';
const FAIT_INTERNAL_SECRET = process.env.FAIT_INTERNAL_SECRET;

export async function getFaitUserId(entraOid) {
  if (!FAIT_INTERNAL_SECRET) {
    console.warn('[fip-mcp] FAIT_INTERNAL_SECRET not set — cannot resolve FAIT user ID');
    return null;
  }
  try {
    const url = `${FAIT_BASE_URL}/api/firm/resolve-user?entraOid=${encodeURIComponent(entraOid)}`;
    const resp = await fetch(url, {
      headers: { 'X-Firm-Secret': FAIT_INTERNAL_SECRET },
      signal: AbortSignal.timeout(5000),
    });
    if (!resp.ok) {
      console.warn(`[fip-mcp] resolve-user returned ${resp.status} for OID ${entraOid}`);
      return null;
    }
    const data = await resp.json();
    return data.userId ?? null;
  } catch (e) {
    console.warn(`[fip-mcp] resolve-user failed: ${e.message}`);
    return null;
  }
}
