// While dragging a task on touch, auto-scroll the page when the finger nears the top or
// bottom edge of the viewport. Returns pixels to scroll per animation frame: negative = up,
// positive = down, 0 = finger is in the safe middle band. Speed ramps from `minSpeed` at the
// edge of the trigger zone to `maxSpeed` at the very screen edge, so it starts gently.
export function edgeScrollVelocity(
  clientY: number,
  viewportHeight: number,
  edgeZone = 100,
  minSpeed = 2,
  maxSpeed = 8,
): number {
  const ramp = (depth: number) => minSpeed + (maxSpeed - minSpeed) * Math.min(1, Math.max(0, depth))

  if (clientY < edgeZone) {
    return -ramp((edgeZone - clientY) / edgeZone)
  }
  const bottomStart = viewportHeight - edgeZone
  if (clientY > bottomStart) {
    return ramp((clientY - bottomStart) / edgeZone)
  }
  return 0
}
