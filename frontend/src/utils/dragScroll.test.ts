import { describe, it, expect } from 'vitest'
import { edgeScrollVelocity } from './dragScroll'

describe('edgeScrollVelocity', () => {
  const VH = 1000
  const zone = 100
  const min = 2
  const max = 8

  it('is zero in the middle of the viewport', () => {
    expect(edgeScrollVelocity(500, VH, zone, min, max)).toBe(0)
    expect(edgeScrollVelocity(300, VH, zone, min, max)).toBe(0)
    expect(edgeScrollVelocity(700, VH, zone, min, max)).toBe(0)
  })

  it('scrolls up (negative) near the top edge, faster closer to the edge', () => {
    expect(edgeScrollVelocity(0, VH, zone, min, max)).toBe(-max)      // at the very top: max speed
    expect(edgeScrollVelocity(50, VH, zone, min, max)).toBe(-5)       // halfway into the zone
    expect(edgeScrollVelocity(99, VH, zone, min, max)).toBeLessThan(0) // just inside the zone
    expect(edgeScrollVelocity(99, VH, zone, min, max)).toBeGreaterThanOrEqual(-min - 0.5)
  })

  it('scrolls down (positive) near the bottom edge, faster closer to the edge', () => {
    expect(edgeScrollVelocity(1000, VH, zone, min, max)).toBe(max)    // at the very bottom
    expect(edgeScrollVelocity(950, VH, zone, min, max)).toBe(5)       // halfway into the bottom zone
    expect(edgeScrollVelocity(901, VH, zone, min, max)).toBeGreaterThan(0)
  })

  it('is zero exactly at the zone boundaries', () => {
    expect(edgeScrollVelocity(100, VH, zone, min, max)).toBe(0)       // top boundary (not inside)
    expect(edgeScrollVelocity(900, VH, zone, min, max)).toBe(0)       // bottom boundary
  })

  it('clamps to max speed if the finger goes past the edge', () => {
    expect(edgeScrollVelocity(-20, VH, zone, min, max)).toBe(-max)
    expect(edgeScrollVelocity(1020, VH, zone, min, max)).toBe(max)
  })
})
