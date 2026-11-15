import sys,json,re
from pathlib import Path
# Test-only dependencies: pypdf, pypdfium2, Pillow, zxing-cpp==2.3.0.
# The renderer itself does not depend on the barcode decoder.
base = Path(sys.argv[1] if len(sys.argv) > 1 else '.run/r3/graphics-final')
import zxingcpp
import pypdfium2 as pdfium
from pypdf import PdfReader
from PIL import Image,ImageDraw
folder=base/'all';doc=pdfium.PdfDocument(str(folder/'result.pdf'));tiles=[];codes=[]
for i in range(len(doc)):
 image=doc[i].render(scale=3).to_pil().convert('RGB');image.save(folder/f'page-{i+1}.png')
 codes.extend([r.text for r in zxingcpp.read_barcodes(image)])
 image.thumbnail((420,594));tile=Image.new('RGB',(440,624),'#eee');tile.paste(image,(10,25));ImageDraw.Draw(tile).text((10,5),'Page '+str(i+1),fill='black');tiles.append(tile)
assert codes==['PR-20261115'],codes
reader=PdfReader(folder/'result.pdf');text=''.join(p.extract_text() or '' for p in reader.pages)
assert '光刻胶A1' in re.sub(r'\s+','',text)
assert '工序一' in text and '工序二' in text
canvas=Image.new('RGB',(440*len(tiles),624),'white')
for i,tile in enumerate(tiles):canvas.paste(tile,(440*i,0))
canvas.save(folder/'contact-sheet.png')
result={'pages':len(doc),'decodedCodes':codes,'text':text}
(folder/'independent-check.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8');print(json.dumps({'pages':len(doc),'decodedCodes':codes}))

folder=base/'long-pivot'
reader=PdfReader(folder/'result.pdf');texts=[p.extract_text() or '' for p in reader.pages]
rows=re.findall(r'ROW-\d{3}','\n'.join(texts))
assert rows==['ROW-'+str(i).zfill(3) for i in range(90)],rows
assert all('列A' in t and '列B' in t for t in texts),texts
assert '9007199254740993.001' in texts[0]
assert all('9007199254744998.001' not in t for t in texts[:-1])
assert texts[-1].count('9007199254744998.001') == 2
result={'pages':len(texts),'rows':len(rows),'uniqueRows':len(set(rows)),'repeatedHeaderEveryPage':True,'exactDecimalPreserved':True}
(folder/'independent-check.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8');print(json.dumps(result))

folder=base/'barcode-alphabet';doc=pdfium.PdfDocument(str(folder/'result.pdf'));codes=[]
for page in doc:
 codes.extend(r.text for r in zxingcpp.read_barcodes(page.render(scale=3).to_pil()))
assert codes==['0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ','-. $/+%'],codes
(folder/'independent-check.json').write_text(json.dumps({'decodedCodes':codes}),encoding='utf8');print(json.dumps({'decodedCodes':codes}))
