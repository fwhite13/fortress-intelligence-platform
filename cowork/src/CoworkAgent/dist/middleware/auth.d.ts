import { Request, Response, NextFunction } from 'express';
export interface AuthedRequest extends Request {
    userId: string;
    userEmail: string;
}
export declare function authMiddleware(req: Request, res: Response, next: NextFunction): void;
