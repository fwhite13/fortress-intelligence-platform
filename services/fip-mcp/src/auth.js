import jwt from 'jsonwebtoken';
import jwksRsa from 'jwks-rsa';

const TENANT_ID = process.env.ENTRA_TENANT_ID;
const CLIENT_ID = process.env.ENTRA_CLIENT_ID;
const JWKS_URI = `https://login.microsoftonline.com/${TENANT_ID}/discovery/v2.0/keys`;

// JWKS client — caches keys with 10min TTL
const jwksClient = jwksRsa({
  jwksUri: JWKS_URI,
  cache: true,
  cacheMaxEntries: 5,
  cacheMaxAge: 10 * 60 * 1000, // 10 minutes
  rateLimit: true,
});

function getSigningKey(header) {
  return new Promise((resolve, reject) => {
    jwksClient.getSigningKey(header.kid, (err, key) => {
      if (err) return reject(err);
      resolve(key.getPublicKey());
    });
  });
}

/**
 * Validate an Entra Bearer JWT. Returns decoded claims or throws.
 * Claims extracted: oid, groups, tid, roles
 */
export async function validateToken(authHeader) {
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    throw new Error('Missing or malformed Authorization header');
  }
  const token = authHeader.slice(7);

  // Decode header to get kid for JWKS lookup
  const decoded = jwt.decode(token, { complete: true });
  if (!decoded || !decoded.header) {
    throw new Error('Malformed JWT');
  }

  const publicKey = await getSigningKey(decoded.header);

  // Verify signature, expiry, aud, iss
  const payload = jwt.verify(token, publicKey, {
    algorithms: ['RS256'],
    audience: CLIENT_ID,
    issuer: [
      `https://login.microsoftonline.com/${TENANT_ID}/v2.0`,
      `https://sts.windows.net/${TENANT_ID}/`,
    ],
  });

  // Validate tid claim
  if (payload.tid !== TENANT_ID) {
    throw new Error('Token tenant mismatch');
  }

  return {
    user_id: payload.oid,        // Entra object ID
    groups: payload.groups ?? [], // Entra group GUIDs
    tid: payload.tid,
    roles: payload.roles ?? [],
    raw: payload,
  };
}

/**
 * Express middleware — validates JWT, attaches req.user. Returns 401 on failure.
 */
export async function authMiddleware(req, res, next) {
  try {
    req.user = await validateToken(req.headers['authorization']);
    next();
  } catch (err) {
    res.status(401).json({ error: 'Unauthorized', message: err.message });
  }
}
