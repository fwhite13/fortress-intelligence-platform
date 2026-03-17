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
                    'items/shapes/items/type']);
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
