/**
 * The only boundary for VXE's private DOM structure. VXE upgrades should only
 * require changes here, rather than in the management table's business logic.
 */
const VXE_SELECTOR = Object.freeze({
  headerTables: 'table.vxe-table--header',
  headerWrapper: '.vxe-table--header-wrapper',
  headerRow: 'thead > .vxe-header--row',
  hiddenFixedColumns: '.vxe-table--column.fixed--hidden',
  customPanel: '.vxe-table-custom-wrapper',
  activeCustomPanel: '.vxe-table-custom-wrapper.is--active',
  customHeader: '.vxe-table-custom--header',
  customSelectAllLabel: '.vxe-table-custom--panel-list .vxe-checkbox--label',
  customBody: '.vxe-table-custom--body',
  mainHeaderWrapperClass: 'body--wrapper',
})

const APP_TABLE_SELECTOR = Object.freeze({
  actionColumns: '.app-data-table__actions-column-header, .app-data-table__actions-column',
  filterRow: '.app-data-table__header-filter-row',
  dateRangeControl: '.app-data-table__date-range-control',
  customHeaderTools: '.app-data-table__native-header-tools',
})

const VXE_FOCUSABLE_SELECTOR =
  'a[href],button,input,select,textarea,[tabindex],[contenteditable="true"]'

function query<T extends Element>(root: ParentNode, selector: string): T | null {
  return root.querySelector<T>(selector)
}

function queryAll<T extends Element>(root: ParentNode, selector: string): T[] {
  return Array.from(root.querySelectorAll<T>(selector))
}

function closest<T extends Element>(element: Element, selector: string): T | null {
  return element.closest<T>(selector)
}

export function findAppDataTableActionColumns(root: ParentNode): HTMLElement[] {
  return queryAll<HTMLElement>(root, APP_TABLE_SELECTOR.actionColumns)
}

export function findAppDataTableSurface(root: HTMLElement): HTMLElement | null {
  return root.parentElement
}

export function findVxeHeaderTables(root: ParentNode): HTMLTableElement[] {
  return queryAll<HTMLTableElement>(root, VXE_SELECTOR.headerTables)
}

export function findVxeHeaderRow(headerTable: HTMLTableElement): HTMLTableRowElement | null {
  return query<HTMLTableRowElement>(headerTable, VXE_SELECTOR.headerRow)
}

export function findVxeHeaderWrapper(headerTable: HTMLTableElement): HTMLElement | null {
  return closest<HTMLElement>(headerTable, VXE_SELECTOR.headerWrapper)
}

export function isVxeMainHeader(headerTable: HTMLTableElement): boolean {
  return findVxeHeaderWrapper(headerTable)?.classList.contains(VXE_SELECTOR.mainHeaderWrapperClass) === true
}

export function findVxeHeaderFilterRows(headerTable: HTMLTableElement): HTMLElement[] {
  return queryAll<HTMLElement>(headerTable, APP_TABLE_SELECTOR.filterRow)
}

export function findVxeDateRangeControls(row: HTMLElement): HTMLElement[] {
  return queryAll<HTMLElement>(row, APP_TABLE_SELECTOR.dateRangeControl)
}

export function findVxeHiddenFixedColumns(root: ParentNode): HTMLElement[] {
  return queryAll<HTMLElement>(root, VXE_SELECTOR.hiddenFixedColumns)
}

export function markVxeDuplicateColumnsDecorative(root: ParentNode): void {
  findVxeHiddenFixedColumns(root).forEach(markVxeElementDecorative)
}

export function findVxeCustomPanel(root: ParentNode): HTMLElement | null {
  return query<HTMLElement>(root, VXE_SELECTOR.customPanel)
}

export function findVxeActiveCustomPanel(root: ParentNode): HTMLElement | null {
  return query<HTMLElement>(root, VXE_SELECTOR.activeCustomPanel)
}

export function isVxeCustomPanelActive(panel: HTMLElement): boolean {
  return panel.classList.contains('is--active')
}

export function markVxeCustomPanelPlatformClass(panel: HTMLElement): void {
  panel.classList.add('app-data-table__native-column-settings')
}

export function findVxeCustomHeader(panel: HTMLElement): HTMLElement | null {
  return query<HTMLElement>(panel, VXE_SELECTOR.customHeader)
}

export function findVxeCustomSelectAllLabel(header: HTMLElement): HTMLElement | null {
  return query<HTMLElement>(header, VXE_SELECTOR.customSelectAllLabel)
}

export function findAppDataTableCustomHeaderTools(header: HTMLElement): HTMLElement | null {
  return query<HTMLElement>(header, APP_TABLE_SELECTOR.customHeaderTools)
}

export function findAppDataTablePreferenceInput(
  tools: HTMLElement,
  testId: string,
): HTMLInputElement | null {
  return query<HTMLInputElement>(tools, `[data-testid="${testId}"]`)
}

export function findVxeCustomBody(panel: HTMLElement): HTMLElement | null {
  return query<HTMLElement>(panel, VXE_SELECTOR.customBody)
}

/**
 * Keep VXE's visual duplicate markup available for layout while removing it
 * from the accessible and keyboard interaction surfaces.
 */
export function markVxeElementDecorative(element: HTMLElement): void {
  element.setAttribute('aria-hidden', 'true')
  element.setAttribute('inert', '')
  if (element.matches(VXE_FOCUSABLE_SELECTOR)) element.setAttribute('tabindex', '-1')
  queryAll<HTMLElement>(element, VXE_FOCUSABLE_SELECTOR).forEach((focusable) => {
    focusable.setAttribute('tabindex', '-1')
  })
}
