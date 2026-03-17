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

export async function applyTextToShape(shapeId: string, text: string, nodeId?: string): Promise<void> {
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

    // Source tagging — inside same PowerPoint.run() as the write
    if (nodeId) {
      try {
        target.tags.add('FAIT_SOURCE', nodeId);
        await ctx.sync();
      } catch {
        // Tagging failure is non-fatal — write already succeeded
      }
    }
  }).catch((e: any) => {
    if (e instanceof PptWriteError) throw e;
    throw new PptWriteError(
      e?.message ?? 'PowerPoint write failed',
      'PPT_ERROR'
    );
  });
}

export class PptNotesError extends Error {
  constructor(
    message: string,
    public readonly code: 'NO_SLIDE' | 'NOTES_UNAVAILABLE' | 'PPT_ERROR'
  ) {
    super(message);
    this.name = 'PptNotesError';
  }
}

export async function writeNotes(notesText: string): Promise<void> {
  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) {
      throw new PptNotesError('No slide selected', 'NO_SLIDE');
    }

    const slide = selectedSlides.items[0];
    slide.load('notes');
    await ctx.sync();

    if (!slide.notes) {
      throw new PptNotesError('Notes API unavailable on this slide', 'NOTES_UNAVAILABLE');
    }

    slide.notes.textFrame.textRange.text = notesText;
    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof PptNotesError) throw e;
    throw new PptNotesError(e?.message ?? 'Notes write failed', 'PPT_ERROR');
  });
}

export async function tagShape(
  shapeId: string,
  tagKey: string,
  tagValue: string
): Promise<void> {
  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items/id');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) return;

    const slide = selectedSlides.items[0];
    const shapes = slide.shapes;
    shapes.load('items/id');
    await ctx.sync();

    const target = (shapes.items as any[]).find((s: any) => s.id === shapeId);
    if (!target) return;

    target.tags.add(tagKey, tagValue);
    await ctx.sync();
  }).catch(() => {
    // tagShape failure is always non-fatal
  });
}
