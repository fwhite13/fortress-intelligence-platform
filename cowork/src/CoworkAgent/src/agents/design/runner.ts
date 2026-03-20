import path from 'path';
import fs from 'fs/promises';
import crypto from 'crypto';
import { S3Client, PutObjectCommand, GetObjectCommand } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { getBrandContext, formatBrandContextForPrompt, type BrandContext } from '../../services/brandService.js';
import { runTask } from '../../agent/runner.js';
import type { SseChunk } from '../../agent/runner.js';

export interface DesignTaskParams {
  taskId:          string;
  userId:          string;
  userEmail:       string;
  orgId:           string;
  projectId:       string;
  screenId?:       string;      // set for edits; undefined for new screens
  priorHtml?:      string;      // for edit tasks
  prompt:          string;
  variantCount:    1 | 2 | 3;  // 1 = single generate, 3 = variants
  deviceTarget:    'mobile' | 'desktop' | 'responsive';
  convertToBlazor: boolean;
  referenceFiles?: string[];    // S3 keys of uploaded reference images
}

const S3_BUCKET  = process.env.DESIGN_S3_BUCKET ?? 'fip-cowork-workspaces';
const S3_PREFIX  = process.env.DESIGN_S3_PREFIX ?? 'design';
const s3         = new S3Client({ region: process.env.AWS_REGION ?? 'us-east-1' });

// Helper: iterate the AsyncGenerator runTask and forward chunks to emit callback
async function runTaskWithEmit(
  params: Parameters<typeof runTask>[0],
  emit: (chunk: SseChunk) => void
): Promise<void> {
  for await (const chunk of runTask(params)) {
    emit(chunk);
  }
}

export async function runDesignTask(
  params: DesignTaskParams,
  emit: (chunk: SseChunk) => void
): Promise<void> {

  const { taskId, orgId, projectId, prompt, variantCount,
          deviceTarget, convertToBlazor, priorHtml, screenId } = params;

  // ── 1. Load brand context ─────────────────────────────────────────────
  emit({ type: 'step', text: 'Loading brand context...' });
  const brand = await getBrandContext(orgId);
  const brandBlock = formatBrandContextForPrompt(brand);

  // ── 2. Build system prompt ────────────────────────────────────────────
  const rawSystemPrompt = await fs.readFile(
    path.join(process.cwd(), 'agents/design/system-prompt.md'), 'utf8');
  const systemPrompt = rawSystemPrompt.replace('{{BRAND_CONTEXT}}', brandBlock);

  // ── 3. Build user prompt ──────────────────────────────────────────────
  let userPrompt: string;

  if (priorHtml) {
    // Edit mode: inject prior HTML
    userPrompt = [
      'Here is the current design. Apply the requested change — preserve all elements',
      'not mentioned in the change request:\n',
      '```html',
      priorHtml,
      '```\n',
      `Requested change: ${prompt}`,
      `Device target: ${deviceTarget}`,
    ].join('\n');
  } else if (variantCount > 1) {
    // Variant mode: generate 3 parallel designs
    await runVariantTask(params, systemPrompt, brand, emit);
    return;
  } else {
    // New screen
    userPrompt = [
      `Generate a UI screen: ${prompt}`,
      `Device target: ${deviceTarget}`,
      'Save the complete HTML to screen.html in the working directory.',
    ].join('\n');
  }

  // ── 4. Run Claude agent ───────────────────────────────────────────────
  emit({ type: 'step', text: 'Generating design...' });

  const workingDir = `/tmp/cowork-${taskId}`;
  await fs.mkdir(workingDir, { recursive: true });

  await runTaskWithEmit(
    { taskId, userId: params.userId, userEmail: params.userEmail,
      prompt: userPrompt, workingDir, maxBudgetUsd: 0.50, maxTurns: 8,
      systemPromptOverride: systemPrompt },
    emit
  );

  // ── 5. Find generated HTML ────────────────────────────────────────────
  const generated = await findGeneratedHtml(workingDir);
  if (!generated) {
    emit({ type: 'error', text: 'No HTML file was generated. Try a more specific prompt.' });
    return;
  }

  // ── 6. Version and upload to S3 ───────────────────────────────────────
  const sid      = screenId ?? crypto.randomUUID();
  const version  = await getNextVersion(orgId, projectId, sid);
  const s3Key    = `${S3_PREFIX}/projects/${orgId}/${projectId}/screens/${sid}_v${version}.html`;

  const htmlContent = await fs.readFile(generated, 'utf8');
  await s3.send(new PutObjectCommand({
    Bucket:      S3_BUCKET,
    Key:         s3Key,
    Body:        htmlContent,
    ContentType: 'text/html',
    Metadata: {
      'org-id':     orgId,
      'project-id': projectId,
      'screen-id':  sid,
      'version':    String(version),
      'task-id':    taskId,
    },
  }));

  // ── 7. Generate presigned download URL ───────────────────────────────
  const downloadUrl = await getSignedUrl(
    s3,
    new GetObjectCommand({ Bucket: S3_BUCKET, Key: s3Key }),
    { expiresIn: 3600 }
  );

  // ── 8. Register screen version in Redis ──────────────────────────────
  const { getRedis } = await import('../../services/taskStore.js');
  const redis = await getRedis();
  const versionKey = `design:screen:${orgId}:${projectId}:${sid}:versions`;
  await redis.rPush(versionKey, JSON.stringify({
    version, s3Key, taskId,
    createdAt: new Date().toISOString(),
    prompt,
  }));
  await redis.expire(versionKey, 60 * 60 * 24 * 30); // 30-day TTL

  // ── 9. Emit file_output with HTML source ─────────────────────────────
  emit({
    type:        'file_output',
    outputType:  'html',
    fileName:    `screen_v${version}.html`,
    downloadUrl,
    sizeBytes:   Buffer.byteLength(htmlContent, 'utf8'),
  });

  // Also emit the HTML source inline for "Copy Code" clipboard
  emit({
    type: 'result',
    text: JSON.stringify({
      screenId:    sid,
      version,
      projectId,
      htmlSource:  htmlContent,
      downloadUrl,
      s3Key,
    }),
  });

  // ── 10. Optional: Blazor conversion pass ─────────────────────────────
  if (convertToBlazor) {
    emit({ type: 'step', text: 'Converting to Blazor component...' });
    await runBlazorConversion(
      taskId, htmlContent, orgId, projectId, sid, version, emit);
  }
}

