import { createRequire } from 'module';
import path from 'path';
import { fileURLToPath } from 'url';
import { KB_INVENTORY, DEFAULT_READABLE_KB_IDS } from '../config/kb-inventory.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Load fallback entitlements config
function loadFallbackEntitlements() {
  const configPath = process.env.FALLBACK_ENTITLEMENTS_CONFIG
    ?? path.join(__dirname, '../config/entitlements.json');
  const require = createRequire(import.meta.url);
  try {
    return require(configPath);
  } catch (e) {
    console.warn('[fip-mcp] Warning: could not load entitlements.json, using hardcoded defaults');
    return {
      groups: {},
      defaults: DEFAULT_READABLE_KB_IDS.map(kb_id => ({
        kb_id,
        read: true,
        write: kb_id !== 'WYSKBKWHPL' && kb_id !== 'WHB6WU9CVW' && kb_id !== 'AOFDTSHGNT',
      })),
    };
  }
}

const fallbackConfig = loadFallbackEntitlements();

/**
 * Resolve entitlements for a user from the fallback static config.
 * Returns array of { kb_id, read, write }.
 *
 * Priority: defaults + group overrides.
 * NEXUS KB is always readable regardless.
 */
export async function getEntitlements(user) {
  const entitlementMap = new Map();

  // Start with defaults
  for (const entry of (fallbackConfig.defaults ?? [])) {
    entitlementMap.set(entry.kb_id, { kb_id: entry.kb_id, read: entry.read, write: entry.write });
  }

  // Apply group-based entitlements
  for (const groupId of (user.groups ?? [])) {
    const groupConfig = fallbackConfig.groups?.[groupId];
    if (groupConfig?.kbs) {
      for (const entry of groupConfig.kbs) {
        const existing = entitlementMap.get(entry.kb_id) ?? { kb_id: entry.kb_id, read: false, write: false };
        entitlementMap.set(entry.kb_id, {
          kb_id: entry.kb_id,
          read: existing.read || entry.read,
          write: existing.write || entry.write,
        });
      }
    }
  }

  // NEXUS KB is always readable for authenticated users
  if (!entitlementMap.has('WHB6WU9CVW')) {
    entitlementMap.set('WHB6WU9CVW', { kb_id: 'WHB6WU9CVW', read: true, write: false });
  } else {
    const nexus = entitlementMap.get('WHB6WU9CVW');
    nexus.read = true;
    nexus.write = false; // NEXUS is always read-only unless forge-kb-admin
  }

  return Array.from(entitlementMap.values());
}

export async function listKbs(args, user) {
  const entitlements = await getEntitlements(user);

  const kbs = entitlements
    .filter(e => e.read)
    .map(e => {
      const kb = KB_INVENTORY[e.kb_id];
      if (!kb) return null;
      return {
        kb_id: kb.kb_id,
        kb_type: kb.kb_type,
        description: kb.description,
        writable: e.write && kb.writable,
      };
    })
    .filter(Boolean);

  return { kbs };
}
