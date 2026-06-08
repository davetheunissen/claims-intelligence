"use client";

import { useEffect, useState } from "react";
import { useClaimStore } from "../store/claimStore";

interface AsyncState<T> {
  loading: boolean;
  error: string | null;
  data: T | null;
}

export function useSectionData<T>(
  step: number,
  fetcher: (claimId: string) => Promise<T>,
  enabled: boolean,
  isPartial?: (payload: T) => boolean,
): AsyncState<T> {
  const claimId = useClaimStore((s) => s.claimId);
  const cached = useClaimStore((s) => s.data[step]) as T | undefined;
  const setData = useClaimStore((s) => s.setData);
  const [state, setState] = useState<AsyncState<T>>({
    loading: false,
    error: null,
    data: cached ?? null,
  });

  useEffect(() => {
    if (!enabled || !claimId) return;
    if (cached && !(isPartial && isPartial(cached))) return;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | null = null;
    setState({ loading: !cached, error: null, data: cached ?? null });

    const isProcessing = (p: unknown): boolean =>
      !!p &&
      typeof p === "object" &&
      (p as { status?: string }).status === "processing";

    const attempt = (delay: number) => {
      if (cancelled) return;
      fetcher(claimId)
        .then((payload) => {
          if (cancelled) return;
          const partial = isPartial && isPartial(payload);
          if (isProcessing(payload) || partial) {
            if (!isProcessing(payload)) {
              setData(step, payload);
              setState({ loading: false, error: null, data: payload });
            }
            const next = Math.min(delay * 1.5, 8000);
            timer = setTimeout(() => attempt(next), next);
            return;
          }
          setData(step, payload);
          setState({ loading: false, error: null, data: payload });
        })
        .catch((err: unknown) => {
          if (cancelled) return;
          const message = err instanceof Error ? err.message : "Request failed";
          setState({ loading: false, error: message, data: null });
        });
    };

    attempt(2000);

    return () => {
      cancelled = true;
      if (timer) clearTimeout(timer);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, claimId, step]);

  useEffect(() => {
    if (cached && !state.data) {
      setState({ loading: false, error: null, data: cached });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cached]);

  return state;
}
