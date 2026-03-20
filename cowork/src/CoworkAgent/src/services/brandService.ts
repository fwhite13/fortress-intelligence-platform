import { S3Client, GetObjectCommand, PutObjectCommand } from '@aws-sdk/client-s3';

export interface BrandContext {
  orgId:           string;
  primaryColor:    string;     // CSS hex, e.g. "#1a2332"
  secondaryColor:  string;
  accentColor:     string;
  backgroundColor: string;
  textColor:       string;
  fontFamily:      string;     // CSS font-family string
  logoUrl?:        string;     // S3 presigned URL for logo (optional)
  borderRadius:    { sm: string; md: string; lg: string };
  shadow:          { sm: string; md: string };
  customRules?:    string;     // freeform CSS rules to inject (e.g. custom components)
}

const BRAND_BUCKET  = process.env.DESIGN_S3_BUCKET ?? 'fip-cowork-workspaces';
const BRAND_PREFIX  = 'design/brand';
const s3            = new S3Client({ region: process.env.AWS_REGION ?? 'us-east-1' });

// In-process cache: orgId → {brand, loadedAt}
const cache = new Map<string, { brand: BrandContext; loadedAt: number }>();
const CACHE_TTL_MS = 5 * 60 * 1000; // 5 minutes

export async function getBrandContext(orgId: string): Promise<BrandContext> {
  const cached = cache.get(orgId);
  if (cached && Date.now() - cached.loadedAt < CACHE_TTL_MS) {
    return cached.brand;
  }

  try {
    const key = `${BRAND_PREFIX}/${orgId}/brand.json`;
    const resp = await s3.send(new GetObjectCommand({
      Bucket: BRAND_BUCKET,
      Key:    key,
    }));
    const raw   = await resp.Body!.transformToString();
    const brand = JSON.parse(raw) as BrandContext;
    cache.set(orgId, { brand, loadedAt: Date.now() });
    return brand;
  } catch {
    // Org has no brand file — return Fortress AM defaults
    return getFortressDefaults(orgId);
  }
}

export async function saveBrandContext(orgId: string, brand: BrandContext): Promise<void> {
  const key = `${BRAND_PREFIX}/${orgId}/brand.json`;
  await s3.send(new PutObjectCommand({
    Bucket:      BRAND_BUCKET,
    Key:         key,
    Body:        JSON.stringify(brand, null, 2),
    ContentType: 'application/json',
  }));
  cache.delete(orgId); // invalidate cache
}

export function formatBrandContextForPrompt(brand: BrandContext): string {
  return `
Primary color:    ${brand.primaryColor}
Secondary color:  ${brand.secondaryColor}
Accent color:     ${brand.accentColor}
Background:       ${brand.backgroundColor}
Text:             ${brand.textColor}
Font family:      ${brand.fontFamily}
Border radius SM: ${brand.borderRadius.sm}
Border radius MD: ${brand.borderRadius.md}
Border radius LG: ${brand.borderRadius.lg}
Shadow SM:        ${brand.shadow.sm}
Shadow MD:        ${brand.shadow.md}
${brand.customRules ? `Custom CSS rules:\n${brand.customRules}` : ''}
`.trim();
}

function getFortressDefaults(orgId: string): BrandContext {
  return {
    orgId,
    primaryColor:    '#1a2332',
    secondaryColor:  '#2c3e58',
    accentColor:     '#d4af37',
    backgroundColor: '#ffffff',
    textColor:       '#1a2332',
    fontFamily:      'Inter, system-ui, -apple-system, sans-serif',
    logoUrl:         undefined,
    borderRadius:    { sm: '4px', md: '8px', lg: '12px' },
    shadow:          {
      sm: '0 1px 3px rgba(0,0,0,0.08)',
      md: '0 4px 16px rgba(0,0,0,0.12)',
    },
  };
}
