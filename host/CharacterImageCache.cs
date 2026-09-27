using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Public CDN images only, without HoYoLAB cookies. No arbitrary frontend URLs.
internal sealed class CharacterImageCache {
    internal readonly string Folder;
    readonly SemaphoreSlim slots=new SemaphoreSlim(4);
    internal static readonly HashSet<string> ObservedHosts=new HashSet<string>();
    internal CharacterImageCache(string root){Folder=Path.Combine(root,"CharacterImages");Directory.CreateDirectory(Folder);}
    internal static string SafeUrl(string url){Uri u;if(!Uri.TryCreate(url,UriKind.Absolute,out u)||u.Scheme!="https"||!u.IsDefaultPort||u.UserInfo.Length>0)return "";
        lock(ObservedHosts)ObservedHosts.Add(u.IdnHost.ToLowerInvariant());
        var hosts=new[]{"act-webstatic.hoyoverse.com","upload-os-bbs.hoyolab.com","act-upload.hoyoverse.com","act-upload.mihoyo.com","upload-bbs.mihoyo.com","fastcdn.hoyoverse.com"};
        return hosts.Contains(u.IdnHost.ToLowerInvariant())?u.AbsoluteUri:"";
    }
    async Task<string> Get(string url,CancellationToken token){
        url=SafeUrl(url);if(url.Length==0)return "";
        string name;using(var h=SHA256.Create())name=BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-","")+".png";
        string path=Path.Combine(Folder,name),local="https://characters.korugaming.example/"+name;
        if(File.Exists(path))return local;
        await slots.WaitAsync(token);
        try{
            if(File.Exists(path))return local;
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)){
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using(var handler=new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false})using(var client=new HttpClient(handler))using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,timeout.Token)){
                    if(!response.IsSuccessStatusCode)return "";
                    using(var stream=await response.Content.ReadAsStreamAsync())using(var memory=new MemoryStream()){
                        var buffer=new byte[8192];int n;while((n=await stream.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0){if(memory.Length+n>4*1024*1024)return "";memory.Write(buffer,0,n);}
                        memory.Position=0;using(var source=Image.FromStream(memory)){if(source.Width>4096||source.Height>4096)return "";using(var thumb=new Bitmap(source,new Size(128,128))){thumb.Save(path,ImageFormat.Png);}}
                    }
                }
            }
            return local;
        }catch(OperationCanceledException){if(token.IsCancellationRequested)throw;return "";}catch{return "";}finally{slots.Release();}
    }
    internal async Task Resolve(List<GenshinCharacter> list,CancellationToken token){
        var urls=new HashSet<string>();foreach(var c in list){urls.Add(c.icon??"");if(c.weapon!=null)urls.Add(c.weapon.icon??"");foreach(var e in c.artifacts)urls.Add(e.icon??"");}
        var tasks=urls.ToDictionary(u=>u,u=>Get(u,token));await Task.WhenAll(tasks.Values);
        foreach(var c in list){c.icon=tasks[c.icon??""].Result;if(c.weapon!=null)c.weapon.icon=tasks[c.weapon.icon??""].Result;foreach(var e in c.artifacts)e.icon=tasks[e.icon??""].Result;}
    }
}
