// R4: bounded, inert document composition. Imported PDF bytes never execute in the preview host.
export async function inspectPdf(bytes, engine) {
 const {PDFDocument,PDFDict,PDFArray,PDFName}=engine;
 let pdf;try{pdf=await PDFDocument.load(bytes,{updateMetadata:false,throwOnInvalidObject:true});}catch{throw Error('REPORT_PDF_INVALID_OR_ENCRYPTED');}
 const forbidden=new Set(['AA','OpenAction','JS','JavaScript','Launch','EmbeddedFiles','RichMedia','XFA','AcroForm','Encrypt']);
 const filters=new Set(['FlateDecode','DCTDecode','LZWDecode','ASCII85Decode','ASCIIHexDecode','RunLengthDecode']);
 const seen=new Set();let count=0;
 function visit(object,depth=0) {
  object=pdf.context.lookup(object);if(!object||seen.has(object))return;seen.add(object);
  if(++count>100000||depth>64)throw Error('REPORT_PDF_OBJECT_LIMIT');
  if(object instanceof PDFArray){for(const value of object.asArray())visit(value,depth+1);return;}
  const dict=object instanceof PDFDict?object:object?.dict;
  if(!(dict instanceof PDFDict))return;
  for(const [key,value] of dict.entries()) {
   const name=key.decodeText();if(forbidden.has(name))throw Error('REPORT_PDF_ACTIVE_CONTENT_UNSUPPORTED');
   if(name==='Annots'){const a=pdf.context.lookup(value);if(!(a instanceof PDFArray)||a.size())throw Error('REPORT_PDF_ANNOTATIONS_UNSUPPORTED');}
   if(name==='Filter'){const v=pdf.context.lookup(value);const list=v instanceof PDFArray?v.asArray():[v];if(list.some(x=>!(x instanceof PDFName)||!filters.has(x.decodeText())))throw Error('REPORT_PDF_ENCODING_UNSUPPORTED');}
   visit(value,depth+1);
  }
 }
 for(const [,object] of pdf.context.enumerateIndirectObjects())visit(object);
 if(pdf.getPageCount()<1||pdf.getPageCount()>100)throw Error('REPORT_PDF_PAGE_LIMIT');
 for(const p of pdf.getPages()){const box=p.getCropBox();if(![box.x,box.y,box.width,box.height,p.getRotation().angle].every(Number.isFinite)||box.width<56||box.height<56||box.width>5669||box.height>5669||p.getRotation().angle%90)throw Error('REPORT_PDF_GEOMETRY_UNSUPPORTED');}
 return pdf;
}
export function prepareReportToc(source, format) {
 const outlines=[...source.querySelectorAll('[data-report-outline]')];const holders=[...source.querySelectorAll('[data-report-toc]')];
 if(holders.length>1||outlines.length>2000)throw Error('REPORT_TOC_LIMIT');
 if(format==='Xlsx'&&(holders.length||source.querySelector('[data-report-pdf]')))throw Error('REPORT_XLSX_DOCUMENT_UNSUPPORTED');
 outlines.forEach((node,i)=>{node.dataset.reportOutlineId=String(i);});
 for(const holder of holders){const h=document.createElement('h2');h.textContent=holder.dataset.reportToc;h.style.cssText='margin:0 0 3mm;break-after:avoid';holder.append(h);const table=document.createElement('table');table.style.cssText='table-layout:fixed;width:100%;border:0';holder.append(table);const col=document.createElement('colgroup');col.innerHTML='<col><col style="width:25mm">';table.append(col);const body=document.createElement('tbody');table.append(body);
 for(const node of outlines){const meta=JSON.parse(node.dataset.reportOutline);const row=document.createElement('tr');row.style.breakInside='avoid';const title=document.createElement('td');title.textContent=meta.title;title.style.cssText='border:0;padding:1mm 2mm 1mm '+(Math.max(0,Number(meta.level||1)-1)*5)+'mm';const number=document.createElement('td');number.dataset.reportTocTarget=node.dataset.reportOutlineId;number.textContent='000000';number.style.cssText='border:0;padding:1mm;text-align:right;white-space:nowrap;font-variant-numeric:tabular-nums';row.append(title,number);body.append(row);}}
}
export function resolveReportToc(pages) {
 if(pages.length>10000)throw Error('REPORT_PAGE_LIMIT');const targets=new Map();pages.forEach((p,i)=>p.querySelectorAll('[data-report-outline-id]').forEach(n=>{if(!targets.has(n.dataset.reportOutlineId))targets.set(n.dataset.reportOutlineId,i+1)}));
 let count=0;for(const p of pages)for(const n of p.querySelectorAll('[data-report-toc-target]')){const number=targets.get(n.dataset.reportTocTarget);if(!number)throw Error('REPORT_TOC_TARGET_MISSING');const before=n.parentElement.getBoundingClientRect();n.textContent=String(number);const after=n.parentElement.getBoundingClientRect();if(Math.abs(before.height-after.height)>.1)throw Error('REPORT_TOC_NOT_STABLE');count++;}return {entries:count,passes:count?2:0,physicalPages:pages.length};
}
export function appendPdfPages(target, images, spec, style, preview) {
 for(const [i,image] of images.entries()) {
  const name='pdfdoc'+spec.index+'p'+i;style.textContent+='@page '+name+'{size:'+image.widthMm+'mm '+image.heightMm+'mm;margin:0}';
  const page=document.createElement('div');page.className='pagedjs_page report-pdf-page';page.dataset.reportPdfIndex=String(spec.index);page.dataset.reportPdfPage=String(i);page.style.cssText='position:relative;overflow:hidden;margin:0;padding:0;box-sizing:border-box;break-after:page;width:'+image.widthMm+'mm;height:'+image.heightMm+'mm;page:'+name;
  page.style.setProperty('--pagedjs-width',image.widthMm+'mm');page.style.setProperty('--pagedjs-height',image.heightMm+'mm');
  const content=document.createElement('div');content.className='pagedjs_page_content';content.style.cssText='position:relative;width:100%;height:100%';page.append(content);content.dataset.reportMarker=spec.marker;
  if(i===0&&spec.outline)content.insertAdjacentHTML('beforeend',spec.outline);
  const img=document.createElement('img');img.src=image.image;img.alt='';img.style.cssText='display:block;max-width:none;width:100%;height:100%;position:absolute;inset:0';content.append(img);
  const v=image.view;for(const item of image.text){const t=item.transform;const x=v[0]*t[4]+v[2]*t[5]+v[4],y=v[1]*t[4]+v[3]*t[5]+v[5];const ax=v[0]*t[0]+v[2]*t[1],ay=v[1]*t[0]+v[3]*t[1];const angle=Math.atan2(ay,ax);const height=Math.max(1,Math.hypot(t[2],t[3])*96/72);const span=document.createElement('span');span.textContent=item.text;span.style.cssText='position:absolute;white-space:pre;color:transparent;line-height:1;transform-origin:0 0;font-size:'+height+'px;left:'+x+'px;top:'+y+'px;transform:rotate('+angle+'rad) translateY(-100%)';content.append(span);}
  if(preview){const watermark=document.createElement('div');watermark.textContent='PREVIEW · 试运行';watermark.style.cssText='position:absolute;opacity:.15;top:45%;left:20%;transform:rotate(-30deg);font-size:40pt';content.append(watermark);}
  target.append(page);
 }
}
