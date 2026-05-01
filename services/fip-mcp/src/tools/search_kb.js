import {
  BedrockAgentRuntimeClient,
  RetrieveCommand,
} from '@aws-sdk/client-bedrock-agent-runtime';
import { getKb, SCOPING_RULE } from '../config/kb-inventory.js';
import { getEntitlements } from './list_kbs.js';

const bedrockClient = new BedrockAgentRuntimeClient({
  region: process.env.BEDROCK_REGION ?? 'us-east-1',
});

/**
 * Build the retrieval filter by merging auto-scoped security filter with caller-supplied filters.
 * Security filter always wins — callers cannot override it.
 */
function buildRetrievalFilter(kb, user, callerFilters) {
  const filters = [];

  // Auto-inject security filter based on KB scoping rule
  switch (kb.scoping_rule) {
    case SCOPING_RULE.USER_ID:
      filters.push({
        equals: { key: 'user_id', value: user.user_id },
      });
      break;

    case SCOPING_RULE.TEAM_ID: {
      const team_id = callerFilters?.team_id;
      if (!team_id) {
        throw { code: 'TEAM_ID_REQUIRED', status: 400, message: 'team_id is required for Team KB' };
      }
      // Phase 0: require team_id param but skip DB membership validation
      filters.push({
        equals: { key: 'team_id', value: team_id },
      });
      break;
    }

    case SCOPING_RULE.PROJECT_ID: {
      const project_id = callerFilters?.project_id;
      if (!project_id) {
        throw { code: 'PROJECT_ID_REQUIRED', status: 400, message: 'project_id is required for Project KB' };
      }
      filters.push({
        equals: { key: 'project_id', value: project_id },
      });
      break;
    }

    case SCOPING_RULE.NONE:
    default:
      // No filter — org-wide
      break;
  }

  // Merge caller-supplied metadata filters (excluding security-controlled keys)
  if (callerFilters) {
    const RESERVED_KEYS = ['user_id', 'team_id', 'project_id'];
    for (const [key, value] of Object.entries(callerFilters)) {
      if (!RESERVED_KEYS.includes(key)) {
        filters.push({ equals: { key, value } });
      }
    }
  }

  if (filters.length === 0) return undefined;
  if (filters.length === 1) return filters[0];
  return { andAll: filters };
}

export async function searchKb(args, user) {
  const { query, kb_id, top_k = 5, filters } = args;

  if (!query) throw { code: 'QUERY_REQUIRED', status: 400, message: 'query is required' };
  if (!kb_id) throw { code: 'KB_ID_REQUIRED', status: 400, message: 'kb_id is required' };

  const kb = getKb(kb_id);
  if (!kb) {
    throw { code: 'UNKNOWN_KB', status: 400, message: `Unknown KB: ${kb_id}` };
  }

  // Check read entitlement
  const entitlements = await getEntitlements(user);
  const entitled = entitlements.find(e => e.kb_id === kb_id && e.read);
  if (!entitled) {
    throw { code: 'NOT_ENTITLED', status: 403, message: `Not entitled to read KB: ${kb_id}` };
  }

  const retrievalFilter = buildRetrievalFilter(kb, user, filters);

  const command = new RetrieveCommand({
    knowledgeBaseId: kb_id,
    retrievalQuery: { text: query },
    retrievalConfiguration: {
      vectorSearchConfiguration: {
        numberOfResults: top_k,
        ...(retrievalFilter ? { filter: retrievalFilter } : {}),
      },
    },
  });

  const response = await bedrockClient.send(command);

  const results = (response.retrievalResults ?? []).map(r => ({
    content: r.content?.text ?? '',
    metadata: r.metadata ?? {},
    relevance_score: r.score ?? 0,
  }));

  return { results };
}
