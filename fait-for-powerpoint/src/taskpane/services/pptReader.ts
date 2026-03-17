/* global PowerPoint */

declare const PowerPoint: any;

export interface ShapeContext {
  id: string;
  name: string;
  text: string;
  isSelected: boolean;
  hasText: boolean;
}

export interface SlideContext {
  slideIndex: number;
  slideNumber: number;
  title: string;
  shapes: ShapeContext[];
  notes: string;
  selectedShapeId: string | null;
  selectedShapeText: string;
}

export async function getSlideContext(): Promise<SlideContext> {
  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    const slides = selectedSlides.items;
    if (!slides || slides.length === 0) {
      return emptySlideContext();
    }

    const slide = slides[0];
    slide.load('id');

    const allSlides = ctx.presentation.slides;
    allSlides.load(['items/id', 'items/shapes/items/id',
                    'items/shapes/items/name',
                    'items/shapes/items/textFrame/textRange/text',
                    'items/shapes/items/type',
                    'items/notes/textFrame/textRange/text']);
    await ctx.sync();

    const slideItems = allSlides.items as any[];
    const slideIndex = slideItems.findIndex((s: any) => s.id === slide.id);
    const slideData = slideIndex >= 0 ? slideItems[slideIndex] : null;

    if (!slideData) {
      return emptySlideContext();
    }

    const selectedShapes = ctx.presentation.getSelectedShapes();
    selectedShapes.load('items/id');
    await ctx.sync();

    const selectedShapeIds = new Set(
      (selectedShapes.items as any[]).map((s: any) => s.id as string)
    );

    const shapeContexts: ShapeContext[] = [];
    let titleText = '';
    let selectedShapeId: string | null = null;
    let selectedShapeText = '';

    for (const shape of (slideData.shapes?.items ?? []) as any[]) {
      const text: string = shape.textFrame?.textRange?.text ?? '';
      const hasText = text.trim().length > 0;
      const isSelected = selectedShapeIds.has(shape.id);

      if (isSelected) {
        selectedShapeId = shape.id;
        selectedShapeText = text;
      }

      const shapeName: string = (shape.name ?? '').toLowerCase();
      if (!titleText && (shapeName.includes('title') || shape.type === 'title')) {
        titleText = text;
      }

      if (hasText) {
        shapeContexts.push({
          id: shape.id,
          name: shape.name ?? '',
          text,
          isSelected,
          hasText: true,
        });
      }
    }

    if (!titleText && shapeContexts.length > 0) {
      titleText = shapeContexts[0].text;
    }

    let notesText = '';
    try {
      const notes = slideData.notes;
      if (notes?.textFrame?.textRange?.text) {
        notesText = notes.textFrame.textRange.text;
      }
    } catch {
      // Notes API not available on this version — silently omit
    }

    return {
      slideIndex,
      slideNumber: slideIndex + 1,
      title: titleText,
      shapes: shapeContexts,
      notes: notesText,
      selectedShapeId,
      selectedShapeText,
    };
  }).catch((): SlideContext => emptySlideContext());
}

function emptySlideContext(): SlideContext {
  return {
    slideIndex: 0,
    slideNumber: 1,
    title: '',
    shapes: [],
    notes: '',
    selectedShapeId: null,
    selectedShapeText: '',
  };
}

export function formatSlideContext(ctx: SlideContext): string {
  let out = `[PRESENTATION CONTEXT]\n`;
  out += `Slide: ${ctx.slideNumber}`;
  if (ctx.title) out += ` — ${ctx.title}`;
  out += `\n`;

  if (ctx.selectedShapeId && ctx.selectedShapeText) {
    out += `Selected shape text:\n${ctx.selectedShapeText.slice(0, 800)}\n`;
  }

  if (ctx.shapes.length > 0) {
    const otherShapes = ctx.shapes.filter(
      (s) => !s.isSelected && s.text.trim()
    );
    if (otherShapes.length > 0) {
      out += `Other shapes on this slide:\n`;
      for (const s of otherShapes.slice(0, 5)) {
        out += `  • ${s.name}: ${s.text.slice(0, 200).replace(/\n/g, ' ')}\n`;
      }
    }
  }

  if (ctx.notes) {
    out += `Speaker notes:\n${ctx.notes.slice(0, 500)}\n`;
  }

  out += `[END PRESENTATION CONTEXT]`;
  return out;
}

export interface SlideSnapshot {
  slideNumber: number;   // 1-based
  title: string;
  shapes: Array<{
    name: string;
    text: string;        // truncated to 150 chars
  }>;
}

const MAX_SLIDES = 20;
const MAX_SHAPES = 3;
const SHAPE_TEXT_CAP = 150;

export async function getAllSlidesContext(): Promise<SlideSnapshot[]> {
  return PowerPoint.run(async (ctx: any) => {
    const allSlides = ctx.presentation.slides;
    allSlides.load([
      'items/shapes/items/id',
      'items/shapes/items/name',
      'items/shapes/items/type',
      'items/shapes/items/textFrame/textRange/text',
    ]);
    await ctx.sync();

    const snapshots: SlideSnapshot[] = [];
    const slideItems = allSlides.items as any[];

    for (let i = 0; i < Math.min(slideItems.length, MAX_SLIDES); i++) {
      const slide = slideItems[i];
      const shapeItems = (slide.shapes?.items ?? []) as any[];

      let title = '';
      const shapes: SlideSnapshot['shapes'] = [];

      for (const shape of shapeItems) {
        const text: string = shape.textFrame?.textRange?.text ?? '';
        if (!text.trim()) continue;

        const shapeName: string = (shape.name ?? '').toLowerCase();
        if (!title && (shapeName.includes('title') || shape.type === 'title')) {
          title = text;
        }

        shapes.push({
          name: shape.name ?? '',
          text: text.length > SHAPE_TEXT_CAP ? text.slice(0, SHAPE_TEXT_CAP) + '…' : text,
        });

        if (shapes.length >= MAX_SHAPES) break;
      }

      if (!title && shapes.length > 0) title = shapes[0].text;

      if (shapes.length > 0) {
        snapshots.push({ slideNumber: i + 1, title, shapes });
      }
    }

    return snapshots;
  }).catch((): SlideSnapshot[] => []);
}

export function formatDeckContext(snapshots: SlideSnapshot[]): string {
  if (snapshots.length === 0) return '';

  let out = `[DECK CONTEXT — ${snapshots.length} slide(s)]\n`;
  for (const s of snapshots) {
    out += `Slide ${s.slideNumber}`;
    if (s.title) out += ` — ${s.title.slice(0, 60)}`;
    out += `\n`;
    for (const shape of s.shapes) {
      out += `  • ${shape.text.replace(/\n/g, ' ')}\n`;
    }
  }
  out += `[END DECK CONTEXT]`;
  return out;
}

export async function getSlideNotes(): Promise<string> {
  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) return '';

    const slide = selectedSlides.items[0];
    slide.load('notes');
    await ctx.sync();

    if (!slide.notes) return '';

    const notesRange = slide.notes.textFrame.textRange;
    notesRange.load('text');
    await ctx.sync();

    return notesRange.text ?? '';
  }).catch((): string => '');
}
