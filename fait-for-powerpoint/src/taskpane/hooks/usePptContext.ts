import { useState, useEffect, useRef } from 'react';
import { getSlideContext } from '../services/pptReader';
import type { SlideContext } from '../services/pptReader';

export interface UsePptContextReturn {
  slideContext: SlideContext | null;
  refreshing: boolean;
  error: string | null;
  refresh: () => Promise<void>;
}

export function usePptContext(): UsePptContextReturn {
  const [slideContext, setSlideContext] = useState<SlideContext | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const refresh = async () => {
    setRefreshing(true);
    try {
      const ctx = await getSlideContext();
      setSlideContext(ctx);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to read slide context');
    } finally {
      setRefreshing(false);
    }
  };

  useEffect(() => {
    refresh();

    intervalRef.current = setInterval(() => {
      refresh();
    }, 2000);

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
      }
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return { slideContext, refreshing, error, refresh };
}
