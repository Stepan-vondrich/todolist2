import '@testing-library/jest-dom';

// happy-dom does not provide Worker, but heic2any references it at import time.
// Stub it so any module that imports heic2any (e.g. CommentsPanel) can load in tests.
if (typeof globalThis.Worker === 'undefined') {
  class WorkerStub {
    onmessage: ((e: unknown) => void) | null = null;
    onerror: ((e: unknown) => void) | null = null;
    postMessage() {}
    addEventListener() {}
    removeEventListener() {}
    terminate() {}
  }
  globalThis.Worker = WorkerStub as unknown as typeof Worker;
}
