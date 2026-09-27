using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class LeagueTests {
    const string TestKey="RGAPI-test-only-not-a-real-key";
    sealed class Provider:ILeagueProvider {
        internal int calls;internal LeagueFailure error;internal LeagueIdentity received;internal TaskCompletionSource<LeagueResult> pending;
        internal LeagueResult value=Result();
        public Task<LeagueResult> Read(string key,LeagueIdentity id,LeagueData previous,CancellationToken token){calls++;received=id;if(error!=null)throw error;return pending==null?Task.FromResult(value):pending.Task;}
        public void Dispose(){}
    }
    sealed class HttpFixture:HttpMessageHandler {
        internal List<string> urls=new List<string>();internal bool leaked;internal int status=200;internal bool malformed;internal int details;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken token){
            urls.Add(req.RequestUri.AbsoluteUri);string path=req.RequestUri.AbsolutePath;bool cdn=req.RequestUri.Host=="ddragon.leagueoflegends.com";
            if(cdn&&req.Headers.Contains("X-Riot-Token"))leaked=true;
            if(!cdn&&!req.Headers.Contains("X-Riot-Token"))throw new Exception("Missing auth header");
            var response=new HttpResponseMessage((HttpStatusCode)status);if(status==429)response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(77));
            string body="{}";
            if(path.Contains("/accounts/"))body="{\"puuid\":\"fixture-puuid\",\"gameName\":\"테스트\",\"tagLine\":\"검증\"}";
            else if(path.Contains("/summoners/"))body="{\"puuid\":\"fixture-puuid\",\"summonerLevel\":123}";
            else if(path.Contains("/entries/"))body=Ranks;
            else if(path.EndsWith("/ids"))body="[\"KR_123\",\"KR_124\"]";
            else if(path.Contains("/matches/")){details++;body=Match(path.EndsWith("123")?"KR_123":"KR_124",path.EndsWith("123"));}
            else if(path.EndsWith("versions.json"))body="[\"16.19.1\"]";
            else if(path.EndsWith("champion.json"))body="{\"data\":{\"Draven\":{\"key\":\"119\",\"name\":\"드레이븐\",\"image\":{\"full\":\"Draven.png\"}}}}";
            else if(path.EndsWith(".png")){using(var b=new System.Drawing.Bitmap(2,2))using(var m=new MemoryStream()){b.Save(m,System.Drawing.Imaging.ImageFormat.Png);response.Content=new ByteArrayContent(m.ToArray());return Task.FromResult(response);}}
            response.Content=new StringContent(malformed?"not-json":body,Encoding.UTF8,"application/json");return Task.FromResult(response);
        }
    }
    const string Ranks="[{\"queueType\":\"RANKED_SOLO_5x5\",\"tier\":\"EMERALD\",\"rank\":\"II\",\"leaguePoints\":42,\"wins\":64,\"losses\":52}]";
    static string Match(string id,bool win){return "{\"metadata\":{\"matchId\":\""+id+"\"},\"info\":{\"queueId\":420,\"gameType\":\"MATCHED_GAME\",\"gameStartTimestamp\":1700000000000,\"gameEndTimestamp\":1700001884000,\"gameDuration\":1884,\"participants\":[{\"puuid\":\"other\"},{\"puuid\":\"fixture-puuid\",\"championId\":119,\"championName\":\"Draven\",\"kills\":12,\"deaths\":3,\"assists\":8,\"totalMinionsKilled\":220,\"neutralMinionsKilled\":25,\"win\":"+(win?"true":"false")+"}]}}";}
    internal static LeagueData Fixture(){var j=LeagueJson.Serializer();var matches=new[]{RiotApiProvider.ParseMatch(j.DeserializeObject(Match("KR_123",true)),"KR_123","fixture-puuid"),RiotApiProvider.ParseMatch(j.DeserializeObject(Match("KR_124",false)),"KR_124","fixture-puuid")};return new LeagueData{riotId="검증용 계정#실제아님",updatedAt=DateTime.UtcNow.ToString("o"),level=123,ranks=RiotApiProvider.ParseRanks(j.DeserializeObject(Ranks)),matches=matches,characters=RiotApiProvider.Aggregate(matches)};}
    static LeagueResult Result(){return new LeagueResult{identity=new LeagueIdentity{riotId="테스트#검증",puuid="fixture-puuid",platform="KR"},data=Fixture()};}
    internal static async Task Run(string output){
        string root=Path.Combine(Path.GetTempPath(),"NyangLeagueTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var lines=new List<string>();
        Action<bool,string> check=(ok,label)=>{lines.Add((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);};
        try{
            check(RiotApiProvider.ValidId("도끼춤#한번춰볼까")&&!RiotApiProvider.ValidId("no-tag")&&!RiotApiProvider.ValidId("a#b#c"),"Unicode Riot ID validation");
            var http=new HttpFixture();using(var api=new RiotApiProvider(root,http)){
                api.SpacingMs=0;var result=await api.Read(TestKey,new LeagueIdentity{riotId="도끼춤#한번춰볼까",platform="KR"},null,CancellationToken.None);var d=result.data;
                check(result.identity.puuid=="fixture-puuid"&&http.urls.Any(u=>u.Contains("asia.api.riotgames.com/riot/account/v1/accounts/by-riot-id/")),"Riot ID -> PUUID, ASIA routing and escaped Korean path");
                check(http.urls.Any(u=>u.Contains("kr.api.riotgames.com/lol/league/v4/entries/by-puuid/"))&&!http.urls.Any(u=>u.Contains("by-summoner")),"Current platform PUUID rank endpoint");
                check(d.ranks[0].points==42&&d.ranks[0].wins==64&&d.ranks[0].losses==52&&d.ranks[0].winRate==55.2,"Rank LP season wins losses computed win rate");
                check(d.ranks[1].tier=="Unranked"&&d.ranks[1].points==null&&d.ranks[1].wins==null,"Absent ranked entry -> Unranked, no invented zero LP");
                check(d.matches.Length==2&&d.matches[0].kills==12&&d.matches[0].deaths==3&&d.matches[0].assists==8&&d.matches[0].cs==245&&d.matches[0].duration==1884&&d.matches[0].queueId==420,"Participant PUUID, KDA, lane+jungle CS, duration, queue/date");
                check(d.characters.Length==1&&d.characters[0].games==2&&d.characters[0].wins==1&&d.characters[0].losses==1&&d.characters[0].winRate==50&&d.characters[0].cs==245,"Champion aggregates use actual fetched match sample");
                check(d.matches[0].characterName=="드레이븐"&&d.matches[0].imageUrl.StartsWith("https://league.korugaming.example/")&&File.Exists(Path.Combine(root,"LeagueImages","16.19.1-Draven.png")),"Data Dragon numeric champion ID localization and local image cache");
                await api.Read(TestKey,result.identity,d,CancellationToken.None);
                check(http.details==2&&http.urls.Any(u=>u.Contains("accounts/by-puuid/fixture-puuid")),"Refresh retains account by PUUID and reuses immutable match cache");
                check(!http.leaked&&!LeagueJson.Serializer().Serialize(d).Contains(TestKey),"No API key on CDN requests or frontend data");
                foreach(int status in new[]{401,403,404,429,503}){http.status=status;bool failed=false;try{await api.Read(TestKey,result.identity,d,CancellationToken.None);}catch(LeagueFailure e){failed=status==429?e.State=="rateLimit"&&e.WaitSeconds==77:status==401||status==403?e.State=="key":status==404?e.State=="notFound":e.State=="service";}check(failed,"HTTP "+status+" safe typed error / Retry-After");if(status==429)break;}
            }
            using(var api=new RiotApiProvider(Path.Combine(root,"bad"),new HttpFixture{malformed=true})){api.SpacingMs=0;bool failed=false;try{await api.Read(TestKey,Result().identity,null,CancellationToken.None);}catch(LeagueFailure e){failed=e.State=="response";}check(failed,"Malformed response rejected, never Unranked");}
            using(var api=new RiotApiProvider(Path.Combine(root,"service"),new HttpFixture{status=503})){api.SpacingMs=0;bool failed=false;try{await api.Read(TestKey,Result().identity,null,CancellationToken.None);}catch(LeagueFailure e){failed=e.State=="service";}check(failed,"HTTP 503 preserves safe service error");}
            var provider=new Provider();DateTime now=new DateTime(2026,9,27,0,0,0,DateTimeKind.Utc);
            string serviceRoot=Path.Combine(root,"service-state");
            using(var service=new LeagueService(serviceRoot,false,provider)){
                service.Now=()=>now;service.SetKey(TestKey);await service.Connect("도끼춤#한번춰볼까","KR");
                check(service.View.connected&&service.View.snapshots.Length==2,"Successful connection saved and SQLite snapshots created");
                check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(serviceRoot,"league-key.dpapi"))).Contains(TestKey)&&!LeagueJson.Serializer().Serialize(service.View).Contains(TestKey),"DPAPI encrypted key; no key in serialized view");
                await service.Refresh();check(provider.calls==1,"Manual refresh cooldown prevents repeated polling");
                now=now.AddSeconds(31);provider.error=new LeagueFailure("network","검증 오류");await service.Refresh();check(service.View.stale&&service.View.data.ranks[0].points==42,"Network failure retains last successful cache and timestamp");
                provider.error=null;now=now.AddMinutes(11);service.GameExited();await service.Tick();int before=provider.calls;check(service.ExitPending,"Game exit schedules delayed refresh");
                now=now.AddSeconds(31);await service.Tick();check(provider.calls==before+1&&service.ExitAttempts==1,"First post-game refresh after 30 seconds");
                now=now.AddSeconds(89);await service.Tick();check(provider.calls==before+1,"No excessive polling before retry deadline");
                now=now.AddSeconds(2);await service.Tick();now=now.AddSeconds(181);await service.Tick();check(service.ExitAttempts==3&&!service.ExitPending,"No new match: bounded 30/90/180-second retry then regular refresh");
                service.GameExited();provider.value=Result();provider.value.data.matches[0].id="KR_999";now=now.AddSeconds(31);await service.Tick();check(service.ExitAttempts==1&&!service.ExitPending,"New match stops retries and updates rank/matches together");
                now=now.AddMinutes(11);provider.error=new LeagueFailure("key","만료",900);await service.Refresh();int calls=provider.calls;now=now.AddDays(1);await service.Tick();check(provider.calls==calls&&service.View.stale,"Expired key stops automatic requests and keeps cache");provider.error=null;
            }
            var startup=new Provider();using(var restored=new LeagueService(serviceRoot,false,startup)){check(restored.View.connected&&restored.View.hasKey&&restored.View.stale&&restored.View.data.matches.Length==2&&restored.View.snapshots.Length>=2,"Restart restores account, encrypted key, cache and SQLite records");await restored.Tick();check(startup.calls==1&&startup.received.puuid=="fixture-puuid"&&!restored.View.stale,"Startup automatically refreshes saved PUUID");}
            var late=new Provider{pending=new TaskCompletionSource<LeagueResult>()};using(var service=new LeagueService(Path.Combine(root,"late"),false,late)){service.SetKey(TestKey);var fetching=service.Connect("테스트#검증","KR");service.Disconnect();late.pending.SetResult(Result());await fetching;check(!service.View.connected&&service.View.data==null&&!File.Exists(Path.Combine(root,"late","league-account.json")),"Disconnect invalidates late in-flight responses");}
            using(var db=new LeagueRankStore(Path.Combine(root,"bound.sqlite"))){var result=Result();result.identity.puuid="apostrophe'and-unicode-테스트";db.Append(result.identity,result.data,now);check(db.Read(result.identity).Length==2&&db.Read(new LeagueIdentity{puuid="other",platform="KR"}).Length==0,"SQLite parameter binding and account isolation");}
            lines.Add("LIVE Riot account verification: requires user-provided key; fixture tests are not live results.");
        }catch(Exception e){lines.Add("FAIL test run: "+e.GetType().Name+" "+e.Message);}
        finally{File.WriteAllLines(output,lines);}
    }
}
