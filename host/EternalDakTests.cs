using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class EternalDakTests {
    const string Seasons="{\"seasons\":[{\"id\":999,\"key\":\"SEASON_999\",\"name\":\"미래\"},{\"id\":41,\"key\":\"SEASON_21\",\"name\":\"시즌 12\",\"isCurrent\":true}]}";
    const string Tiers="{\"tiers\":[{\"id\":6,\"name\":\"다이아몬드\"}]}";
    const string Characters="{\"characters\":[{\"id\":1,\"name\":\"재키\"}]}";
    const string Profile="{\"meta\":{\"season\":\"SEASON_21\"},\"player\":{\"name\":\"검증\",\"syncedAt\":1790428640000},\"playerSeasons\":[{\"seasonId\":41,\"mmr\":5917,\"tierId\":6,\"tierGradeId\":2}],\"playerSeasonOverviews\":[{\"seasonId\":41,\"matchingModeId\":3,\"teamModeId\":3,\"mmr\":5917,\"tierId\":6,\"tierGradeId\":2,\"play\":43,\"win\":9,\"rank\":{\"global\":{\"rank\":2345}}}]}";
    const string Matches="{\"meta\":{\"season\":\"SEASON_21\"},\"matches\":[{\"nickname\":\"검증\",\"gameId\":123,\"seasonId\":41,\"matchingMode\":3,\"characterNum\":1,\"gameRank\":2,\"playerKill\":0,\"playerAssistant\":7,\"mmrGain\":30,\"playTime\":1340,\"damageToPlayer\":12345,\"startDtm\":\"2026-09-26T02:42:33+09:00\",\"IsLeaving\":false,\"isLeaving\":false},{\"nickname\":\"검증\",\"gameId\":124,\"seasonId\":0,\"matchingMode\":2,\"characterNum\":1,\"gameRank\":1,\"mmrGain\":999}]}";
    static object Json(string value){return new JavaScriptSerializer().DeserializeObject(value);}
    internal static EternalDakProvider.DakMetadata Metadata(){return new EternalDakProvider.DakMetadata{seasons=Json(Seasons),tiers=Json(Tiers),characters=Json(Characters)};}
    internal static ErData Fixture(){return EternalDakProvider.Parse("검증",Json(Profile),Json(Matches),Metadata());}
    sealed class Transport:HttpMessageHandler {
        internal int calls,status=200;internal bool keyLeaked,invalid;internal readonly List<string> urls=new List<string>();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken token){
            calls++;urls.Add(req.RequestUri.AbsoluteUri);keyLeaked|=req.Headers.Contains("x-api-key")||req.Headers.Contains("Authorization")||req.Headers.Contains("Cookie");
            string p=req.RequestUri.AbsolutePath,body=p.EndsWith("seasons")?Seasons:p.EndsWith("tiers")?Tiers:p.EndsWith("characters")?Characters:p.EndsWith("profile")?Profile:Matches;
            var r=new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent(invalid?"<html>invalid</html>":body,System.Text.Encoding.UTF8,"application/json")};if(status==429)r.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(180));return Task.FromResult(r);
        }
    }
    sealed class Fake:IEternalReturnDataProvider {
        internal int calls;internal ErFailure error;internal TaskCompletionSource<ErData> pending;
        public Task<ErData> Read(string key,string name,CancellationToken token){calls++;if(key!=null)throw new Exception("Key sent to site");if(error!=null)throw error;return pending==null?Task.FromResult(Fixture()):pending.Task;}
        public void Dispose(){}
    }
    internal static async Task Run(string directory){
        Directory.CreateDirectory(directory);string root=Path.Combine(directory,"dak-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var lines=new List<string>();
        Action<bool,string> check=(ok,name)=>{lines.Add((ok?"PASS ":"FAIL ")+name);if(!ok)throw new Exception(name);};
        try{
            check(EternalDakProvider.Normalize("https://dak.gg/er/players/%EB%83%A5%EB%83%A5?hl=ko")=="냥냥","Public profile URL and Korean nickname normalization");
            foreach(string invalid in new[]{"https://evil.example/er/players/test","https://dak.gg@evil.example/er/players/test","../x","a/b"}){bool rejected=false;try{EternalDakProvider.Normalize(invalid);}catch(ErFailure){rejected=true;}check(rejected,"Reject untrusted path: "+invalid);}
            var data=Fixture();check(data.rp==5917&&data.tier=="다이아몬드 2"&&data.seasonId==41&&data.seasonGames==43&&data.seasonWins==9&&data.ranking==2345,"Current season rank, tier, RP and season counters without estimation");
            check(data.matches.Count==2&&data.matches[1].kills==0&&data.matches[1].assists==7&&data.matches[1].seconds==1340&&data.matches[1].damage==12345,"Actual match fields: zero kept, CS not invented, time uses playTime");
            check(data.matches[0].season==0&&data.matches[0].delta==null&&data.matches[0].seconds==null,"Normal games season 0 accepted; normal MMR and missing values not fabricated");
            check(data.source=="dak"&&data.sourceUpdatedAt!=data.updatedAt&&data.sourceUrl.StartsWith("https://dak.gg/er/players/"),"Source attribution and site update time separate from fetch time");
            bool changed=false;try{EternalDakProvider.Parse("검증",Json(Profile.Replace("SEASON_21","SEASON_20")),Json(Matches),Metadata());}catch(ErFailure){changed=true;}check(changed,"Season mismatch rejected rather than showing past RP as current");
            var noRank=Json("{\"meta\":{\"season\":\"SEASON_21\"},\"player\":{\"name\":\"검증\"},\"playerSeasons\":[]}");var empty=EternalDakProvider.Parse("검증",noRank,Json("{\"meta\":{\"season\":\"SEASON_21\"},\"matches\":[]}"),Metadata());check(empty.rp==null&&empty.seasonGames==null&&empty.matches.Count==0,"No current season rank is not invented zero or old season rank");
            bool wrong=false;try{EternalDakProvider.Parse("other",Json(Profile),Json(Matches),Metadata());}catch(ErFailure){wrong=true;}check(wrong,"Reject another account's response");
            var transport=new Transport();using(var reader=new EternalDakProvider(root,transport)){reader.SpacingMs=0;await reader.Read("should-not-be-used","검증",CancellationToken.None);await reader.Read(null,"검증",CancellationToken.None);check(transport.calls==7&&!transport.keyLeaked,"Metadata cache and no cookies/keys on site requests");check(transport.urls.All(u=>u.StartsWith("https://er.dakgg.io/api/v1/"))&&!transport.urls.Any(u=>u.Contains("sync")),"Only observed public GET data endpoints; no sync mutation");foreach(int status in new[]{403,404,503,429}){transport.status=status;bool caught=false;try{await reader.Read(null,"검증",CancellationToken.None);}catch(ErFailure e){caught=status==403?e.State=="siteBlocked":status==404?e.State=="notFound":status==429?e.State=="rateLimit"&&e.WaitSeconds==180:e.State=="service";}check(caught,"HTTP "+status+" safe handling");}}
            using(var reader=new EternalDakProvider(Path.Combine(root,"broken"),new Transport{invalid=true})){reader.SpacingMs=0;bool caught=false;try{await reader.Read(null,"검증",CancellationToken.None);}catch(ErFailure e){caught=e.State=="format";}check(caught,"Malformed/challenge HTML rejected without browser bypass");}
            string appRoot=Path.Combine(root,"app");Directory.CreateDirectory(appRoot);DateTime now=DateTime.UtcNow;var fake=new Fake();
            using(var app=new EternalReturnService(appRoot,false,null,fake)){
                app.Now=()=>now;check(!app.View.hasKey&&app.View.source=="dak","Fresh app defaults to nickname-only website mode");await app.Search("검증");check(app.View.preview!=null&&!app.View.connected,"Search previews website data without API key");app.Connect();check(app.View.connected&&app.View.data.source=="dak","Connect persists correct source");await app.Refresh(true);check(fake.calls==1,"Manual website refresh cooldown");now=now.AddMinutes(2);fake.error=new ErFailure("format","changed",900);await app.Refresh(true);check(app.View.stale&&app.View.data.rp==5917,"Schema failure preserves successful cached data");fake.error=null;now=now.AddMinutes(16);app.GameExited();int before=fake.calls;now=now.AddSeconds(31);await app.Tick();check(fake.calls==before,"Website game-exit check waits 60 seconds");now=now.AddSeconds(30);await app.Tick();check(fake.calls==before+1,"Website game-exit refresh uses existing process detection");
                fake.error=new ErFailure("siteBlocked","blocked",86400);now=now.AddDays(1);await app.Refresh(true);before=fake.calls;now=now.AddDays(2);await app.Tick();check(fake.calls==before,"403 stops background polling instead of circumventing denial");
            }
            var resumed=new Fake();using(var app=new EternalReturnService(appRoot,false,null,resumed)){check(app.View.connected&&!app.View.hasKey&&app.View.source=="dak"&&app.View.stale,"Restart restores website identity and cache with no key");await app.Tick();check(resumed.calls==1&&!app.View.stale,"Restart refreshes stored nickname");}
            var late=new Fake{pending=new TaskCompletionSource<ErData>()};using(var app=new EternalReturnService(appRoot,false,null,late)){var task=app.Refresh(true);app.Disconnect();late.pending.SetResult(Fixture());await task;check(!app.View.connected&&app.View.data==null&&!File.Exists(Path.Combine(appRoot,"eternal-account.json")),"Disconnect cancels late website result and removes account");}
        }catch(Exception ex){lines.Add("FAIL "+ex.GetType().Name+": "+ex.Message);}File.WriteAllLines(Path.Combine(directory,"dak-tests.txt"),lines);
    }
    internal static async Task Live(string nickname,string output){
        string folder=Path.GetDirectoryName(Path.GetFullPath(output));Directory.CreateDirectory(folder);
        using(var provider=new EternalDakProvider(Path.Combine(folder,"dak-live-cache"))){try{var data=await provider.Read(null,nickname,CancellationToken.None);File.WriteAllText(output,new JavaScriptSerializer().Serialize(data));}catch(ErFailure e){File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{error=e.State,message=e.Message}));}}
    }
}
