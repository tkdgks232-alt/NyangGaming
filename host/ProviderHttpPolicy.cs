using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Net;using System.Net.Http;using System.Security.Cryptography;using System.Text;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;

// One durable policy/lock per provider, shared across instances/processes. Never follows
// challenges or switches transport/provider. Raw responses are DPAPI encrypted on disk.
internal sealed class ProviderHttpPolicy:DelegatingHandler {
 internal static readonly TimeSpan MinimumAge=TimeSpan.FromMinutes(15);
 internal Func<DateTime> Clock=()=>DateTime.UtcNow;
 readonly string folder;readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=24*1024*1024,RecursionLimit=100};
 internal sealed class State {public bool blocked;public int strikes;public DateTime retryAt;public Dictionary<string,DateTime> attempts=new Dictionary<string,DateTime>();}
 internal sealed class Entry {public DateTime fetched;public int status;public string type,body,location;}
 internal static string DefaultRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KoruGaming_Next","UserData");}}
 internal ProviderHttpPolicy(string provider,string root,HttpMessageHandler transport=null):base(transport??new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){if(provider.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))throw new ArgumentException();folder=Path.Combine(root??DefaultRoot,"ProviderCache",provider);}
 static string Hash(string value){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");}
 T Load<T>(string path) where T:class {if(!File.Exists(path))return null;return json.Deserialize<T>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser)));}
 void Save(string path,object value){LeagueJson.AtomicBytes(path,ProtectedData.Protect(Encoding.UTF8.GetBytes(json.Serialize(value)),null,DataProtectionScope.CurrentUser));}
 HttpResponseMessage Response(Entry entry,HttpRequestMessage request,bool cached){var response=new HttpResponseMessage((HttpStatusCode)entry.status){RequestMessage=request,Content=new ByteArrayContent(Convert.FromBase64String(entry.body??""))};if(!string.IsNullOrEmpty(entry.type))response.Content.Headers.TryAddWithoutValidation("Content-Type",entry.type);if(!string.IsNullOrEmpty(entry.location))response.Headers.TryAddWithoutValidation("Location",entry.location);response.Headers.TryAddWithoutValidation("X-Nyang-Fetched-At",entry.fetched.ToString("o"));response.Headers.TryAddWithoutValidation("X-Nyang-Cache",cached?"hit":"miss");return response;}
 static HttpResponseMessage Failure(int code,string reason,DateTime? retry,DateTime now){var response=new HttpResponseMessage((HttpStatusCode)code){Content=new StringContent("{\"error\":\""+reason+"\"}",Encoding.UTF8,"application/json")};response.Headers.TryAddWithoutValidation("X-Nyang-Policy",reason);if(retry.HasValue)response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(Math.Max(1,Math.Ceiling((retry.Value-now).TotalSeconds))));return response;}
 internal static object Stamp(object value,HttpResponseMessage response){var map=value as Dictionary<string,object>;if(map!=null)map["__nyangFetchedAt"]=Fetched(response);return value;}
 internal static string DataTime(object value){string time=ErJson.Text(value,"__nyangFetchedAt");return string.IsNullOrEmpty(time)?DateTime.UtcNow.ToString("o"):time;}
 internal static string Fetched(HttpResponseMessage response){IEnumerable<string> values;return response.Headers.TryGetValues("X-Nyang-Fetched-At",out values)?values.First():DateTime.UtcNow.ToString("o");}
 internal static DateTime RetryUntil(HttpResponseMessage response,int strike,DateTime now){var retry=response.Headers.RetryAfter;if(retry!=null){if(retry.Date.HasValue)return retry.Date.Value.UtcDateTime>now?retry.Date.Value.UtcDateTime:now;if(retry.Delta.HasValue)return now.Add(retry.Delta.Value);}return now.AddMinutes(Math.Min(1440,15*Math.Pow(2,Math.Min(7,Math.Max(0,strike-1)))));}
 internal static string AccessFailure(HttpResponseMessage response,string text){int code=(int)response.StatusCode;if(code==401||code==403||code==451)return "access-blocked";IEnumerable<string> mitigated;if(response.Headers.TryGetValues("cf-mitigated",out mitigated)&&mitigated.Any(v=>v.IndexOf("challenge",StringComparison.OrdinalIgnoreCase)>=0))return "access-challenge";
  string type=response.Content.Headers.ContentType==null?"":response.Content.Headers.ContentType.MediaType;string lower=(text??"").ToLowerInvariant();
  if(type.Contains("html")&&(lower.Contains("/cdn-cgi/challenge-platform/")||lower.Contains("cf-chl-")||lower.Contains("g-recaptcha")||lower.Contains("h-captcha")||lower.Contains("verify you are human")||lower.Contains("checking your browser")))return "access-challenge";
  if(code>=300&&code<400&&response.Headers.Location!=null){string path=response.Headers.Location.ToString().ToLowerInvariant();if(path.Contains("/login")||path.Contains("/signin")||path.Contains("/captcha")||path.Contains("/challenge"))return "login-required";}
  if(type.Contains("json"))try{var value=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.DeserializeObject(text);int? resultCode=ErJson.Int(value,"code")??ErJson.Int(ErJson.Get(value,"status"),"status_code");if(resultCode==401||resultCode==403)return "access-blocked";if(resultCode==429)return "rate-limit";int? ret=ErJson.Int(value,"retcode");if(ret.HasValue&&new[]{-100,10001,10103,10035,5003,10041,1034,10102,10104}.Contains(ret.Value))return "login-or-access-required";if(ret.HasValue&&new[]{10101,1028,-110}.Contains(ret.Value))return "rate-limit";
   if(object.Equals(ErJson.Get(ErJson.Get(value,"profile"),"isPrivate"),true))return "private-profile";
   string err=(ErJson.Text(value,"error")+" "+ErJson.Text(value,"code")+" "+ErJson.Text(value,"message")).ToLowerInvariant();if(new[]{"access_denied","access denied","unauthorized","forbidden","login_required","captcha_required","challenge_required"}.Any(err.Contains))return "access-blocked";
  }catch{}return null;
 }
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
  // Authentication scope for private HoYoLAB responses is stable across token renewal.
  string scope="";IEnumerable<string> cookie;if(request.Headers.TryGetValues("Cookie",out cookie)){var parts=string.Join(";",cookie).Split(';');scope=string.Join(";",parts.Select(p=>p.Trim()).Where(p=>p.StartsWith("ltuid=")||p.StartsWith("ltuid_v2=")).OrderBy(p=>p));if(scope.Length==0)scope=string.Join(";",cookie);}
  string payload=request.Content==null?"":await request.Content.ReadAsStringAsync();string id=Hash(request.Method.Method+"\n"+request.RequestUri.AbsoluteUri+"\n"+scope+"\n"+payload);
  FileStream lease=null;try{
   Directory.CreateDirectory(folder);while(lease==null){token.ThrowIfCancellationRequested();try{lease=new FileStream(Path.Combine(folder,"policy.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){}if(lease==null)await Task.Delay(50,token);}
   DateTime now=Clock();string statePath=Path.Combine(folder,"state.dpapi"),cachePath=Path.Combine(folder,id+".cache");State state=Load<State>(statePath)??new State();
   if(state.blocked)return Failure(403,"provider-stopped",null,now);
   if(now<state.retryAt)return Failure(429,"provider-wait",state.retryAt,now);
   Entry cached=null;try{cached=Load<Entry>(cachePath);}catch{}if(cached!=null&&now<cached.fetched.Add(MinimumAge))return Response(cached,request,true);
   DateTime previous;if(state.attempts.TryGetValue(id,out previous)&&now<previous.Add(MinimumAge))return Failure(429,"cached-request-wait",previous.Add(MinimumAge),now);
   state.attempts=state.attempts.Where(p=>now<p.Value.Add(MinimumAge)).ToDictionary(p=>p.Key,p=>p.Value);state.attempts[id]=now;Save(statePath,state); // persist BEFORE network, including crashes/timeouts
   using(var response=await base.SendAsync(request,token)){
    string headerFailure=AccessFailure(response,"");if((int)response.StatusCode==429){state.strikes=Math.Min(32,state.strikes+1);state.retryAt=RetryUntil(response,state.strikes,now);Save(statePath,state);return Failure(429,"provider-rate-limit",state.retryAt,now);}if(headerFailure!=null){state.blocked=true;Save(statePath,state);return Failure(403,headerFailure,null,now);}
    using(var stream=await response.Content.ReadAsStreamAsync())using(var memory=new MemoryStream()){var buffer=new byte[8192];int count;while((count=await stream.ReadAsync(buffer,0,buffer.Length,token))>0){if(memory.Length+count>8*1024*1024)return Failure(502,"response-too-large",null,now);memory.Write(buffer,0,count);}byte[] bytes=memory.ToArray();string text=Encoding.UTF8.GetString(bytes),access=AccessFailure(response,text);
     if((int)response.StatusCode==429||access=="rate-limit"){state.strikes=Math.Min(32,state.strikes+1);state.retryAt=RetryUntil(response,state.strikes,now);Save(statePath,state);return Failure(429,"provider-rate-limit",state.retryAt,now);}
     if(access!=null){state.blocked=true;Save(statePath,state);return Failure(403,access,null,now);}
     if(response.IsSuccessStatusCode){state.strikes=0;state.retryAt=DateTime.MinValue;Save(statePath,state);}
     var entry=new Entry{fetched=now,status=(int)response.StatusCode,body=Convert.ToBase64String(bytes),type=response.Content.Headers.ContentType==null?null:response.Content.Headers.ContentType.ToString(),location=response.Headers.Location==null?null:response.Headers.Location.ToString()};Save(cachePath,entry);return Response(entry,request,false);
    }
   }
  }catch(OperationCanceledException){throw;}catch{return Failure(503,"provider-request-failed",null,Clock());}finally{if(lease!=null)lease.Dispose();}
 }
}
