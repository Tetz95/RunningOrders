"""Package a release zip: python tools/package.py <repo> <build output dir> <zip path>. One top folder Tetz's Running Orders."""
import json, re, sys, zipfile, os
repo, out, dest = sys.argv[1], sys.argv[2], sys.argv[3]
TOP = "Tetz's Running Orders/"
ver = re.search(r'AssemblyVersion\("([\d.]+)"\)', open(os.path.join(repo, 'RunningOrders/Properties/AssemblyInfo.cs'), encoding='utf-8-sig').read()).group(1)
info = open(os.path.join(repo, 'Info.json'), encoding='utf-8').read()
info = re.sub(r'"version":\s*"[^"]+"', '"version": "%s"' % '.'.join(ver.split('.')[:3]), info)
json.loads(info)
with zipfile.ZipFile(dest, 'w', zipfile.ZIP_DEFLATED) as z:
    for f in ('RunningOrders.dll', '0Harmony.dll'):
        z.write(os.path.join(out, f), TOP + f)
    z.writestr(TOP + 'Info.json', info)
    for f in ('LICENSE.txt', 'THIRD_PARTY_NOTICES.txt'):
        z.write(os.path.join(repo, f), TOP + f)
    # 0Harmony.dll's MIT licence (THIRD_PARTY_NOTICES.txt holds it) under the name all our Harmony zips use
    z.write(os.path.join(repo, 'THIRD_PARTY_NOTICES.txt'), TOP + 'Harmony LICENSE.txt')
print(ver, dest)
