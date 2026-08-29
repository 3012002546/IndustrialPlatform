/**
 * The only boundary for VXE's private DOM structure. VXE upgrades should only
 * require changes here, rather than in the management table's business logic.
 */
export function findVxeElement<T extends Element>(root: ParentNode, selector: string): T | null {
  return root.querySelector<T>(selector)
}

export function findVxeElements<T extends Element>(root: ParentNode, selector: string): T[] {
  return Array.from(root.querySelectorAll<T>(selector))
}

export function findVxeClosest<T extends Element>(element: Element, selector: string): T | null {
  return element.closest<T>(selector)
}
