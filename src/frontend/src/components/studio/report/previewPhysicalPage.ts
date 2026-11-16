const policy =
  "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:; base-uri 'none'; form-action 'none'\">"

export function isolatedPage(html: string, no: number, total: number) {
  return (
    '<!doctype html>' +
    policy +
    html +
    `<style>html,body{margin:0}body{counter-reset:page ${Math.max(0, Math.trunc(no) - 1)} pages ${Math.trunc(total)}}.pagedjs_page{margin:0!important;box-shadow:none!important}</style>`
  )
}

// Parse in an inert template. Only the sandboxed frame receives the report markup.
export function inspectPage(html: string, term: string) {
  const template = document.createElement('template')
  template.innerHTML = html
  const page = template.content.querySelector<HTMLElement>('.pagedjs_page')
  const mm = (property: string) => {
    const value = page?.style.getPropertyValue(property).trim()
    return value && /^\d+(?:\.\d+)?mm$/.test(value) && parseFloat(value) > 0 ? value : undefined
  }
  const width = mm('--pagedjs-width')
  const height = mm('--pagedjs-height')
  const anchors: string[] = []
  if (!term.trim()) return { width, height, anchors, html }
  // Inline formatting may split one word. Never join different rows, cells or paragraphs.
  let nodes: Text[] = []
  const prefix = 'report-find-' + crypto.getRandomValues(new Uint32Array(2)).join('-')
  const pattern = new RegExp(term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'giu')
  function searchRun() {
    const source = nodes.map((node) => node.data).join('')
    const matches = Array.from(source.matchAll(pattern), (match) => {
      const id = prefix + '-' + anchors.length
      anchors.push(id)
      return { start: match.index!, end: match.index! + match[0].length, id }
    })
    if (!matches.length) {
      nodes = []
      return
    }
    let offset = 0
    let index = 0
    for (const node of nodes) {
      const end = offset + node.length
      let cursor = offset
      const fragment = document.createDocumentFragment()
      while (index < matches.length && matches[index]!.start < end) {
        const match = matches[index]!
        const start = Math.max(offset, match.start)
        fragment.append(node.data.slice(cursor - offset, start - offset))
        const mark = document.createElement('mark')
        if (match.start === start) mark.id = match.id
        mark.textContent = node.data.slice(start - offset, Math.min(end, match.end) - offset)
        fragment.append(mark)
        cursor = Math.min(end, match.end)
        if (match.end > end) break
        index++
      }
      if (fragment.childNodes.length) {
        fragment.append(node.data.slice(cursor - offset))
        node.replaceWith(fragment)
      }
      offset = end
    }
    nodes = []
  }
  function visit(node: Node) {
    if (node.nodeType === Node.TEXT_NODE) {
      nodes.push(node as Text)
      return
    }
    if (
      node instanceof Element &&
      node.matches('style,script,template,[hidden],[aria-hidden="true"]')
    )
      return
    const boundary =
      node instanceof Element && !node.matches('span,b,strong,em,i,u,s,sub,sup,small,code,label')
    if (boundary && nodes.length) searchRun()
    for (const child of Array.from(node.childNodes)) visit(child)
    if (boundary && nodes.length) searchRun()
  }
  visit(page ?? template.content)
  if (nodes.length) searchRun()
  return {
    width,
    height,
    anchors,
    html:
      template.innerHTML +
      '<style>mark{background:#fff19a;color:#151515}mark:target{background:#ffba54;outline:2px solid #754300}</style>',
  }
}

// The outer window owns print permission. Each report page stays scriptless and isolated.
export async function printPhysicalPages(
  popup: Window,
  total: number,
  items: readonly { html?: string; pageNo?: number }[],
  read: (item: { html?: string; pageNo?: number }) => Promise<string>,
  title: string,
  active: () => boolean,
  invalidPaper: string,
) {
  const doc = popup.document
  popup.opener = null
  doc.title = title
  const style = doc.createElement('style')
  doc.head.append(style)
  style.sheet!.insertRule('html,body{margin:0}')
  const formats = new Map<string, string>()
  for (const item of items) {
    const html = await read(item)
    if (!active() || popup.closed) return
    const { width, height } = inspectPage(html, '')
    if (!width || !height) throw new Error(invalidPaper)
    const size = width + ' ' + height
    let name = formats.get(size)
    if (!name) {
      name = 'paper' + formats.size
      formats.set(size, name)
      style.sheet!.insertRule(`@page ${name}{size:${size};margin:0}`)
    }
    const sheet = doc.createElement('section')
    sheet.style.cssText = `page:${name};break-after:page;width:${width};height:${height};overflow:hidden`
    const frame = doc.createElement('iframe')
    frame.style.cssText = 'display:block;border:0;width:100%;height:100%'
    frame.title = title + ' ' + item.pageNo
    frame.setAttribute('sandbox', '')
    frame.referrerPolicy = 'no-referrer'
    frame.srcdoc = isolatedPage(html, Number(item.pageNo), total)
    sheet.append(frame)
    await new Promise<void>((done, fail) => {
      const timeout = window.setTimeout(() => fail(new Error(invalidPaper)), 15000)
      frame.onload = () => {
        window.clearTimeout(timeout)
        done()
      }
      doc.body.append(sheet)
    })
  }
  if (active() && !popup.closed) popup.print()
}
