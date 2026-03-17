import express from 'express';
import multer from 'multer';
import { tasksRouter } from './routes/tasks.js';
import { authMiddleware } from './middleware/auth.js';

const app = express();

app.use(express.json());
app.use(authMiddleware);
app.use('/tasks', tasksRouter);

const port = parseInt(process.env.PORT ?? '3000', 10);
app.listen(port, () => {
  console.log(`CoworkAgent listening on :${port}`);
});
