"""Publish a tested Windows package. Run from the committed repository checkout.
Usage: python scripts/publish-release.py 0.1.9 path/to/package.zip path/to/notes.txt
Uses the existing Git credential manager; never stores or prints credentials.
"""
import hashlib,json,pathlib,re,subprocess,sys,urllib.error,urllib.request
version,package,notes,*installers=sys.argv[1:]
if not re.fullmatch(r'\d+\.\d+\.\d+',version):raise ValueError('Use a three-part release version')
p=pathlib.Path(package)
if p.name!=f'Fellowship-{version}-win-x64.zip':raise ValueError('Unexpected package name')
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
credential=subprocess.run(['git','credential','fill'],input='protocol=https\nhost=github.com\n\n',capture_output=True,text=True,check=True)
fields=dict(line.split('=',1) for line in credential.stdout.splitlines() if '=' in line)
headers={'Authorization':'Bearer '+fields['password'],'User-Agent':'Fellowship-release','Accept':'application/vnd.github+json'}
def request(url,data=None,method=None,content_type='application/json'):
    req=urllib.request.Request(url,data=data,method=method,headers={**headers,'Content-Type':content_type})
    with urllib.request.urlopen(req,timeout=180) as response:return json.load(response)
base='https://api.github.com/repos/Joshua131313/fellowship-bfme2';tag='v'+version
try:release=request(base+'/releases/tags/'+tag)
except urllib.error.HTTPError as error:
    if error.code!=404:raise
    release=request(base+'/releases',json.dumps({'tag_name':tag,'target_commitish':commit,'name':'Fellowship '+version,'body':pathlib.Path(notes).read_text(encoding='utf-8-sig'),'draft':True}).encode(),'POST')
packages=[p]+[pathlib.Path(i) for i in installers]
for p in packages:
    if p.name not in (f'Fellowship-{version}-win-x64.zip',f'Fellowship-{version}-win-x64.msi',f'Fellowship-Repair-{version}.exe'):raise ValueError('Unexpected asset name')
assets_to_upload=[(p.name,p) for p in packages]
installer=next((p for p in packages if p.name==f'Fellowship-{version}-win-x64.msi'),None)
if installer is not None:assets_to_upload.append(('Fellowship-Setup.msi',installer))
for asset_name,p in assets_to_upload:
    payload=p.read_bytes();assets=request(base+'/releases/'+str(release['id'])+'/assets')
    asset=next((a for a in assets if a['name']==asset_name),None)
    if asset is None:asset=request(release['upload_url'].split('{')[0]+'?name='+asset_name,payload,'POST','application/octet-stream')
    if asset['digest']!='sha256:'+hashlib.sha256(payload).hexdigest():raise ValueError('Published package differs. Bump version; do not overwrite an existing release.')
release=request(base+'/releases/'+str(release['id']),json.dumps({'draft':False,'prerelease':False,'make_latest':'true'}).encode(),'PATCH')
if request(base+'/releases/latest')['tag_name']!=tag:raise ValueError('Latest release did not update')
print('Published and checksum verified:',release['html_url'])
