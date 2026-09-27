using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
internal static class ValorantTests {
    sealed class Handler:HttpMessageHandler {
        internal int Calls;internal HttpStatusCode Status=HttpStatusCode.OK;internal string Profile,History;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token){Calls++;var response=new HttpResponseMessage(Status);response.Content=new StringContent(r.RequestUri.AbsolutePath.Contains("/history/")?(History??"{}"):(Profile??"{}"),System.Text.Encoding.UTF8,"application/json");return Task.FromResult(response);}
    }
    internal static async Task Run(string fixtures,string output){
        var lines=new List<string>();Action<bool,string> check=(ok,label)=>{lines.Add((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);};var j=new JavaScriptSerializer();
        string id="심쿵냥펀치#냥냥펀치",p=File.ReadAllText(Path.Combine(fixtures,"valking-profile.json")),h=File.ReadAllText(Path.Combine(fixtures,"valking-history.json"));
        try{
            var parsed=ValorantService.Parse(id,j.DeserializeObject(p),j.DeserializeObject(h));
            check(parsed.riotId==id&&parsed.state=="connected","identity verified");
            check(parsed.rank=="Unranked"&&parsed.points==null,"rank without fabricated RR");
            check(parsed.headshotPercent==null,"zero hits does not fabricate headshot percent");
            check(parsed.matches.Length==1&&parsed.matches[0].kills==12&&parsed.matches[0].deaths==8&&parsed.matches[0].assists==1&&parsed.matches[0].score=="13 : 2","real competitive match normalized");
            check(parsed.characters.Length==2&&parsed.characters[0].games==3,"site recent agent scope retained");
            bool rejected=false;try{ValorantService.Parse("다른계정#KR1",j.DeserializeObject(p),j.DeserializeObject(h));}catch(ErFailure){rejected=true;}check(rejected,"wrong identity rejected");
            rejected=false;try{ValorantService.Parse(id,j.DeserializeObject(p.Replace("\"isPrivate\":false","\"isPrivate\":true")),j.DeserializeObject(h));}catch(ErFailure e){rejected=e.State=="private";}check(rejected,"private account rejected");
            rejected=false;try{ValorantService.Parse(id,j.DeserializeObject(p),j.DeserializeObject("{}"));}catch(ErFailure){rejected=true;}check(rejected,"missing history rejected");
            var handler=new Handler{Profile=p,History=h};using(var service=new ValorantService(null,false,handler)){await service.Connect(id);check(service.Read().matches.Length==1&&handler.Calls==2,"two GET connection");await service.Refresh();check(handler.Calls==2,"manual cooldown");await service.Tick();check(handler.Calls==2,"periodic cooldown");}
            var denied=new Handler{Status=HttpStatusCode.Forbidden};using(var service=new ValorantService(null,false,denied)){await service.Connect(id);await service.Tick();check(service.Read().state=="siteBlocked"&&denied.Calls==1,"403 stops automatic requests");}
            var throttled=new Handler{Status=(HttpStatusCode)429};using(var service=new ValorantService(null,false,throttled)){await service.Connect(id);await service.Refresh();check(service.Read().state=="rateLimit"&&throttled.Calls==1,"429 cooldown");}
            string root=Path.Combine(Path.GetDirectoryName(output),"valking-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"valorant-valking.json"),j.Serialize(parsed));
            using(var service=new ValorantService(root,false,new Handler{Status=HttpStatusCode.ServiceUnavailable})){check(service.Read().matches.Length==1&&service.Read().stale,"cache restored");await service.Refresh();check(service.Read().stale&&service.Read().matches[0].kills==12,"failed refresh preserves last good data");}
            File.WriteAllText(output,j.Serialize(parsed));
        }finally{File.WriteAllLines(output+".tests.txt",lines);}
    }
}
