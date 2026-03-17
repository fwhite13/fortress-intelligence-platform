import express from 'express';
import multer from 'multer';
import { tasksRouter } from './routes/tasks.js';
import { usersRouter } from './routes/users.js';
import { authMiddleware } from './middleware/auth.js';

const app = express();

app.use(express.json());
app.use(authMiddleware);
app.use('/tasks', tasksRouter);
app.use('/users', usersRouter);

const port = parseInt(process.env.PORT ?? '3000', 10);
app.listen(port, () => {
  console.log(`CoworkAgent listening on :${port}`);
});