// ── Variant generation ────────────────────────────────────────────────────

async function runVariantTask(
  params: DesignTaskParams,
  systemPrompt: string,
  _brand: BrandContext,
  emit: (chunk: SseChunk) => void
): Promise<void> {
  const { taskId, orgId, projectId, prompt, deviceTarget } = params;

  const variantInstructions = [
    { suffix: 'varA', style: 'REFINED — clean, minimal, maximum whitespace. Typography-led. Restrained color.' },
    { suffix: 'varB', style: 'CONTEMPORARY — card-based, soft shadows, brand accent highlights. Modern SaaS.' },
    { suffix: 'varC', style: 'BOLD — strong hero, prominent brand color, high visual impact.' },
  ];

  emit({ type: 'step', text: 'Generating 3 design variants in parallel...' });

  const screenId = crypto.randomUUID();

  // Stagger parallel Bedrock calls by 500ms to reduce throttling risk
  const results = await Promise.allSettled(
    variantInstructions.map(async (variant, i) => {
      if (i > 0) await new Promise(r => setTimeout(r, i * 500)); // 0ms, 500ms, 1000ms
      const varWorkingDir = `/tmp/cowork-${taskId}-${variant.suffix}`;
      await fs.mkdir(varWorkingDir, { recursive: true });

      const varPrompt = [
        `Generate a UI screen — variant ${i + 1} of 3.`,
        `Design direction: ${variant.style}`,
        `Screen description: ${prompt}`,
        `Device target: ${deviceTarget}`,
        `Save as screen.html in the working directory.`,
      ].join('\n');

      const chunks: SseChunk[] = [];
      await runTaskWithEmit(
        { taskId: `${taskId}-${variant.suffix}`, userId: params.userId,
          userEmail: params.userEmail, prompt: varPrompt,
          workingDir: varWorkingDir, maxBudgetUsd: 0.35, maxTurns: 6,
          systemPromptOverride: systemPrompt },
        (chunk) => chunks.push(chunk)
      );

      const generated = await findGeneratedHtml(varWorkingDir);
      if (!generated) return null;

      const htmlContent = await fs.readFile(generated, 'utf8');
      const s3Key = `${S3_PREFIX}/projects/${orgId}/${projectId}/screens/${screenId}_${variant.suffix}.html`;

      await s3.send(new PutObjectCommand({
        Bucket: S3_BUCKET, Key: s3Key, Body: htmlContent, ContentType: 'text/html',
        Metadata: { 'org-id': orgId, 'project-id': projectId, 'screen-id': screenId,
                    'variant': variant.suffix, 'task-id': taskId },
      }));

      const downloadUrl = await getSignedUrl(
        s3, new GetObjectCommand({ Bucket: S3_BUCKET, Key: s3Key }), { expiresIn: 3600 });

      return { suffix: variant.suffix, downloadUrl, htmlContent, s3Key };
    })
  );

  const variants = results
    .filter(r => r.status === 'fulfilled' && r.value !== null)
    .map(r => (r as PromiseFulfilledResult<NonNullable<{ suffix: string; downloadUrl: string; htmlContent: string; s3Key: string }>>).value);

  // Emit all variants as file_output chunks
  for (const v of variants) {
    emit({
      type: 'file_output', outputType: 'html',
      fileName: `screen_${v.suffix}.html`,
      downloadUrl: v.downloadUrl,
      sizeBytes: Buffer.byteLength(v.htmlContent, 'utf8'),
    });
  }

  // Emit consolidated result for workspace to parse variant list
  emit({
    type: 'result',
    text: JSON.stringify({
      screenId,
      projectId,
      isVariants: true,
      variants: variants.map(v => ({
        label:       v.suffix === 'varA' ? 'Refined' : v.suffix === 'varB' ? 'Contemporary' : 'Bold',
        suffix:      v.suffix,
        downloadUrl: v.downloadUrl,
        s3Key:       v.s3Key,
      })),
    }),
  });

  emit({ type: 'step', text: `${variants.length} variants generated.` });
}

