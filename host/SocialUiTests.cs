using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Drawing;using System.Threading.Tasks;using Microsoft.Web.WebView2.Core;
sealed partial class GamingWindow {
 async Task VerifySocial(){
  string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification-social");Directory.CreateDirectory(folder);var lines=new List<string>();
  try{await Task.Delay(400);FriendsTests.Fixture(service.Friends);service.Settings.brand="Nyang Gaming";service.Friends.CloseHistory();var social=service.Friends.View.social;
   social.connected=true;social.message="검증용 파티 상태 · 실제 친구에게 공유되지 않아요.";social.options.partyState="looking";social.options.partyGame="eternal";
   social.parties=new object[]{new{id="game",state="looking",gameId="eternal"}};
   social.weekly=new{weekStart="2026-09-28",previousStart="2026-09-21",seconds=18000,previousSeconds=25200,games=new[]{new{gameId="eternal",seconds=10800},new{gameId="genshin",seconds=7200}}};
   social.weeklyMessage="화면 검증 전용 기록 · 실제 플레이 기록 아님";
   foreach(string theme in new[]{"default","pink"})foreach(int width in new[]{600,1300}){
    service.Settings.theme=theme;ClientSize=new Size(width,950);web.ZoomFactor=1;Send();await Task.Delay(150);await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=friends]').click();window.scrollTo(0,0)");await Task.Delay(250);
    lines.Add(theme+"/"+width+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({party:!!document.querySelector('.social-party'),weekly:document.querySelector('.weekly-summary').innerText.includes('2시간 0분 감소'),badge:!!document.querySelector('.party-badge'),bells:document.querySelectorAll('.friend-alert-button').length,overflow:document.documentElement.scrollWidth>innerWidth+1,errors:window.__errors||[]})"));
    await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.social-party').scrollIntoView()");await Task.Delay(100);
    using(var f=File.Create(Path.Combine(folder,"social-"+theme+"-"+width+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
   }
   await web.CoreWebView2.ExecuteScriptAsync("chrome.webview.postMessage({version:1,type:'friends.social',loginNotifications:false,partyState:'away',partyGame:'league'})");await Task.Delay(120);
   lines.Add("party change: "+(social.options.partyState=="away"&&social.options.partyGame==""&&!social.options.loginNotifications));
   foreach(string theme in new[]{"default","pink"})foreach(int width in new[]{600,1300})foreach(double zoom in new[]{1.0,1.5}){
    service.Settings.theme=theme;ClientSize=new Size(width,900);web.ZoomFactor=zoom;service.Updates.Fixture(new UpdateView{currentVersion="1.0.2",changelogDialog=true,installedNotes="친구 접속 알림\n파티 상태 · 주간 플레이 요약\n<script>window.__socialInjected=true</script>"});Send();await Task.Delay(180);
    lines.Add("notes "+theme+"/"+width+"/"+zoom+": "+await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({title:document.querySelector('#update-title').innerText,notes:document.querySelector('.update-notes').innerText.includes('<script>'),safe:!window.__socialInjected,overflow:document.documentElement.scrollWidth>innerWidth+1,buttons:document.querySelectorAll('.update-actions button').length,errors:window.__errors||[]})"));
    if(width==1300&&zoom==1)using(var f=File.Create(Path.Combine(folder,"changelog-"+theme+".png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
   }
   await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.update-actions button').click()");await Task.Delay(100);lines.Add("notes closed: "+!service.Updates.View.changelogDialog);
   File.WriteAllLines(Path.Combine(folder,"checks.txt"),lines);
  }catch(Exception e){File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());}
  quitting=true;Close();
 }
}
