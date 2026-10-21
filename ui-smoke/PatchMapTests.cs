using Fellowship;
using System.Buffers.Binary;
using System.IO;
using System.Text;

static class PatchMapTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    const string Rhun=@"maps\map wor rhun\map wor rhun.map";
    const string Shared=@"maps\shared\shared.map";
    const string Community=@"maps\community_canyon\community_canyon.map";
    const string Campaign=@"maps\map wor campaign\map wor campaign.map";
    const string Missing=@"maps\missing\missing.map";
    const string Preview=@"maps\shared\shared.tga";
    static byte[] Bytes(string text)=>Encoding.ASCII.GetBytes(text);
    static string Key(string path)=>string.Concat(path.Select(c=>char.IsAsciiLetterOrDigit(c)?c.ToString():"_"+((int)c).ToString("X2")));
    static string Cache(string path,bool multiplayer,double start=20)=>
        $"MapCache {Key(path)}\n"+
        $" isMultiplayer = {(multiplayer?"yes":"no")}\n numPlayers = 2\n"+
        " extentMin = X:0 Y:0 Z:0\n extentMax = X:100 Y:100 Z:0\n"+
        $" Player_1_Start = X:{start.ToString(System.Globalization.CultureInfo.InvariantCulture)} Y:30 Z:0\n"+
        " Player_2_Start = X:80 Y:70 Z:0\nEND\n";
    static void Big(string path,params (string Name,byte[] Data)[] entries)
    {
        int headerSize=16+entries.Sum(e=>8+Encoding.ASCII.GetByteCount(e.Name)+1);
        int totalSize=headerSize+entries.Sum(e=>e.Data.Length);
        using var stream=File.Create(path);using var writer=new BinaryWriter(stream,Encoding.ASCII);
        void Be(int value){Span<byte> bytes=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(bytes,value);writer.Write(bytes);}
        writer.Write(Bytes("BIGF"));writer.Write(totalSize);Be(entries.Length);Be(headerSize);
        int offset=headerSize;
        foreach(var entry in entries){Be(offset);Be(entry.Data.Length);writer.Write(Bytes(entry.Name));writer.Write((byte)0);offset+=entry.Data.Length;}
        foreach(var entry in entries)writer.Write(entry.Data);
    }
    static bool Has(IEnumerable<GameMap> maps,string path)=>maps.Any(m=>m.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
    static bool Starts(List<MapSpot> spots,double expected)=>spots.Count==2&&Math.Abs(spots[0].X-expected)<.000001;
    public static void Run()
    {
        string fixture=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Fellowship-patch-maps-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(fixture);
        try
        {
            string original=Path.Combine(fixture,"Maps.big"),overlay=Path.Combine(fixture,"109Maps.big");
            Big(original,(@"maps\mapcache.ini",Bytes(Cache(Rhun,true)+Cache(Shared,true,10)+Cache(Campaign,false)+Cache(Missing,true))),
                (Rhun,Bytes("rhun-map")),(Shared,Bytes("shared-base-map")),(Campaign,Bytes("campaign-map")),(Preview,[1,2,3]));
            void Overlay(string community,byte preview)=>Big(overlay,
                (@"maps\mapcache.ini",Bytes(Cache(Shared,true,70)+Cache(community,true,25))),
                (Shared,Bytes("shared-patch-map")),(community,Bytes("community-map")),(Preview,[preview,8,7]));
            Overlay(Community,9);

            var baseMaps=GameAssets.Maps([original]);
            Check(Has(baseMaps,Rhun)&&Has(baseMaps,Shared),"Base multiplayer maps were not read from their archive cache");
            Check(!Has(baseMaps,Campaign),"MapCache isMultiplayer=no campaign map appeared in the multiplayer catalog");
            Check(!Has(baseMaps,Missing),"MapCache entry without its .map asset appeared in the catalog");
            Check(!Has(baseMaps,Community),"Base-patch catalog included a map from another patch");

            var patchedMaps=GameAssets.Maps([overlay,original]);
            Check(Has(patchedMaps,Community),"Patch map without a map wor/map mp folder prefix was omitted");
            Check(Has(patchedMaps,Rhun)&&Has(patchedMaps,Shared),"Patch catalog discarded base maps absent from its partial MapCache");
            Check(patchedMaps.Count(m=>m.Path.Equals(Shared,StringComparison.OrdinalIgnoreCase))==1,"Patch and base map appeared as duplicate entries");
            Check(!Has(patchedMaps,Campaign)&&!Has(patchedMaps,Missing),"Patch overlay bypassed multiplayer metadata or map-asset validation");
            Check(Starts(GameAssets.Spots([overlay,original],Shared),.7),"Highest-priority MapCache did not override that map's spots");
            Check(Starts(GameAssets.Spots([original,overlay],Shared),.1),"Archive priority was ignored when reading map coordinates");
            Check(Starts(GameAssets.Spots([overlay,original],Rhun),.2),"Partial patch MapCache hid the base Rhûn coordinates");
            Check(GameAssets.ReadAsset([overlay,original],Preview.ToUpperInvariant())?.SequenceEqual(new byte[]{9,8,7})==true,"Preview lookup ignored overlay priority or case-insensitive asset paths");
            Check(GameAssets.ReadAsset([original,overlay],Preview)?.SequenceEqual(new byte[]{1,2,3})==true,"Preview lookup changed priority order");
            Check(GameAssets.ReadAsset([overlay,original],@"maps\absent.tga")==null,"Missing preview did not return null");

            // Change names without changing archive length: cached indexes must
            // notice the new file timestamp as well as changes in file size.
            long previousLength=new FileInfo(overlay).Length;var previousWrite=File.GetLastWriteTimeUtc(overlay);
            string replacement=Community.Replace("canyon","valley");
            Overlay(replacement,4);File.SetLastWriteTimeUtc(overlay,previousWrite.AddSeconds(5));
            Check(new FileInfo(overlay).Length==previousLength,"Equal-length archive replacement fixture changed length");
            var changedMaps=GameAssets.Maps([overlay,original]);
            Check(Has(changedMaps,replacement)&&!Has(changedMaps,Community),"Map catalog reused an index after the archive was replaced");
            Check(GameAssets.ReadAsset([overlay,original],Preview)?.SequenceEqual(new byte[]{4,8,7})==true,"Asset lookup reused stale data after an archive changed");

            var malformed=new List<string>();
            string Bad(string name,byte[] bytes){string path=Path.Combine(fixture,name+".big");File.WriteAllBytes(path,bytes);malformed.Add(path);return path;}
            Bad("truncated",Bytes("BIGF"));
            var excessive=new byte[16];Bytes("BIGF").CopyTo(excessive,0);BinaryPrimitives.WriteInt32BigEndian(excessive.AsSpan(8,4),int.MaxValue);Bad("excessive-count",excessive);
            var invalidOffset=File.ReadAllBytes(overlay);BinaryPrimitives.WriteUInt32BigEndian(invalidOffset.AsSpan(16,4),uint.MaxValue);Bad("out-of-bounds-entry",invalidOffset);
            var invalidSize=File.ReadAllBytes(overlay);BinaryPrimitives.WriteUInt32BigEndian(invalidSize.AsSpan(20,4),uint.MaxValue);Bad("out-of-bounds-size",invalidSize);
            var badDataOffset=File.ReadAllBytes(overlay);BinaryPrimitives.WriteUInt32BigEndian(badDataOffset.AsSpan(12,4),uint.MaxValue);Bad("out-of-bounds-index",badDataOffset);
            foreach(string bad in malformed)
            {
                Check(GameAssets.Maps([bad]).Count==0,"Malformed archive supplied map entries: "+Path.GetFileName(bad));
                Check(GameAssets.Spots([bad],Shared).Count==0,"Malformed archive supplied coordinates: "+Path.GetFileName(bad));
                Check(GameAssets.ReadAsset([bad],Preview)==null,"Malformed archive supplied an asset: "+Path.GetFileName(bad));
            }
            Check(Has(GameAssets.Maps(malformed.Concat(new[]{original})),Rhun),"Malformed overlay prevented a valid lower-priority archive from being read");
            Console.WriteLine("PASS patch-specific multiplayer maps, arbitrary community names, campaign/missing-asset filtering, per-map cache fallback, archive priority, changed-file invalidation and malformed BIG bounds");
        }
        finally
        {
            string tempRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(Directory.Exists(fixture)&&fixture.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase))Directory.Delete(fixture,true);
        }
    }
}
