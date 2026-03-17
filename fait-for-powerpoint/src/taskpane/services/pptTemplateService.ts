/* global PowerPoint */
declare const PowerPoint: any;

import { fetchTemplateBase64 } from './faitApi';

export class PptTemplateError extends Error {
  constructor(
    message: string,
    public readonly code: 'NO_SLIDE' | 'FETCH_FAILED' | 'PPT_ERROR'
  ) {
    super(message);
    this.name = 'PptTemplateError';
  }
}

export async function insertTemplateSlide(
  templateId: string,
  apiKey: string,
  keepSourceFormatting = false
): Promise<void> {
  let base64Pptx: string;
  try {
    base64Pptx = await fetchTemplateBase64(templateId, apiKey);
  } catch (e: any) {
    throw new PptTemplateError(
      e?.message ?? 'Failed to fetch template from FORGE',
      'FETCH_FAILED'
    );
  }

  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    const targetSlide = selectedSlides.items?.length > 0
      ? selectedSlides.items[0]
      : null;

    ctx.presentation.insertSlidesFromBase64(base64Pptx, {
      formatting: keepSourceFormatting
        ? PowerPoint.InsertSlideFormatting.keepSourceFormatting
        : PowerPoint.InsertSlideFormatting.useDestinationTheme,
      targetSlide,
    });

    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof PptTemplateError) throw e;
    throw new PptTemplateError(e?.message ?? 'Template insert failed', 'PPT_ERROR');
  });
}
