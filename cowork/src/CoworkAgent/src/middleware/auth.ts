import { Request, Response, NextFunction } from 'express';
import jwt from 'jsonwebtoken';

const SECRET = process.env.COWORK_INTERNAL_SECRET;
if (!SECRET) throw new Error('COWORK_INTERNAL_SECRET env var required');

// Capture as non-nullable after the guard above
const VERIFIED_SECRET: string = SECRET;

export interface AuthedRequest extends Request {
  userId: string;
  userEmail: string;
}

export function authMiddleware(req: Request, res: Response, next: NextFunction): void {
  const auth = req.headers.authorization;
  if (!auth?.startsWith('Bearer ')) {
    res.status(401).json({ error: 'Missing internal auth token' });
    return;
  }

  try {
    const token = auth.slice(7);
    const payload = jwt.verify(token, VERIFIED_SECRET, {
      issuer:   'cowork-web',
      audience: 'cowork-agent',
    }) as unknown as { sub: string; email: string };

    (req as AuthedRequest).userId    = payload.sub;
    (req as AuthedRequest).userEmail = payload.email;
    next();
  } catch {
    res.status(401).json({ error: 'Invalid internal auth token' });
  }
}
