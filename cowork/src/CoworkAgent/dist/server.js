"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
const express_1 = __importDefault(require("express"));
const tasks_js_1 = require("./routes/tasks.js");
const users_js_1 = require("./routes/users.js");
const auth_js_1 = require("./middleware/auth.js");
const agents_js_1 = __importDefault(require("./routes/agents.js"));
const design_js_1 = __importDefault(require("./routes/design.js"));
const app = (0, express_1.default)();
app.use(express_1.default.json());
app.use(auth_js_1.authMiddleware);
app.use('/tasks', tasks_js_1.tasksRouter);
app.use('/users', users_js_1.usersRouter);
app.use('/agents/design', design_js_1.default);
app.use('/agents', agents_js_1.default);
const port = parseInt(process.env.PORT ?? '3000', 10);
app.listen(port, () => {
    console.log(`CoworkAgent listening on :${port}`);
});
//# sourceMappingURL=server.js.map