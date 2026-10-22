using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Fellowship;

// Read-only patch discovery. The switcher is never launched and game files are
// never renamed, copied or patched. Unknown/mixed installations fail closed.
public static class GamePatches
{
    sealed record Patch(string Name,string Source,string Parent,double[] Order);
    sealed record Options(bool Zoom=true,bool Floors=true);
    sealed record ActiveResult(long Checked,long Verified,string Stamp,string Value);
    static readonly string[] Common=["1.00","1.06","1.09","1.09v2","1.09v3","1.09v3.1"];
    static readonly string[] Shared=["AmbientStreams.big","Audio.big","Bases.big","lotrsec.big","Music.big","Shaders.big","Terrain.big","Textures0.big","Textures1.big","Textures2.big","Textures3.big","Textures4.big","W3D.big","Window.big"];
    static readonly Lazy<string[]> CommonRoots=new(DiscoverRoots);
    static readonly ConcurrentDictionary<string,(long Stamp,long Length,string Hash)> Digests=new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string,(long Stamp,Patch[] Patches)> Manifests=new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string,ActiveResult> ActiveResults=new(StringComparer.OrdinalIgnoreCase);
    static readonly Lazy<Patch[]> BuiltIn=new(()=>CatalogData.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>
    {
        var fields=line.TrimEnd('\r').Split('|');
        return new Patch(fields[0],fields[1],fields[2],fields[3].Split(',').Select(x=>double.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray());
    }).ToArray());

