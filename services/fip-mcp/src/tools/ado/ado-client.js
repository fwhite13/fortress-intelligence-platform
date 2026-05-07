// src/tools/ado/ado-client.js

const ORG = process.env.AZDO_ORG ?? 'FortressAffinityGroup';
const PAT = process.env.AZDO_PAT;

const BASE_URL = `https://dev.azure.com/${ORG}`;

function getAuthHeader() {
  if (!PAT) throw new Error('[fip-mcp] AZDO_PAT env var not set — ADO tools unavailable');
  return 'Basic ' + Buffer.from(`:${PAT}`).toString('base64');
}

export async function adoGet(path) {
  const url = `${BASE_URL}${path}`;
  const res = await fetch(url, {
    headers: {
      'Authorization': getAuthHeader(),
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
  });
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new Error(`[ADO] GET ${path} failed: ${res.status} ${res.statusText} — ${body}`);
  }
  return res.json();
}

export async function adoPost(path, body) {
  const url = `${BASE_URL}${path}`;
  const res = await fetch(url, {
    method: 'POST',
    headers: {
      'Authorization': getAuthHeader(),
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`[ADO] POST ${path} failed: ${res.status} ${res.statusText} — ${text}`);
  }
  return res.json();
}

/**
 * POST with JSON Patch content type — used for work item creation.
 * ADO create work item requires HTTP POST but with application/json-patch+json body.
 */
export async function adoPostPatch(path, body) {
  const url = `${BASE_URL}${path}`;
  const res = await fetch(url, {
    method: 'POST',
    headers: {
      'Authorization': getAuthHeader(),
      'Content-Type': 'application/json-patch+json',
      'Accept': 'application/json',
    },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`[ADO] POST(patch) ${path} failed: ${res.status} ${res.statusText} — ${text}`);
  }
  return res.json();
}

export async function adoPatch(path, body) {
  const url = `${BASE_URL}${path}`;
  const res = await fetch(url, {
    method: 'PATCH',
    headers: {
      'Authorization': getAuthHeader(),
      'Content-Type': 'application/json-patch+json',
      'Accept': 'application/json',
    },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`[ADO] PATCH ${path} failed: ${res.status} ${res.statusText} — ${text}`);
  }
  return res.json();
}

export function getAdoBaseUrl() {
  return BASE_URL;
}

export function getAdoOrg() {
  return ORG;
}

export function isPATConfigured() {
  return !!PAT;
}
