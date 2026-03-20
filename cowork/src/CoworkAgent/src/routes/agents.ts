import express from 'express';
import { AGENT_REGISTRY } from '../agents/registry.js';

const router = express.Router();

// GET /agents — list all agents the requesting user has access to
// Phase 1: all registered agents visible to all authenticated users.
// Phase 2: filter by AgentAccessGrant (see COWORK-SPECIALIST-AGENTS-SPEC.md).
router.get('/', (_req, res) => {
  const agents = Object.values(AGENT_REGISTRY).map(a => ({
    id:          a.id,
    name:        a.name,
    description: a.description,
    icon:        a.icon,
    color:       a.color,
  }));
  res.json({ agents });
});

// GET /agents/:agentId — single agent metadata
router.get('/:agentId', (req, res) => {
  const agent = AGENT_REGISTRY[req.params.agentId];
  if (!agent) { res.status(404).json({ error: 'Agent not found' }); return; }
  res.json({
    id:          agent.id,
    name:        agent.name,
    description: agent.description,
    icon:        agent.icon,
    color:       agent.color,
    workspaceComponent: agent.workspaceComponent,
  });
});

export default router;
