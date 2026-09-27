using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
internal static class LeagueIplolTests {
 sealed class Mock:HttpMessageHandler {
  internal string body;internal int status=200,calls;internal bool safe=true;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage q,CancellationToken t){calls++;safe&=q.Method==HttpMethod.Get&&q.RequestUri.Host=="iplol.kr"&&!q.RequestUri.Query.Contains("refresh=")&&!q.Headers.Contains("X-Riot-Token")&&!q.Headers.Contains("Cookie");var r=new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")};if(status==429)r.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(180));return Task.FromResult(r);}
 }
 internal static async Task Run(string fixture,string output){
  var log=new List<string>();Action<bool,string> check=(b,n)=>{log.Add((b?"PASS ":"FAIL ")+n);if(!b)throw new Exception(n);};var j=LeagueJson.Serializer();string root=Path.Combine(Path.GetDirectoryName(output),"iplol-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   string raw=File.ReadAllText(fixture);var id=new LeagueIdentity{riotId="도끼춤#한번춰볼까",platform="KR"};var result=LeagueIplolProvider.Parse(id,j.DeserializeObject(raw));var d=result.data;
   check(d.ranks[0].points==34&&d.ranks[0].wins==25&&d.ranks[0].losses==33&&d.ranks[1].points==null,"Rank and unranked mapping");
   check(d.matches.Length==5&&d.matches[0].cs==283&&d.matches[0].duration==2424&&d.matches[0].kills==7,"Site limited five matches and total CS mapping");check(d.source=="iplol"&&d.sourceUpdatedAt!=d.updatedAt,"Source and timestamps");
   bool caught=false;try{LeagueIplolProvider.Parse(new LeagueIdentity{riotId="other#KR1"},j.DeserializeObject(raw));}catch{caught=true;}check(caught,"Wrong account rejected");
   foreach(int status in new[]{200,403,404,429,503}){var h=new Mock{body=raw,status=status};using(var p=new LeagueIplolProvider(root,h)){string state="";try{await p.Read("unused",id,null,CancellationToken.None);}catch(LeagueFailure e){state=e.State;if(status==429)check(e.WaitSeconds==180,"Retry-After respected");}check(status==200?state=="":state==(status==403?"siteBlocked":status==404?"notFound":status==429?"rateLimit":"service"),"HTTP "+status);check(h.safe,"Single public GET without credentials or force refresh");}}
   var transport=new Mock{body=raw};using(var app=new LeagueService(root,false,null,new LeagueIplolProvider(root,transport))){DateTime now=DateTime.UtcNow;app.Now=()=>now;await app.Connect(id.riotId,"KR");check(app.View.connected&&!app.View.hasKey&&app.View.source=="iplol","Keyless connect and persistence");await app.Refresh();check(transport.calls==1,"Manual cooldown");now=now.AddMinutes(31);transport.status=503;await app.Tick();check(app.View.stale&&app.View.data.ranks[0].points==34,"Failure keeps previous record");now=now.AddHours(1);transport.status=403;await app.Tick();int count=transport.calls;now=now.AddDays(2);await app.Tick();check(transport.calls==count,"403 pauses background calls");}
   using(var app=new LeagueService(root,false)){check(app.View.connected&&app.View.source=="iplol"&&app.View.data.ranks[0].points==34,"Restart restores source and cached data");}
   using(var live=new LeagueIplolProvider(root)){var r=await live.Read(null,id,null,CancellationToken.None);File.WriteAllText(output,j.Serialize(r.data));check(r.data.source=="iplol"&&r.data.matches.Length>0,"Live account lookup");}
  }catch(Exception e){log.Add("FAIL "+e.Message);}File.WriteAllLines(output+".tests.txt",log);
 }
}