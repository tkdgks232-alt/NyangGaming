using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
internal static class EternalCraftTests {
    sealed class Fake:IEternalReturnDataProvider {
        internal ErData value;internal ErFailure error;internal int calls;
        public Task<ErData> Read(string key,string name,CancellationToken t){if(key!=null)throw new Exception("key leaked");calls++;if(error!=null)throw error;return Task.FromResult(value);}
        public void Dispose(){}
    }
    sealed class Http:HttpMessageHandler {
        internal int status;internal bool invalid,keyLeaked;internal string profile,matches;internal int calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage q,CancellationToken t){
            if(q.Method!=HttpMethod.Get||q.RequestUri.Host!="api.ercraft.net")throw new Exception("unexpected request");
            keyLeaked|=q.Headers.Contains("Authorization")||q.Headers.Contains("Cookie")||q.Headers.Contains("x-api-key");calls++;
            var r=new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent(invalid?"<html>":q.RequestUri.AbsolutePath.EndsWith("profile-view")?profile:matches,System.Text.Encoding.UTF8,"application/json")};
            if(status==429)r.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(180));return Task.FromResult(r);
        }
    }
    internal static async Task Run(string fixtures,string output){
        var lines=new List<string>();var j=new JavaScriptSerializer{MaxJsonLength=8388608};Action<bool,string> check=(b,n)=>{lines.Add((b?"PASS ":"FAIL ")+n);if(!b)throw new Exception(n);};
        string root=Path.Combine(Path.GetDirectoryName(output),"craft-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string p=File.ReadAllText(Path.Combine(fixtures,"ercraft-profile.json")),m=File.ReadAllText(Path.Combine(fixtures,"ercraft-matches.json"));
            var d=EternalCraftProvider.Parse("냥냥",j.DeserializeObject(p),j.DeserializeObject(m));
            check(d.source=="ercraft"&&d.rp==5656&&d.tier=="다이아몬드 3","Recorded real profile mapping");
            check(d.seasonGames==null&&d.seasonWins==null,"Detailed sample not misrepresented as full season");
            check(d.matches.Count==10&&d.matches[0].id==65661546&&d.matches[0].seconds==615&&d.matches[0].delta==-41,"Recent match mapping");
            check(d.matches[0].character=="유키"&&EternalCraftProvider.CharacterName(70,"실험체 #70")=="츠바메"&&EternalCraftProvider.CharacterName(null,"실험체 #87")=="코렐라인","Character labels resolve for new and cached records");
            check(EternalCraftProvider.CharacterName(999,"실험체 #999")=="실험체 #999"&&EternalCraftProvider.CharacterName(70,"기존 이름")=="기존 이름","Unknown IDs and supplied names preserved");
            check(d.matches[0].received==null&&d.matches[0].healing==null,"Unavailable fields remain missing");
            check(d.sourceUpdatedAt.StartsWith("2026-09-24")&&d.updatedAt!=d.sourceUpdatedAt,"Source time separate from fetch time");
            bool rejected=false;try{EternalCraftProvider.Parse("other",j.DeserializeObject(p),j.DeserializeObject(m));}catch(ErFailure){rejected=true;}check(rejected,"Wrong identity rejected");
            check(EternalCraftProvider.Normalize("https://ercraft.net/player/%EB%83%A5%EB%83%A5")=="냥냥","Profile URL input");
            foreach(string url in new[]{"https://evil.example/player/a","https://ercraft.net@evil.example/player/a","../x"}){rejected=false;try{EternalCraftProvider.Normalize(url);}catch(ErFailure){rejected=true;}check(rejected,"Unsafe URL rejected");}
            foreach(int status in new[]{200,202,403,404,429,503}){var h=new Http{status=status,profile=p,matches=m};using(var provider=new EternalCraftProvider(root,h)){provider.SpacingMs=0;string state="";try{await provider.Read("unused-key","냥냥",CancellationToken.None);}catch(ErFailure e){state=e.State;check(status!=429||e.WaitSeconds==180,"Retry-After respected");}check(status==200?state==""&&h.calls==2:state==(status==403?"siteBlocked":status==404?"notFound":status==429?"rateLimit":"service"),"HTTP "+status);check(!h.keyLeaked,"No credentials in public GET");}}
            File.WriteAllText(Path.Combine(root,"eternal-account.json"),j.Serialize(new ErStored{nickname="냥냥",source="dak",data=new ErData{source="dak",rp=5917}}));
            var fake=new Fake{value=d};DateTime now=DateTime.UtcNow;
            using(var app=new EternalReturnService(root,false,null,fake)){
                app.Now=()=>now;check(app.View.connected&&app.View.source=="ercraft"&&app.View.data==null,"DAK migration never relabels old data");await app.Tick();check(app.View.data.rp==5656&&fake.calls==1,"Migration fetches ERCraft");await app.Refresh(true);check(fake.calls==1,"Manual cooldown");now=now.AddMinutes(16);fake.error=new ErFailure("format","changed",900);await app.Tick();check(app.View.stale&&app.View.data.rp==5656,"Failure preserves ERCraft cache");fake.error=new ErFailure("siteBlocked","blocked",86400);now=now.AddDays(1);await app.Tick();int calls=fake.calls;now=now.AddDays(2);await app.Tick();check(fake.calls==calls,"403 stops polling");}
            using(var app=new EternalReturnService(root,false,null,new Fake{value=d})){check(app.View.source=="ercraft"&&app.View.data.rp==5656,"Restart restores ERCraft source");}
            using(var live=new EternalCraftProvider(root)){var value=await live.Read(null,"냥냥",CancellationToken.None);File.WriteAllText(output,j.Serialize(value));check(value.source=="ercraft"&&value.matches.Count>0,"Live ERCraft lookup");}
        }catch(Exception e){lines.Add("FAIL "+e.Message);}
        File.WriteAllLines(output+".tests.txt",lines);
    }
}