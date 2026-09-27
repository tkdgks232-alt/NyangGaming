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

// Experimental reader of the same unauthenticated GET resources used by 아이피롤's
// public profile page. This is NOT a supported public developer API. No login,
// cookies, API keys, synchronization RPCs, challenge bypasses, or bulk crawling.
internal sealed class LeagueIplolProvider:ILeagueProvider {
    readonly HttpClient http;
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024,RecursionLimit=100};
    readonly SemaphoreSlim gate=new SemaphoreSlim(1);
    DateTime next=DateTime.MinValue;internal int SpacingMs=1200;
    internal LeagueIplolProvider(string root,HttpMessageHandler handler=null){
        Directory.CreateDirectory(root);
        http=new HttpClient(new ProviderHttpPolicy("iplol",root,handler)){Timeout=TimeSpan.FromSeconds(20)};
    }
    static ErFailure Format(){return new ErFailure("format","아이피롤 응답 형식이 바뀌었거나 기록이 불완전해요. 마지막 정상 기록을 유지해요.",900);}
    internal static int RetryAfter(HttpResponseMessage response){var header=response.Headers.RetryAfter;double seconds=header==null?900:header.Delta.HasValue?header.Delta.Value.TotalSeconds:header.Date.HasValue?(header.Date.Value-DateTimeOffset.UtcNow).TotalSeconds:900;return (int)Math.Min(int.MaxValue,Math.Max(60,Math.Ceiling(seconds)));}
    async Task<object> Get(string path,CancellationToken caller){
        if(!path.StartsWith("/api/search?",StringComparison.Ordinal))throw new InvalidOperationException();
        double delay=(next-DateTime.UtcNow).TotalMilliseconds;if(delay>0)await Task.Delay((int)Math.Min(int.MaxValue,delay),caller);next=DateTime.UtcNow.AddMilliseconds(SpacingMs);
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(caller)){
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try{using(var request=new HttpRequestMessage(HttpMethod.Get,"https://iplol.kr"+path)){
                request.Headers.UserAgent.ParseAdd("NyangGaming/0.1");
                using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token)){
                    int status=(int)response.StatusCode;
                    if(status==401||status==403)throw new ErFailure("siteBlocked","아이피롤에서 자동 조회를 허용하지 않아 중지했어요. 원본 사이트에서 확인해 주세요.",86400);
                    if(status==429){int retry=RetryAfter(response);next=DateTime.UtcNow.AddSeconds(retry);throw new ErFailure("rateLimit","아이피롤 호출 제한이에요. 지정된 대기 시간 동안 요청하지 않아요.",retry);}
                    if(status==404)throw new ErFailure("notFound","아이피롤에서 이 닉네임을 찾지 못했어요. 원본 사이트에서 이름과 공개 기록을 확인해 주세요.",300);
                    if(status==202)throw new ErFailure("service","아이피롤에서 기록을 준비 중이에요. 잠시 후 다시 확인해 주세요.",900);if(!response.IsSuccessStatusCode)throw new ErFailure("service","아이피롤에 연결하지 못했어요. 이전 정상 기록을 유지해요.",900);
                    if(response.Content.Headers.ContentType!=null&&response.Content.Headers.ContentType.MediaType.IndexOf("json",StringComparison.OrdinalIgnoreCase)<0)throw Format();
                    using(var stream=await response.Content.ReadAsStreamAsync())using(var memory=new MemoryStream()){
                        var buffer=new byte[8192];int n;while((n=await stream.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0){if(memory.Length+n>8*1024*1024)throw Format();memory.Write(buffer,0,n);}
                        try{var parsed=json.DeserializeObject(Encoding.UTF8.GetString(memory.ToArray()));if(!(parsed is Dictionary<string,object>))throw Format();return ProviderHttpPolicy.Stamp(parsed,response);}catch(ErFailure){throw;}catch{throw Format();}
                    }
                }
            }}catch(OperationCanceledException){if(caller.IsCancellationRequested)throw;throw new ErFailure("network","아이피롤 응답 시간이 초과되었어요. 이전 정상 기록을 유지해요.",900);}
            catch(HttpRequestException){throw new ErFailure("network","인터넷 또는 아이피롤 연결을 확인해 주세요.",900);}
        }
    }
    public async Task<LeagueResult> Read(string key,LeagueIdentity identity,LeagueData previous,CancellationToken token){
        if(identity.platform!="KR")throw new LeagueFailure("input","아이피롤 연결은 한국(KR) 서버를 지원해요.");
        if(!RiotApiProvider.ValidId(identity.riotId))throw new LeagueFailure("input","Riot ID를 확인해 주세요.");
        await gate.WaitAsync(token);try{
            return Parse(identity,await Get("/api/search?riotId="+Uri.EscapeDataString(identity.riotId)+"&count=20",token));
        }catch(ErFailure e){throw new LeagueFailure(e.State,e.Message,e.WaitSeconds);}finally{gate.Release();}
    }
    internal static LeagueResult Parse(LeagueIdentity identity,object response){
        if(!object.Equals(ErJson.Get(response,"ok"),true))throw new LeagueFailure("response","아이피롤에서 전적을 제공하지 못했어요.",1800);
        var data=LeagueJson.Get(response,"data");var profile=LeagueJson.Get(data,"profile");
        string id=LeagueJson.Text(profile,"riotId"),puuid=LeagueJson.Text(profile,"puuid");
        if(!string.Equals(id,identity.riotId,StringComparison.OrdinalIgnoreCase)||string.IsNullOrEmpty(puuid))throw LeagueJson.Bad();
        var result=new LeagueData{riotId=id,source="iplol",updatedAt=ProviderHttpPolicy.DataTime(response),sourceUpdatedAt=Time(LeagueJson.Num(profile,"lastUpdated")),level=LeagueJson.Num(profile,"level"),assetMessage="출처: IPLOL · 사이트 자동 조회 제한을 적용한 최근 경기예요."};
        var ranks=new List<LeagueRank>();var ranked=LeagueJson.Get(data,"ranked");
        foreach(string q in new[]{"solo","flex"}){string queue=q=="solo"?"RANKED_SOLO_5x5":"RANKED_FLEX_SR";var r=LeagueJson.Get(ranked,q);if(r==null){ranks.Add(new LeagueRank{queue=queue});continue;}
            if(LeagueJson.Text(r,"queueType")!=queue)throw LeagueJson.Bad();int wins=(int)LeagueJson.Num(r,"wins"),losses=(int)LeagueJson.Num(r,"losses");ranks.Add(new LeagueRank{queue=queue,tier=LeagueJson.Text(r,"tier"),division=LeagueJson.Text(r,"rank"),points=(int)LeagueJson.Num(r,"leaguePoints"),wins=wins,losses=losses,winRate=wins+losses==0?0:Math.Round(100.0*wins/(wins+losses),1)});
        }
        result.ranks=ranks.ToArray();var matches=new List<LeagueMatch>();
        foreach(var m in LeagueJson.Array(LeagueJson.Get(data,"matches"))){int queue=(int)LeagueJson.Num(m,"queueId");if(queue==0)continue;var win=LeagueJson.Get(m,"win");if(!(win is bool))throw LeagueJson.Bad();
            matches.Add(new LeagueMatch{id=LeagueJson.Text(m,"matchId"),queueId=queue,queue=queue==420?"솔로/듀오":queue==440?"자유랭크":queue==450?"칼바람":"큐 "+queue,characterName=LeagueJson.Text(m,"champion"),championId=(int)LeagueJson.Num(m,"championId"),kills=(int)LeagueJson.Num(m,"kills"),deaths=(int)LeagueJson.Num(m,"deaths"),assists=(int)LeagueJson.Num(m,"assists"),cs=(int)(LeagueJson.Num(m,"cs")+LeagueJson.Num(m,"neutralMinionsKilled")),duration=LeagueJson.Num(m,"gameDuration"),playedAt=Time(LeagueJson.Num(m,"playedAt")),win=(bool)win,result=(bool)win?"승리":"패배"});
        }
        result.matches=matches.GroupBy(m=>m.id).Select(g=>g.First()).Take(20).ToArray();
        result.characters=result.matches.GroupBy(m=>m.championId).Select(g=>new LeagueChampion{id=g.Key,name=g.First().characterName,games=g.Count(),wins=g.Count(m=>m.win),losses=g.Count(m=>!m.win),winRate=Math.Round(100.0*g.Count(m=>m.win)/g.Count(),1),kills=Math.Round(g.Average(m=>m.kills),1),deaths=Math.Round(g.Average(m=>m.deaths),1),assists=Math.Round(g.Average(m=>m.assists),1),cs=Math.Round(g.Average(m=>m.cs),1)}).OrderByDescending(c=>c.games).ToArray();
        return new LeagueResult{identity=new LeagueIdentity{riotId=id,platform="KR",puuid=puuid},data=result};
    }
    static string Time(long ms){return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(ms).ToString("o");}
    public void Dispose(){http.Dispose();}
}