// ── Blazor conversion ─────────────────────────────────────────────────────

async function runBlazorConversion(
  taskId: string, htmlContent: string, orgId: string,
  projectId: string, screenId: string, version: number,
  emit: (chunk: SseChunk) => void
): Promise<void> {
  const conversionPrompt = [
    'Convert the following HTML/CSS design to a Blazor Razor component.',
    'Follow these rules:',
    '1. Use MudBlazor components where appropriate (MudButton, MudTextField, MudSelect, etc.)',
    '2. Keep custom CSS for layout, spacing, and brand-specific styling',
    '3. Extract hardcoded text as [Parameter] properties with defaults',
    '4. Output the .razor file as component.razor in the working directory',
    '5. Do not add any functionality beyond what is visible in the HTML',
    '\nHTML to convert:\n```html\n',
    htmlContent,
    '\n```',
  ].join('\n');

  const convWorkingDir = `/tmp/cowork-${taskId}-blazor`;
  await fs.mkdir(convWorkingDir, { recursive: true });

  await runTaskWithEmit(
    { taskId: `${taskId}-blazor`, userId: 'system', userEmail: 'system',
      prompt: conversionPrompt, workingDir: convWorkingDir,
      maxBudgetUsd: 0.25, maxTurns: 4 },
    // No systemPromptOverride — Blazor conversion uses generic runner default
    (chunk) => {
      if (chunk.type !== 'step' && chunk.type !== 'tool_call') return; // suppress verbose
    }
  );

  const razorFile = path.join(convWorkingDir, 'component.razor');
  try {
    const razorContent = await fs.readFile(razorFile, 'utf8');
    const s3Key = `${S3_PREFIX}/projects/${orgId}/${projectId}/screens/${screenId}_v${version}.razor`;

    await s3.send(new PutObjectCommand({
      Bucket: S3_BUCKET, Key: s3Key, Body: razorContent, ContentType: 'text/plain',
    }));

    const downloadUrl = await getSignedUrl(
      s3, new GetObjectCommand({ Bucket: S3_BUCKET, Key: s3Key }), { expiresIn: 3600 });

    emit({
      type: 'file_output', outputType: 'other',
      fileName: `component_v${version}.razor`,
      downloadUrl,
      sizeBytes: Buffer.byteLength(razorContent, 'utf8'),
    });

    emit({ type: 'step', text: 'Blazor component ready for download.' });
  } catch {
    emit({ type: 'step', text: 'Blazor conversion complete — check output files.' });
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────

async function findGeneratedHtml(dir: string): Promise<string | null> {
  try {
    const files = await fs.readdir(dir);
    const html  = files.find(f => f.endsWith('.html'));
    return html ? path.join(dir, html) : null;
  } catch { return null; }
}

async function getNextVersion(
  orgId: string, projectId: string, screenId: string
): Promise<number> {
  const { getRedis } = await import('../../services/taskStore.js');
  const redis  = await getRedis();
  const key    = `design:screen:${orgId}:${projectId}:${screenId}:versions`;
  const len    = await redis.lLen(key);
  return len + 1;
}
