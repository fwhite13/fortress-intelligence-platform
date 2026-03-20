"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
const express_1 = __importDefault(require("express"));
const registry_js_1 = require("../agents/registry.js");
const router = express_1.default.Router();
// GET /agents — list all agents the requesting user has access to
// Phase 1: all registered agents visible to all authenticated users.
// Phase 2: filter by AgentAccessGrant (see COWORK-SPECIALIST-AGENTS-SPEC.md).
router.get('/', (_req, res) => {
    const agents = Object.values(registry_js_1.AGENT_REGISTRY).map(a => ({
        id: a.id,
        name: a.name,
        description: a.description,
        icon: a.icon,
        color: a.color,
    }));
    res.json({ agents });
});
// GET /agents/:agentId — single agent metadata
router.get('/:agentId', (req, res) => {
    const agent = registry_js_1.AGENT_REGISTRY[req.params.agentId];
    if (!agent) {
        res.status(404).json({ error: 'Agent not found' });
        return;
    }
    res.json({
        id: agent.id,
        name: agent.name,
        description: agent.description,
        icon: agent.icon,
        color: agent.color,
        workspaceComponent: agent.workspaceComponent,
    });
});
exports.default = router;
//# sourceMappingURL=agents.js.map