// KB types
export const KB_TYPE = {
  CORP: 'corp',
  PERSONAL: 'personal',
  TEAM: 'team',
  PROJECT: 'project',
  NEXUS: 'nexus',
};

// Scoping rules per KB type
export const SCOPING_RULE = {
  NONE: 'none',             // org-wide, no filter
  USER_ID: 'user_id',       // auto-inject token.oid as user_id filter
  TEAM_ID: 'team_id',       // require team_id param, validate membership
  PROJECT_ID: 'project_id', // require project_id param, validate access
};

// KB inventory — keyed by KB ID
// IMPORTANT: Do NOT reference stale FORGE-DevTeam-Shared (EE1X6QJ9WH) anywhere
export const KB_INVENTORY = {
  // Production KBs
  WYSKBKWHPL: {
    kb_id: 'WYSKBKWHPL',
    kb_type: KB_TYPE.CORP,
    kb_name: 'Corp KB',
    data_source_id: 'O6DPFQ08WN',
    env: 'prod',
    scoping_rule: SCOPING_RULE.NONE,
    description: 'Organization-wide corporate knowledge base',
    writable: false, // requires forge-kb-admin role to write
  },
  ZCEZCJGHQC: {
    kb_id: 'ZCEZCJGHQC',
    kb_type: KB_TYPE.PERSONAL,
    kb_name: 'Personal KB',
    data_source_id: '3X5E9L4HAC',
    env: 'prod',
    scoping_rule: SCOPING_RULE.USER_ID,
    description: 'Personal knowledge base — user-scoped content',
    writable: true,
  },
  NRGEACKSBJ: {
    kb_id: 'NRGEACKSBJ',
    kb_type: KB_TYPE.TEAM,
    kb_name: 'Team KB',
    data_source_id: 'VYMEB3BA12',
    env: 'prod',
    scoping_rule: SCOPING_RULE.TEAM_ID,
    description: 'Team knowledge base — requires team membership',
    writable: true,
  },
  A5U1GKN0TS: {
    kb_id: 'A5U1GKN0TS',
    kb_type: KB_TYPE.PROJECT,
    kb_name: 'Project KB',
    data_source_id: null, // data_source_id TBD — see spec §8 open question #6
    env: 'prod',
    scoping_rule: SCOPING_RULE.PROJECT_ID,
    description: 'Project knowledge base — requires project access',
    writable: true,
  },
  WHB6WU9CVW: {
    kb_id: 'WHB6WU9CVW',
    kb_type: KB_TYPE.NEXUS,
    kb_name: 'NEXUS-Discovery KB',
    data_source_id: 'C9P8RCCNSO',
    env: 'prod',
    scoping_rule: SCOPING_RULE.NONE,
    description: 'NEXUS discovery context — org-wide read-only',
    writable: false, // requires forge-kb-admin role to write
  },

  // Dev KBs
  AOFDTSHGNT: {
    kb_id: 'AOFDTSHGNT',
    kb_type: KB_TYPE.CORP,
    kb_name: 'FORGE-Corp-Dev',
    data_source_id: 'VEJXTDPXXR',
    env: 'dev',
    scoping_rule: SCOPING_RULE.NONE,
    description: 'Dev corp knowledge base',
    writable: false,
  },
  PBKCTCPNUU: {
    kb_id: 'PBKCTCPNUU',
    kb_type: KB_TYPE.PERSONAL,
    kb_name: 'FORGE-Personal-Dev',
    data_source_id: 'JBYQ1PRBPC',
    env: 'dev',
    scoping_rule: SCOPING_RULE.USER_ID,
    description: 'Dev personal knowledge base',
    writable: true,
  },
  XLVSGM2BXH: {
    kb_id: 'XLVSGM2BXH',
    kb_type: KB_TYPE.TEAM,
    kb_name: 'FORGE-Team-Dev',
    data_source_id: 'ERBMWIFKG4',
    env: 'dev',
    scoping_rule: SCOPING_RULE.TEAM_ID,
    description: 'Dev team knowledge base',
    writable: true,
  },
  '70MDNR521D': {
    kb_id: '70MDNR521D',
    kb_type: KB_TYPE.PROJECT,
    kb_name: 'FORGE-Project-Dev',
    data_source_id: 'UJUDDNJTE1',
    env: 'dev',
    scoping_rule: SCOPING_RULE.PROJECT_ID,
    description: 'Dev project knowledge base',
    writable: true,
  },
};

// Helper: look up a KB by ID — returns null if not found
export function getKb(kb_id) {
  return KB_INVENTORY[kb_id] ?? null;
}

// Helper: get all KBs of a given type
export function getKbsByType(kb_type) {
  return Object.values(KB_INVENTORY).filter(kb => kb.kb_type === kb_type);
}

// Default readable KBs for all authenticated users (Phase 0 fallback)
// Corp KB (prod+dev), Personal KB (prod+dev), NEXUS KB
export const DEFAULT_READABLE_KB_IDS = [
  'WYSKBKWHPL', // Corp prod
  'AOFDTSHGNT', // Corp dev
  'ZCEZCJGHQC', // Personal prod
  'PBKCTCPNUU', // Personal dev
  'WHB6WU9CVW', // NEXUS
];
