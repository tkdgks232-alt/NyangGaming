using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class LeagueIdentity { public string riotId,puuid,platform; }
internal sealed class LeagueRank {
    public string queue,tier="Unranked",division="";
    public int? points,wins,losses;
    public double? winRate;
}
internal sealed class LeagueMatch {
    public string id,result,characterName,imageUrl,playedAt,queue;
    public int championId,kills,deaths,assists,cs,queueId;
    public double duration;
    public bool win;
}
internal sealed class LeagueChampion {
    public string name,imageUrl; public int id,games,wins,losses;
    public double winRate,kills,deaths,assists,cs;
}
internal sealed class LeagueData {
    public string riotId,updatedAt,assetMessage,source="api",sourceUpdatedAt;
    public long level;
    public LeagueRank[] ranks=new LeagueRank[0];
    public LeagueMatch[] matches=new LeagueMatch[0];
    public LeagueChampion[] characters=new LeagueChampion[0];
}
internal sealed class LeagueResult { internal LeagueIdentity identity; internal LeagueData data; }
internal sealed class LeagueFailure:Exception {
    internal readonly string State;internal readonly int WaitSeconds;
    internal LeagueFailure(string state,string message,int wait=120):base(message){State=state;WaitSeconds=wait;}
}
internal interface ILeagueProvider:IDisposable {
    Task<LeagueResult> Read(string key,LeagueIdentity identity,LeagueData previous,CancellationToken token);
}
internal static class LeagueJson {
    internal static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=8*1024*1024};}
    internal static Dictionary<string,object> Obj(object value){var d=value as Dictionary<string,object>;if(d==null)throw Bad();return d;}
    internal static object Get(object value,string key){object found;return Obj(value).TryGetValue(key,out found)?found:null;}
    internal static string Text(object value,string key){return Convert.ToString(Get(value,key),CultureInfo.InvariantCulture);}
    internal static long Num(object value,string key){long n;if(!long.TryParse(Text(value,key),out n))throw Bad();return n;}
    internal static object[] Array(object value){var list=value as IEnumerable;if(value==null||value is string||value is IDictionary||list==null)throw Bad();return list.Cast<object>().ToArray();}
    internal static LeagueFailure Bad(){return new LeagueFailure("response","Riot 응답 형식이 예상과 달라요. 마지막 정상 기록을 유지해요.");}
    internal static void Atomic(string path,string value){AtomicBytes(path,Encoding.UTF8.GetBytes(value));}
    internal static void AtomicBytes(string path,byte[] value){string tmp=path+".tmp";File.WriteAllBytes(tmp,value);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
}

