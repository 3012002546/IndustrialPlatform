"""Independently inspect the font-check PDFs, including embedded font streams."""
import json,re,sys
from pathlib import Path
from pypdf import PdfReader
import pypdfium2 as pdfium
from PIL import Image,ImageDraw
folder=Path(sys.argv[1] if len(sys.argv)>1 else '.run/r2-resource-font/fonts')
manifest=json.loads((folder/'font-check.json').read_text(encoding='utf8'))
results=[];tiles=[]
for case in manifest['cases']:
 path=Path(case['folder'])/'result.pdf';reader=PdfReader(path)
 text='\n'.join(page.extract_text() or '' for page in reader.pages)
 # SimSun synthetic bold is extracted with inter-glyph spaces; normalize whitespace only.
 compact=re.sub(r'\s+','',text)
 ids=re.findall(r'FONTROW-(\d{3})',compact)
 assert ids==[str(i).zfill(3) for i in range(60)], str(path)+' lost/repeated/reordered records'
 for token in ['光刻胶工单','称量记录','合计','繁體','µg','℃','±','≤','≥','0123456789','ABCxyz']:
  assert compact.count(token)==60, str(path)+' incorrect text: '+token
 fonts={}
 for page in reader.pages:
  for key,ref in page['/Resources']['/Font'].items():
   font=ref.get_object();name=str(font.get('/BaseFont',str(font['/Subtype'])+str(key)));descendants=font.get('/DescendantFonts',[font]);embedded=False
   if font['/Subtype']=='/Type3':
    glyphs=font['/CharProcs'].get_object()
    embedded=bool(glyphs) and all(bool(g.get_object().get_data()) for g in glyphs.values())
   for child in descendants:
    desc=child.get_object().get('/FontDescriptor')
    if desc and any(k in desc.get_object() for k in ['/FontFile','/FontFile2','/FontFile3']):embedded=True
   fonts[name]=embedded
 assert fonts and all(fonts.values()),str(path)+' non-embedded font'
 doc=pdfium.PdfDocument(str(path))
 for n in sorted(set([0,len(doc)-1])):
  image=doc[n].render(scale=1.5).to_pil().convert('RGB');image.save(path.parent/f'page-{n+1}.png')
  image.thumbnail((350,480));tile=Image.new('RGB',(370,515),'#eee');tile.paste(image,((370-image.width)//2,25));ImageDraw.Draw(tile).text((8,5),path.parent.name+' page '+str(n+1),fill='black');tiles.append(tile)
 results.append({'family':case['family'],'orientation':case['orientation'],'pages':len(reader.pages),'rows':len(ids),'fontsEmbedded':fonts,'chineseAndTraditionalTextPreserved':True})
canvas=Image.new('RGB',(370*4,515*((len(tiles)+3)//4)),'white')
for i,tile in enumerate(tiles):canvas.paste(tile,((i%4)*370,(i//4)*515))
canvas.save(folder/'font-contact-sheet.png')
(folder/'pdf-check.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({'passed':len(results),'pdfPages':sum(r['pages'] for r in results),'verifiedRows':sum(r['rows'] for r in results),'allFontsEmbedded':True}))
