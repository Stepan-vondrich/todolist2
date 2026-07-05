// Polyfills for older browsers. Imported first in main.tsx so they run before any
// library code (notably pdf.js) that relies on these newer APIs.

// Promise.withResolvers() — used by pdf.js 4.3+. Only native in Safari 17.4+, so on
// older iOS Safari it's undefined and pdf.js throws "undefined is not a function",
// which surfaced as "PDF se nepodařilo načíst". This shim restores it everywhere.
if (typeof (Promise as unknown as { withResolvers?: unknown }).withResolvers !== 'function') {
  ;(Promise as unknown as { withResolvers: <T>() => {
    promise: Promise<T>; resolve: (value: T | PromiseLike<T>) => void; reject: (reason?: unknown) => void
  } }).withResolvers = function withResolvers<T>() {
    let resolve!: (value: T | PromiseLike<T>) => void
    let reject!: (reason?: unknown) => void
    const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
    return { promise, resolve, reject }
  }
}