// Only official HTTPS endpoints. Token is attached per Riot request, never to CDN or WebView.
internal sealed class RiotApiProvider:ILeagueProvider {
    readonly HttpClient http;readonly string folder,metadataPath;
    readonly JavaScriptSerializer json=LeagueJson.Serializer();
    readonly SemaphoreSlim serial=new SemaphoreSlim(1);
    internal int SpacingMs=1500;
    DateTime next=DateTime.MinValue,metadataDue=DateTime.MinValue;
    string rankFetched;
    ChampionMetadata metadata;
    internal sealed class ChampionMetadata { public string version,checkedAt; public Dictionary<string,ChampionAsset> champions=new Dictionary<string,ChampionAsset>(); }
    internal sealed class ChampionAsset { public string name,file; }
    internal RiotApiProvider(string root,HttpMessageHandler handler=null){
        folder=Path.Combine(root,"LeagueImages");Directory.CreateDirectory(folder);metadataPath=Path.Combine(root,"league-champions.json");
        http=new HttpClient(new ProviderHttpPolicy("riot",root,handler)){Timeout=TimeSpan.FromSeconds(20)};
        try{metadata=json.Deserialize<ChampionMetadata>(File.ReadAllText(metadataPath));DateTime checkedAt;if(metadata!=null&&DateTime.TryParse(metadata.checkedAt,null,DateTimeStyles.RoundtripKind,out checkedAt))metadataDue=checkedAt.ToUniversalTime().AddHours(12);}catch{metadata=null;}
    }
    internal static string Region(string platform){switch(platform){case "KR":case "JP1":return "asia";case "NA1":case "BR1":case "LA1":case "LA2":return "americas";case "EUW1":case "EUN1":case "TR1":case "RU":return "europe";default:throw new LeagueFailure("input","지원하는 서버를 선택해 주세요.",0);}}
    internal static bool ValidPlatform(string p){try{Region(p);return true;}catch{return false;}}
    internal static bool ValidId(string id){if(string.IsNullOrWhiteSpace(id)||id.Any(char.IsControl))return false;var parts=id.Split('#');return parts.Length==2&&parts[0].Trim().Length>=1&&parts[0].Length<=32&&parts[1].Trim().Length>=1&&parts[1].Length<=16;}
    static string Enc(string s){return Uri.EscapeDataString(s);}
    internal static int RetryAfter(HttpResponseMessage response){
        var r=response.Headers.RetryAfter;if(r==null)return 120;
        double seconds=r.Delta.HasValue?r.Delta.Value.TotalSeconds:r.Date.HasValue?(r.Date.Value-DateTimeOffset.UtcNow).TotalSeconds:120;
        return (int)Math.Min(int.MaxValue,Math.Max(1,Math.Ceiling(seconds)));
    }
    async Task<byte[]> Get(string url,string key,CancellationToken token){
        var uri=new Uri(url);bool riot=uri.Host.EndsWith(".api.riotgames.com",StringComparison.Ordinal)&&uri.Scheme=="https";
        if(!riot&&(uri.Host!="ddragon.leagueoflegends.com"||uri.Scheme!="https"))throw new InvalidOperationException();
        await serial.WaitAsync(token);
        try{
            if(riot){double delay=(next-DateTime.UtcNow).TotalMilliseconds;if(delay>0)await Task.Delay((int)Math.Min(delay,int.MaxValue),token);next=DateTime.UtcNow.AddMilliseconds(SpacingMs);}
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)){
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using(var req=new HttpRequestMessage(HttpMethod.Get,uri)){
                    if(riot)req.Headers.Add("X-Riot-Token",key);
                    using(var response=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,timeout.Token)){
                        if(uri.AbsolutePath.Contains("/league/v4/"))rankFetched=ProviderHttpPolicy.Fetched(response);int code=(int)response.StatusCode;
                        if(code==401||code==403)throw new LeagueFailure("key","Riot API 키가 만료되었거나 권한이 없어요. 개발자 포털에서 키를 확인하고 다시 등록해 주세요.",900);
                        if(code==429){int wait=RetryAfter(response);next=DateTime.UtcNow.AddSeconds(wait);throw new LeagueFailure("rateLimit","Riot 호출 제한에 도달했어요. 서버가 지정한 대기 시간 후 다시 확인해요.",wait);}
                        if(code==404)throw new LeagueFailure("notFound","계정 또는 경기 기록을 찾지 못했어요. Riot ID와 서버를 확인해 주세요.",60);
                        if(!response.IsSuccessStatusCode)throw new LeagueFailure("service","Riot 서비스가 응답하지 않아요. 마지막 정상 기록을 유지해요.");
                        using(var stream=await response.Content.ReadAsStreamAsync())using(var memory=new MemoryStream()){
                            var buffer=new byte[8192];int n;while((n=await stream.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0){if(memory.Length+n>8*1024*1024)throw LeagueJson.Bad();memory.Write(buffer,0,n);}return memory.ToArray();
                        }
                    }
                }
            }
        }catch(OperationCanceledException){if(token.IsCancellationRequested)throw;throw new LeagueFailure("network","요청 시간이 초과되었어요. 마지막 정상 기록을 유지해요.");}
        catch(HttpRequestException){throw new LeagueFailure("network","인터넷 연결 또는 Riot 서비스 상태를 확인해 주세요.");}
        finally{serial.Release();}
    }
    async Task<object> Json(string url,string key,CancellationToken token){var bytes=await Get(url,key,token);try{return json.DeserializeObject(Encoding.UTF8.GetString(bytes));}catch{throw LeagueJson.Bad();}}
    internal static LeagueRank[] ParseRanks(object response){
        var rows=LeagueJson.Array(response);var list=new List<LeagueRank>();
        foreach(string q in new[]{"RANKED_SOLO_5x5","RANKED_FLEX_SR"}){
            var row=rows.FirstOrDefault(r=>LeagueJson.Text(r,"queueType")==q);var rank=new LeagueRank{queue=q};
            if(row!=null){rank.tier=LeagueJson.Text(row,"tier");rank.division=LeagueJson.Text(row,"rank");if(rank.tier.Length==0||rank.division.Length==0)throw LeagueJson.Bad();rank.points=checked((int)LeagueJson.Num(row,"leaguePoints"));rank.wins=checked((int)LeagueJson.Num(row,"wins"));rank.losses=checked((int)LeagueJson.Num(row,"losses"));int total=rank.wins.Value+rank.losses.Value;rank.winRate=total>0?(double?)Math.Round(100.0*rank.wins.Value/total,1):null;}
            list.Add(rank);
        }return list.ToArray();
    }
    internal static string Queue(int id){switch(id){case 420:return "솔로/듀오 랭크";case 440:return "자유 랭크";case 450:return "무작위 총력전";case 400:return "일반 드래프트";case 430:return "일반 선택";case 490:return "빠른 대전";case 1700:case 1710:return "아레나";default:return "Queue "+id;}}
    internal static LeagueMatch ParseMatch(object raw,string id,string puuid){
        var info=LeagueJson.Get(raw,"info");
        if(LeagueJson.Text(LeagueJson.Get(raw,"metadata"),"matchId")!=id)throw LeagueJson.Bad();
        // Custom games are not displayed without an explicit opt-in.
        if(LeagueJson.Text(info,"gameType")=="CUSTOM_GAME"||LeagueJson.Num(info,"queueId")==0)return null;
        var p=LeagueJson.Array(LeagueJson.Get(info,"participants")).FirstOrDefault(r=>LeagueJson.Text(r,"puuid")==puuid);if(p==null)throw LeagueJson.Bad();
        var won=LeagueJson.Get(p,"win");if(!(won is bool))throw LeagueJson.Bad();
        long start=LeagueJson.Num(info,"gameStartTimestamp"),duration=LeagueJson.Num(info,"gameDuration");
        // Pre-11.20 match-v5 duration was milliseconds. Prefer actual end/start when supplied.
        long end;if(long.TryParse(LeagueJson.Text(info,"gameEndTimestamp"),out end)&&end>=start)duration=(end-start)/1000;else if(duration>86400)duration/=1000;
        int q=checked((int)LeagueJson.Num(info,"queueId"));
        return new LeagueMatch{id=id,win=(bool)won,result=(bool)won?"승리":"패배",championId=checked((int)LeagueJson.Num(p,"championId")),characterName=LeagueJson.Text(p,"championName"),kills=checked((int)LeagueJson.Num(p,"kills")),deaths=checked((int)LeagueJson.Num(p,"deaths")),assists=checked((int)LeagueJson.Num(p,"assists")),cs=checked((int)(LeagueJson.Num(p,"totalMinionsKilled")+LeagueJson.Num(p,"neutralMinionsKilled"))),duration=duration,playedAt=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(start).ToString("o"),queueId=q,queue=Queue(q)};
    }
    internal static LeagueChampion[] Aggregate(IEnumerable<LeagueMatch> matches){return matches.GroupBy(m=>m.championId).Select(g=>new LeagueChampion{id=g.Key,name=g.First().characterName,imageUrl=g.First().imageUrl,games=g.Count(),wins=g.Count(m=>m.win),losses=g.Count(m=>!m.win),winRate=Math.Round(g.Count(m=>m.win)*100.0/g.Count(),1),kills=Math.Round(g.Average(m=>m.kills),1),deaths=Math.Round(g.Average(m=>m.deaths),1),assists=Math.Round(g.Average(m=>m.assists),1),cs=Math.Round(g.Average(m=>m.cs),1)}).OrderByDescending(c=>c.games).ThenBy(c=>c.name).ToArray();}
    async Task Assets(LeagueData data,CancellationToken token){
        bool failed=false;
        try{
            if(metadata==null||DateTime.UtcNow>=metadataDue){
                var versions=LeagueJson.Array(await Json("https://ddragon.leagueoflegends.com/api/versions.json",null,token));string version=Convert.ToString(versions.FirstOrDefault());if(!Regex.IsMatch(version??"","^[0-9]+\\.[0-9]+\\.[0-9]+$"))throw LeagueJson.Bad();
                var rows=LeagueJson.Obj(LeagueJson.Get(await Json("https://ddragon.leagueoflegends.com/cdn/"+version+"/data/ko_KR/champion.json",null,token),"data"));
                var loaded=new ChampionMetadata{version=version,checkedAt=DateTime.UtcNow.ToString("o")};
                foreach(var row in rows.Values){int id;if(!int.TryParse(LeagueJson.Text(row,"key"),out id))continue;string file=LeagueJson.Text(LeagueJson.Get(row,"image"),"full");if(!Regex.IsMatch(file,"^[A-Za-z0-9_]+\\.png$"))continue;loaded.champions[id.ToString(CultureInfo.InvariantCulture)]=new ChampionAsset{name=LeagueJson.Text(row,"name"),file=file};}
                if(loaded.champions.Count==0)throw LeagueJson.Bad();LeagueJson.Atomic(metadataPath,json.Serialize(loaded));metadata=loaded;metadataDue=DateTime.UtcNow.AddHours(12);
            }
        }catch(OperationCanceledException){throw;}catch{failed=true;metadataDue=DateTime.UtcNow.AddMinutes(5);}
        foreach(var group in data.matches.GroupBy(m=>m.championId)){
            ChampionAsset asset;if(metadata==null||!metadata.champions.TryGetValue(group.Key.ToString(CultureInfo.InvariantCulture),out asset)){failed=true;continue;}
            string file=metadata.version+"-"+asset.file,path=Path.Combine(folder,file),local=null;
            try{
                if(!File.Exists(path)){
                    var bytes=await Get("https://ddragon.leagueoflegends.com/cdn/"+metadata.version+"/img/champion/"+asset.file,null,token);
                    using(var mem=new MemoryStream(bytes))using(var image=System.Drawing.Image.FromStream(mem)){if(image.Width>1024||image.Height>1024)throw LeagueJson.Bad();}
                    LeagueJson.AtomicBytes(path,bytes);
                }local="https://league.korugaming.example/"+file;
            }catch(OperationCanceledException){throw;}catch{failed=true;}
            foreach(var match in group){match.characterName=asset.name;match.imageUrl=local;}
        }
        if(failed)data.assetMessage="챔피언 이미지/번역 일부를 불러오지 못했어요. 경기 기록은 실제 Riot 데이터예요.";
    }
    public async Task<LeagueResult> Read(string key,LeagueIdentity identity,LeagueData previous,CancellationToken token){
        string regional="https://"+Region(identity.platform)+".api.riotgames.com",platform="https://"+identity.platform.ToLowerInvariant()+".api.riotgames.com";
        string path;if(string.IsNullOrEmpty(identity.puuid)){if(!ValidId(identity.riotId))throw new LeagueFailure("input","GameName#TagLine 형식의 Riot ID를 입력해 주세요.",0);var parts=identity.riotId.Split('#');path="/riot/account/v1/accounts/by-riot-id/"+Enc(parts[0].Trim())+"/"+Enc(parts[1].Trim());}else path="/riot/account/v1/accounts/by-puuid/"+Enc(identity.puuid);
        var account=await Json(regional+path,key,token);string puuid=LeagueJson.Text(account,"puuid"),name=LeagueJson.Text(account,"gameName"),tag=LeagueJson.Text(account,"tagLine");if(string.IsNullOrEmpty(puuid)||string.IsNullOrEmpty(name)||string.IsNullOrEmpty(tag))throw LeagueJson.Bad();
        if(!string.IsNullOrEmpty(identity.puuid)&&puuid!=identity.puuid)throw LeagueJson.Bad();
        var summoner=await Json(platform+"/lol/summoner/v4/summoners/by-puuid/"+Enc(puuid),key,token);if(LeagueJson.Text(summoner,"puuid")!=puuid)throw LeagueJson.Bad();
        var data=new LeagueData{riotId=name+"#"+tag,level=LeagueJson.Num(summoner,"summonerLevel"),ranks=ParseRanks(await Json(platform+"/lol/league/v4/entries/by-puuid/"+Enc(puuid),key,token))};
        var ids=LeagueJson.Array(await Json(regional+"/lol/match/v5/matches/by-puuid/"+Enc(puuid)+"/ids?start=0&count=20",key,token)).Select(Convert.ToString).Distinct().Take(20).ToArray();
        var matches=new List<LeagueMatch>();
        foreach(string id in ids){
            if(!Regex.IsMatch(id??"","^[A-Z0-9]+_[0-9]+$"))throw LeagueJson.Bad();
            var cached=previous==null?null:previous.matches.FirstOrDefault(m=>m.id==id);
            var match=cached==null?ParseMatch(await Json(regional+"/lol/match/v5/matches/"+Enc(id),key,token),id,puuid):json.Deserialize<LeagueMatch>(json.Serialize(cached));if(match!=null)matches.Add(match);
        }
        data.matches=matches.ToArray();await Assets(data,token);data.characters=Aggregate(data.matches);data.updatedAt=rankFetched??DateTime.UtcNow.ToString("o");
        return new LeagueResult{identity=new LeagueIdentity{riotId=data.riotId,puuid=puuid,platform=identity.platform},data=data};
    }
    public void Dispose(){http.Dispose();}
}