    public static string[] Labels(string executable)
    {
        var catalog=Catalog(Roots(executable));
        var latest=catalog.Where(p=>IsBeta(p.Name))
            .OrderByDescending(p=>p.Order[0]).ThenByDescending(p=>p.Order[1]).ThenByDescending(p=>p.Order[2]).FirstOrDefault();
        // Keep historical definitions for archive inheritance and detection, not
        // as choices in the room form. Only the newest beta belongs in this list.
        return latest==null?Common.ToArray():Common.Append(latest.Name).ToArray();
    }
    static bool IsBeta(string name)=>(name.StartsWith("1.09",StringComparison.OrdinalIgnoreCase)||name.StartsWith("v2 ",StringComparison.OrdinalIgnoreCase))&&Key(name).Contains('ß');
    public static string? LatestInstalled(string executable,string? switcherRoot=null)
    {
        var roots=switcherRoot==null?Roots(executable):[switcherRoot];var catalog=Catalog(roots);
        return catalog.Where(p=>roots.Any(root=>
        {
            var chain=Chain(catalog,p);
            if(chain==null||chain.Any(parent=>Source(root,parent.Source)==null))return false;
            var own=OwnArchives(root,p,ReadOptions(root));
            return p.Name is "1.00" or "1.06"?own.ContainsKey("Maps.big"):own.Keys.Any(IsPatchArchive);
        })).OrderByDescending(p=>p.Order[0]).ThenByDescending(p=>p.Order[1]).ThenByDescending(p=>p.Order[2]).Select(p=>p.Name).FirstOrDefault();
    }
    public static string? LatestSwitcherTarget(string executable,string? switcherRoot=null)
    {
        var roots=switcherRoot==null?Roots(executable):[switcherRoot];var catalog=Catalog(roots);
        try
        {
            var known=catalog.Select(p=>p.Source.Replace('/','\\')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var betaParents=catalog.Where(p=>Key(p.Name).Contains('ß')).Select(p=>Path.GetDirectoryName(p.Source)).Where(p=>!string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach(var root in roots)
            foreach(var parent in betaParents)
            {
                var folder=Path.Combine(root,parent!);if(!Directory.Exists(folder))continue;
                foreach(var child in Directory.EnumerateDirectories(folder))
                    if(Regex.IsMatch(Path.GetFileName(child),@"^(?:ß|β|ss|beta)\s*\d",RegexOptions.IgnoreCase)&&!known.Contains(Path.GetRelativePath(root,child).Replace('/','\\')))return null;
            }
            // Ecth's CLI ranks source folders even before their archives are
            // complete. Compare this answer with LatestInstalled before using
            // -latest, so an incomplete newer folder cannot change the target.
            return catalog.Where(p=>roots.Any(root=>Source(root,p.Source)!=null))
                .OrderByDescending(p=>p.Order[0]).ThenByDescending(p=>p.Order[1]).ThenByDescending(p=>p.Order[2]).Select(p=>p.Name).FirstOrDefault();
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException){return null;}
    }
    public static string NewRoomPatch(string executable)
    {
        string active=Active(executable);
        return Common.Contains(active)||IsBeta(active)?active:"1.00";
    }
    public static string Normalize(string value)
    {
        var key=Key(value);if(key.Length==0)return "";
        var found=Catalog(Roots(null)).FirstOrDefault(p=>Key(p.Name)==key);
        if(found!=null)return found.Name;
        // The u4 family was renamed after build 16. Keep individual builds exact.
        if(key.StartsWith("1.09u4ß",StringComparison.Ordinal))
            found=Catalog(Roots(null)).FirstOrDefault(p=>Key(p.Name)==key.Replace("1.09u4ß","1.09v3.2ß",StringComparison.Ordinal));
        return found?.Name??(value??"").Trim();
    }
    public static bool IsKnown(string value)=>Catalog(Roots(null)).Any(p=>Key(p.Name)==Key(Normalize(value)));

    public static string Active(string executable)
    {
        var game=GameDirectory(executable);if(game==null)return "Unknown";
        long now=Environment.TickCount64;
        if(ActiveResults.TryGetValue(game,out var cached)&&now-cached.Checked<1000)return cached.Value;
        try
        {
            var roots=Roots(executable);var stamp=Fingerprint(game,roots);
            if(cached!=null&&cached.Stamp==stamp&&now-cached.Verified<10000)
            {ActiveResults[game]=cached with{Checked=now};return cached.Value;}
            var value=DetectActive(game,roots);
            // A switch in progress cannot produce a trusted cached answer.
            if(stamp!=Fingerprint(game,roots))value="Unknown";
            ActiveResults[game]=new(now,now,stamp,value);return value;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException){return "Unknown";}
    }
    static string Fingerprint(string game,string[] roots)
    {
        var result=new StringBuilder();
        foreach(var file in new DirectoryInfo(game).EnumerateFiles().OrderBy(f=>f.Name,StringComparer.OrdinalIgnoreCase))
            result.Append(file.Name).Append(':').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks).Append('|');
        foreach(var root in roots)
        foreach(var relative in new[]{@"PlayerCustomFiles\settings.json",@"data\GUI\web_cached_data\releases_new.json"})
        {var file=new FileInfo(Path.Combine(root,relative));result.Append(file.FullName).Append(':').Append(file.Exists?file.LastWriteTimeUtc.Ticks:0).Append('|');}
        return result.ToString();
    }
    static string DetectActive(string game,string[] roots)
    {
        try
        {
            var catalog=Catalog(roots);var matches=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var loadedPatchFiles=Directory.EnumerateFiles(game).Where(p=>Path.GetExtension(p).Equals(".big",StringComparison.OrdinalIgnoreCase)&&IsPatchArchive(Path.GetFileName(p))&&Big(p))
                .Select(Path.GetFileName).ToArray();
            foreach(var root in roots)
            {
                var options=ReadOptions(root);
                var installed=catalog.Where(p=>Source(root,p.Source)!=null).ToArray();
                foreach(var patch in installed)
                {
                    var chain=Chain(catalog,patch);if(chain==null||chain.Any(p=>Source(root,p.Source)==null))continue;
                    var archives=Merge(root,chain,options);
                    if(!archives.TryGetValue("Maps.big",out var maps)||!Same(Path.Combine(game,"Maps.big"),maps))continue;
                    // A newer/unknown loaded overlay must not be mistaken for its
                    // older parent merely because that parent's files still match.
                    if(loadedPatchFiles.Any(name=>name==null||!archives.ContainsKey(name)))continue;
                    var own=OwnArchives(root,patch,options);
                    var identity=own.Where(e=>IsPatchArchive(e.Key)).ToArray();
                    if(patch.Name is not ("1.00" or "1.06")&&identity.Length==0)continue;
                    // The child overlay alone cannot authenticate its inherited
                    // layout. A missing or modified parent is a mixed patch too.
                    if(archives.Where(e=>IsPatchArchive(e.Key)).Any(e=>!Same(Path.Combine(game,e.Key),e.Value)))continue;
                    var binaries=chain.Reverse().Select(p=>Source(root,p.Source)).Where(p=>p!=null)
                        .Select(p=>new[]{Path.Combine(p!,"game.dat"),Path.Combine(p!,"delayfixer","game.dat"),Path.Combine(p!,"singleplayer","game.dat")})
                        .FirstOrDefault(paths=>paths.Any(File.Exists));
                    if(binaries==null||!binaries.Any(path=>Same(Path.Combine(game,"game.dat"),path)))continue;
                    matches.Add(patch.Name);
                }
            }
            if(matches.Count==1)return matches.Single();
            if(matches.Count>1)return "Unknown";
            // These stock signatures also work when no switcher is installed.
            if(Directory.EnumerateFiles(game).Any(p=>Path.GetExtension(p).Equals(".big",StringComparison.OrdinalIgnoreCase)&&IsPatchArchive(Path.GetFileName(p))&&Big(p)))return "Unknown";
            var binary=Hash(Path.Combine(game,"game.dat"));var mapsHash=Hash(Path.Combine(game,"Maps.big"));
            if(binary==HostPreset.SupportedHash&&mapsHash=="95255431D435E90F6023D049BC847434953D14AFA7CF3B877E69BBCB792B2301")return "1.00";
            if(binary=="F008B587570BAD693981DC7218588C81D192A1E064B0F7F861539C51156A7640"&&mapsHash=="8CA19783C3A4969DFFEB5A6493A18D6F7447A310BF8B4152B17FC7ED4307E1C8")return "1.06";
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException){ }
        return "Unknown";
    }

    // SAGE compares names through CRT _stricmp: lowercase ASCII order, first
    // match wins. OrdinalIgnoreCase instead folds to uppercase and incorrectly
    // puts Maps ahead of _WSMaps and patch bases ahead of their underscore add-ons.
    // Equal logical names are replaced by the selected patch, not unioned together.
    public static IReadOnlyList<string> Archives(string executable,string? patch=null)
    {
        var game=GameDirectory(executable);if(game==null)return Array.Empty<string>();
        try
        {
            string active=Active(executable);string selected=patch==null?active:Normalize(patch);
            if(selected==active&&active!="Unknown")
                return Directory.EnumerateFiles(game).Where(p=>Path.GetExtension(p).Equals(".big",StringComparison.OrdinalIgnoreCase)&&Big(p))
                    .OrderBy(p=>Path.GetFileName(p).ToLowerInvariant(),StringComparer.Ordinal).ToArray();
            if(patch==null&&active=="Unknown")
                return Directory.EnumerateFiles(game).Where(p=>Path.GetExtension(p).Equals(".big",StringComparison.OrdinalIgnoreCase)&&Big(p))
                    .OrderBy(p=>Path.GetFileName(p).ToLowerInvariant(),StringComparer.Ordinal).ToArray();
            var roots=Roots(executable);var catalog=Catalog(roots);var definition=catalog.FirstOrDefault(p=>p.Name.Equals(selected,StringComparison.OrdinalIgnoreCase));
            if(definition==null)return Array.Empty<string>();
            var chain=Chain(catalog,definition);if(chain==null)return Array.Empty<string>();
            foreach(var root in roots)
            {
                if(chain.Any(p=>Source(root,p.Source)==null))continue;
                var merged=Merge(root,chain,ReadOptions(root));
                foreach(var name in Shared)
                    if(Big(Path.Combine(game,name)))merged.TryAdd(name,Path.Combine(game,name));
                return merged.OrderBy(e=>e.Key.ToLowerInvariant(),StringComparer.Ordinal).Select(e=>e.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException){ }
        return Array.Empty<string>();
    }
    // Map browsing can inherit the locally installed v3 catalogue when a beta
    // is advertised before its download is complete. Detection and switching
    // continue to use the strict archive/dependency resolver above.
    public static IReadOnlyList<string> MapArchives(string executable,string? patch=null,string? switcherRoot=null)
    {
        string selected=Normalize(patch??Active(executable));
        var roots=switcherRoot==null?Roots(executable):[switcherRoot];
        var catalog=Catalog(roots);var definition=catalog.FirstOrDefault(p=>p.Name.Equals(selected,StringComparison.OrdinalIgnoreCase));
        bool modernBeta=IsBeta(selected)&&(definition?.Order[1]>=3||Key(selected).StartsWith("1.09v3",StringComparison.Ordinal)||Key(selected).StartsWith("1.09u4",StringComparison.Ordinal));
        if(!modernBeta)return Archives(executable,patch);
        var game=GameDirectory(executable);if(game==null)return Array.Empty<string>();
        try
        {
            var floor=catalog.FirstOrDefault(p=>p.Name=="1.09v3");
            var floorChain=floor==null?null:Chain(catalog,floor);
            var requested=definition==null?Array.Empty<Patch>():Chain(catalog,definition)??[definition];
            foreach(var root in roots)
            {
                if(floorChain==null||floorChain.Any(p=>Source(root,p.Source)==null))continue;
                var options=ReadOptions(root);var baseline=Merge(root,floorChain,options);
                if(!OwnArchives(root,floor!,options).Keys.Any(IsPatchArchive))continue;
                var selectedFiles=Merge(root,requested,options);
                foreach(var pair in baseline)selectedFiles.TryAdd(pair.Key,pair.Value);
                foreach(var name in Shared)
                    if(Big(Path.Combine(game,name)))selectedFiles.TryAdd(name,Path.Combine(game,name));
                // Preserve a physical v3 archive even when a partial beta uses
                // the same logical filename. Missing map/art/cache entries then
                // fall through without importing an unrelated installed beta.
                return selectedFiles.Concat(baseline).OrderBy(e=>e.Key.ToLowerInvariant(),StringComparer.Ordinal).Select(e=>e.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException){ }
        return switcherRoot==null?Archives(executable,patch):Array.Empty<string>();
    }
    static Dictionary<string,string> Merge(string root,Patch[] chain,Options options)
    {
        var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var patch in chain)
            foreach(var pair in OwnArchives(root,patch,options))files[pair.Key]=pair.Value;
        // 1.09 onward requires the 1.06 WS base independently of zoom options;
        // optional 560 overlays then replace its map entries when zoom is on.
        if(chain.Any(p=>p.Name=="1.09"))
        {
            var ws=Path.Combine(root,"106","_WSMaps.big");
            if(Big(ws))files["_WSMaps.big"]=Custom(root,"106","_WSMaps.big",ws);
        }
        return files;
    }
    static Dictionary<string,string> OwnArchives(string root,Patch patch,Options options)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var source=Source(root,patch.Source);
        if(source==null)return result;
        foreach(var directory in new[]{source,Path.Combine(source,"cache")})
        {
            if(!Directory.Exists(directory))continue;
            foreach(var file in Directory.EnumerateFiles(directory))
            {
                string name=Path.GetFileName(file);if(!name.EndsWith(".big",StringComparison.OrdinalIgnoreCase)||!Big(file))continue;
                if(!options.Zoom&&(name.Equals("_WSMaps.big",StringComparison.OrdinalIgnoreCase)||name.Contains("_560",StringComparison.OrdinalIgnoreCase)))continue;
                if(!options.Floors&&name.Contains("floors",StringComparison.OrdinalIgnoreCase))continue;
                if(result.ContainsKey(name))continue;
                result[name]=Custom(root,patch.Source.Split('\\')[0],name,file);
            }
        }
        return result;
    }
    static string Custom(string root,string family,string name,string fallback)
    {var custom=Path.Combine(root,"PlayerCustomFiles",family,name);return Big(custom)?custom:fallback;}
    static Options ReadOptions(string root)
    {
        try
        {
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"PlayerCustomFiles","settings.json")));
            bool Flag(string key,bool fallback)=>json.RootElement.TryGetProperty(key,out var v)&&v.ValueKind is JsonValueKind.True or JsonValueKind.False?v.GetBoolean():fallback;
            return new(Flag("zoomout",true),Flag("floors",true));
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return new();}
    }
    static bool IsPatchArchive(string name)=>name.Contains("BT2DC",StringComparison.OrdinalIgnoreCase)||name.Contains("109floors",StringComparison.OrdinalIgnoreCase);
    static Patch[]? Chain(Patch[] catalog,Patch patch)
    {
        var chain=new List<Patch>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<64;i++)
        {
            if(!seen.Add(patch.Name))return null;chain.Add(patch);
            if(patch.Parent=="NoPatch"){chain.Reverse();return chain.ToArray();}
            var parent=catalog.FirstOrDefault(p=>p.Name.Equals(patch.Parent,StringComparison.OrdinalIgnoreCase));if(parent==null)return null;patch=parent;
        }
        return null;
    }
    static Patch[] Catalog(string[] roots)
    {
        var catalog=BuiltIn.Value.ToDictionary(p=>p.Name,StringComparer.OrdinalIgnoreCase);
        // Roots are ordered nearest-game first. Prefer that switcher's metadata
        // when another installed copy advertises the same release differently.
        foreach(var root in roots.Reverse())
        {
            string file=Path.Combine(root,"data","GUI","web_cached_data","releases_new.json");
            try
            {
                var info=new FileInfo(file);if(!info.Exists||info.Length>4*1024*1024)continue;
                if(!Manifests.TryGetValue(file,out var cached)||cached.Stamp!=info.LastWriteTimeUtc.Ticks)
                {
                    using var json=JsonDocument.Parse(File.ReadAllText(file));var patches=new List<Patch>();
                    if(json.RootElement.TryGetProperty("patches_2026",out var groups)&&groups.ValueKind==JsonValueKind.Object)
                    foreach(var group in groups.EnumerateObject())
                    {
                        if(group.Value.ValueKind!=JsonValueKind.Array)continue;
                        foreach(var entry in group.Value.EnumerateArray())
                        {
                            if(!entry.TryGetProperty("name",out var name)||!entry.TryGetProperty("source",out var source)||!entry.TryGetProperty("dependency",out var parent)||!entry.TryGetProperty("tupla",out var tuple))continue;
                            string label=name.GetString()??"",relative=source.GetString()??"",dependency=parent.GetString()??"";
                            if(label.Length is <1 or >80||relative.Length>160||dependency.Length>80||!SafeRelative(relative))continue;
                            var order=tuple.EnumerateArray().Select(x=>x.GetDouble()).ToArray();if(order.Length!=3||order.Any(x=>!double.IsFinite(x)))continue;
                            patches.Add(new(label,relative,dependency,order));
                        }
                    }
                    cached=(info.LastWriteTimeUtc.Ticks,patches.ToArray());Manifests[file]=cached;
                }
                foreach(var patch in cached.Patches)catalog[patch.Name]=patch;
            }
            catch(Exception e) when(e is IOException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException){ }
        }
        return catalog.Values.ToArray();
    }
    static string Key(string? value)
    {
        try{return Regex.Replace((value??"").Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"\s+","")
            .Replace("1.09v3.01","1.09v3.1",StringComparison.Ordinal).Replace("1.09v3.02","1.09v3.2",StringComparison.Ordinal)
            .Replace("beta","ß",StringComparison.Ordinal).Replace('β','ß');}
        catch(ArgumentException){return "";}
    }
    static string? GameDirectory(string executable)
    {try{return string.IsNullOrWhiteSpace(executable)||!File.Exists(executable)?null:Path.GetDirectoryName(Path.GetFullPath(executable));}catch(Exception e) when(e is IOException or ArgumentException or NotSupportedException){return null;}}
    static bool SafeRelative(string value)=>value.Length>0&&!Path.IsPathRooted(value)&&value.Split('\\','/').All(p=>p.Length>0&&p is not ("." or "..")&&p.IndexOfAny(Path.GetInvalidFileNameChars())<0);
    static string? Source(string root,string relative)
    {if(!SafeRelative(relative))return null;var path=Path.Combine(root,relative);return Directory.Exists(path)?path:null;}
    static string[] Roots(string? executable)
    {
        var roots=new List<string>(CommonRoots.Value);
        var game=executable==null?null:GameDirectory(executable);
        if(game!=null)
        {
            roots.Insert(0,Path.Combine(game,"BFME2 Ecth's Patch Switcher"));
            var parent=Path.GetDirectoryName(game);if(parent!=null)roots.Insert(0,Path.Combine(parent,"BFME2 Ecth's Patch Switcher"));
        }
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static string[] DiscoverRoots()
    {
        var roots=new List<string>();
        foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)})
            if(folder.Length>0)roots.Add(Path.Combine(folder,"BFME2 Ecth's Patch Switcher"));
        if(OperatingSystem.IsWindows())
        foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})
        try
        {
            using var registry=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);using var uninstall=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if(uninstall==null)continue;
            foreach(var key in uninstall.GetSubKeyNames())
            {
                using var app=uninstall.OpenSubKey(key);var name=app?.GetValue("DisplayName") as string??"";
                if(!name.Contains("BFME2",StringComparison.OrdinalIgnoreCase)||!name.Contains("Patch",StringComparison.OrdinalIgnoreCase))continue;
                if(app?.GetValue("InstallLocation") is string location)roots.Add(location);
                if(app?.GetValue("UninstallString") is string command)
                {var quoted=Regex.Match(command,"^\\\"([^\\\"]+)\\\"");var path=quoted.Success?quoted.Groups[1].Value:command;var directory=Path.GetDirectoryName(path);if(directory!=null)roots.Add(directory);}
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException){ }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static bool Big(string path)
    {
        try{using var file=File.OpenRead(path);Span<byte> header=stackalloc byte[4];return file.Read(header)==4&&(header.SequenceEqual("BIGF"u8)||header.SequenceEqual("BIG4"u8));}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){return false;}
    }
    static bool Same(string first,string second)
    {
        try
        {var a=new FileInfo(first);var b=new FileInfo(second);return a.Exists&&b.Exists&&a.Length==b.Length&&Hash(first)==Hash(second);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){return false;}
    }
    static string Hash(string path)
    {
        var info=new FileInfo(path);if(!info.Exists)return "";
        if(Digests.TryGetValue(path,out var cached)&&cached.Stamp==info.LastWriteTimeUtc.Ticks&&cached.Length==info.Length)return cached.Hash;
        using var file=File.OpenRead(path);var hash=Convert.ToHexString(SHA256.HashData(file));Digests[path]=(info.LastWriteTimeUtc.Ticks,info.Length,hash);return hash;
    }
    // Public switcher metadata is embedded so protocol validation does not depend
    // on a switcher being installed on every participant's computer.
    const string CatalogData="""
1.09v3.1|109v301\release|1.09v3|1.09,3.1,44.0
1.09v3|109v3\release|1.09v2|1.09,3.0,62.0
1.09v2|109v2|1.09|1.09,2.0,24.0
1.09|109|1.06|1.09,0,0
1.06|106|1.00|1.06,0,0
1.00|100|NoPatch|1.0,0,0
1.09u4 ß17|109u4\ß17|1.09v3.1|1.09,4,17.0
1.09v3.2 ß16|109v302\ß16|1.09v3.1|1.09,4,16.0
1.09v3.2 ß15|109v302\ß15|1.09v3.1|1.09,4,15.0
1.09v3.2 ß14|109v302\ß14|1.09v3.1|1.09,4,14.0
1.09v3.2 ß13|109v302\ß13|1.09v3.1|1.09,4,13.0
1.09v3.2 ß12|109v302\ß12|1.09v3.1|1.09,4,12.0
1.09v3.2 ß11|109v302\ß11|1.09v3.1|1.09,4,11.0
1.09v3.2 ß10|109v302\ß10|1.09v3.1|1.09,4,10.0
1.09v3.2 ß09|109v302\ß09|1.09v3.1|1.09,4,9.0
1.09v3.2 ß08|109v302\ß08|1.09v3.1|1.09,4,8.0
1.09v3.2 ß07|109v302\ß07|1.09v3.1|1.09,4,7.0
1.09v3.2 ß06|109v302\ß06|1.09v3.1|1.09,4,6.0
1.09v3.2 ß05|109v302\ß05|1.09v3.1|1.09,4,5.0
1.09v3.2 ß04|109v302\ß04|1.09v3.1|1.09,4,4.0
1.09v3.2 ß03|109v302\ß03|1.09v3.1|1.09,4,3.0
1.09v3.2 ß02|109v302\ß02|1.09v3.1|1.09,4,2.0
1.09v3.2 ß01|109v302\ß01|1.09v3.1|1.09,4,1.0
1.09v3.1 ß43|109v301\ß43|1.09v3|1.09,3.1,43.0
1.09v3.1 ß42|109v301\ß42|1.09v3|1.09,3.1,42.0
1.09v3.1 ß20|109v301\ß20|1.09v3|1.09,3.1,20.0
1.09v3.1 ß19|109v301\ß19|1.09v3|1.09,3.1,19
1.09v3.1 ß18|109v301\ß18|1.09v3|1.09,3.1,18
1.09v3.1 ß16|109v301\ß16|1.09v3|1.09,3.1,16
1.09v3.1 ß15|109v301\ß15|1.09v3|1.09,3.1,15
1.09v3.1 ß14|109v301\ß14|1.09v3|1.09,3.1,14
1.09v3.1 ß13|109v301\ß13|1.09v3|1.09,3.1,13
1.09v3.1 ß11|109v301\ß11|1.09v3|1.09,3.1,12
1.09v3.1 ß10|109v301\ß10|1.09v3|1.09,3.1,10
1.09v3.1 ß09|109v301\ß09|1.09v3|1.09,3.1,9
1.09v3.1 ß08|109v301\ß08|1.09v3|1.09,3.1,8
1.09v3.1 ß07|109v301\ß07|1.09v3|1.09,3.1,7
1.09v3.1 ß06|109v301\ß06|1.09v3|1.09,3.1,6
1.09v3.1 ß05|109v301\ß05|1.09v3|1.09,3.1,5
1.09v3.1 ß04|109v301\ß04|1.09v3|1.09,3.1,4
1.09v3.1 ß03|109v301\ß03|1.09v3|1.09,3.1,3
1.09v3.1 ß02|109v301\ß02|1.09v3|1.09,3.1,2
1.09v3.1 ß01|109v301\ß01|1.09v3|1.09,3.1,1
1.09v3 ß61.35|109v3\ß61.35|1.09v3 ß61|1.09,3.0,61.35
1.09v3 ß61.2|109v3\ß61.2|1.09v3 ß61|1.09,3.0,61.2
1.09v3 ß61.1|109v3\ß61.1|1.09v3 ß61|1.09,3.0,61.1
1.09v3 ß61|109v3\ß61|1.09v2|1.09,3.0,61
1.09v3 ß60.9|109v3\ß60.9|1.09v3 ß60|1.09,3.0,60.9
1.09v3 ß60.8|109v3\ß60.8|1.09v3 ß60|1.09,3.0,60.8
1.09v3 ß60.7|109v3\ß60.7|1.09v3 ß60|1.09,3.0,60.7
1.09v3 ß60.4|109v3\ß60.4|1.09v3 ß60|1.09,3.0,60.4
1.09v3 ß60.3|109v3\ß60.3|1.09v3 ß60|1.09,3.0,60.3
1.09v3 ß60.2|109v3\ß60.2|1.09v3 ß60|1.09,3.0,60.2
1.09v3 ß60.1|109v3\ß60.1|1.09v3 ß60|1.09,3.0,60.1
1.09v3 ß60|109v3\ß60|1.09v2|1.09,3.0,60
1.09v3 ß59|109v3\ß59|1.09v2|1.09,3.0,59
1.09v3 ß58|109v3\ß58|1.09v2|1.09,3.0,58
1.09v3 ß57|109v3\ß57|1.09v2|1.09,3.0,57
1.09v3 ß56|109v3\ß56|1.09v2|1.09,3.0,56
1.09v3 ß55|109v3\ß55|1.09v2|1.09,3.0,55
1.09v3 ß54|109v3\ß54|1.09v2|1.09,3.0,54
1.09v3 ß53|109v3\ß53|1.09v2|1.09,3.0,53
1.09v3 ß52|109v3\ß52|1.09v2|1.09,3.0,52
1.09v3 ß51|109v3\ß51|1.09v2|1.09,3.0,51
1.09v3 ß50|109v3\ß50|1.09v2|1.09,3.0,50
1.09v3 ß49|109v3\ß49|1.09v2|1.09,3.0,49
1.09v3 ß48|109v3\ß48|1.09v2|1.09,3.0,48
1.09v3 ß47|109v3\ß47|1.09v2|1.09,3.0,47
1.09v3 ß46|109v3\ß46|1.09v2|1.09,3.0,46
1.09v3 ß45.1|109v3\ß45.1|1.09v3 ß45|1.09,3.0,45.1
1.09v3 ß45|109v3\ß45|1.09v2|1.09,3.0,45
1.09v3 ß44|109v3\ß44|1.09v2|1.09,3.0,44
1.09v3 ß43|109v3\ß43|1.09v2|1.09,3.0,43
1.09v3 ß41|109v3\ß41|1.09v2|1.09,3.0,41
1.09v3 ß40.2|109v3\ß40.2|1.09v3 ß40|1.09,3.0,40.2
1.09v3 ß40.1|109v3\ß40.1|1.09v3 ß40|1.09,3.0,40.1
1.09v3 ß40|109v3\ß40|1.09v2|1.09,3.0,40
1.09v3 ß39|109v3\ß39|1.09v2|1.09,3.0,39
1.09v3 ß38|109v3\ß38|1.09v2|1.09,3.0,38
1.09v3 ß37|109v3\ß37|1.09v2|1.09,3.0,37
1.09v3 ß36|109v3\ß36|1.09v2|1.09,3.0,36
1.09v3 ß35|109v3\ß35|1.09v2|1.09,3.0,35
1.09v3 ß34|109v3\ß34|1.09v2|1.09,3.0,34
1.09v3 ß33|109v3\ß33|1.09v2|1.09,3.0,33
1.09v3 ß31|109v3\ß31|1.09v2|1.09,3.0,31
1.09v3 ß30|109v3\ß30|1.09v2|1.09,3.0,30
1.09v3 ß29|109v3\ß29|1.09v2|1.09,3.0,29
v2 ß21.12|109v2betas\ss21.12|v2 ß21.1|1.09,2.0,21.12
v2 ß21.11|109v2betas\ss21.11|v2 ß21.1|1.09,2.0,21.11
v2 ß21.1|109v2betas\ss21.1|v2 ß21.0|1.09,2.0,21.1
v2 ß21.09|109v2betas\ss21.09|v2 ß21.0|1.09,2.0,21.09
v2 ß21.06|109v2betas\ss21.06|v2 ß21.0|1.09,2.0,21.06
v2 ß21.05|109v2betas\ss21.05|v2 ß21.0|1.09,2.0,21.05
v2 ß21.04|109v2betas\ss21.04|v2 ß21.0|1.09,2.0,21.04
v2 ß21.03|109v2betas\ss21.03|v2 ß21.0|1.09,2.0,21.03
v2 ß21.02|109v2betas\ss21.02|v2 ß21.0|1.09,2.0,21.02
v2 ß21.01|109v2betas\ss21.01|v2 ß21.0|1.09,2.0,21.01
v2 ß21.0|109v2betas\ss21.0|1.09|1.09,2.0,21.0
v2 ß19.14|109v2betas\ss19.14|v2 ß19.1|1.09,2.0,19.14
v2 ß19.13|109v2betas\ss19.13|v2 ß19.1|1.09,2.0,19.13
v2 ß19.12|109v2betas\ss19.12|v2 ß19.1|1.09,2.0,19.12
v2 ß19.11|109v2betas\ss19.11|v2 ß19.1|1.09,2.0,19.11
v2 ß19.1|109v2betas\ss19.1|v2 ß19.0|1.09,2.0,19.1
v2 ß19.09|109v2betas\ss19.09|v2 ß19.0|1.09,2.0,19.09
v2 ß19.07|109v2betas\ss19.07|v2 ß19.0|1.09,2.0,19.07
v2 ß19.06|109v2betas\ss19.06|v2 ß19.0|1.09,2.0,19.06
v2 ß19.05|109v2betas\ss19.05|v2 ß19.0|1.09,2.0,19.05
v2 ß19.04|109v2betas\ss19.04|v2 ß19.0|1.09,2.0,19.04
v2 ß19.03|109v2betas\ss19.03|v2 ß19.0|1.09,2.0,19.03
v2 ß19.02|109v2betas\ss19.02|v2 ß19.0|1.09,2.0,19.02
v2 ß19.01|109v2betas\ss19.01|v2 ß19.0|1.09,2.0,19.01
v2 ß19.0|109v2betas\ss19.0|1.09|1.09,2.0,19.0
v2 ß18.07|109v2betas\ss18.07|v2 ß18.0|1.09,2.0,18.07
v2 ß18.06|109v2betas\ss18.06|v2 ß18.0|1.09,2.0,18.06
v2 ß18.051|109v2betas\ss18.051|v2 ß18.05|1.09,2.0,18.051
v2 ß18.05|109v2betas\ss18.05|v2 ß18.0|1.09,2.0,18.05
v2 ß18.04|109v2betas\ss18.04|v2 ß18.0|1.09,2.0,18.04
v2 ß18.032|109v2betas\ss18.032|v2 ß18.03|1.09,2.0,18.032
v2 ß18.031|109v2betas\ss18.031|v2 ß18.03|1.09,2.0,18.031
v2 ß18.03|109v2betas\ss18.03|v2 ß18.0|1.09,2.0,18.03
v2 ß18.02|109v2betas\ss18.02|v2 ß18.0|1.09,2.0,18.02
v2 ß18.01|109v2betas\ss18.01|v2 ß18.0|1.09,2.0,18.01
v2 ß18.0|109v2betas\ss18.0|1.09|1.09,2.0,18.0
v2 ß17.09|109v2betas\ss17.09|v2 ß17.0|1.09,2.0,17.09
v2 ß17.08|109v2betas\ss17.08|v2 ß17.0|1.09,2.0,17.08
v2 ß17.07|109v2betas\ss17.07|v2 ß17.0|1.09,2.0,17.07
v2 ß17.06|109v2betas\ss17.06|v2 ß17.0|1.09,2.0,17.06
v2 ß17.05|109v2betas\ss17.05|v2 ß17.0|1.09,2.0,17.05
v2 ß17.04|109v2betas\ss17.04|v2 ß17.0|1.09,2.0,17.04
v2 ß17.03|109v2betas\ss17.03|v2 ß17.0|1.09,2.0,17.03
v2 ß17.02|109v2betas\ss17.02|v2 ß17.0|1.09,2.0,17.02
v2 ß17.01|109v2betas\ss17.01|v2 ß17.0|1.09,2.0,17.01
v2 ß17.0|109v2betas\ss17.0|1.09|1.09,2.0,17.0
v2 ß16.06|109v2betas\ss16.06|v2 ß16.0|1.09,2.0,16.06
v2 ß16.05|109v2betas\ss16.05|v2 ß16.0|1.09,2.0,16.05
v2 ß16.04|109v2betas\ss16.04|v2 ß16.0|1.09,2.0,16.04
v2 ß16.03|109v2betas\ss16.03|v2 ß16.0|1.09,2.0,16.03
v2 ß16.02|109v2betas\ss16.02|v2 ß16.0|1.09,2.0,16.02
v2 ß16.01|109v2betas\ss16.01|v2 ß16.0|1.09,2.0,16.01
v2 ß16.0|109v2betas\ss16.0|1.09|1.09,2.0,16.0
v2 ß15.09|109v2betas\ss15.09|v2 ß15.0|1.09,2.0,15.09
v2 ß15.08|109v2betas\ss15.08|v2 ß15.0|1.09,2.0,15.08
v2 ß15.07|109v2betas\ss15.07|v2 ß15.0|1.09,2.0,15.07
v2 ß15.06|109v2betas\ss15.06|v2 ß15.0|1.09,2.0,15.06
v2 ß15.05|109v2betas\ss15.05|v2 ß15.0|1.09,2.0,15.05
v2 ß15.04|109v2betas\ss15.04|v2 ß15.0|1.09,2.0,15.04
v2 ß15.03|109v2betas\ss15.03|v2 ß15.0|1.09,2.0,15.03
v2 ß15.02|109v2betas\ss15.02|v2 ß15.0|1.09,2.0,15.02
v2 ß15.01|109v2betas\ss15.01|v2 ß15.0|1.09,2.0,15.01
v2 ß15.0|109v2betas\ss15.0|1.09|1.09,2.0,15.0
v2 ß14.08|109v2betas\ss14.08|v2 ß14.0|1.09,2.0,14.08
v2 ß14.07|109v2betas\ss14.07|v2 ß14.0|1.09,2.0,14.07
v2 ß14.06|109v2betas\ss14.06|v2 ß14.0|1.09,2.0,14.06
v2 ß14.05|109v2betas\ss14.05|v2 ß14.0|1.09,2.0,14.05
v2 ß14.04|109v2betas\ss14.04|v2 ß14.0|1.09,2.0,14.04
v2 ß14.03|109v2betas\ss14.03|v2 ß14.0|1.09,2.0,14.03
v2 ß14.02|109v2betas\ss14.02|v2 ß14.0|1.09,2.0,14.02
v2 ß14.01|109v2betas\ss14.01|v2 ß14.0|1.09,2.0,14.01
v2 ß14.0|109v2betas\ss14.0|1.09|1.09,2.0,14.0
v2 ß13.06|109v2betas\ss13.06|v2 ß13.0|1.09,2.0,13.06
v2 ß13.05|109v2betas\ss13.05|v2 ß13.0|1.09,2.0,13.05
v2 ß13.04|109v2betas\ss13.04|v2 ß13.0|1.09,2.0,13.04
v2 ß13.03|109v2betas\ss13.03|v2 ß13.0|1.09,2.0,13.03
v2 ß13.02|109v2betas\ss13.02|v2 ß13.0|1.09,2.0,13.02
v2 ß13.01|109v2betas\ss13.01|v2 ß13.0|1.09,2.0,13.01
v2 ß13.0|109v2betas\ss13.0|1.09|1.09,2.0,13.0
v2 ß12.09|109v2betas\ss12.09|v2 ß12.0|1.09,2.0,12.09
v2 ß12.08|109v2betas\ss12.08|v2 ß12.0|1.09,2.0,12.08
v2 ß12.07|109v2betas\ss12.07|v2 ß12.0|1.09,2.0,12.07
v2 ß12.06|109v2betas\ss12.06|v2 ß12.0|1.09,2.0,12.06
v2 ß12.05|109v2betas\ss12.05|v2 ß12.0|1.09,2.0,12.05
v2 ß12.04|109v2betas\ss12.04|v2 ß12.0|1.09,2.0,12.04
v2 ß12.03|109v2betas\ss12.03|v2 ß12.0|1.09,2.0,12.03
v2 ß12.02|109v2betas\ss12.02|v2 ß12.0|1.09,2.0,12.02
v2 ß12.01|109v2betas\ss12.01|v2 ß12.0|1.09,2.0,12.01
v2 ß12.0|109v2betas\ss12.0|1.09|1.09,2.0,12.0
v2 ß11.05|109v2betas\ss11.05|v2 ß11.0|1.09,2.0,11.05
v2 ß11.04|109v2betas\ss11.04|v2 ß11.0|1.09,2.0,11.04
v2 ß11.03|109v2betas\ss11.03|v2 ß11.0|1.09,2.0,11.03
v2 ß11.02|109v2betas\ss11.02|v2 ß11.0|1.09,2.0,11.02
v2 ß11.01|109v2betas\ss11.01|v2 ß11.0|1.09,2.0,11.01
v2 ß11.0|109v2betas\ss11.0|1.09|1.09,2.0,11.0
v2 ß10.06|109v2betas\ss10.06|v2 ß10.0|1.09,2.0,10.06
v2 ß10.05|109v2betas\ss10.05|v2 ß10.0|1.09,2.0,10.05
v2 ß10.04|109v2betas\ss10.04|v2 ß10.0|1.09,2.0,10.04
v2 ß10.03|109v2betas\ss10.03|v2 ß10.0|1.09,2.0,10.03
v2 ß10.02|109v2betas\ss10.02|v2 ß10.0|1.09,2.0,10.02
v2 ß10.01|109v2betas\ss10.01|v2 ß10.0|1.09,2.0,10.01
v2 ß10.0|109v2betas\ss10.0|1.09|1.09,2.0,10.0
v2 ß9.04|109v2betas\ss9.04|v2 ß9.0|1.09,2.0,9.04
v2 ß9.03|109v2betas\ss9.03|v2 ß9.0|1.09,2.0,9.03
v2 ß9.02|109v2betas\ss9.02|v2 ß9.0|1.09,2.0,9.02
v2 ß9.01|109v2betas\ss9.01|v2 ß9.0|1.09,2.0,9.01
v2 ß9.0|109v2betas\ss9.0|1.09|1.09,2.0,9.0
v2 ß8.02|109v2betas\ss8.02|v2 ß8.0|1.09,2.0,8.02
v2 ß8.01|109v2betas\ss8.01|v2 ß8.0|1.09,2.0,8.01
v2 ß8.0|109v2betas\ss8.0|v2 ß5.0|1.09,2.0,8.0
v2 ß7.0|109v2betas\ss7.0|v2 ß5.0|1.09,2.0,7.0
v2 ß6.01|109v2betas\ss6.01|v2 ß6.0|1.09,2.0,6.01
v2 ß6.0|109v2betas\ss6.0|v2 ß5.0|1.09,2.0,6.0
v2 ß5.0|109v2betas\ss5.0|1.09|1.09,2.0,5.0
v2 ß4.01|109v2betas\ss4.01|v2 ß4.0|1.09,2.0,4.01
v2 ß4.0|109v2betas\ss4.0|v2 ß1.0|1.09,2.0,4.0
v2 ß3.0|109v2betas\ss3.0|v2 ß1.0|1.09,2.0,3.0
v2 ß2.01|109v2betas\ss2.01|v2 ß2.0|1.09,2.0,2.01
v2 ß2.0|109v2betas\ss2.0|v2 ß1.0|1.09,2.0,2.0
v2 ß1.0|109v2betas\ss1.0|1.09|1.09,2.0,1.0
1.08|BT2DC\108|1.06|1.08,0,0
1.07|BT2DC\107|1.06|1.07,0,0
BT2DCv3.3|BT2DC\3.3|1.06|1.065,3.3,0
BT2DCv3.2|BT2DC\3.2|1.06|1.065,3.2,0
BT2DCv3.1|BT2DC\3.1|1.06|1.065,3.1,0
BT2DCv3.0|BT2DC\3.0|1.06|1.065,3.0,0
BT2DCv2.6|BT2DC\2.6|1.06|1.065,2.6,0
BT2DCv2.5|BT2DC\2.5|1.06|1.065,2.5,0
BT2DCv2.4|BT2DC\2.4|1.06|1.065,2.4,0
BT2DCv2.3|BT2DC\2.3|1.06|1.065,2.3,0
BT2DCv2.2|BT2DC\2.2|1.06|1.065,2.2,0
BT2DCv2.1|BT2DC\2.1|1.06|1.065,2.1,0
BT2DCv2.0|BT2DC\2.0|1.06|1.065,2.0,0
BT2DCv1.7|BT2DC\1.7|1.06|1.065,1.7,0
BT2DCv1.6|BT2DC\1.6|1.06|1.065,1.6,0
BT2DCv1.5|BT2DC\1.5|1.06|1.065,1.5,0
BT2DCv1.4|BT2DC\1.4|1.06|1.065,1.4,0
BT2DCv1.3|BT2DC\1.3|1.06|1.065,1.3,0
BT2DCv1.2|BT2DC\1.2|1.06|1.065,1.2,0
BT2DCv1.1|BT2DC\1.1|1.06|1.065,1.1,0
BT2DCv1.0|BT2DC\1.0|1.06|1.065,1.0,0
""";
}
