// ADO#3531 — unit test: pruneToolResults
// Run: node test-prune.js

function pruneToolResults(messages, windowSize = 10) {
    const STUB = '[result from prior session — call tool again for fresh data]';
    const pruneBeforeIndex = Math.max(0, messages.length - windowSize);
    return messages.map((msg, idx) => {
        if (idx >= pruneBeforeIndex) return msg; // within window — keep verbatim
        // Check if this message has any toolResult content blocks
        if (!Array.isArray(msg.content)) return msg;
        const hasToolResult = msg.content.some(block => block.toolResult !== undefined);
        if (!hasToolResult) return msg;
        // Replace toolResult content with stub — preserve structure
        return {
            ...msg,
            content: msg.content.map(block => {
                if (block.toolResult === undefined) return block;
                return {
                    toolResult: {
                        ...block.toolResult,
                        content: [{ text: STUB }]
                    }
                };
            })
        };
    });
}

// Test 1: messages within window — untouched
const msgs1 = Array.from({ length: 5 }, (_, i) => ({
    role: i % 2 === 0 ? 'user' : 'assistant',
    content: [{ toolResult: { toolUseId: `id${i}`, content: [{ text: `result ${i}` }], status: 'success' } }]
}));
const pruned1 = pruneToolResults(msgs1, 10);
console.assert(pruned1[0].content[0].toolResult.content[0].text === 'result 0', 'FAIL: short array should be untouched');
console.log('Test 1 PASS: short array untouched');

// Test 2: 15 messages — first 5 should be stubbed
const msgs2 = Array.from({ length: 15 }, (_, i) => ({
    role: 'user',
    content: [{ toolResult: { toolUseId: `id${i}`, content: [{ text: `result ${i}` }], status: 'success' } }]
}));
const pruned2 = pruneToolResults(msgs2, 10);
console.assert(pruned2[0].content[0].toolResult.content[0].text === '[result from prior session — call tool again for fresh data]', 'FAIL: old msg should be stubbed');
console.assert(pruned2[14].content[0].toolResult.content[0].text === 'result 14', 'FAIL: recent msg should be untouched');
console.log('Test 2 PASS: old msgs stubbed, recent msgs untouched');

// Test 3: structure preserved — toolUseId still present
console.assert(pruned2[0].content[0].toolResult.toolUseId === 'id0', 'FAIL: toolUseId must be preserved');
console.log('Test 3 PASS: toolUseId preserved after pruning');

// Test 4: non-toolResult messages untouched
const msgs3 = Array.from({ length: 15 }, (_, i) => ({
    role: i % 2 === 0 ? 'user' : 'assistant',
    content: [{ text: `message ${i}` }]
}));
const pruned3 = pruneToolResults(msgs3, 10);
console.assert(pruned3[0].content[0].text === 'message 0', 'FAIL: text messages should be untouched');
console.log('Test 4 PASS: plain text messages untouched');

console.log('All tests PASSED ✅');
