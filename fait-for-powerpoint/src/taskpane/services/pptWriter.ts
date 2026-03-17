/* global PowerPoint */

declare const PowerPoint: any;

export class PptWriteError extends Error {
  constructor(
    message: string,
    public readonly code: 'SHAPE_NOT_FOUND' | 'NO_TEXT_FRAME' | 'PPT_ERROR'
  ) {
    super(message);
    this.name = 'PptWriteError';
  }
}

export async function applyTextToShape(shapeId: string, text: string): Promise<void> {
  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items/id');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) {
      throw new PptWriteError('No slide selected', 'SHAPE_NOT_FOUND');
    }

    const slide = selectedSlides.items[0];
    const shapes = slide.shapes;
    shapes.load('items/id');
    await ctx.sync();

    const target = (shapes.items as any[]).find((s: any) => s.id === shapeId);
    if (!target) {
      throw new PptWriteError(`Shape ${shapeId} not found on active slide`, 'SHAPE_NOT_FOUND');
    }

    target.load('textFrame/hasText');
    await ctx.sync();

    if (!target.textFrame.hasText) {
      throw new PptWriteError(`Shape ${shapeId} has no text frame`, 'NO_TEXT_FRAME');
    }

    target.textFrame.textRange.text = text;
    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof PptWriteError) throw e;
    throw new PptWriteError(
      e?.message ?? 'PowerPoint write failed',
      'PPT_ERROR'
    );
  });
}
