using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class EternalReturnTests {
    internal static ErData Fixture(){return new ErData{nickname="검증용 긴 닉네임 · 실제 계정 아님",seasonId=99,seasonName="레이아웃 검증 시즌",rp=3214,ranking=12345,seasonGames=72,seasonWins=12,rankMessage="검증 데이터 · 실제 조회 결과 아님",updatedAt="2026-09-24T08:00:00Z",matches=new List<ErMatch>{
        new ErMatch{id=123456789,season=99,mode=3,rank=2,character="검증용 캐릭터 이름 Very Long Character",kills=7,assists=4,seconds=1334,delta=31,damage=123456,received=98765,level=20,healing=12345,teamKills=22,startedAt="2026-09-24T07:35:00Z"},
        new ErMatch{id=123456788,season=99,mode=3,rank=8,character="검증용 캐릭터",kills=0,assists=0,seconds=750,delta=-12,startedAt="2026-09-23T07:35:00Z"},
        new ErMatch{id=123456787,season=99,mode=2,rank=1,character="검증용 캐릭터",kills=null,assists=null,seconds=null,delta=null}}};}
    sealed class Fake:IEternalReturnDataProvider {
        internal int calls;internal ErFailure failure;internal TaskCompletionSource<ErData> pending;internal ErData result=Fixture();
        public Task<ErData> Read(string key,string nickname,CancellationToken cancel){calls++;if(failure!=null)throw failure;return pending==null?Task.FromResult(result):pending.Task;}
        public void Dispose(){}
    }
    sealed class Transport:HttpMessageHandler {
        internal readonly List<string> paths=new List<string>();internal bool hasKey=true;internal int status=200;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
            string path=request.RequestUri.PathAndQuery;paths.Add(path);hasKey&=request.RequestUri.Host=="open-api.bser.io"&&request.Headers.Contains("x-api-key")&&!request.RequestUri.Query.Contains("synthetic");
            string body;
            if(path.StartsWith("/v1/user/nickname?"))body="{\"code\":200,\"user\":{\"uid\":\"fixture-uid\",\"nickname\":\"Fixture\"}}";
            else if(path=="/v2/data/Season")body="{\"code\":200,\"data\":[{\"seasonID\":99,\"seasonName\":\"Fixture season\",\"isCurrent\":1}]}";
            else if(path=="/v2/data/Character")body="{\"code\":200,\"data\":[{\"code\":2,\"name\":\"Aya\"}]}";
            else if(path=="/v2/user/stats/uid/fixture-uid/99/3")body="{\"code\":200,\"userStats\":[{\"seasonId\":99,\"matchingMode\":3,\"matchingTeamMode\":3,\"mmr\":3214,\"rank\":9,\"totalGames\":10,\"totalWins\":1}]}";
            else if(path=="/v1/user/games/uid/fixture-uid")body="{\"code\":200,\"userGames\":[{\"gameId\":123,\"seasonId\":99,\"matchingMode\":3,\"characterNum\":2,\"gameRank\":1,\"playerKill\":0,\"mmrGain\":31,\"playTime\":1250}]}";
            else throw new InvalidOperationException("Unexpected fixture API route");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent(body)});
        }
    }
    internal static async Task Run(string directory){
        Directory.CreateDirectory(directory);string root=Path.Combine(directory,"er-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var report=new List<string>();
        Action<bool,string> check=(value,name)=>{report.Add((value?"PASS ":"FAIL ")+name);};
        try{
            var json=new JavaScriptSerializer();
            var partial=ErJson.Obj(json.DeserializeObject("{\"gameId\":12,\"matchingMode\":3,\"characterNum\":2,\"playerKill\":0,\"duration\":99999}"));
            var m=EternalReturnApiProvider.ParseMatch(partial,new Dictionary<int,string>{{2,"Aya"}});
            check(m.kills==0&&m.assists==null&&m.delta==null&&m.seconds==null,"Missing metrics remain null; duration is not seconds; genuine zero retained");
            partial["mmrGain"]=-12;m=EternalReturnApiProvider.ParseMatch(partial,new Dictionary<int,string>());check(m.delta==-12&&m.character=="캐릭터 #2","Official delta preserved, unknown character not invented");
            partial["matchingMode"]=2;check(EternalReturnApiProvider.ParseMatch(partial,new Dictionary<int,string>()).delta==null,"Normal hidden MMR never labeled RP");
            var rows=new[]{new Dictionary<string,object>{{"seasonID",3},{"isCurrent",true}},new Dictionary<string,object>{{"seasonID",999},{"isCurrent",false}}};
            check(ErJson.Int(EternalReturnApiProvider.CurrentSeason(rows,DateTime.UtcNow),"seasonID")==3,"Future season is not selected by max ID");
            rows[1]["isCurrent"]=true;check(EternalReturnApiProvider.CurrentSeason(rows,DateTime.UtcNow)==null,"Ambiguous current season unavailable");
            check(EternalReturnApiProvider.Failure(429,120).WaitSeconds==120&&EternalReturnApiProvider.Failure(403,0).State=="forbidden"&&EternalReturnApiProvider.Failure(401,0).State=="key"&&EternalReturnApiProvider.Failure(404,0).State=="notFound","Rate limit, forbidden, bad key and missing resource distinguishable");
            check(!EternalReturnService.ValidKey("a\r\nb")&&!EternalReturnService.ValidKey("a b"),"API key header injection rejected");
            var transport=new Transport();using(var api=new EternalReturnApiProvider(transport)){
                var data=await api.Read("synthetic-key","Fixture",CancellationToken.None);
                check(data.rp==3214&&data.seasonId==99&&data.matches.Count==1&&data.matches[0].character=="Aya"&&data.matches[0].delta==31,"Provider parses official UID, metadata, ranked stats and match envelopes");
                check(transport.hasKey&&transport.paths.All(p=>!p.Contains("userNum"))&&transport.paths.Count==5,"Only official UID routes, key only in header");
                await api.Read("synthetic-key","Fixture",CancellationToken.None);check(transport.paths.Count==8,"Public metadata cache avoids repeated requests");
            }
            var fake=new Fake();DateTime now=DateTime.UtcNow;string synthetic="synthetic-er-test-key-not-a-credential";
            using(var service=new EternalReturnService(root,false,fake)){
                service.Now=()=>now;service.SetKey(synthetic);
                check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"eternal-key.dpapi"))).Contains(synthetic),"DPAPI encrypted storage");
                await service.Search("test");check(service.View.preview!=null&&!service.View.connected&&service.View.data==null,"Nickname search requires explicit account confirmation");
                service.Connect();check(service.View.connected&&service.View.data.rp==3214&&File.Exists(Path.Combine(root,"eternal-account.json")),"Connect persists account and successful cache");
                check(!json.Serialize(service.View).Contains(synthetic),"Bridge DTO has no API key");
                await service.Refresh(true);check(fake.calls==1,"Manual refresh cooldown prevents burst");
                now=now.AddMinutes(1);fake.failure=new ErFailure("network","Test network");await service.Refresh(true);
                check(service.View.stale&&service.View.data.rp==3214&&service.View.state=="network","Network failure retains last success");
                fake.failure=null;now=now.AddMinutes(1);await service.Refresh(true);check(!service.View.stale,"Successful retry clears stale marker");
                now=now.AddMinutes(1);fake.failure=EternalReturnApiProvider.Failure(429,120);await service.Refresh(true);int before=fake.calls;
                now=now.AddSeconds(60);await service.Refresh(true);check(fake.calls==before,"429 backoff respected by manual refresh");
                fake.failure=null;now=now.AddMinutes(2);service.GameExited();before=fake.calls;await service.Tick();check(fake.calls==before,"Game exit waits before query");
                now=now.AddSeconds(31);await service.Tick();now=now.AddSeconds(91);await service.Tick();now=now.AddSeconds(181);await service.Tick();
                check(fake.calls==before+3,"Game exit performs at most three delayed checks");
                now=now.AddSeconds(31);await service.Tick();check(fake.calls==before+3,"No tight loop after match propagation retries");
                now=now.AddMinutes(6);service.GameExited();fake.result=Fixture();fake.result.matches[0].id++;now=now.AddSeconds(31);await service.Tick();before=fake.calls;
                now=now.AddSeconds(91);await service.Tick();check(fake.calls==before,"New match stops exit retries");
            }
            using(var restored=new EternalReturnService(root,false,new Fake())){check(restored.View.connected&&restored.View.hasKey&&restored.View.stale&&restored.View.data!=null,"Restart restores encrypted key and marked-stale cache");await restored.Tick();check(!restored.View.stale,"Startup refresh uses saved account");}
            File.WriteAllText(Path.Combine(root,"hoyolab-untouched.test"),"unchanged");
            var pendingFake=new Fake{pending=new TaskCompletionSource<ErData>()};
            using(var late=new EternalReturnService(root,false,pendingFake)){
                var pending=late.Refresh(true);late.Disconnect();pendingFake.pending.SetResult(Fixture());await pending;
                check(!late.View.connected&&late.View.data==null&&!late.View.hasKey,"Disconnect ignores in-flight response");
                check(!File.Exists(Path.Combine(root,"eternal-account.json"))&&!File.Exists(Path.Combine(root,"eternal-key.dpapi"))&&File.Exists(Path.Combine(root,"hoyolab-untouched.test")),"Disconnect removes ER-only cache and key");
                int before=pendingFake.calls;await late.Tick();check(pendingFake.calls==before,"Disconnect stops automatic refresh");
            }
        }catch(Exception ex){report.Add("FAIL unexpected "+ex.GetType().Name+" "+ex.Message);}
        File.WriteAllLines(Path.Combine(directory,"eternal-tests.txt"),report);
    }
}
