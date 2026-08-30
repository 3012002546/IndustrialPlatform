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

const VXE_FOCUSABLE_SELECTOR =
  'a[href],button,input,select,textarea,[tabindex],[contenteditable="true"]'

/**
 * Keep VXE's visual duplicate markup available for layout while removing it
 * from the accessible and keyboard interaction surfaces.
 */
export function markVxeElementDecorative(element: HTMLElement): void {
  element.setAttribute('aria-hidden', 'true')
  element.setAttribute('inert', '')
  if (element.matches(VXE_FOCUSABLE_SELECTOR)) element.setAttribute('tabindex', '-1')
  findVxeElements<HTMLElement>(element, VXE_FOCUSABLE_SELECTOR).forEach((focusable) => {
    focusable.setAttribute('tabindex', '-1')
  })
}
