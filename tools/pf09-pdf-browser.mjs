import {getDocument} from 'pdfjs-dist/build/pdf.mjs';
import {WorkerMessageHandler} from 'pdfjs-dist/build/pdf.worker.mjs';
globalThis.pdfjsWorker={WorkerMessageHandler};
export async function renderPdf(data, assets) {
 class CMaps { async fetch({name}) {if(!assets.cmaps[name+'.bcmap'])throw Error('REPORT_PDF_FONT_UNAVAILABLE');return {cMapData:Uint8Array.from(atob(assets.cmaps[name+'.bcmap']),c=>c.charCodeAt(0)),compressionType:1}} }
 class Fonts { async fetch({filename}) {if(!assets.fonts[filename])throw Error('REPORT_PDF_FONT_UNAVAILABLE');return Uint8Array.from(atob(assets.fonts[filename]),c=>c.charCodeAt(0))} }
 const task=getDocument({data:Uint8Array.from(atob(data),c=>c.charCodeAt(0)),isEvalSupported:false,useSystemFonts:false,useWorkerFetch:false,CMapReaderFactory:CMaps,StandardFontDataFactory:Fonts,stopAtErrors:true,disableAutoFetch:true,disableStream:true,enableXfa:false});
 const doc=await task.promise;const pages=[];let pixels=0;
 try {for(let i=1;i<=doc.numPages;i++) { const p=await doc.getPage(i);const viewport=p.getViewport({scale:150/72});pixels+=Math.ceil(viewport.width)*Math.ceil(viewport.height);if(pixels>80000000)throw Error('REPORT_PDF_RASTER_LIMIT');const canvas=document.createElement('canvas');canvas.width=Math.ceil(viewport.width);canvas.height=Math.ceil(viewport.height);await p.render({canvasContext:canvas.getContext('2d'),viewport}).promise;const text=await p.getTextContent();pages.push({image:canvas.toDataURL('image/png'),widthMm:viewport.width*25.4/150,heightMm:viewport.height*25.4/150,text:text.items.filter(x=>x.str).map(x=>({text:x.str,transform:x.transform,width:x.width,height:x.height})),view:p.getViewport({scale:96/72}).transform});canvas.width=canvas.height=0;p.cleanup();}return pages;}finally {await task.destroy();}
}
