import express from 'express';
import { tasksRouter } from './routes/tasks.js';
import { usersRouter } from './routes/users.js';
import { authMiddleware } from './middleware/auth.js';
import agentsRouter from './routes/agents.js';
import designRouter from './routes/design.js';

const app = express();

app.use(express.json());
app.use(authMiddleware);
app.use('/tasks', tasksRouter);
app.use('/users', usersRouter);
app.use('/agents/design', designRouter);
app.use('/agents', agentsRouter);

const port = parseInt(process.env.PORT ?? '3000', 10);
app.listen(port, () => {
  console.log(`CoworkAgent listening on :${port}`);
});
