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

// ReadableStream async iteration — `for await (const chunk of stream)`. pdf.js 5.x uses
// it to read the fetched PDF; Safari (incl. current iOS 18/26) still ships no
// ReadableStream.prototype[Symbol.asyncIterator], so it threw "undefined is not a
// function" and the PDF never loaded. This is the standard spec-shaped shim.
{
  const proto = (typeof ReadableStream !== 'undefined' ? ReadableStream.prototype : undefined) as
    | (ReadableStream & Record<PropertyKey, unknown>)
    | undefined
  if (proto && typeof proto[Symbol.asyncIterator] !== 'function') {
    function values(this: ReadableStream<unknown>, { preventCancel = false }: { preventCancel?: boolean } = {}) {
      const reader = this.getReader()
      return {
        next() {
          return reader.read().then(
            result => { if (result.done) reader.releaseLock(); return result },
            reason => { reader.releaseLock(); throw reason },
          )
        },
        return(value?: unknown) {
          if (!preventCancel) {
            const cancelPromise = reader.cancel(value)
            reader.releaseLock()
            return cancelPromise.then(() => ({ done: true, value }))
          }
          reader.releaseLock()
          return Promise.resolve({ done: true, value })
        },
        [Symbol.asyncIterator]() { return this },
      }
    }
    proto.values = values
    proto[Symbol.asyncIterator] = values
  }
}
