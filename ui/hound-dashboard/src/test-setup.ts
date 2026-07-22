// Polyfills for browser APIs that jsdom doesn't ship but our component libs
// (spartan-ng's HlmToaster, lightweight-charts) call eagerly during creation.
// Without these, tests still pass but emit ~100 "unhandled" errors that mask
// real failures.

if (typeof window !== 'undefined') {
  if (typeof window.matchMedia !== 'function') {
    window.matchMedia = (query: string): MediaQueryList => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => undefined,
      removeListener: () => undefined,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      dispatchEvent: () => false,
    }) as MediaQueryList;
  }

  if (typeof window.ResizeObserver === 'undefined') {
    class StubResizeObserver implements ResizeObserver {
      observe(): void { /* no-op */ }
      unobserve(): void { /* no-op */ }
      disconnect(): void { /* no-op */ }
    }
    (window as unknown as { ResizeObserver: typeof ResizeObserver }).ResizeObserver = StubResizeObserver as unknown as typeof ResizeObserver;
  }
}
