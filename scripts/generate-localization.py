"""Compile the reviewed UTF-8 translation catalog into .NET ResourceManager resources."""
from pathlib import Path
import csv
import xml.etree.ElementTree as E
base=Path(__file__).resolve().parents[1]/'Fellowship'/'Localization'
rows=list(csv.reader((base/'catalog.tsv').read_text(encoding='utf-8-sig').splitlines(),delimiter='\t'))[1:]
for i,lang in enumerate(['','fr','es','ar','tr']):
    root=E.Element('root');seen=set()
    for name,val in [('resmimetype','text/microsoft-resx'),('version','2.0'),('reader','System.Resources.ResXResourceReader, System.Windows.Forms'),('writer','System.Resources.ResXResourceWriter, System.Windows.Forms')]:
        h=E.SubElement(root,'resheader',name=name);E.SubElement(h,'value').text=val
    for row in rows:
        assert len(row)==5 and all(row),row
        if row[0].lower() in seen:continue
        seen.add(row[0].lower())
        entry=E.SubElement(root,'data',name=row[0].replace('\\n','\n'))
        entry.set('{http://www.w3.org/XML/1998/namespace}space','preserve')
        E.SubElement(entry,'value').text=row[i].replace('\\n','\n')
    E.indent(root)
    E.ElementTree(root).write(base/('Strings'+('.'+lang if lang else '')+'.resx'),encoding='utf-8',xml_declaration=True)
