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

// Experimental reader of the same unauthenticated GET resources used by DAK.GG's
// public profile page. This is NOT a supported public developer API. No login,
// cookies, API keys, synchronization RPCs, challenge bypasses, or bulk crawling.
internal sealed class EternalDakProvider:IEternalReturnDataProvider {
    readonly HttpClient http;readonly string metadataPath;
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024,RecursionLimit=100};
    readonly SemaphoreSlim gate=new SemaphoreSlim(1);
    DateTime next=DateTime.MinValue;internal int SpacingMs=1200;
    DakMetadata metadata;
    internal sealed class DakMetadata {public string checkedAt;public object seasons,tiers,characters;}
    internal EternalDakProvider(string root,HttpMessageHandler handler=null){
        Directory.CreateDirectory(root);metadataPath=Path.Combine(root,"eternal-dak-metadata.json");
        http=new HttpClient(new ProviderHttpPolicy("dakgg",root,handler)){Timeout=TimeSpan.FromSeconds(20)};
        try{metadata=json.Deserialize<DakMetadata>(File.ReadAllText(metadataPath));}catch{}
    }
    internal static string Normalize(string input){
        string name=(input??"").Trim();Uri uri;
        if(name.StartsWith("https://",StringComparison.OrdinalIgnoreCase)){
            if(!Uri.TryCreate(name,UriKind.Absolute,out uri)||uri.Host!="dak.gg"||!uri.IsDefaultPort||uri.UserInfo.Length>0)throw Input();
            var match=Regex.Match(uri.AbsolutePath,"^/er/players/([^/]+)/?$");if(!match.Success)throw Input();name=Uri.UnescapeDataString(match.Groups[1].Value);
        }
        if(name.Length<1||name.Length>80||name.Any(char.IsControl)||name.IndexOfAny(new[]{'/','\\','?','#'})>=0||name.Contains("://"))throw Input();
        return name;
    }
    static ErFailure Input(){return new ErFailure("input","이터널 리턴 닉네임 또는 닥지지 프로필 주소를 입력해 주세요.");}
    static ErFailure Format(){return new ErFailure("format","닥지지 응답 형식이 바뀌었거나 기록이 불완전해요. 마지막 정상 기록을 유지해요.",900);}
    internal static List<Dictionary<string,object>> Rows(object obj,string field,bool optional=false){
        var raw=ErJson.Get(obj,field);if(raw==null&&optional)return new List<Dictionary<string,object>>();
        if(!(raw is IEnumerable)||raw is string||raw is IDictionary)throw Format();
        var rows=new List<Dictionary<string,object>>();foreach(var value in (IEnumerable)raw){var row=value as Dictionary<string,object>;if(row==null)throw Format();rows.Add(row);}return rows;
    }
    internal static int RetryAfter(HttpResponseMessage response){var header=response.Headers.RetryAfter;double seconds=header==null?900:header.Delta.HasValue?header.Delta.Value.TotalSeconds:header.Date.HasValue?(header.Date.Value-DateTimeOffset.UtcNow).TotalSeconds:900;return (int)Math.Min(int.MaxValue,Math.Max(60,Math.Ceiling(seconds)));}
    async Task<object> Get(string path,CancellationToken caller){
        if(!path.StartsWith("/api/v1/data/",StringComparison.Ordinal)&&!path.StartsWith("/api/v1/players/",StringComparison.Ordinal))throw new InvalidOperationException();
        double delay=(next-DateTime.UtcNow).TotalMilliseconds;if(delay>0)await Task.Delay((int)Math.Min(int.MaxValue,delay),caller);next=DateTime.UtcNow.AddMilliseconds(SpacingMs);
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(caller)){
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try{using(var request=new HttpRequestMessage(HttpMethod.Get,"https://er.dakgg.io"+path)){
                request.Headers.Add("Dakgg-Language","ko");request.Headers.UserAgent.ParseAdd("NyangGaming/0.1");
                using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token)){
                    int status=(int)response.StatusCode;
                    if(status==401||status==403)throw new ErFailure("siteBlocked","닥지지에서 자동 조회를 허용하지 않아 중지했어요. 원본 사이트에서 확인해 주세요.",86400);
                    if(status==429){int retry=RetryAfter(response);next=DateTime.UtcNow.AddSeconds(retry);throw new ErFailure("rateLimit","닥지지 호출 제한이에요. 지정된 대기 시간 동안 요청하지 않아요.",retry);}
                    if(status==404)throw new ErFailure("notFound","닥지지에서 이 닉네임을 찾지 못했어요. 원본 사이트에서 이름과 공개 기록을 확인해 주세요.",300);
                    if(!response.IsSuccessStatusCode)throw new ErFailure("service","닥지지에 연결하지 못했어요. 이전 정상 기록을 유지해요.",900);
                    if(response.Content.Headers.ContentType!=null&&response.Content.Headers.ContentType.MediaType.IndexOf("json",StringComparison.OrdinalIgnoreCase)<0)throw Format();
                    using(var stream=await response.Content.ReadAsStreamAsync())using(var memory=new MemoryStream()){
                        var buffer=new byte[8192];int n;while((n=await stream.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0){if(memory.Length+n>8*1024*1024)throw Format();memory.Write(buffer,0,n);}
                        try{var parsed=json.DeserializeObject(Encoding.UTF8.GetString(memory.ToArray()));if(!(parsed is Dictionary<string,object>))throw Format();return ProviderHttpPolicy.Stamp(parsed,response);}catch(ErFailure){throw;}catch{throw Format();}
                    }
                }
            }}catch(OperationCanceledException){if(caller.IsCancellationRequested)throw;throw new ErFailure("network","닥지지 응답 시간이 초과되었어요. 이전 정상 기록을 유지해요.",900);}
            catch(HttpRequestException){throw new ErFailure("network","인터넷 또는 닥지지 연결을 확인해 주세요.",900);}
        }
    }
    async Task<DakMetadata> Metadata(CancellationToken token){
        DateTime timestamp;if(metadata!=null&&DateTime.TryParse(metadata.checkedAt,null,DateTimeStyles.RoundtripKind,out timestamp)&&DateTime.UtcNow<timestamp.ToUniversalTime().AddHours(6))return metadata;
        var value=new DakMetadata{seasons=await Get("/api/v1/data/seasons?hl=ko",token),tiers=await Get("/api/v1/data/tiers?hl=ko",token),characters=await Get("/api/v1/data/characters?hl=ko",token),checkedAt=DateTime.UtcNow.ToString("o")};
        CurrentSeason(value.seasons);Rows(value.tiers,"tiers");Rows(value.characters,"characters");metadata=value;
        // Public metadata cache is optional; failure must not discard player results.
        try{LeagueJson.Atomic(metadataPath,json.Serialize(value));}catch{}return value;
    }
    internal static Dictionary<string,object> CurrentSeason(object seasons){
        var current=Rows(seasons,"seasons").Where(s=>ErJson.Get(s,"isCurrent") is bool&&(bool)ErJson.Get(s,"isCurrent")).ToList();
        if(current.Count!=1||!ErJson.Int(current[0],"id").HasValue||!Regex.IsMatch(ErJson.Text(current[0],"key"),"^(PRE_)?SEASON_[0-9]+$"))throw Format();return current[0];
    }
    internal static string Timestamp(object obj,string key){long milliseconds=ErJson.Long(obj,key);if(milliseconds<=0)return null;try{return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(milliseconds).ToString("o");}catch{return null;}}
    internal static ErData Parse(string query,object profile,object history,DakMetadata meta){
        var season=CurrentSeason(meta.seasons);int seasonId=ErJson.Int(season,"id").Value;string seasonKey=ErJson.Text(season,"key");
        var player=ErJson.Get(profile,"player");string found=ErJson.Text(player,"name");
        if(!string.Equals(found,query,StringComparison.OrdinalIgnoreCase)||ErJson.Text(ErJson.Get(profile,"meta"),"season")!=seasonKey||ErJson.Text(ErJson.Get(history,"meta"),"season")!=seasonKey)throw Format();
        var seasons=Rows(profile,"playerSeasons");var rank=seasons.SingleOrDefault(s=>ErJson.Int(s,"seasonId")==seasonId);
        var overview=Rows(profile,"playerSeasonOverviews",true).SingleOrDefault(s=>ErJson.Int(s,"seasonId")==seasonId&&ErJson.Int(s,"matchingModeId")==3&&ErJson.Int(s,"teamModeId")==3);
        if(overview!=null)rank=overview;
        var tiers=Rows(meta.tiers,"tiers");var tier=rank==null?null:tiers.FirstOrDefault(t=>ErJson.Int(t,"id")==ErJson.Int(rank,"tierId"));
        string tierName=rank==null?"현재 시즌 랭크 기록 없음":tier==null?null:ErJson.Text(tier,"name");int? grade=ErJson.Int(rank,"tierGradeId");if(tier!=null&&grade.HasValue&&grade.Value>0&&ErJson.Int(tier,"id")>0&&ErJson.Int(tier,"id")<=6)tierName+=" "+grade.Value;
        var result=new ErData{nickname=found,source="dak",sourceUrl="https://dak.gg/er/players/"+Uri.EscapeDataString(found)+"?hl=ko",sourceUpdatedAt=Timestamp(player,"syncedAt"),seasonId=seasonId,seasonName=ErJson.Text(season,"name"),rp=ErJson.Int(rank,"mmr"),tier=tierName,seasonGames=ErJson.Int(overview,"play"),seasonWins=ErJson.Int(overview,"win"),ranking=ErJson.Int(ErJson.Get(ErJson.Get(overview,"rank"),"global"),"rank"),rankMessage="닥지지에 저장된 현재 시즌 스쿼드 랭크 기록이에요. 사이트 갱신 전에는 이전 값일 수 있어요."};
        var names=new Dictionary<int,string>();foreach(var c in Rows(meta.characters,"characters")){int? id=ErJson.Int(c,"id");string name=ErJson.Text(c,"name");if(id.HasValue&&!string.IsNullOrEmpty(name))names[id.Value]=name;}
        var matches=Rows(history,"matches");
        foreach(var row in matches.Take(20)){
            if(ErJson.Long(row,"gameId")<=0||(ErJson.Int(row,"seasonId")!=seasonId&&!(ErJson.Int(row,"seasonId")==0&&ErJson.Int(row,"matchingMode")!=3))||!string.Equals(ErJson.Text(row,"nickname"),found,StringComparison.OrdinalIgnoreCase))throw Format();
            var match=EternalReturnApiProvider.ParseMatch(row,names);if(!match.mode.HasValue)throw Format();result.matches.Add(match);
        }
        result.matches=result.matches.GroupBy(m=>m.id).Select(g=>g.First()).OrderByDescending(m=>m.id).ToList();result.updatedAt=DateTime.UtcNow.ToString("o");return result;
    }
    public async Task<ErData> Read(string ignoredKey,string nickname,CancellationToken token){
        string name=Normalize(nickname);await gate.WaitAsync(token);
        try{
            var meta=await Metadata(token);string season=ErJson.Text(CurrentSeason(meta.seasons),"key"),encoded=Uri.EscapeDataString(name);
            var profile=await Get("/api/v1/players/"+encoded+"/profile?season="+season,token);
            var history=await Get("/api/v1/players/"+encoded+"/matches?season="+season+"&matchingMode=ALL&teamMode=ALL&page=1",token);
            var result=Parse(name,profile,history,meta);result.updatedAt=ProviderHttpPolicy.DataTime(profile);return result;
        }finally{gate.Release();}
    }
    public void Dispose(){http.Dispose();}
}
